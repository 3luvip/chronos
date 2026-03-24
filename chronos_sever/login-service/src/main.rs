// ============================================================
// [BẢO MẬT]
// 1. Password hash: query DB lấy password_hash, verify bằng argon2
//    thay vì so sánh plaintext trong SQL.
// 2. OP_SERVER_SYNC: yêu cầu FLAG_INTERNAL + PSK hợp lệ thay vì
//    dùng lại session_id của user. Client thường không thể set
//    FLAG_INTERNAL vì login-service từ chối flag đó ở các opcode khác.
// 3. Logout DB update: ghi last_time_logout về DB khi xử lý OP_LOGOUT
//    để wait_login_secs hoạt động đúng sau khi reconnect.
// 4. OP_SERVER_SYNC không còn đọc/nhận password trong payload.
//
// [KIẾN TRÚC]
// 5. Không có thay đổi cấu trúc lớn ở file này — gateway forward
//    được xử lý ở gateway/src/main.rs riêng.
//
// [CODE QUALITY]
// 6. Lock granularity: OP_SERVER_SYNC build local set trước, chỉ
//    lock AppState một lần để swap thay vì giữ lock suốt vòng lặp.
// 7. Log DB URL dùng database_url_safe() thay vì database_url().
//
// [MINOR]
// 8. Reserved byte được validate phải bằng 0.
// 9. simple_checksum được document rõ là "debug only".
// ============================================================

