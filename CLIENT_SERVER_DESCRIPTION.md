## Tổng quan
Repo này gồm 2 phần chính:
1. `chronos-client`: ứng dụng Godot 4 (C#) đóng vai trò **client đăng nhập** qua TCP.
2. `chronos_sever` (viết theo tên thư mục dự án): backend Rust gồm:
   - `gateway`: nhận kết nối từ client và **forward** gói tin sang `login-service`.
   - `login-service`: xác thực tài khoản với DB, cấp `session_id`, xử lý logout và các luồng internal.

Trao đổi giữa `client` và backend dùng một wire protocol nhị phân chung (protocol v2).

---

## Client (`chronos-client`)
### Thành phần UI
- Giao diện Godot tạo màn hình đăng nhập và các panel chọn server.
- File quan trọng:
  - `chronos-client/scripts/Main.cs`: tạo UI, cho phép bật/tắt `TLS` và `HMAC integrity`, bấm `Connect + Login` / `Logout`.
  - `LoginScreen.cs`: quản lý màn hình đăng nhập / đổi tài khoản / chọn server (phần UI).

### Thành phần mạng
- `chronos-client/scripts/ChronosTcpClient.cs` là lớp TCP client thực thi:
  - `ConnectAsync(host, port, ct)`: mở kết nối TCP; nếu bật `UseTls` thì bọc bằng `SslStream`.
  - `LoginAsync(serverId, clientId, username, password, ct)`:
    - đóng gói payload `OP_LOGIN` theo format protocol v2.
    - nếu bật `UseHmac` thì append HMAC-SHA256 (32 bytes) vào cuối payload và set flag integrity.
    - đọc response cho đúng `request_id`.
  - `LogoutAsync(ct)`:
    - gửi `OP_LOGOUT` với `user_id` trong payload và kèm `session_id` ở header.

### Luồng thao tác
1. Người dùng nhập `host/port/serverId/clientId/username/password`.
2. Client `ConnectAsync`.
3. Client gửi `OP_LOGIN`.
4. Server phản hồi:
   - `OP_LOGIN` response: có `status` (0=success, 1=failed). Nếu failed thì payload chứa `error_text`.
   - Ngoài ra, `login-service` có thể gửi thêm `OP_SERVER_MESSAGE` để thông báo (ví dụ khi server đang xử lý admin-mode), sau đó vẫn có `OP_LOGIN` để kết luận.
5. Khi người dùng bấm `Logout`: client gửi `OP_LOGOUT`, xóa `SessionId/UserId` cục bộ.

---

## Backend Server (`chronos_sever`)
### Kiến trúc phân lớp
1. `gateway`:
   - Mở TCP listener (option TLS cho phía client).
   - Mỗi kết nối client được **authenticate nội bộ** đến `login-service` ngay khi bắt đầu.
   - Forward các opcode từ client sang `login-service` và relay response ngược lại.
2. `login-service`:
   - Mở TCP listener (option TLS cho phía internal và client).
   - Thực hiện xác thực login bằng DB + `argon2`.
   - Quản lý session (`session_id`) và tập user đang online.
   - Xử lý `OP_LOGOUT` và `OP_SERVER_SYNC` cho internal service.

---

## Wire Protocol v2 (chia sẻ giữa client và server)
Tất cả số là **big-endian**.

### Frame header (24 bytes)
| Field | Type | Size | Ghi chú |
|---|---:|---:|---|
| `magic` | u16 | 2 | luôn `0x4E52` (`'N''R'`) |
| `version` | u16 | 2 | luôn `2` |
| `opcode` | u16 | 2 | mã lệnh |
| `flags` | u8 | 1 | bit flags (integrity/encrypted/internal) |
| `reserved` | u8 | 1 | phải = `0` |
| `payload_len` | u32 | 4 | độ dài payload |
| `request_id` | u32 | 4 | id do client tạo để correlate response |
| `session_id` | u64 | 8 | session đang xác thực (0 trước khi login thành công) |

Header tổng cộng: **24 bytes**.

### Opcodes đang dùng
- `OP_LOGIN` (`0x1001`): client <-> server (login request + login result)
- `OP_LOGOUT` (`0x1002`): client -> server
- `OP_SERVER_MESSAGE` (`0x1004`): server -> client (message/error text)
- Internal (gateway/game-server -> login-service):
  - `OP_INTERNAL_AUTH` (`0x2001`): xác thực internal bằng `INTERNAL_PSK`
  - `OP_SERVER_SYNC` (`0x1005`): internal sync danh sách user online theo `server_id`

### Payload types (primitives)
- `i32`: 4 bytes signed (big-endian)
- `i64`: 8 bytes signed (big-endian)
- `u8`: 1 byte
- `u64`: 8 bytes unsigned (big-endian)
- `utf`: `u16 length + UTF-8 bytes`

### Format payload chính
`OP_LOGIN` request:
1. `server_id: i32`
2. `client_id: i32`
3. `username: utf`
4. `password: utf`

`OP_LOGIN` response:
1. `client_id: i32`
2. `status: u8` (0=success, 1=failed)
3. Nếu failed: `error_text: utf`
4. Nếu success: kèm các trường account + `session_id_echo` ở cuối payload.

`OP_LOGOUT` request:
1. `user_id: i32`
Header phải có `session_id` hợp lệ.

`OP_SERVER_MESSAGE` response:
1. `client_id: i32`
2. `text: utf`

---

## Bảo mật & chống lạm dụng
### TLS
- Cả `gateway` (phía client) và `login-service` đều có option TLS.
- Client bật TLS bằng `UseTls` (trong UI `Main.cs`).

### Integrity (toàn vẹn)
- Client có thể bật `UseHmac`:
  - Set flag integrity và append HMAC-SHA256 32 bytes vào cuối payload.
  - HMAC bao gồm: `opcode`, `request_id`, `session_id`, và `payload` (body).
- Server có `INTEGRITY_MODE`:
  - `off`: không kèm tag
  - `checksum`: debug (checksum 32-bit)
  - `hmac`: production (HMAC-SHA256)

### Rate limit & brute-force guard (per connection)
- Server áp `RATE_LIMIT_WINDOW_MS` / `RATE_LIMIT_MAX_MESSAGES`.
- Server giới hạn `MAX_LOGIN_ATTEMPTS` cho `OP_LOGIN` trên mỗi kết nối.

### Kiểm soát internal opcode
- `login-service` chỉ cho phép `FLAG_INTERNAL` trên `OP_INTERNAL_AUTH`.
- `OP_SERVER_SYNC` chỉ được xử lý nếu kết nối đã pass internal auth.

### Lưu ý về `FLAG_ENCRYPTED`
- Mặc dù protocol có cờ `FLAG_ENCRYPTED`, `login-service` hiện **chưa hỗ trợ** gói tin “encrypted”; nếu nhận `FLAG_ENCRYPTED` sẽ trả lỗi.

---

## Cấu hình qua env (tham chiếu `.env.example`)
Các env chính mà `login-service` dùng (`chronos_sever/shared/src/config.rs` và `.env.example`):
- `LOGIN_HOST`, `LOGIN_PORT`: địa chỉ/port của `login-service`
- DB:
  - `DB_HOST`, `DB_PORT`, `DB_USER`, `DB_PASSWORD`, `DB_NAME`
- Giới hạn:
  - `ADMIN_MODE`, `WAIT_LOGIN_SECS`, `MAX_FRAME_SIZE`
  - `RATE_LIMIT_WINDOW_MS`, `RATE_LIMIT_MAX_MESSAGES`, `MAX_LOGIN_ATTEMPTS`
- Bảo mật:
  - `TLS_ENABLED`, `TLS_CERT_PATH`, `TLS_KEY_PATH`
  - `INTEGRITY_MODE` (off/checksum/hmac), `HMAC_SECRET`
  - `INTERNAL_PSK` (bắt buộc để internal auth cho `gateway`)

Riêng `gateway` còn dùng:
- `LOGIN_SERVICE_ADDR`: địa chỉ kết nối tới `login-service` (mặc định `127.0.0.1:14447`).

---

## Gợi ý hiểu nhanh luồng login
1. `client` gửi `OP_LOGIN` (payload có username/password).
2. `gateway`:
   - đã authenticate nội bộ với `login-service` bằng `OP_INTERNAL_AUTH`.
   - forward `OP_LOGIN` nguyên gói và relay lại phản hồi.
3. `login-service`:
   - validate format + rate limits.
   - query DB lấy `password_hash`.
   - verify bằng `argon2` (không so sánh plaintext trong SQL).
   - nếu hợp lệ: tạo `session_id` và trả payload success.
4. Khi logout: client gửi `OP_LOGOUT`, server cập nhật `last_time_logout` trong DB và xóa user khỏi online set.

