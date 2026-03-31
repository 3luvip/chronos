// shared/src/config.rs
// TLS = mã hóa kết nối
// HMAC chống giả mạo packet
// PSK key nội bộ giữa các service



use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct LoginConfig {
    pub host: String,
    pub port: u16,
    pub db_host: String,
    pub db_port: u16,
    pub db_user: String,
    pub db_password: String,
    pub db_name: String,
    pub admin_mode: i16,
    pub wait_login_secs: i32,
    pub max_frame_size: usize,
    pub rate_limit_window_ms: i64,
    pub rate_limit_max_messages: u32,
    pub max_login_attempts: u32,
    pub tls_enabled: bool,
    pub tls_cert_path: String,
    pub tls_key_path: String,
    pub integrity_mode: String,
    pub hmac_secret: String,
    pub internal_psk: String,
}

impl LoginConfig {

    pub fn validate(&self) -> anyhow::Result<()> {
        if self.port == 0 {
            anyhow::bail!("port cannot be 0");
        }

        if self.hmac_secret.is_empty() {
            anyhow::bail!("HMAC_SECRET is required");
        }

        if self.tls_enabled {
            if self.tls_cert_path.is_empty() || self.tls_key_path.is_empty() {
                anyhow::bail!("TLS enabled but cert/key missing");
            }
        }

        Ok(())
    }

    pub fn from_env() -> Self {
        let _ = dotenvy::dotenv();
        let host = std::env::var("LOGIN_HOST").unwrap_or_else(|_| "0.0.0.0".to_string());
        let port = std::env::var("LOGIN_PORT")
            .ok()
            .and_then(|v| v.parse::<u16>().ok())
            .unwrap_or(14446);
        let db_host = std::env::var("DB_HOST").unwrap_or_else(|_| "127.0.0.1".to_string());
        let db_port = std::env::var("DB_PORT")
            .ok()
            .and_then(|v| v.parse::<u16>().ok())
            .unwrap_or(3306);
        let db_user = std::env::var("DB_USER").unwrap_or_else(|_| "root".to_string());
        let db_password = std::env::var("DB_PASSWORD").unwrap_or_default();
        let db_name = std::env::var("DB_NAME").unwrap_or_else(|_| "ngocrong".to_string());
        let admin_mode = std::env::var("ADMIN_MODE")
            .ok()
            .and_then(|v| v.parse::<i16>().ok())
            .unwrap_or(0);
        let wait_login_secs = std::env::var("WAIT_LOGIN_SECS")
            .ok()
            .and_then(|v| v.parse::<i32>().ok())
            .unwrap_or(5);
        let max_frame_size = std::env::var("MAX_FRAME_SIZE")
            .ok()
            .and_then(|v| v.parse::<usize>().ok())
            .unwrap_or(64 * 1024);
        let rate_limit_window_ms = std::env::var("RATE_LIMIT_WINDOW_MS")
            .ok()
            .and_then(|v| v.parse::<i64>().ok())
            .unwrap_or(10_000);
        let rate_limit_max_messages = std::env::var("RATE_LIMIT_MAX_MESSAGES")
            .ok()
            .and_then(|v| v.parse::<u32>().ok())
            .unwrap_or(120);
        let max_login_attempts = std::env::var("MAX_LOGIN_ATTEMPTS")
            .ok()
            .and_then(|v| v.parse::<u32>().ok())
            .unwrap_or(8);
        let tls_enabled = std::env::var("TLS_ENABLED")
            .ok()
            .map(|v| matches!(v.as_str(), "1" | "true" | "TRUE" | "yes" | "YES"))
            .unwrap_or(false);
        let tls_cert_path =
            std::env::var("TLS_CERT_PATH").unwrap_or_else(|_| "certs/login-cert.pem".to_string());
        let tls_key_path =
            std::env::var("TLS_KEY_PATH").unwrap_or_else(|_| "certs/login-key.pem".to_string());
        let integrity_mode =
            std::env::var("INTEGRITY_MODE").unwrap_or_else(|_| "off".to_string());
        let hmac_secret = std::env::var("HMAC_SECRET").unwrap_or_default();
        let internal_psk = std::env::var("INTERNAL_PSK").unwrap_or_default();
        Self {
            host,
            port,
            db_host,
            db_port,
            db_user,
            db_password,
            db_name,
            admin_mode,
            wait_login_secs,
            max_frame_size,
            rate_limit_window_ms,
            rate_limit_max_messages,
            max_login_attempts,
            tls_enabled,
            tls_cert_path,
            tls_key_path,
            integrity_mode,
            hmac_secret,
            internal_psk,
        }
    }

    pub fn from_ini(path: &str) -> anyhow::Result<Self> {
        let text = std::fs::read_to_string(path)?;
        let mut cfg = Self::from_env();
        for line in text.lines() {
            let line = line.trim();
            if line.is_empty() || line.starts_with('#') {
                continue;
            }
            if let Some((k, v)) = line.split_once('=') {
                let key = k.trim();
                let val = v.trim(); 
                match key {
                    "server.host" => cfg.host = val.to_string(),
                    "server.port" => {
                        if let Ok(p) = val.parse::<u16>() {
                            cfg.port = p;
                        }
                    }
                    "db.host" => cfg.db_host = val.to_string(),
                    "db.port" => {
                        if let Ok(p) = val.parse::<u16>() {
                            cfg.db_port = p;
                        }
                    }
                    "db.user" => cfg.db_user = val.to_string(),
                    "db.password" => cfg.db_password = val.to_string(),
                    "db.name" => cfg.db_name = val.to_string(),
                    "admin.mode" => {
                        if let Ok(m) = val.parse::<i16>() {
                            cfg.admin_mode = m;
                        }
                    }
                    "wait.login" => {
                        if let Ok(s) = val.parse::<i32>() {
                            cfg.wait_login_secs = s;
                        }
                    }
                    "max.frame.size" => {
                        if let Ok(v) = val.parse::<usize>() {
                            cfg.max_frame_size = v;
                        }
                    }
                    "rate.limit.window.ms" => {
                        if let Ok(v) = val.parse::<i64>() {
                            cfg.rate_limit_window_ms = v;
                        }
                    }
                    "rate.limit.max.messages" => {
                        if let Ok(v) = val.parse::<u32>() {
                            cfg.rate_limit_max_messages = v;
                        }
                    }
                    "max.login.attempts" => {
                        if let Ok(v) = val.parse::<u32>() {
                            cfg.max_login_attempts = v;
                        }
                    }
                    "tls.enabled" => {
                        cfg.tls_enabled =
                            matches!(val, "1" | "true" | "TRUE" | "yes" | "YES");
                    }
                    "tls.cert.path" => cfg.tls_cert_path = val.to_string(),
                    "tls.key.path" => cfg.tls_key_path = val.to_string(),
                    "integrity.mode" => cfg.integrity_mode = val.to_string(),
                    "hmac.secret" => cfg.hmac_secret = val.to_string(),
                    "internal.psk" => cfg.internal_psk = val.to_string(),
                    _ => {}
                }
            }
        }
        Ok(cfg)
    }

    pub fn addr(&self) -> String {
        format!("{}:{}", self.host, self.port)
    }


    pub fn database_url(&self) -> String {
        format!(
            "mysql://{}:{}@{}:{}/{}",
            self.db_user, self.db_password, self.db_host, self.db_port, self.db_name
        )
    }

    pub fn database_url_safe(&self) -> String {
        format!(
            "mysql://{}:***@{}:{}/{}",
            self.db_user, self.db_host, self.db_port, self.db_name
        )
    }
}


