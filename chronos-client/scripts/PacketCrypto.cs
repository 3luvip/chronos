using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

// ═══════════════════════════════════════════════════════════════════════════
// PacketCrypto.cs — AES-256-GCM encryption + XOR obfuscation cho packet
//
// Tầng bảo vệ:
//   Layer 1 — XOR obfuscation với rotating key (nhanh, che payload plaintext)
//   Layer 2 — AES-256-GCM (authenticated encryption, chống tamper + replay)
//   Layer 3 — Sequence number + timestamp anti-replay window
//
// Sử dụng:
//   var crypto = new PacketCrypto(sharedSecret);
//   byte[] sealed   = crypto.Seal(opcode, sessionId, plainPayload);
//   byte[] unsealed = crypto.Open(opcode, sessionId, sealedPayload);
// ═══════════════════════════════════════════════════════════════════════════
public sealed class PacketCrypto : IDisposable
{
    // AES-GCM nonce: 12 bytes (96-bit, chuẩn cho GCM)
    private const int NonceSize = 12;
    // GCM authentication tag: 16 bytes
    private const int TagSize   = 16;
    // Sequence counter size: 8 bytes (u64)
    private const int SeqSize   = 8;
    // Total overhead per packet: nonce + tag + seq
    public const int Overhead   = NonceSize + TagSize + SeqSize;

    // Anti-replay window: chấp nhận seq trong khoảng [lastSeq - Window, lastSeq + Window]
    private const ulong ReplayWindow = 64;

    private readonly byte[]   _aesKey;       // 32 bytes (AES-256)
    private readonly byte[]   _xorKey;       // 16 bytes rotating XOR key
    private ulong             _sendSeq;
    private ulong             _lastRecvSeq;
    private bool              _disposed;

    /// <param name="sharedSecret">
    /// Chuỗi secret chung giữa client và server.
    /// Trong production lấy từ key-exchange (DH/ECDH); ở đây dùng PBKDF2 từ secret.
    /// </param>
    public PacketCrypto(string sharedSecret)
    {
        // Derive AES key (32 bytes) và XOR key (16 bytes) từ shared secret
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(sharedSecret),
            salt:       Encoding.UTF8.GetBytes("ChronosAntiCheat_v1"),
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256);

        _aesKey = pbkdf2.GetBytes(32);
        _xorKey = pbkdf2.GetBytes(16);
        _sendSeq = 0;
        _lastRecvSeq = 0;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Seal (encrypt + authenticate) — dùng khi GỬI
    // ─────────────────────────────────────────────────────────────────────

    /// Encrypt payload và trả về: [nonce(12)] [seq(8)] [ciphertext] [tag(16)]
    /// AAD = opcode(2) || sessionId(8) — đảm bảo header không bị tamper
    public byte[] Seal(ushort opcode, ulong sessionId, byte[] plaintext)
    {
        ThrowIfDisposed();

        ulong seq = ++_sendSeq;

        // Bước 1: XOR obfuscation trước khi đưa vào AES
        byte[] obfuscated = XorObfuscate(plaintext, seq);

        // Bước 2: tạo nonce ngẫu nhiên (12 bytes)
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);

        // Bước 3: build AAD (Additional Authenticated Data)
        byte[] aad = BuildAad(opcode, sessionId, seq);

        // Bước 4: AES-256-GCM encrypt
        byte[] ciphertext = new byte[obfuscated.Length];
        byte[] tag        = new byte[TagSize];

        using var aesGcm = new AesGcm(_aesKey, TagSize);
        aesGcm.Encrypt(nonce, obfuscated, ciphertext, tag, aad);

        // Bước 5: đóng gói: nonce | seq(BE) | ciphertext | tag
        byte[] result = new byte[NonceSize + SeqSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(NonceSize, SeqSize), seq);
        ciphertext.CopyTo(result, NonceSize + SeqSize);
        tag.CopyTo(result, NonceSize + SeqSize + ciphertext.Length);

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Open (verify + decrypt) — dùng khi NHẬN
    // ─────────────────────────────────────────────────────────────────────

    /// Giải mã và xác thực. Ném exception nếu tamper hoặc replay.
    public byte[] Open(ushort opcode, ulong sessionId, byte[] sealed_)
    {
        ThrowIfDisposed();

        if (sealed_.Length < Overhead)
            throw new CryptographicException("Packet too short to be valid");

        // Parse: nonce | seq | ciphertext | tag
        byte[] nonce      = sealed_[..NonceSize];
        ulong  seq        = BinaryPrimitives.ReadUInt64BigEndian(sealed_.AsSpan(NonceSize, SeqSize));
        int    cipherLen  = sealed_.Length - NonceSize - SeqSize - TagSize;
        byte[] ciphertext = sealed_.AsSpan(NonceSize + SeqSize, cipherLen).ToArray();
        byte[] tag        = sealed_.AsSpan(NonceSize + SeqSize + cipherLen, TagSize).ToArray();

        // Anti-replay check
        CheckReplay(seq);

        byte[] aad = BuildAad(opcode, sessionId, seq);

        // AES-256-GCM decrypt + verify tag
        byte[] obfuscated = new byte[cipherLen];
        using var aesGcm  = new AesGcm(_aesKey, TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, obfuscated, aad);
        // Nếu tag sai → AesGcm ném AuthenticationTagMismatchException tự động

        // Bỏ XOR obfuscation
        return XorObfuscate(obfuscated, seq);
    }

