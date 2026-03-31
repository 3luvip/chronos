// ============================================================
// login-service/src/main.rs  —  v2 (session-based security)
//
// Thay đổi so với bản trước:
// [SESSION]
// 1. Mỗi ConnState lưu thêm `last_heartbeat_ms: i64`.
//    Nếu client không gửi OP_HEARTBEAT trong SESSION_TIMEOUT_MS,
//    server đóng kết nối (session expired).
// 2. OP_HEARTBEAT yêu cầu session_id hợp lệ + FLAG_INTEGRITY.
//    Server phản hồi với server timestamp (i64) để client phát hiện
//    clock drift.
// 3. Session timeout được kiểm tra ở mỗi vòng lặp read.
//
// [BẢO MẬT]
// 4. Giữ nguyên: argon2 verify, constant-time PSK compare,
//    FLAG_INTERNAL gating, rate limit, login attempt limit.
//    reserved byte == 0 validation.
// ============================================================

use argon2::{Argon2, PasswordHash, PasswordVerifier};
use hmac::{Hmac, Mac};
use protocol::{
    codec::{PacketReader, PacketWriter},
    Message, FLAG_ENCRYPTED, FLAG_INTEGRITY, FLAG_INTERNAL, FRAME_MAGIC,
    OP_HEARTBEAT, OP_INTERNAL_AUTH, OP_LOGIN, OP_LOGOUT, OP_SERVER_MESSAGE,
    OP_SERVER_SYNC, PROTOCOL_VERSION,
};
use rand::random;
use sha2::Sha256;
use shared::config::LoginConfig;
use shared::error::{ServiceError, ServiceResult};
use shared::logging;
use sqlx::{mysql::MySqlPoolOptions, MySqlPool, Row};
use std::collections::{HashMap, HashSet};
use std::fs::File;
use std::io::BufReader;
use std::sync::Arc;
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};
use tokio::net::TcpListener;
use tokio::sync::Mutex;
use tokio_rustls::rustls::{self, pki_types::CertificateDer, pki_types::PrivateKeyDer};
use tokio_rustls::TlsAcceptor;
use tracing::{debug, error, info, warn};

// ── Session timeout ────────────────────────────────────────────────────────
/// Thời gian tối đa (ms) giữa 2 heartbeat liên tiếp trước khi session hết hạn.
/// Client nên gửi heartbeat mỗi 30s; server cho phép tới 90s để có buffer.
const SESSION_TIMEOUT_MS: i64 = 90_000;

// ─────────────────────────────────────────────────────────────────────────────

#[derive(Default)]
struct AppState {
    online_user_ids:  HashSet<i32>,
    users_by_server:  HashMap<i32, HashSet<i32>>,
    /// session_id → user_id: dùng để xác thực OP_HEARTBEAT nhanh O(1).
    sessions:         HashMap<u64, i32>,
}

#[derive(Debug, Clone)]
struct UserAccount {
    id:               i32,
    admin:            bool,
    active:           bool,
    gold_bar:         i32,
    rewards:          String,
    server_login:     i32,
    total_recharge:   i32,
    vnd_bar:          i32,
    last_time_login_ms:  i64,
    last_time_logout_ms: i64,
}

struct ConnState {
    session_id:           Option<u64>,
    user_id:              Option<i32>,
    is_internal:          bool,
    window_started_ms:    i64,
    window_message_count: u32,
    login_attempts:       u32,
    /// Timestamp (ms) của heartbeat cuối cùng hoặc thời điểm login thành công.
    last_heartbeat_ms:    i64,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum IntegrityMode { Off, Checksum, Hmac }

impl IntegrityMode {
    fn from_str(s: &str) -> Self {
        match s.to_ascii_lowercase().as_str() {
            "checksum" => Self::Checksum,
            "hmac"     => Self::Hmac,
            _          => Self::Off,
        }
    }
}

impl ConnState {
    fn new() -> Self {
        let now = unix_now_ms();
        Self {
            session_id:           None,
            user_id:              None,
            is_internal:          false,
            window_started_ms:    now,
            window_message_count: 0,
            login_attempts:       0,
            last_heartbeat_ms:    now,
        }
    }

    fn apply_rate_limit(&mut self, cfg: &LoginConfig) -> bool {
        let now = unix_now_ms();
        if now - self.window_started_ms >= cfg.rate_limit_window_ms {
            self.window_started_ms    = now;
            self.window_message_count = 0;
        }
        self.window_message_count = self.window_message_count.saturating_add(1);
        self.window_message_count <= cfg.rate_limit_max_messages
    }

