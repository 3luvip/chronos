namespace Chronos.Core.Infrastructure.Protocol;

public static class WireProtocol
{
    public const ushort FrameMagic = 0x4E52;
    public const ushort Version    = 2;

    public const ushort OpLogin         = 0x1001;
    public const ushort OpLogout        = 0x1002;
    public const ushort OpServerMessage = 0x1004;
    public const ushort OpServerSync    = 0x1005;
    public const ushort OpHeartbeat     = 0x1006;
    public const ushort OpInternalAuth  = 0x2001;
    public const ushort OpPlayerInput   = 0x2001;
    public const ushort OpPlayerDelta   = 0x2002;

    public const byte FlagEncrypted = 0x01;
    public const byte FlagIntegrity = 0x02;
    public const byte FlagInternal  = 0x04;
}

public sealed class Frame
{
    public ushort Opcode    { get; init; }
    public byte   Flags     { get; init; }
    public uint   RequestId { get; init; }
    public ulong  SessionId { get; init; }
    public byte[] Payload   { get; set; } = System.Array.Empty<byte>();
}
