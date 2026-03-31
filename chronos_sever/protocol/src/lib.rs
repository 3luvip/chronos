pub mod codec;

pub const PROTOCOL_VERSION: u16 = 2;
pub const FRAME_MAGIC: u16      = 0x4E52;
pub const FLAG_ENCRYPTED: u8    = 0x01;
pub const FLAG_INTEGRITY: u8    = 0x02;
/// Packet từ internal service (gateway → login-service). Yêu cầu PSK.
pub const FLAG_INTERNAL: u8     = 0x04;

pub const OP_LOGIN:         u16 = 0x1001;
pub const OP_LOGOUT:        u16 = 0x1002;
pub const OP_SERVER_MESSAGE:u16 = 0x1004;
pub const OP_SERVER_SYNC:   u16 = 0x1005;
/// Heartbeat: client gửi định kỳ kèm session_id + HMAC để server xác nhận
/// session còn hợp lệ. Server trả lại cùng opcode với payload chứa
/// timestamp server (i64 ms) — client dùng để phát hiện clock drift.
pub const OP_HEARTBEAT:     u16 = 0x1006;
/// Internal auth giữa gateway và login-service.
pub const OP_INTERNAL_AUTH: u16 = 0x2001;

#[derive(Debug, Clone)]
pub struct Message {
    pub opcode:     u16,
    pub flags:      u8,
    pub request_id: u32,
    pub session_id: u64,
    pub payload:    Vec<u8>,
}

impl Message {
    pub fn new(opcode: u16, payload: Vec<u8>) -> Self {
        Self { opcode, flags: 0, request_id: 0, session_id: 0, payload }
    }
}