use argon2::{Argon2, PasswordHash, PasswordVerifier};
use hmac::{Hmac, Mac};
use protocol::{
    codec::{PacketReader, PacketWriter},
    Message, FLAG_ENCRYPTED, FLAG_INTEGRITY, FLAG_INTERNAL, FRAME_MAGIC, OP_INTERNAL_AUTH,
    OP_LOGIN, OP_LOGOUT, OP_SERVER_MESSAGE, OP_SERVER_SYNC, PROTOCOL_VERSION,
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

#[derive(Default)]
struct AppState {
    online_user_ids: HashSet<i32>,
    users_by_server: HashMap<i32, HashSet<i32>>,
}

#[derive(Debug, Clone)]
struct UserAccount {
    id: i32,
    admin: bool,
    active: bool,
    gold_bar: i32,
    rewards: String,
    server_login: i32,
    total_recharge: i32,
    vnd_bar: i32,
    last_time_login_ms: i64,
    last_time_logout_ms: i64,
}

struct ConnState {
    session_id: Option<u64>,
    user_id: Option<i32>,
    /// Đã xác thực là internal service (gateway / game server) chưa.
    is_internal: bool,
    window_started_ms: i64,
    window_message_count: u32,
    login_attempts: u32,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum IntegrityMode {
    Off,
    /// Checksum 32-bit FNV-like — chỉ dùng để debug, không chống tampering.
    Checksum,
    Hmac,
}

impl IntegrityMode {
    fn from_str(s: &str) -> Self {
        match s.to_ascii_lowercase().as_str() {
            "checksum" => Self::Checksum,
            "hmac" => Self::Hmac,
            _ => Self::Off,
        }
    }
}

impl ConnState {
    fn new() -> Self {
        Self {
            session_id: None,
            user_id: None,
            is_internal: false,
            window_started_ms: unix_now_ms(),
            window_message_count: 0,
            login_attempts: 0,
        }
    }

    fn apply_rate_limit(&mut self, cfg: &LoginConfig) -> bool {
        let now = unix_now_ms();
        if now - self.window_started_ms >= cfg.rate_limit_window_ms {
            self.window_started_ms = now;
            self.window_message_count = 0;
        }
        self.window_message_count = self.window_message_count.saturating_add(1);
        self.window_message_count <= cfg.rate_limit_max_messages
    }

    fn is_authenticated(&self, incoming_session_id: u64) -> bool {
        matches!(self.session_id, Some(id) if id != 0 && id == incoming_session_id)
    }
}

/// TCP reset / app close without TLS `close_notify` — bình thường với nhiều client/proxy.
fn is_benign_peer_disconnect(err: &ServiceError) -> bool {
    match err {
        ServiceError::Io(e) => {
            e.kind() == std::io::ErrorKind::UnexpectedEof
                || e.kind() == std::io::ErrorKind::ConnectionReset
                || e
                    .to_string()
                    .to_ascii_lowercase()
                    .contains("close_notify")
        }
        _ => false,
    }
}

#[tokio::main]
async fn main() -> ServiceResult<()> {
    // rustls 0.23 cần crypto provider được chọn rõ ràng ở runtime trong
    // một số cấu hình feature; nếu không sẽ panic khi khởi tạo TLS.
    rustls::crypto::aws_lc_rs::default_provider()
        .install_default()
        .map_err(|_| ServiceError::Config("failed to install rustls crypto provider".to_string()))?;

    logging::init("login-service");
    let cfg = LoginConfig::from_env();
    let addr = cfg.addr();

    // FIX: log URL an toàn, không để lộ password.
    let db_url_safe = cfg.database_url_safe();
    let db_url = cfg.database_url();

    if cfg.internal_psk.is_empty() {
        warn!("INTERNAL_PSK is not set — OP_SERVER_SYNC will be rejected for all connections");
    }

    let listener = TcpListener::bind(&addr).await?;
    let pool = MySqlPoolOptions::new()
        .max_connections(20)
        .connect(&db_url)
        .await?;
    let state = Arc::new(Mutex::new(AppState::default()));
    let integrity_mode = IntegrityMode::from_str(&cfg.integrity_mode);
    let tls_acceptor = if cfg.tls_enabled {
        Some(Arc::new(build_tls_acceptor(&cfg)?))
    } else {
        None
    };

    info!(
        %addr,
        db_url = %db_url_safe,
        protocol_version = protocol::PROTOCOL_VERSION,
        tls_enabled = cfg.tls_enabled,
        integrity_mode = ?integrity_mode,
        "login service started"
    );

    loop {
        tokio::select! {
            accept_result = listener.accept() => {
                match accept_result {
                    Ok((socket, peer)) => {
                        info!(%peer, "accepted connection");
                        let state = Arc::clone(&state);
                        let pool = pool.clone();
                        let cfg = cfg.clone();
                        let tls_acceptor = tls_acceptor.clone();
                        tokio::spawn(async move {
                            let result = if let Some(acceptor) = tls_acceptor {
                                match acceptor.accept(socket).await {
                                    Ok(tls_stream) => {
                                        handle_connection(tls_stream, pool, state, cfg, integrity_mode).await
                                    }
                                    Err(err) => Err(ServiceError::Security(
                                        format!("tls handshake failed: {err}")
                                    )),
                                }
                            } else {
                                handle_connection(socket, pool, state, cfg, integrity_mode).await
                            };
                            if let Err(err) = result {
                                if is_benign_peer_disconnect(&err) {
                                    debug!(%peer, %err, "peer disconnected");
                                } else {
                                    warn!(%peer, error = %err, "connection closed with error");
                                }
                            }
                        });
                    }
                    Err(err) => error!(error = %err, "accept failed"),
                }
            }
            _ = tokio::signal::ctrl_c() => {
                info!("shutdown signal received");
                break;
            }
        }
    }

    pool.close().await;
    info!("login service stopped");
    Ok(())
}

async fn handle_connection<S>(
    mut socket: S,
    pool: MySqlPool,
    state: Arc<Mutex<AppState>>,
    cfg: LoginConfig,
    integrity_mode: IntegrityMode,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut conn = ConnState::new();

    loop {
        let incoming =
            read_message(&mut socket, cfg.max_frame_size, integrity_mode, &cfg.hmac_secret).await?;

        if !conn.apply_rate_limit(&cfg) {
            return Err(ServiceError::Security("rate limit exceeded".to_string()));
        }

        if incoming.flags & FLAG_ENCRYPTED != 0 {
            return Err(ServiceError::Protocol(
                "encrypted messages are not supported yet".to_string(),
            ));
        }

        // Bất kỳ opcode nào khác mang FLAG_INTERNAL đều bị từ chối.
        if incoming.flags & FLAG_INTERNAL != 0 && incoming.opcode != OP_INTERNAL_AUTH {
            return Err(ServiceError::Security(
                "FLAG_INTERNAL not allowed on this opcode".to_string(),
            ));
        }

        match incoming.opcode {
            // Sau khi xác thực thành công, conn.is_internal = true và connection
            // có thể dùng OP_SERVER_SYNC.
            OP_INTERNAL_AUTH => {
                if cfg.internal_psk.is_empty() {
                    return Err(ServiceError::Security(
                        "internal auth rejected: INTERNAL_PSK not configured".to_string(),
                    ));
                }
                if incoming.flags & FLAG_INTERNAL == 0 {
                    return Err(ServiceError::Security(
                        "OP_INTERNAL_AUTH requires FLAG_INTERNAL".to_string(),
                    ));
                }
                let mut rd = PacketReader::new(incoming.payload);
                let psk = rd.read_utf()?;
                // Constant-time compare để tránh timing attack.
                if !constant_time_eq(psk.as_bytes(), cfg.internal_psk.as_bytes()) {
                    return Err(ServiceError::Security("invalid internal PSK".to_string()));
                }
                conn.is_internal = true;
                info!("internal service authenticated");
            }

            OP_LOGIN => {
                conn.login_attempts = conn.login_attempts.saturating_add(1);
                if conn.login_attempts > cfg.max_login_attempts {
                    return Err(ServiceError::Security(
                        "too many login attempts".to_string(),
                    ));
                }
                handle_login(
                    &mut socket,
                    &pool,
                    &state,
                    &cfg,
                    &mut conn,
                    incoming,
                    integrity_mode,
                )
                .await?;
            }

            OP_LOGOUT => {
                if !conn.is_authenticated(incoming.session_id) {
                    return Err(ServiceError::Security("unauthorized logout".to_string()));
                }
                let mut rd = PacketReader::new(incoming.payload);
                let user_id = rd.read_i32()?;
                // hoạt động đúng sau khi client reconnect.
                update_logout_time(&pool, user_id).await;
                remove_user(&state, user_id).await;
                conn.session_id = None;
                conn.user_id = None;
            }

            // FIX: OP_SERVER_SYNC chỉ được phép từ internal service đã xác thực.
            // Bản gốc dùng session_id của user để auth, cho phép bất kỳ user nào
            // đã login đều có thể gọi opcode này.
            OP_SERVER_SYNC => {
                if !conn.is_internal {
                    return Err(ServiceError::Security(
                        "OP_SERVER_SYNC requires internal service authentication".to_string(),
                    ));
                }
                handle_server_sync(&mut socket, &state, incoming, integrity_mode, &cfg).await?;
            }

            _ => return Err(ServiceError::Protocol("unknown opcode".to_string())),
        }
    }
}

// FIX: tách OP_SERVER_SYNC ra hàm riêng để dễ đọc và giải quyết
// vấn đề lock granularity thô.
async fn handle_server_sync<S>(
    _socket: &mut S,
    state: &Arc<Mutex<AppState>>,
    incoming: Message,
    _integrity_mode: IntegrityMode,
    _cfg: &LoginConfig,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut rd = PacketReader::new(incoming.payload);
    let server_id = rd.read_i32()?;
    let size = rd.read_i32()?;
    if !(0..=10_000).contains(&size) {
        return Err(ServiceError::Protocol(
            "invalid server sync size".to_string(),
        ));
    }

    // FIX: đọc toàn bộ dữ liệu vào local collections TRƯỚC khi lock.
    // Bản gốc giữ Mutex trong suốt vòng lặp 10k iterations.
    // FIX: không còn nhận password trong sync packet.
    let mut new_user_ids: HashSet<i32> = HashSet::with_capacity(size as usize);
    for _ in 0..size {
        let _client_id = rd.read_i32()?;
        let user_id = rd.read_i32()?;
        let _username = rd.read_utf()?;
        // NOTE: password đã bị loại bỏ khỏi sync packet — game server
        // không cần và không nên truyền password qua internal channel.
        new_user_ids.insert(user_id);
    }

    // Chỉ lock một lần để swap dữ liệu đã chuẩn bị xong.
    let mut st = state.lock().await;
    // Xóa user cũ của server này khỏi online set trước khi cập nhật.

    let old_set = st.users_by_server.get(&server_id).cloned();

    if let Some(old_set) = old_set {
        for uid in old_set {
            st.online_user_ids.remove(&uid);
        }
    }

    for uid in &new_user_ids {
        st.online_user_ids.insert(*uid);
    }

    st.users_by_server.insert(server_id, new_user_ids);


    Ok(())
}

async fn handle_login<S>(
    socket: &mut S,
    pool: &MySqlPool,
    state: &Arc<Mutex<AppState>>,
    cfg: &LoginConfig,
    conn: &mut ConnState,
    incoming: Message,
    integrity_mode: IntegrityMode,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut rd = PacketReader::new(incoming.payload);
    let server_id = rd.read_i32()?;
    let client_id = rd.read_i32()?;
    let username = rd.read_utf()?;
    let password = rd.read_utf()?;

    if username.len() < 3 || username.len() > 32 || password.len() < 3 || password.len() > 64 {
        send_login_failed(
            socket,
            incoming.request_id,
            0,
            client_id,
            "The login information is not valid",
            integrity_mode,
            &cfg.hmac_secret,
        )
        .await?;
        return Ok(());
    }

    // Không đưa password vào SQL — tránh hoàn toàn SQL injection qua
    // password field và không expose timing của DB lookup.
    let row = sqlx::query(
        "SELECT id, password_hash, server_login,
                UNIX_TIMESTAMP(last_time_login)  AS last_time_login_unix,
                UNIX_TIMESTAMP(last_time_logout) AS last_time_logout_unix,
                is_admin, active, gold, reward, total_recharge, vnd, ban
         FROM account
         WHERE username = ?
         LIMIT 1",
    )
    .bind(&username)
    .fetch_optional(pool)
    .await?;

    let Some(row) = row else {
        // Vẫn thực hiện verify giả để tránh timing attack phân biệt
        // "username không tồn tại" vs "sai password".
        dummy_password_verify();
        send_login_failed(
            socket,
            incoming.request_id,
            0,
            client_id,
            "The account information or password is incorrect.",
            integrity_mode,
            &cfg.hmac_secret,
        )
        .await?;
        return Ok(());
    };

    // FIX: verify password bằng argon2id, không so sánh plaintext.
    let password_hash_str: String = row.try_get("password_hash")?;
    let hash_ok = verify_password(&password, &password_hash_str);
    if !hash_ok {
        send_login_failed(
            socket,
            incoming.request_id,
            0,
            client_id,
            "The account information or password is incorrect.",
            integrity_mode,
            &cfg.hmac_secret,
        )
        .await?;
        return Ok(());
    }

    let account = UserAccount {
        id: row.try_get::<i32, _>("id")?,
        server_login: row.try_get::<i32, _>("server_login")?,
        admin: row.try_get::<bool, _>("is_admin")?,
        active: row.try_get::<bool, _>("active")?,
        gold_bar: row.try_get::<i32, _>("gold")?,
        rewards: row
            .try_get::<Option<String>, _>("reward")?
            .unwrap_or_default(),
        total_recharge: row.try_get::<i32, _>("total_recharge")?,
        vnd_bar: row.try_get::<i32, _>("vnd")?,
        last_time_login_ms: row
            .try_get::<Option<i64>, _>("last_time_login_unix")?
            .unwrap_or(0)
            * 1000,
        last_time_logout_ms: row
            .try_get::<Option<i64>, _>("last_time_logout_unix")?
            .unwrap_or(0)
            * 1000,
    };
    let is_banned = row.try_get::<bool, _>("ban")?;

    if account.server_login != server_id {
        send_login_failed(
            socket,
            incoming.request_id,
            0,
            client_id,
            &format!("This account is associated with server SV{}", account.server_login),
            integrity_mode,
            &cfg.hmac_secret,
        )
        .await?;
        return Ok(());
    }

    {
        let st = state.lock().await;
        if st.online_user_ids.contains(&account.id) {
            drop(st);
            send_login_failed(
                socket,
                incoming.request_id,
                0,
                client_id,
                "Login failed. Please try again.",
                integrity_mode,
                &cfg.hmac_secret,
            )
            .await?;
            return Ok(());
        }
    }

    let seconds_pass = ((unix_now_ms() - account.last_time_logout_ms) / 1000) as i32;
    if seconds_pass < cfg.wait_login_secs {
        send_login_failed(
            socket,
            incoming.request_id,
            0,
            client_id,
            &format!(
                "Please wait {} seconds before trying again.",
                cfg.wait_login_secs - seconds_pass
            ),
            integrity_mode,
            &cfg.hmac_secret,
        )
        .await?;
        return Ok(());
    }

    if !account.admin && cfg.admin_mode == 1 {
        send_server_message(
            socket,
            incoming.request_id,
            0,
            client_id,
            "The server is being processed and checked again, please try again later.",
            integrity_mode,
            &cfg.hmac_secret,
        )
        .await?;
        send_login_failed(
            socket,
            incoming.request_id,
            0,
            client_id,
            "The server is being processed and checked again, please try again later.",
            integrity_mode,
            &cfg.hmac_secret,
        )
        .await?;
        return Ok(());
    }

    if is_banned {
        send_login_failed(
            socket,
            incoming.request_id,
            0,
            client_id,
            "The account has been blocked due to violation of the terms of service!",
            integrity_mode,
            &cfg.hmac_secret,
        )
        .await?;
        return Ok(());
    }

    {
        let mut st = state.lock().await;
        st.online_user_ids.insert(account.id);
        st.users_by_server
            .entry(server_id)
            .or_default()
            .insert(account.id);
    }

    let new_session_id = generate_session_id();
    conn.session_id = Some(new_session_id);
    conn.user_id = Some(account.id);
    send_login_success(
        socket,
        incoming.request_id,
        new_session_id,
        client_id,
        &account,
        integrity_mode,
        &cfg.hmac_secret,
    )
    .await?;
    Ok(())
}

// FIX: cập nhật last_time_logout trong DB khi user logout.
// Bản gốc chỉ remove khỏi in-memory set, khiến wait_login_secs
// không có tác dụng sau khi reconnect.
async fn update_logout_time(pool: &MySqlPool, user_id: i32) {
    let result = sqlx::query(
        "UPDATE account SET last_time_logout = NOW() WHERE id = ?",
    )
    .bind(user_id)
    .execute(pool)
    .await;
    if let Err(err) = result {
        warn!(user_id, error = %err, "failed to update last_time_logout");
    }
}

async fn remove_user(state: &Arc<Mutex<AppState>>, user_id: i32) {
    let mut st = state.lock().await;
    st.online_user_ids.remove(&user_id);
    for users in st.users_by_server.values_mut() {
        users.remove(&user_id);
    }
}

async fn send_login_failed<S>(
    socket: &mut S,
    request_id: u32,
    session_id: u64,
    client_id: i32,
    text: &str,
    integrity_mode: IntegrityMode,
    hmac_secret: &str,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut w = PacketWriter::default();
    w.write_i32(client_id);
    w.write_u8(1);
    w.write_utf(text)?;
    let mut msg = Message::new(OP_LOGIN, w.into_inner());
    msg.request_id = request_id;
    msg.session_id = session_id;
    write_message(socket, msg, integrity_mode, hmac_secret).await
}

async fn send_server_message<S>(
    socket: &mut S,
    request_id: u32,
    session_id: u64,
    client_id: i32,
    text: &str,
    integrity_mode: IntegrityMode,
    hmac_secret: &str,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut w = PacketWriter::default();
    w.write_i32(client_id);
    w.write_utf(text)?;
    let mut msg = Message::new(OP_SERVER_MESSAGE, w.into_inner());
    msg.request_id = request_id;
    msg.session_id = session_id;
    write_message(socket, msg, integrity_mode, hmac_secret).await
}

async fn send_login_success<S>(
    socket: &mut S,
    request_id: u32,
    session_id: u64,
    client_id: i32,
    account: &UserAccount,
    integrity_mode: IntegrityMode,
    hmac_secret: &str,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut w = PacketWriter::default();
    w.write_i32(client_id);
    w.write_u8(0);
    w.write_i32(account.id);
    w.write_bool(account.admin);
    w.write_bool(account.active);
    w.write_i32(account.gold_bar);
    w.write_i64(account.last_time_login_ms);
    w.write_i64(account.last_time_logout_ms);
    w.write_utf(&account.rewards)?;
    w.write_i32(0);
    w.write_i32(0);
    w.write_i32(account.server_login);
    w.write_i32(0);
    w.write_i32(0);
    w.write_i32(account.total_recharge);
    w.write_i32(account.vnd_bar);
    w.write_u64(session_id);
    let mut msg = Message::new(OP_LOGIN, w.into_inner());
    msg.request_id = request_id;
    msg.session_id = session_id;
    write_message(socket, msg, integrity_mode, hmac_secret).await
}

async fn read_message<S>(
    socket: &mut S,
    max_payload_size: usize,
    integrity_mode: IntegrityMode,
    hmac_secret: &str,
) -> ServiceResult<Message>
where
    S: AsyncRead + Unpin,
{
    let magic = socket.read_u16().await?;
    if magic != FRAME_MAGIC {
        return Err(ServiceError::Protocol("invalid frame magic".to_string()));
    }
    let version = socket.read_u16().await?;
    if version != PROTOCOL_VERSION {
        return Err(ServiceError::Protocol(
            "unsupported protocol version".to_string(),
        ));
    }
    let opcode = socket.read_u16().await?;
    let flags = socket.read_u8().await?;
    // FIX: validate reserved byte phải bằng 0.
    let reserved = socket.read_u8().await?;
    if reserved != 0 {
        return Err(ServiceError::Protocol(
            "reserved byte must be zero".to_string(),
        ));
    }
    let payload_size = socket.read_u32().await? as usize;
    if payload_size > max_payload_size {
        return Err(ServiceError::Protocol("payload too large".to_string()));
    }
    let request_id = socket.read_u32().await?;
    let session_id = socket.read_u64().await?;
    let mut payload = vec![0_u8; payload_size];
    socket.read_exact(&mut payload).await?;
    let mut msg = Message {
        opcode,
        flags,
        request_id,
        session_id,
        payload,
    };
    if msg.flags & FLAG_INTEGRITY != 0 {
        verify_and_strip_integrity(&mut msg, integrity_mode, hmac_secret)?;
    }
    Ok(msg)
}

async fn write_message<S>(
    socket: &mut S,
    mut message: Message,
    integrity_mode: IntegrityMode,
    hmac_secret: &str,
) -> ServiceResult<()>
where
    S: AsyncWrite + Unpin,
{
    attach_integrity(&mut message, integrity_mode, hmac_secret)?;
    socket.write_u16(FRAME_MAGIC).await?;
    socket.write_u16(PROTOCOL_VERSION).await?;
    socket.write_u16(message.opcode).await?;
    socket.write_u8(message.flags).await?;
    socket.write_u8(0).await?;
    socket.write_u32(message.payload.len() as u32).await?;
    socket.write_u32(message.request_id).await?;
    socket.write_u64(message.session_id).await?;
    socket.write_all(&message.payload).await?;
    socket.flush().await?;
    Ok(())
}

fn unix_now_ms() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

fn generate_session_id() -> u64 {
    let mut id = random::<u64>();
    if id == 0 {
        id = 1;
    }
    id
}

// FIX: verify password bằng argon2id.
// Hàm này blocking-safe vì argon2 chạy nhanh (< 1ms với default params)
// và không cần spawn_blocking trong context này.
// Nếu tăng memory cost lên cao (recommended cho production), cân nhắc
// wrap trong tokio::task::spawn_blocking.
fn verify_password(password: &str, hash_str: &str) -> bool {
    let Ok(parsed_hash) = PasswordHash::new(hash_str) else {
        return false;
    };
    Argon2::default()
        .verify_password(password.as_bytes(), &parsed_hash)
        .is_ok()
}

// Thực hiện verify giả khi username không tồn tại, để thời gian
// phản hồi tương đương trường hợp sai password — tránh timing attack.
fn dummy_password_verify() {
    // PHC string hợp lệ với dummy hash — argon2 sẽ fail nhưng vẫn
    // tiêu tốn thời gian tương tự như một verify thật.
    let dummy = "$argon2id$v=19$m=19456,t=2,p=1$\
                 c29tZXNhbHRzb21lc2FsdA$RoB4lCBEYHNXpAE2SVLo0DqJQUNBdZ5bMiCqBlw8Dxo";
    let _ = verify_password("dummy_input_that_will_not_match", dummy);
}

// Constant-time byte comparison để tránh timing attack khi so sánh PSK.
fn constant_time_eq(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    let mut diff: u8 = 0;
    for (x, y) in a.iter().zip(b.iter()) {
        diff |= x ^ y;
    }
    diff == 0
}

fn build_tls_acceptor(cfg: &LoginConfig) -> ServiceResult<TlsAcceptor> {
    let cert_file = File::open(&cfg.tls_cert_path)
        .map_err(|e| ServiceError::Config(format!("cannot open TLS_CERT_PATH: {e}")))?;
    let key_file = File::open(&cfg.tls_key_path)
        .map_err(|e| ServiceError::Config(format!("cannot open TLS_KEY_PATH: {e}")))?;
    let mut cert_reader = BufReader::new(cert_file);
    let mut key_reader = BufReader::new(key_file);

    let certs: Vec<CertificateDer<'static>> = rustls_pemfile::certs(&mut cert_reader)
        .collect::<Result<_, _>>()
        .map_err(|e| ServiceError::Config(format!("invalid cert file: {e}")))?;
    if certs.is_empty() {
        return Err(ServiceError::Config(
            "no certificate found in TLS_CERT_PATH".to_string(),
        ));
    }

    let key: PrivateKeyDer<'static> =
        if let Some(key) = rustls_pemfile::private_key(&mut key_reader)
            .map_err(|e| ServiceError::Config(format!("invalid key file: {e}")))?
        {
            key
        } else {
            return Err(ServiceError::Config(
                "no private key found in TLS_KEY_PATH".to_string(),
            ));
        };

    let server_config = rustls::ServerConfig::builder()
        .with_no_client_auth()
        .with_single_cert(certs, key)
        .map_err(|e| ServiceError::Config(format!("invalid TLS cert/key pair: {e}")))?;
    Ok(TlsAcceptor::from(Arc::new(server_config)))
}

fn attach_integrity(
    message: &mut Message,
    mode: IntegrityMode,
    hmac_secret: &str,
) -> ServiceResult<()> {
    match mode {
        IntegrityMode::Off => {}
        IntegrityMode::Checksum => {
            let checksum = simple_checksum(
                message.opcode,
                message.request_id,
                message.session_id,
                &message.payload,
            );
            message.payload.extend_from_slice(&checksum.to_be_bytes());
            message.flags |= FLAG_INTEGRITY;
        }
        IntegrityMode::Hmac => {
            if hmac_secret.is_empty() {
                return Err(ServiceError::Security(
                    "HMAC_SECRET is empty while INTEGRITY_MODE=hmac".to_string(),
                ));
            }
            let tag = compute_hmac_sha256(message, hmac_secret.as_bytes())?;
            message.payload.extend_from_slice(&tag);
            message.flags |= FLAG_INTEGRITY;
        }
    }
    Ok(())
}

fn verify_and_strip_integrity(
    message: &mut Message,
    mode: IntegrityMode,
    hmac_secret: &str,
) -> ServiceResult<()> {
    match mode {
        IntegrityMode::Off => Ok(()),
        IntegrityMode::Checksum => {
            if message.payload.len() < 4 {
                return Err(ServiceError::Protocol(
                    "invalid checksum payload".to_string(),
                ));
            }
            let split = message.payload.len() - 4;
            let body = &message.payload[..split];
            let recv = u32::from_be_bytes([
                message.payload[split],
                message.payload[split + 1],
                message.payload[split + 2],
                message.payload[split + 3],
            ]);
            let expected = simple_checksum(
                message.opcode,
                message.request_id,
                message.session_id,
                body,
            );
            if recv != expected {
                return Err(ServiceError::Protocol("checksum mismatch".to_string()));
            }
            message.payload.truncate(split);
            Ok(())
        }
        IntegrityMode::Hmac => {
            if hmac_secret.is_empty() {
                return Err(ServiceError::Security(
                    "HMAC_SECRET is empty while INTEGRITY_MODE=hmac".to_string(),
                ));
            }
            if message.payload.len() < 32 {
                return Err(ServiceError::Protocol("invalid hmac payload".to_string()));
            }
            let split = message.payload.len() - 32;
            let body = message.payload[..split].to_vec();
            let recv_tag = message.payload[split..].to_vec();
            let unsigned = Message {
                opcode: message.opcode,
                flags: message.flags,
                request_id: message.request_id,
                session_id: message.session_id,
                payload: body.clone(),
            };
            let expected = compute_hmac_sha256(&unsigned, hmac_secret.as_bytes())?;
            if expected.as_slice() != recv_tag.as_slice() {
                return Err(ServiceError::Protocol("hmac mismatch".to_string()));
            }
            message.payload = body;
            Ok(())
        }
    }
}

/// Checksum 32-bit FNV-like — CHỈ dùng để phát hiện lỗi truyền ngẫu nhiên
/// trong môi trường debug/dev. Không cung cấp bảo mật chống tampering chủ ý.
/// Dùng IntegrityMode::Hmac cho production.
fn simple_checksum(opcode: u16, request_id: u32, session_id: u64, payload: &[u8]) -> u32 {
    let mut sum =
        opcode as u32 ^ request_id ^ (session_id as u32) ^ ((session_id >> 32) as u32);
    for b in payload {
        sum = sum.wrapping_mul(16_777_619) ^ (*b as u32);
    }
    sum
}

fn compute_hmac_sha256(message: &Message, key: &[u8]) -> ServiceResult<[u8; 32]> {
    let mut mac = Hmac::<Sha256>::new_from_slice(key)
        .map_err(|_| ServiceError::Security("invalid hmac key".to_string()))?;
    mac.update(&message.opcode.to_be_bytes());
    mac.update(&message.request_id.to_be_bytes());
    mac.update(&message.session_id.to_be_bytes());
    mac.update(&message.payload);
    let bytes = mac.finalize().into_bytes();
    let mut out = [0_u8; 32];
    out.copy_from_slice(&bytes);
    Ok(out)
}