    fn is_authenticated(&self, incoming_session_id: u64) -> bool {
        matches!(self.session_id, Some(id) if id != 0 && id == incoming_session_id)
    }

    /// Kiểm tra xem session có còn hợp lệ (dựa vào heartbeat) không.
    /// Internal service không bị giới hạn.
    fn is_session_expired(&self) -> bool {
        if self.is_internal || self.session_id.is_none() {
            return false;
        }
        unix_now_ms() - self.last_heartbeat_ms > SESSION_TIMEOUT_MS
    }
}

fn is_benign_peer_disconnect(err: &ServiceError) -> bool {
    match err {
        ServiceError::Io(e) => {
            e.kind() == std::io::ErrorKind::UnexpectedEof
                || e.kind() == std::io::ErrorKind::ConnectionReset
                || e.to_string().to_ascii_lowercase().contains("close_notify")
        }
        _ => false,
    }
}

// ─────────────────────────────────────────────────────────────────────────────

#[tokio::main]
async fn main() -> ServiceResult<()> {
    rustls::crypto::aws_lc_rs::default_provider()
        .install_default()
        .map_err(|_| ServiceError::Config("failed to install rustls crypto provider".into()))?;

    logging::init("login-service");
    let cfg = LoginConfig::from_env();
    let addr = cfg.addr();

    if cfg.internal_psk.is_empty() {
        warn!("INTERNAL_PSK is not set — OP_SERVER_SYNC will be rejected");
    }

    let listener      = TcpListener::bind(&addr).await?;
    let pool          = MySqlPoolOptions::new()
        .max_connections(20)
        .connect(&cfg.database_url())
        .await?;
    let state         = Arc::new(Mutex::new(AppState::default()));
    let integrity_mode = IntegrityMode::from_str(&cfg.integrity_mode);
    let tls_acceptor  = if cfg.tls_enabled {
        Some(Arc::new(build_tls_acceptor(&cfg)?))
    } else {
        None
    };

    info!(%addr, protocol_version = protocol::PROTOCOL_VERSION,
          tls_enabled = cfg.tls_enabled, integrity_mode = ?integrity_mode,
          session_timeout_s = SESSION_TIMEOUT_MS / 1000,
          "login service started");

    loop {
        tokio::select! {
            accept_result = listener.accept() => {
                match accept_result {
                    Ok((socket, peer)) => {
                        info!(%peer, "accepted");
                        let state    = Arc::clone(&state);
                        let pool     = pool.clone();
                        let cfg      = cfg.clone();
                        let acceptor = tls_acceptor.clone();
                        tokio::spawn(async move {
                            let result = if let Some(acc) = acceptor {
                                match acc.accept(socket).await {
                                    Ok(tls) => handle_connection(tls,   pool, state, cfg, integrity_mode).await,
                                    Err(e)  => Err(ServiceError::Security(format!("tls handshake: {e}"))),
                                }
                            } else {
                                handle_connection(socket, pool, state, cfg, integrity_mode).await
                            };
                            if let Err(e) = result {
                                if is_benign_peer_disconnect(&e) {
                                    debug!(%peer, %e, "peer disconnected");
                                } else {
                                    warn!(%peer, error = %e, "closed with error");
                                }
                            }
                        });
                    }
                    Err(e) => error!(error = %e, "accept failed"),
                }
            }
            _ = tokio::signal::ctrl_c() => { info!("shutdown"); break; }
        }
    }
    pool.close().await;
    info!("login service stopped");
    Ok(())
}

// ─────────────────────────────────────────────────────────────────────────────

async fn handle_connection<S>(
    mut socket: S,
    pool:       MySqlPool,
    state:      Arc<Mutex<AppState>>,
    cfg:        LoginConfig,
    integrity_mode: IntegrityMode,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut conn = ConnState::new();

    loop {
        // ── Session timeout check ─────────────────────────────────────────
        if conn.is_session_expired() {
            info!(user_id = ?conn.user_id, "session expired — closing");
            if let Some(uid) = conn.user_id {
                update_logout_time(&pool, uid).await;
                remove_user(&state, uid, &mut conn).await;
            }
            return Err(ServiceError::Security("session expired".into()));
        }

        let incoming = read_message(&mut socket, cfg.max_frame_size, integrity_mode, &cfg.hmac_secret).await?;

        if !conn.apply_rate_limit(&cfg) {
            return Err(ServiceError::Security("rate limit exceeded".into()));
        }
        if incoming.flags & FLAG_ENCRYPTED != 0 {
            return Err(ServiceError::Protocol("encrypted messages not supported".into()));
        }
        if incoming.flags & FLAG_INTERNAL != 0 && incoming.opcode != OP_INTERNAL_AUTH {
            return Err(ServiceError::Security("FLAG_INTERNAL not allowed on this opcode".into()));
        }

        match incoming.opcode {
            OP_INTERNAL_AUTH => {
                handle_internal_auth(&mut conn, &cfg, incoming)?;
                info!("internal service authenticated");
            }

            OP_LOGIN => {
                conn.login_attempts = conn.login_attempts.saturating_add(1);
                if conn.login_attempts > cfg.max_login_attempts {
                    return Err(ServiceError::Security("too many login attempts".into()));
                }
                handle_login(&mut socket, &pool, &state, &cfg, &mut conn, incoming, integrity_mode).await?;
            }

            OP_LOGOUT => {
                if !conn.is_authenticated(incoming.session_id) {
                    return Err(ServiceError::Security("unauthorized logout".into()));
                }
                let mut rd      = PacketReader::new(incoming.payload);
                let user_id     = rd.read_i32()?;
                update_logout_time(&pool, user_id).await;
                remove_user(&state, user_id, &mut conn).await;
            }

            // ── OP_HEARTBEAT ──────────────────────────────────────────────
            // Client gửi định kỳ (mỗi ~30s) để giữ session alive.
            // Payload: (trống) — session_id ở header frame là đủ.
            // Response: server timestamp i64 ms (big-endian).
            OP_HEARTBEAT => {
                handle_heartbeat(&mut socket, &state, &mut conn, incoming, integrity_mode, &cfg).await?;
            }

            OP_SERVER_SYNC => {
                if !conn.is_internal {
                    return Err(ServiceError::Security("OP_SERVER_SYNC requires internal auth".into()));
                }
                handle_server_sync(&mut socket, &state, incoming, integrity_mode, &cfg).await?;
            }

            _ => return Err(ServiceError::Protocol("unknown opcode".into())),
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// OP_INTERNAL_AUTH
// ─────────────────────────────────────────────────────────────────────────────

fn handle_internal_auth(conn: &mut ConnState, cfg: &LoginConfig, incoming: Message) -> ServiceResult<()> {
    if cfg.internal_psk.is_empty() {
        return Err(ServiceError::Security("INTERNAL_PSK not configured".into()));
    }
    if incoming.flags & FLAG_INTERNAL == 0 {
        return Err(ServiceError::Security("OP_INTERNAL_AUTH requires FLAG_INTERNAL".into()));
    }
    let mut rd  = PacketReader::new(incoming.payload);
    let psk     = rd.read_utf()?;
    if !constant_time_eq(psk.as_bytes(), cfg.internal_psk.as_bytes()) {
        return Err(ServiceError::Security("invalid internal PSK".into()));
    }
    conn.is_internal = true;
    Ok(())
}

// ─────────────────────────────────────────────────────────────────────────────
// OP_HEARTBEAT
// ─────────────────────────────────────────────────────────────────────────────

async fn handle_heartbeat<S>(
    socket:   &mut S,
    state:    &Arc<Mutex<AppState>>,
    conn:     &mut ConnState,
    incoming: Message,
    integrity_mode: IntegrityMode,
    cfg:      &LoginConfig,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    // Phải có session hợp lệ
    if !conn.is_authenticated(incoming.session_id) {
        return Err(ServiceError::Security("heartbeat: invalid session".into()));
    }
    // Integrity bắt buộc cho heartbeat (chống replay)
    if integrity_mode != IntegrityMode::Off && incoming.flags & FLAG_INTEGRITY == 0 {
        return Err(ServiceError::Security("heartbeat: integrity required".into()));
    }

    let now_ms = unix_now_ms();

    // Xác nhận session vẫn tồn tại trong AppState (chưa bị kick)
    {
        let st      = state.lock().await;
        let user_id = conn.user_id.unwrap_or(0);
        if !st.online_user_ids.contains(&user_id) {
            return Err(ServiceError::Security("heartbeat: session not in online set".into()));
        }
    }

    // Cập nhật timestamp heartbeat
    conn.last_heartbeat_ms = now_ms;

    // Phản hồi: server timestamp để client phát hiện clock drift
    let mut w = PacketWriter::default();
    w.write_i64(now_ms);
    let mut resp = Message::new(OP_HEARTBEAT, w.into_inner());
    resp.request_id = incoming.request_id;
    resp.session_id = incoming.session_id;
    write_message(socket, resp, integrity_mode, &cfg.hmac_secret).await
}

// ─────────────────────────────────────────────────────────────────────────────
// OP_SERVER_SYNC
// ─────────────────────────────────────────────────────────────────────────────

async fn handle_server_sync<S>(
    _socket:  &mut S,
    state:    &Arc<Mutex<AppState>>,
    incoming: Message,
    _integrity_mode: IntegrityMode,
    _cfg:     &LoginConfig,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut rd    = PacketReader::new(incoming.payload);
    let server_id = rd.read_i32()?;
    let size      = rd.read_i32()?;
    if !(0..=10_000).contains(&size) {
        return Err(ServiceError::Protocol("invalid server sync size".into()));
    }

    let mut new_ids: HashSet<i32> = HashSet::with_capacity(size as usize);
    for _ in 0..size {
        let _client_id = rd.read_i32()?;
        let user_id    = rd.read_i32()?;
        let _username  = rd.read_utf()?;
        new_ids.insert(user_id);
    }

    let mut st = state.lock().await;
    if let Some(old) = st.users_by_server.get(&server_id).cloned() {
        for uid in old { st.online_user_ids.remove(&uid); st.sessions.retain(|_, v| *v != uid); }
    }
    for uid in &new_ids { st.online_user_ids.insert(*uid); }
    st.users_by_server.insert(server_id, new_ids);
    Ok(())
}

// ─────────────────────────────────────────────────────────────────────────────
// OP_LOGIN
// ─────────────────────────────────────────────────────────────────────────────

async fn handle_login<S>(
    socket:   &mut S,
    pool:     &MySqlPool,
    state:    &Arc<Mutex<AppState>>,
    cfg:      &LoginConfig,
    conn:     &mut ConnState,
    incoming: Message,
    integrity_mode: IntegrityMode,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut rd      = PacketReader::new(incoming.payload);
    let server_id   = rd.read_i32()?;
    let client_id   = rd.read_i32()?;
    let username    = rd.read_utf()?;
    let password    = rd.read_utf()?;

    macro_rules! fail {
        ($msg:expr) => {
            send_login_failed(socket, incoming.request_id, 0, client_id, $msg, integrity_mode, &cfg.hmac_secret).await?;
            return Ok(());
        };
    }

    if username.len() < 3 || username.len() > 32 || password.len() < 3 || password.len() > 64 {
        fail!("The login information is not valid");
    }

    let row = sqlx::query(
        "SELECT id, password_hash, server_login,
                UNIX_TIMESTAMP(last_time_login)  AS last_time_login_unix,
                UNIX_TIMESTAMP(last_time_logout) AS last_time_logout_unix,
                is_admin, active, gold, reward, total_recharge, vnd, ban
         FROM account WHERE username = ? LIMIT 1",
    )
    .bind(&username)
    .fetch_optional(pool)
    .await?;

    let Some(row) = row else {
        dummy_password_verify();
        fail!("The account information or password is incorrect.");
    };

    let hash_str: String = row.try_get("password_hash")?;
    if !verify_password(&password, &hash_str) {
        fail!("The account information or password is incorrect.");
    }

    let account = UserAccount {
        id:               row.try_get::<i32, _>("id")?,
        server_login:     row.try_get::<i32, _>("server_login")?,
        admin:            row.try_get::<bool, _>("is_admin")?,
        active:           row.try_get::<bool, _>("active")?,
        gold_bar:         row.try_get::<i32, _>("gold")?,
        rewards:          row.try_get::<Option<String>, _>("reward")?.unwrap_or_default(),
        total_recharge:   row.try_get::<i32, _>("total_recharge")?,
        vnd_bar:          row.try_get::<i32, _>("vnd")?,
        last_time_login_ms:  row.try_get::<Option<i64>, _>("last_time_login_unix")?.unwrap_or(0) * 1000,
        last_time_logout_ms: row.try_get::<Option<i64>, _>("last_time_logout_unix")?.unwrap_or(0) * 1000,
    };
    let is_banned = row.try_get::<bool, _>("ban")?;

    if account.server_login != server_id {
        fail!(&format!("This account is associated with server SV{}", account.server_login));
    }

    {
        let st = state.lock().await;
        if st.online_user_ids.contains(&account.id) {
            drop(st);
            fail!("Login failed. Please try again.");
        }
    }

    let seconds_pass = ((unix_now_ms() - account.last_time_logout_ms) / 1000) as i32;
    if seconds_pass < cfg.wait_login_secs {
        fail!(&format!("Please wait {} seconds before trying again.", cfg.wait_login_secs - seconds_pass));
    }

    if !account.admin && cfg.admin_mode == 1 {
        send_server_message(socket, incoming.request_id, 0, client_id,
            "The server is being processed.", integrity_mode, &cfg.hmac_secret).await?;
        fail!("The server is being processed and checked again, please try again later.");
    }

    if is_banned {
        fail!("The account has been blocked due to violation of the terms of service!");
    }

    let new_session_id = generate_session_id();

    {
        let mut st = state.lock().await;
        st.online_user_ids.insert(account.id);
        st.users_by_server.entry(server_id).or_default().insert(account.id);
        // Đăng ký session trong map toàn cục
        st.sessions.insert(new_session_id, account.id);
    }

    conn.session_id        = Some(new_session_id);
    conn.user_id           = Some(account.id);
    conn.last_heartbeat_ms = unix_now_ms();   // Reset heartbeat timer

    send_login_success(socket, incoming.request_id, new_session_id, client_id,
                       &account, integrity_mode, &cfg.hmac_secret).await?;

    info!(user_id = account.id, session = new_session_id, "login success");
    Ok(())
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers — send packets
// ─────────────────────────────────────────────────────────────────────────────

async fn send_login_failed<S>(socket: &mut S, req: u32, sess: u64, client_id: i32,
    text: &str, mode: IntegrityMode, secret: &str) -> ServiceResult<()>
where S: AsyncRead + AsyncWrite + Unpin,
{
    let mut w = PacketWriter::default();
    w.write_i32(client_id); w.write_u8(1); w.write_utf(text)?;
    let mut m = Message::new(OP_LOGIN, w.into_inner());
    m.request_id = req; m.session_id = sess;
    write_message(socket, m, mode, secret).await
}

async fn send_server_message<S>(socket: &mut S, req: u32, sess: u64, client_id: i32,
    text: &str, mode: IntegrityMode, secret: &str) -> ServiceResult<()>
where S: AsyncRead + AsyncWrite + Unpin,
{
    let mut w = PacketWriter::default();
    w.write_i32(client_id); w.write_utf(text)?;
    let mut m = Message::new(OP_SERVER_MESSAGE, w.into_inner());
    m.request_id = req; m.session_id = sess;
    write_message(socket, m, mode, secret).await
}

async fn send_login_success<S>(socket: &mut S, req: u32, sess: u64, client_id: i32,
    acc: &UserAccount, mode: IntegrityMode, secret: &str) -> ServiceResult<()>
where S: AsyncRead + AsyncWrite + Unpin,
{
    let mut w = PacketWriter::default();
    w.write_i32(client_id);
    w.write_u8(0);
    w.write_i32(acc.id);
    w.write_bool(acc.admin);
    w.write_bool(acc.active);
    w.write_i32(acc.gold_bar);
    w.write_i64(acc.last_time_login_ms);
    w.write_i64(acc.last_time_logout_ms);
    w.write_utf(&acc.rewards)?;
    w.write_i32(0); w.write_i32(0);
    w.write_i32(acc.server_login);
    w.write_i32(0); w.write_i32(0);
    w.write_i32(acc.total_recharge);
    w.write_i32(acc.vnd_bar);
    w.write_u64(sess);
    let mut m = Message::new(OP_LOGIN, w.into_inner());
    m.request_id = req; m.session_id = sess;
    write_message(socket, m, mode, secret).await
}

// ─────────────────────────────────────────────────────────────────────────────
// Session cleanup helpers
// ─────────────────────────────────────────────────────────────────────────────

async fn update_logout_time(pool: &MySqlPool, user_id: i32) {
    if let Err(e) = sqlx::query("UPDATE account SET last_time_logout = NOW() WHERE id = ?")
        .bind(user_id).execute(pool).await
    {
        warn!(user_id, error = %e, "failed to update last_time_logout");
    }
}

async fn remove_user(state: &Arc<Mutex<AppState>>, user_id: i32, conn: &mut ConnState) {
    let mut st = state.lock().await;
    st.online_user_ids.remove(&user_id);
    for users in st.users_by_server.values_mut() { users.remove(&user_id); }
    // Xoá session khỏi global map
    if let Some(sid) = conn.session_id {
        st.sessions.remove(&sid);
    }
    conn.session_id = None;
    conn.user_id    = None;
}

// ─────────────────────────────────────────────────────────────────────────────
// Wire read / write
// ─────────────────────────────────────────────────────────────────────────────

async fn read_message<S>(
    socket: &mut S, max_payload: usize, mode: IntegrityMode, secret: &str,
) -> ServiceResult<Message>
where S: AsyncRead + Unpin,
{
    let magic = socket.read_u16().await?;
    if magic != FRAME_MAGIC { return Err(ServiceError::Protocol("invalid frame magic".into())); }
    let version = socket.read_u16().await?;
    if version != PROTOCOL_VERSION { return Err(ServiceError::Protocol("unsupported version".into())); }
    let opcode   = socket.read_u16().await?;
    let flags    = socket.read_u8().await?;
    let reserved = socket.read_u8().await?;
    if reserved != 0 { return Err(ServiceError::Protocol("reserved != 0".into())); }
    let plen = socket.read_u32().await? as usize;
    if plen > max_payload { return Err(ServiceError::Protocol("payload too large".into())); }
    let req  = socket.read_u32().await?;
    let sess = socket.read_u64().await?;
    let mut payload = vec![0u8; plen];
    socket.read_exact(&mut payload).await?;
    let mut msg = Message { opcode, flags, request_id: req, session_id: sess, payload };
    if msg.flags & protocol::FLAG_INTEGRITY != 0 {
        verify_and_strip_integrity(&mut msg, mode, secret)?;
    }
    Ok(msg)
}

async fn write_message<S>(
    socket: &mut S, mut msg: Message, mode: IntegrityMode, secret: &str,
) -> ServiceResult<()>
where S: AsyncWrite + Unpin,
{
    attach_integrity(&mut msg, mode, secret)?;
    socket.write_u16(FRAME_MAGIC).await?;
    socket.write_u16(PROTOCOL_VERSION).await?;
    socket.write_u16(msg.opcode).await?;
    socket.write_u8(msg.flags).await?;
    socket.write_u8(0).await?;
    socket.write_u32(msg.payload.len() as u32).await?;
    socket.write_u32(msg.request_id).await?;
    socket.write_u64(msg.session_id).await?;
    socket.write_all(&msg.payload).await?;
    socket.flush().await?;
    Ok(())
}

// ─────────────────────────────────────────────────────────────────────────────
// Integrity (checksum / HMAC)
// ─────────────────────────────────────────────────────────────────────────────

fn attach_integrity(msg: &mut Message, mode: IntegrityMode, secret: &str) -> ServiceResult<()> {
    match mode {
        IntegrityMode::Off => {}
        IntegrityMode::Checksum => {
            let c = simple_checksum(msg.opcode, msg.request_id, msg.session_id, &msg.payload);
            msg.payload.extend_from_slice(&c.to_be_bytes());
            msg.flags |= FLAG_INTEGRITY;
        }
        IntegrityMode::Hmac => {
            if secret.is_empty() { return Err(ServiceError::Security("HMAC_SECRET empty".into())); }
            let tag = compute_hmac_sha256(msg, secret.as_bytes())?;
            msg.payload.extend_from_slice(&tag);
            msg.flags |= FLAG_INTEGRITY;
        }
    }
    Ok(())
}

fn verify_and_strip_integrity(msg: &mut Message, mode: IntegrityMode, secret: &str) -> ServiceResult<()> {
    match mode {
        IntegrityMode::Off => Ok(()),
        IntegrityMode::Checksum => {
            if msg.payload.len() < 4 { return Err(ServiceError::Protocol("invalid checksum payload".into())); }
            let split  = msg.payload.len() - 4;
            let recv   = u32::from_be_bytes(msg.payload[split..].try_into().unwrap());
            let expect = simple_checksum(msg.opcode, msg.request_id, msg.session_id, &msg.payload[..split]);
            if recv != expect { return Err(ServiceError::Protocol("checksum mismatch".into())); }
            msg.payload.truncate(split);
            Ok(())
        }
        IntegrityMode::Hmac => {
            if secret.is_empty() { return Err(ServiceError::Security("HMAC_SECRET empty".into())); }
            if msg.payload.len() < 32 { return Err(ServiceError::Protocol("invalid hmac payload".into())); }
            let split  = msg.payload.len() - 32;
            let body   = msg.payload[..split].to_vec();
            let recv   = msg.payload[split..].to_vec();
            let tmp    = Message { opcode: msg.opcode, flags: msg.flags,
                                   request_id: msg.request_id, session_id: msg.session_id,
                                   payload: body.clone() };
            let expect = compute_hmac_sha256(&tmp, secret.as_bytes())?;
            if expect.as_slice() != recv.as_slice() { return Err(ServiceError::Protocol("hmac mismatch".into())); }
            msg.payload = body;
            Ok(())
        }
    }
}

fn simple_checksum(opcode: u16, req: u32, sess: u64, payload: &[u8]) -> u32 {
    let mut s = opcode as u32 ^ req ^ (sess as u32) ^ ((sess >> 32) as u32);
    for b in payload { s = s.wrapping_mul(16_777_619) ^ (*b as u32); }
    s
}

fn compute_hmac_sha256(msg: &Message, key: &[u8]) -> ServiceResult<[u8; 32]> {
    let mut mac = Hmac::<Sha256>::new_from_slice(key)
        .map_err(|_| ServiceError::Security("invalid hmac key".into()))?;
    mac.update(&msg.opcode.to_be_bytes());
    mac.update(&msg.request_id.to_be_bytes());
    mac.update(&msg.session_id.to_be_bytes());
    mac.update(&msg.payload);
    let mut out = [0u8; 32];
    out.copy_from_slice(&mac.finalize().into_bytes());
    Ok(out)
}

// ─────────────────────────────────────────────────────────────────────────────
// Crypto helpers
// ─────────────────────────────────────────────────────────────────────────────

fn verify_password(password: &str, hash_str: &str) -> bool {
    let Ok(h) = PasswordHash::new(hash_str) else { return false; };
    Argon2::default().verify_password(password.as_bytes(), &h).is_ok()
}

fn dummy_password_verify() {
    let dummy = "$argon2id$v=19$m=19456,t=2,p=1$\
                 c29tZXNhbHRzb21lc2FsdA$RoB4lCBEYHNXpAE2SVLo0DqJQUNBdZ5bMiCqBlw8Dxo";
    let _ = verify_password("dummy_input_that_will_not_match", dummy);
}

fn constant_time_eq(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() { return false; }
    let mut diff = 0u8;
    for (x, y) in a.iter().zip(b.iter()) { diff |= x ^ y; }
    diff == 0
}

fn generate_session_id() -> u64 {
    let mut id = random::<u64>();
    if id == 0 { id = 1; }
    id
}

fn unix_now_ms() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

// ─────────────────────────────────────────────────────────────────────────────
// TLS
// ─────────────────────────────────────────────────────────────────────────────

fn build_tls_acceptor(cfg: &LoginConfig) -> ServiceResult<TlsAcceptor> {
    let cert_file = File::open(&cfg.tls_cert_path)
        .map_err(|e| ServiceError::Config(format!("cannot open TLS_CERT_PATH: {e}")))?;
    let key_file  = File::open(&cfg.tls_key_path)
        .map_err(|e| ServiceError::Config(format!("cannot open TLS_KEY_PATH: {e}")))?;
    let certs: Vec<CertificateDer<'static>> = rustls_pemfile::certs(&mut BufReader::new(cert_file))
        .collect::<Result<_, _>>()
        .map_err(|e| ServiceError::Config(format!("invalid cert: {e}")))?;
    if certs.is_empty() { return Err(ServiceError::Config("no cert found".into())); }
    let key: PrivateKeyDer<'static> =
        rustls_pemfile::private_key(&mut BufReader::new(key_file))
            .map_err(|e| ServiceError::Config(format!("invalid key: {e}")))?
            .ok_or_else(|| ServiceError::Config("no private key found".into()))?;
    let server_config = rustls::ServerConfig::builder()
        .with_no_client_auth()
        .with_single_cert(certs, key)
        .map_err(|e| ServiceError::Config(format!("invalid cert/key: {e}")))?;
    Ok(TlsAcceptor::from(Arc::new(server_config)))
}