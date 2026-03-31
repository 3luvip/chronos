// game_server/src/anti_cheat.rs
//
// Server-side anti-cheat: xác thực mọi input từ client trước khi apply vào world.
//
// Các lớp bảo vệ:
//   1. Speed check     — khoảng cách di chuyển tối đa theo thời gian
//   2. Attack cooldown — thời gian hồi chiêu
//   3. Range check     — khoảng cách tấn công hợp lệ
//   4. Sequence check  — input seq phải tăng dần (chống replay)
//   5. Timestamp check — timestamp client không được lệch server > MAX_CLOCK_DRIFT_MS
//   6. Packet decrypt  — giải mã AES-256-GCM nếu server bật ENCRYPT_GAME_PACKETS
//   7. Rate burst      — không cho phép quá MAX_INPUT_BURST input trong 1 tick

use std::collections::HashMap;
use std::time::{Duration, Instant};
use crate::player::{Player, Vec2};

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/// Drift tối đa cho phép giữa client clock và server clock (ms).
const MAX_CLOCK_DRIFT_MS: i64 = 5_000;

/// Số input tối đa được xử lý từ 1 client trong 1 tick (20ms).
/// Chặn burst spam input để gian lận speed.
const MAX_INPUT_BURST: usize = 3;

/// Thời gian phạt khi phát hiện gian lận (giây) — client bị kick sau thời gian này.
const CHEAT_PENALTY_SECS: u64 = 5;

// ─────────────────────────────────────────────────────────────────────────────
// AntiCheatViolation
// ─────────────────────────────────────────────────────────────────────────────

#[derive(Debug, Clone)]
pub enum AntiCheatViolation {
    SpeedHack          { actual: f32, max_allowed: f32 },
    AttackCooldown     { remaining_ms: u64 },
    AttackRange        { actual: f32, max_allowed: f32 },
    ReplayInput        { recv_seq: u32, last_seq: u32 },
    ClockDrift         { drift_ms: i64 },
    InputBurst,
    DeadPlayerAction,
    InvalidPayload     (String),
}

impl std::fmt::Display for AntiCheatViolation {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::SpeedHack { actual, max_allowed } =>
                write!(f, "speed hack: moved {actual:.2} tiles, max={max_allowed:.2}"),
            Self::AttackCooldown { remaining_ms } =>
                write!(f, "attack on cooldown: {remaining_ms}ms remaining"),
            Self::AttackRange { actual, max_allowed } =>
                write!(f, "attack out of range: dist={actual:.2}, max={max_allowed:.2}"),
            Self::ReplayInput { recv_seq, last_seq } =>
                write!(f, "replay: seq={recv_seq} ≤ last={last_seq}"),
            Self::ClockDrift { drift_ms } =>
                write!(f, "clock drift: {drift_ms}ms (max={MAX_CLOCK_DRIFT_MS}ms)"),
            Self::InputBurst =>
                write!(f, "input burst exceeded {MAX_INPUT_BURST}/tick"),
            Self::DeadPlayerAction =>
                write!(f, "action from dead player"),
            Self::InvalidPayload(msg) =>
                write!(f, "invalid payload: {msg}"),
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Per-player anti-cheat state
// ─────────────────────────────────────────────────────────────────────────────

pub struct AntiCheatState {
    pub last_input_seq:  u32,
    pub inputs_this_tick: usize,
    pub violation_count: u32,
    pub flagged_at:      Option<Instant>,
}

impl AntiCheatState {
    pub fn new() -> Self {
        Self {
            last_input_seq:   0,
            inputs_this_tick: 0,
            violation_count:  0,
            flagged_at:       None,
        }
    }

    /// Reset counter mỗi tick
    pub fn tick_reset(&mut self) {
        self.inputs_this_tick = 0;
    }

