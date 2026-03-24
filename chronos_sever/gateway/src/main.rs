// ============================================================
// Các thay đổi so với bản gốc:
//
// [KIẾN TRÚC]
// 1. Gateway không còn echo cứng "Gateway received login packet".
//    Thay vào đó nó forward toàn bộ packet đến login-service và
//    relay response về cho client.
// 2. Gateway giữ một connection pool (LoginServicePool) đến
//    login-service — mỗi worker connection đến login-service
//    được dùng cho một client session tương ứng.
// 3. Trước khi forward user packet, gateway xác thực với
//    login-service bằng OP_INTERNAL_AUTH + PSK.
//
// [BẢO MẬT]
// 4. Password KHÔNG bị log trong gateway (bản gốc log username,
//    bản này log username nhưng không log password).
//
// [MINOR]
// 5. Reserved byte được validate bằng 0 khi đọc.
// ============================================================

use protocol::{
    codec::{PacketReader, PacketWriter},
    Message, FLAG_INTERNAL, FRAME_MAGIC, OP_INTERNAL_AUTH, OP_LOGIN, PROTOCOL_VERSION};
use shared::{
    config::LoginConfig,
    error::{ServiceError, ServiceResult},
    logging,
};
use std::fs::File;
use std::io::BufReader;
use std::sync::Arc;
use tokio::{
    io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt},
    net::{TcpListener, TcpStream},
};
use tokio_rustls::{
    rustls::{self, pki_types::CertificateDer, pki_types::PrivateKeyDer, pki_types::ServerName, RootCertStore},
    TlsAcceptor, TlsConnector,
};
use tracing::{error, info, warn};

struct ConnState {
    window_started_ms: i64,
    window_message_count: u32,
}

