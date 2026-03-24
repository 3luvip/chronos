use tracing_subscriber::EnvFilter;

pub fn init(service_name: &str) {
    let env_filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| "info".into());
    tracing_subscriber::fmt()
        .with_target(true)
        .with_env_filter(env_filter)
        .with_thread_names(true)
        .with_line_number(true)
        .with_ansi(true)
        .init();
    tracing::info!(service = service_name, "logging initialized");
}