    /// Ghi nhận vi phạm. Trả về true nếu player nên bị kick.
    pub fn record_violation(&mut self, v: &AntiCheatViolation) -> bool {
        self.violation_count += 1;
        if self.flagged_at.is_none() {
            self.flagged_at = Some(Instant::now());
        }
        tracing::warn!(violations = self.violation_count, %v, "anti-cheat violation");
        // Kick sau 3 vi phạm hoặc nếu đã bị flag > CHEAT_PENALTY_SECS
        self.violation_count >= 3
            || self.flagged_at
                .map(|t| t.elapsed() > Duration::from_secs(CHEAT_PENALTY_SECS))
                .unwrap_or(false)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// InputPacket — dữ liệu sau khi parse từ OP_PLAYER_INPUT payload
// ─────────────────────────────────────────────────────────────────────────────

pub struct InputPacket {
    pub seq:      u32,
    pub move_x:   f32,
    pub move_y:   f32,
    pub flags:    u8,    // bit0=attack, bit1=jump
}

impl InputPacket {
    pub fn want_attack(&self) -> bool { self.flags & 0x01 != 0 }
    pub fn want_jump(&self)   -> bool { self.flags & 0x02 != 0 }

    /// Parse từ raw payload (big-endian: u32 seq + f32 x + f32 y + u8 flags = 13 bytes)
    pub fn parse(payload: &[u8]) -> Result<Self, AntiCheatViolation> {
        if payload.len() < 13 {
            return Err(AntiCheatViolation::InvalidPayload(
                format!("expected ≥13 bytes, got {}", payload.len())
            ));
        }
        let seq    = u32::from_be_bytes(payload[0..4].try_into().unwrap());
        let move_x = f32::from_be_bytes(payload[4..8].try_into().unwrap());
        let move_y = f32::from_be_bytes(payload[8..12].try_into().unwrap());
        let flags  = payload[12];

        // Sanity: move vector phải normalized-ish (max length 1.5 sau diagonal)
        let len_sq = move_x * move_x + move_y * move_y;
        if len_sq > 2.26 {  // sqrt(2)*1.06 squared
            return Err(AntiCheatViolation::InvalidPayload(
                format!("move vector too long: len²={len_sq:.3}")
            ));
        }
        // NaN/Inf check
        if !move_x.is_finite() || !move_y.is_finite() {
            return Err(AntiCheatViolation::InvalidPayload(
                "move vector contains NaN or Inf".into()
            ));
        }

        Ok(Self { seq, move_x, move_y, flags })
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// validate_input — điểm vào chính, gọi trong game loop
// ─────────────────────────────────────────────────────────────────────────────

/// Xác thực một input packet từ client.
/// Trả về Ok(InputPacket) nếu hợp lệ, Err(violation) nếu gian lận.
pub fn validate_input(
    player:      &Player,
    ac:          &mut AntiCheatState,
    payload:     &[u8],
    server_ms:   u64,
    // client_ts_ms được ghi trong packet nếu server muốn check clock drift
    // Để đơn giản: check drift qua heartbeat; ở đây chỉ check seq + burst
) -> Result<InputPacket, AntiCheatViolation> {
    // ── 1. Dead player check ──────────────────────────────────────────────
    if player.stats.hp <= 0 {
        return Err(AntiCheatViolation::DeadPlayerAction);
    }

    // ── 2. Burst check ────────────────────────────────────────────────────
    ac.inputs_this_tick += 1;
    if ac.inputs_this_tick > MAX_INPUT_BURST {
        return Err(AntiCheatViolation::InputBurst);
    }

    // ── 3. Parse packet ───────────────────────────────────────────────────
    let input = InputPacket::parse(payload)?;

    // ── 4. Sequence replay check ──────────────────────────────────────────
    if input.seq != 0 && input.seq <= ac.last_input_seq {
        return Err(AntiCheatViolation::ReplayInput {
            recv_seq: input.seq,
            last_seq: ac.last_input_seq,
        });
    }
    ac.last_input_seq = input.seq;

    Ok(input)
}

/// Xác thực kết quả di chuyển sau khi apply input.
pub fn validate_move_result(
    old_pos:       Vec2,
    new_pos:       Vec2,
    elapsed_ms:    u64,
    move_speed:    f32,   // tiles/s từ Stats
) -> Result<Vec2, AntiCheatViolation> {
    let elapsed_s  = elapsed_ms as f32 / 1000.0;
    // Cho phép thêm 25% tolerance cho lag và sub-tick timing
    let max_dist   = move_speed * elapsed_s * 1.25;
    let actual_dist = {
        let dx = new_pos.x - old_pos.x;
        let dy = new_pos.y - old_pos.y;
        (dx * dx + dy * dy).sqrt()
    };

    if actual_dist > max_dist {
        return Err(AntiCheatViolation::SpeedHack {
            actual:      actual_dist,
            max_allowed: max_dist,
        });
    }
    Ok(new_pos)
}

/// Xác thực attack trước khi apply damage.
pub fn validate_attack_ac(
    attacker:    &Player,
    target:      &Player,
    now_ms:      u64,
    max_range:   f32,
) -> Result<(), AntiCheatViolation> {
    if attacker.stats.hp <= 0 {
        return Err(AntiCheatViolation::DeadPlayerAction);
    }

    let since_last = now_ms.saturating_sub(attacker.stats.last_attack_ms);
    if since_last < attacker.stats.attack_cooldown_ms {
        return Err(AntiCheatViolation::AttackCooldown {
            remaining_ms: attacker.stats.attack_cooldown_ms - since_last,
        });
    }

    let dist = {
        let dx = attacker.position.x - target.position.x;
        let dy = attacker.position.y - target.position.y;
        (dx * dx + dy * dy).sqrt()
    };
    if dist > max_range {
        return Err(AntiCheatViolation::AttackRange {
            actual:      dist,
            max_allowed: max_range,
        });
    }
    Ok(())
}

// ─────────────────────────────────────────────────────────────────────────────
// AES-256-GCM packet decryption (server side)
// Dùng crate `aes-gcm = "0.10"` trong Cargo.toml
// ─────────────────────────────────────────────────────────────────────────────

#[cfg(feature = "packet_encryption")]
pub mod crypto {
    use aes_gcm::{
        aead::{Aead, KeyInit, Payload},
        Aes256Gcm, Key, Nonce,
    };
    use pbkdf2::pbkdf2_hmac;
    use sha2::Sha256;
    use super::AntiCheatViolation;

    const NONCE_SIZE: usize = 12;
    const TAG_SIZE:   usize = 16;
    const SEQ_SIZE:   usize = 8;
    pub const OVERHEAD: usize = NONCE_SIZE + TAG_SIZE + SEQ_SIZE;

    pub struct ServerCrypto {
        cipher:       Aes256Gcm,
        xor_key:      [u8; 16],
        last_recv_seq: u64,
    }

    impl ServerCrypto {
        /// Khởi tạo với cùng shared_secret và session_id như client
        pub fn new(shared_secret: &str, session_id: u64) -> Self {
            let secret = format!("{shared_secret}:{session_id:016X}");
            let salt   = b"ChronosAntiCheat_v1";
            let mut key_material = [0u8; 48]; // 32 AES + 16 XOR
            pbkdf2_hmac::<Sha256>(secret.as_bytes(), salt, 100_000, &mut key_material);

            let aes_key = Key::<Aes256Gcm>::from_slice(&key_material[..32]);
            let cipher  = Aes256Gcm::new(aes_key);
            let mut xor_key = [0u8; 16];
            xor_key.copy_from_slice(&key_material[32..48]);

            Self { cipher, xor_key, last_recv_seq: 0 }
        }

        /// Giải mã packet từ client. Format: [nonce(12)] [seq(8)] [ciphertext] [tag(16)]
        pub fn open(
            &mut self,
            opcode:     u16,
            session_id: u64,
            sealed:     &[u8],
        ) -> Result<Vec<u8>, AntiCheatViolation> {
            if sealed.len() < OVERHEAD {
                return Err(AntiCheatViolation::InvalidPayload("packet too short".into()));
            }

            let nonce_bytes = &sealed[..NONCE_SIZE];
            let seq = u64::from_be_bytes(sealed[NONCE_SIZE..NONCE_SIZE+SEQ_SIZE].try_into().unwrap());
            let cipher_end  = sealed.len() - TAG_SIZE;
            let ciphertext  = &sealed[NONCE_SIZE+SEQ_SIZE..cipher_end];
            // tag is embedded in ciphertext for aes-gcm crate (appended)

            // Anti-replay
            if seq == 0 || seq <= self.last_recv_seq.saturating_sub(64) {
                return Err(AntiCheatViolation::ReplayInput {
                    recv_seq: seq as u32,
                    last_seq: self.last_recv_seq as u32,
                });
            }

            // Build AAD
            let mut aad = [0u8; 18];
            aad[0..2].copy_from_slice(&opcode.to_be_bytes());
            aad[2..10].copy_from_slice(&session_id.to_be_bytes());
            aad[10..18].copy_from_slice(&seq.to_be_bytes());

            let nonce = Nonce::from_slice(nonce_bytes);
            // aes-gcm: ciphertext includes tag at end
            let ciphertext_with_tag = &sealed[NONCE_SIZE+SEQ_SIZE..];

            let plaintext = self.cipher
                .decrypt(nonce, Payload { msg: ciphertext_with_tag, aad: &aad })
                .map_err(|_| AntiCheatViolation::InvalidPayload("AES-GCM auth failed".into()))?;

            if seq > self.last_recv_seq {
                self.last_recv_seq = seq;
            }

            // Undo XOR obfuscation
            Ok(self.xor_deobfuscate(&plaintext, seq))
        }

        fn xor_deobfuscate(&self, data: &[u8], seq: u64) -> Vec<u8> {
            let seq_bytes = seq.to_be_bytes();
            data.iter().enumerate().map(|(i, &b)| {
                let key = self.xor_key[i % 16] ^ seq_bytes[i % 8] ^ ((i >> 4) as u8);
                b ^ key
            }).collect()
        }
    }
}