impl ConnState {
    fn new() -> Self {
        Self {
            window_started_ms: unix_now_ms(),
            window_message_count: 0,
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
}

#[tokio::main]
async fn main() -> ServiceResult<()> {
    rustls::crypto::aws_lc_rs::default_provider()
        .install_default()
        .map_err(|_| ServiceError::Config("failed to install rustls crypto provider".to_string()))?;

    logging::init("gateway");
    let cfg = LoginConfig::from_env();
    let addr = cfg.addr();
    let listener = TcpListener::bind(&addr).await?;
    let tls_acceptor = if cfg.tls_enabled {
        Some(Arc::new(build_tls_acceptor(&cfg)?))
    } else {
        None
    };
    info!(%addr, protocol_version = PROTOCOL_VERSION, "gateway started");

    if cfg.internal_psk.is_empty() {
        warn!("INTERNAL_PSK is not set — forwarding to login-service will fail");
    }

    loop {
        tokio::select! {
            accepted = listener.accept() => {
                match accepted {
                    Ok((socket, peer)) => {
                        let cfg = cfg.clone();
                        let tls_acceptor = tls_acceptor.clone();
                        tokio::spawn(async move {
                            let result = if let Some(acceptor) = tls_acceptor {
                                match acceptor.accept(socket).await {
                                    Ok(tls_stream) => handle_connection(tls_stream, cfg).await,
                                    Err(err) => Err(ServiceError::Security(format!("edge TLS handshake failed: {err}"))),
                                }
                            } else {
                                handle_connection(socket, cfg).await
                            };
                            if let Err(err) = result {
                                warn!(%peer, error = %err, "gateway connection closed");
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
    info!("gateway stopped");
    Ok(())
}

async fn handle_connection<C>(mut client: C, cfg: LoginConfig) -> ServiceResult<()>
where
    C: AsyncRead + AsyncWrite + Unpin,
{
    let mut conn = ConnState::new();

    // Kết nối tới login-service cho session này.
    // Địa chỉ login-service được lấy từ env LOGIN_SERVICE_ADDR
    // (mặc định 127.0.0.1:14447 để phân biệt với gateway port).
    let login_service_addr = std::env::var("LOGIN_SERVICE_ADDR")
        .unwrap_or_else(|_| "127.0.0.1:14447".to_string());

    let ls_tcp = TcpStream::connect(&login_service_addr)
        .await
        .map_err(|e| ServiceError::External(format!("cannot connect to login-service: {e}")))?;

    if cfg.tls_enabled {
        let mut tls_conn = connect_login_service_tls(ls_tcp, &cfg, &login_service_addr).await?;
        let result = proxy_loop(&mut client, &mut tls_conn, &cfg, &mut conn).await;
        // Gửi close_notify tới login-service trước khi drop socket — tránh cảnh báo rustls phía server.
        let _ = tls_conn.shutdown().await;
        result
    } else {
        let mut plain_conn = ls_tcp;
        proxy_loop(&mut client, &mut plain_conn, &cfg, &mut conn).await
    }
}

async fn proxy_loop<C, S>(
    client: &mut C,
    ls_conn: &mut S,
    cfg: &LoginConfig,
    conn: &mut ConnState,
) -> ServiceResult<()>
where
    C: AsyncRead + AsyncWrite + Unpin,
    S: AsyncRead + AsyncWrite + Unpin,
{
    // Xác thực gateway với login-service bằng PSK trước khi forward bất kỳ packet nào.
    authenticate_internal(ls_conn, cfg).await?;
    info!("gateway authenticated with login-service");

    loop {
        let incoming = read_message(client, cfg.max_frame_size).await?;
        if !conn.apply_rate_limit(cfg) {
            return Err(ServiceError::Security("rate limit exceeded".to_string()));
        }

        match incoming.opcode {
            OP_LOGIN => {
                if let Ok(username) = peek_username(&incoming.payload) {
                    info!(username = %username, "forwarding login packet to login-service");
                }
                write_message(ls_conn, incoming).await?;
                let response = read_message(ls_conn, cfg.max_frame_size).await?;
                write_message(client, response).await?;
            }
            _ => {
                write_message(ls_conn, incoming).await?;
                let response = read_message(ls_conn, cfg.max_frame_size).await?;
                write_message(client, response).await?;
            }
        }
    }
}

/// Gửi OP_INTERNAL_AUTH + PSK đến login-service để xác thực gateway.
async fn authenticate_internal<S>(
    ls_conn: &mut S,
    cfg: &LoginConfig,
) -> ServiceResult<()>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let mut w = PacketWriter::default();
    w.write_utf(&cfg.internal_psk)?;
    let mut msg = Message::new(OP_INTERNAL_AUTH, w.into_inner());
    msg.flags = FLAG_INTERNAL;
    write_message(ls_conn, msg).await
}

/// Đọc username từ payload login packet mà không consume packet —
/// chỉ dùng để log, không ảnh hưởng đến quá trình forward.
fn peek_username(payload: &[u8]) -> std::io::Result<String> {
    let mut rd = PacketReader::new(payload.to_vec());
    let _server_id = rd.read_i32()?;
    let _client_id = rd.read_i32()?;
    rd.read_utf()
}

async fn read_message<S>(socket: &mut S, max_payload_size: usize) -> ServiceResult<Message>
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
    Ok(Message {
        opcode,
        flags,
        request_id,
        session_id,
        payload,
    })
}

async fn write_message<S>(socket: &mut S, message: Message) -> ServiceResult<()>
where
    S: AsyncWrite + Unpin,
{
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

async fn connect_login_service_tls(
    tcp: TcpStream,
    cfg: &LoginConfig,
    login_service_addr: &str,
) -> ServiceResult<tokio_rustls::client::TlsStream<TcpStream>> {
    let cert_file = File::open(&cfg.tls_cert_path)
        .map_err(|e| ServiceError::Config(format!("cannot open TLS_CERT_PATH: {e}")))?;
    let mut cert_reader = BufReader::new(cert_file);
    let certs = rustls_pemfile::certs(&mut cert_reader)
        .collect::<Result<Vec<_>, _>>()
        .map_err(|e| ServiceError::Config(format!("invalid TLS cert file: {e}")))?;
    if certs.is_empty() {
        return Err(ServiceError::Config("no cert found in TLS_CERT_PATH".to_string()));
    }

    let mut roots = RootCertStore::empty();
    for cert in certs {
        roots
            .add(cert)
            .map_err(|e| ServiceError::Config(format!("invalid root cert: {e}")))?;
    }
    let client_config = rustls::ClientConfig::builder()
        .with_root_certificates(roots)
        .with_no_client_auth();
    let connector = TlsConnector::from(Arc::new(client_config));

    let host = login_service_addr
        .split(':')
        .next()
        .filter(|s| !s.is_empty())
        .unwrap_or("127.0.0.1");
    let server_name = ServerName::try_from(host.to_string())
        .map_err(|_| ServiceError::Config("invalid login-service TLS server name".to_string()))?;

    connector
        .connect(server_name, tcp)
        .await
        .map_err(|e| ServiceError::Security(format!("upstream TLS handshake failed: {e}")))
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