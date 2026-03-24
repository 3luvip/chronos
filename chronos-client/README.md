# Chronos Client (Godot 4.6 C#)

Client test cho `gateway` / `login-service` theo `PROTOCOL_V2`.

## Run

1. Mở Godot 4.6 bản .NET.
2. Import project `Nro/chronos-client`.
3. Run project.
4. Mặc định:
   - Host: `127.0.0.1`
   - Port: `14446` (gateway)
   - Server ID: `1`
   - Client ID: `1001`
   - Username: `admin`
   - Password: `password123`

## Lưu ý

- Hỗ trợ `OP_LOGIN` + parse response.
- Hỗ trợ `OP_LOGOUT`.
- Hỗ trợ tùy chọn TLS (`Use TLS`) và HMAC integrity (`Use HMAC integrity`).
- Với TLS self-signed trong dev, bật `Skip TLS cert validation`.
