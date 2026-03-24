use thiserror::Error;

#[derive(Debug, Error)]
pub enum ServiceError {
    #[error("io error: {0}")]
    Io(#[from] std::io::Error),
    #[error("database error: {0}")]
    Database(#[from] sqlx::Error),
    #[error("config error: {0}")]
    Config(String),
    #[error("protocol error: {0}")]
    Protocol(String),
    #[error("security error: {0}")]
    Security(String),
    #[error("external error: {0}")]
    External(String),
    #[error("password hash error: {0}")]
    PasswordHash(String),
}

pub type ServiceResult<T> = Result<T, ServiceError>;