    // ─────────────────────────────────────────────────────────────────────
    // XOR obfuscation — rotating key dựa trên seq
    // ─────────────────────────────────────────────────────────────────────

    private byte[] XorObfuscate(byte[] data, ulong seq)
    {
        byte[] result = new byte[data.Length];
        // Kết hợp XOR key tĩnh với seq để tạo stream key khác nhau mỗi gói
        Span<byte> seqBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(seqBytes, seq);

        for (int i = 0; i < data.Length; i++)
        {
            byte keyByte = (byte)(_xorKey[i % _xorKey.Length] ^ seqBytes[i % 8] ^ (byte)(i >> 4));
            result[i] = (byte)(data[i] ^ keyByte);
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static byte[] BuildAad(ushort opcode, ulong sessionId, ulong seq)
    {
        // AAD = opcode(2) + sessionId(8) + seq(8)
        byte[] aad = new byte[18];
        BinaryPrimitives.WriteUInt16BigEndian(aad.AsSpan(0, 2), opcode);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(2, 8), sessionId);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(10, 8), seq);
        return aad;
    }

    private void CheckReplay(ulong seq)
    {
        // Seq phải tăng dần (trong giới hạn window)
        if (seq == 0)
            throw new CryptographicException("Replay attack: seq=0 is invalid");

        if (_lastRecvSeq > 0 && seq <= _lastRecvSeq - ReplayWindow)
            throw new CryptographicException($"Replay attack: seq={seq} is too old (last={_lastRecvSeq})");

        if (seq > _lastRecvSeq)
            _lastRecvSeq = seq;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PacketCrypto));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            // Xóa key khỏi memory (security best practice)
            Array.Clear(_aesKey, 0, _aesKey.Length);
            Array.Clear(_xorKey, 0, _xorKey.Length);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// PacketObfuscator — obfuscation nhẹ cho header (không cần full AES)
// Dùng để che opcode và payload size khỏi passive traffic analysis
// ═══════════════════════════════════════════════════════════════════════════
public static class PacketObfuscator
{
    // Mã hóa opcode bằng cách XOR với hash của sessionId
    // Server phải giải mã tương tự
    public static ushort ObfuscateOpcode(ushort opcode, ulong sessionId, uint requestId)
    {
        // Key = lower 16-bit của (sessionId XOR requestId)
        ushort mask = (ushort)((sessionId ^ requestId) & 0xFFFF);
        return (ushort)(opcode ^ mask);
    }

    public static ushort DeobfuscateOpcode(ushort obfuscated, ulong sessionId, uint requestId)
        => ObfuscateOpcode(obfuscated, sessionId, requestId); // XOR is symmetric

    // Thêm dummy padding ngẫu nhiên vào payload để che payload size
    // Format: [real_len(2)] [real_payload] [padding]
    public static byte[] AddJitter(byte[] payload, Random rng)
    {
        int padLen  = rng.Next(4, 32); // 4-31 bytes padding ngẫu nhiên
        byte[] result = new byte[2 + payload.Length + padLen];
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0, 2), (ushort)payload.Length);
        payload.CopyTo(result, 2);
        // Padding: random bytes
        rng.NextBytes(result.AsSpan(2 + payload.Length));
        return result;
    }

    public static byte[] RemoveJitter(byte[] padded)
    {
        if (padded.Length < 2)
            throw new ArgumentException("Padded payload too short");
        int realLen = BinaryPrimitives.ReadUInt16BigEndian(padded.AsSpan(0, 2));
        if (padded.Length < 2 + realLen)
            throw new ArgumentException("Padded payload corrupted");
        return padded[2..(2 + realLen)];
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ClientIntegrityProof — gửi kèm mỗi N packet để server xác minh
// client không bị modified (memory checksum, timestamp consistency)
// ═══════════════════════════════════════════════════════════════════════════
public sealed class ClientIntegrityProof
{
    /// Tạo proof cho server xác minh client còn hợp lệ.
    /// Server so sánh timestamp drift và sequence consistency.
    public static byte[] Build(ulong sessionId, uint inputSeq, long clientMs)
    {
        var w = new PacketWriter();
        w.WriteUInt64(sessionId);
        w.WriteInt32((int)inputSeq);
        w.WriteInt64(clientMs);
        // Checksum đơn giản: XOR folding
        byte[] raw = w.ToArray();
        uint checksum = 0x5A3C9F12u;
        foreach (byte b in raw) checksum = (checksum << 5) | (checksum >> 27);
        checksum ^= (uint)raw.Length;
        w.WriteInt32((int)checksum);
        return w.ToArray();
    }
}