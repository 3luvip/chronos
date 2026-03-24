using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

public static class Protocol
{
	public const ushort FrameMagic = 0x4E52;
	public const ushort Version = 2;

	public const ushort OpLogin = 0x1001;
	public const ushort OpLogout = 0x1002;
	public const ushort OpServerMessage = 0x1004;
	public const byte FlagIntegrity = 0x02;
}

public sealed class PacketWriter
{
	private readonly MemoryStream _ms = new();

	public void WriteByte(byte value) => _ms.WriteByte(value);

	public void WriteInt32(int value)
	{
		Span<byte> b = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(b, value);
		_ms.Write(b);
	}

	public void WriteInt64(long value)
	{
		Span<byte> b = stackalloc byte[8];
		BinaryPrimitives.WriteInt64BigEndian(b, value);
		_ms.Write(b);
	}

	public void WriteUInt64(ulong value)
	{
		Span<byte> b = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(b, value);
		_ms.Write(b);
	}

	public void WriteUtf(string value)
	{
		byte[] data = Encoding.UTF8.GetBytes(value);
		if (data.Length > ushort.MaxValue)
		{
			throw new InvalidOperationException("String too long for protocol UTF field");
		}

		Span<byte> len = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)data.Length);
		_ms.Write(len);
		_ms.Write(data, 0, data.Length);
	}

	public byte[] ToArray() => _ms.ToArray();
}

public sealed class PacketReader
{
	private readonly byte[] _data;
	private int _pos;

	public PacketReader(byte[] data)
	{
		_data = data;
		_pos = 0;
	}

	public byte ReadByte()
	{
		Ensure(1);
		return _data[_pos++];
	}

	public int ReadInt32()
	{
		Ensure(4);
		int value = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_pos, 4));
		_pos += 4;
		return value;
	}

	public long ReadInt64()
	{
		Ensure(8);
		long value = BinaryPrimitives.ReadInt64BigEndian(_data.AsSpan(_pos, 8));
		_pos += 8;
		return value;
	}

	public ulong ReadUInt64()
	{
		Ensure(8);
		ulong value = BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(_pos, 8));
		_pos += 8;
		return value;
	}

	public string ReadUtf()
	{
		Ensure(2);
		ushort len = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_pos, 2));
		_pos += 2;
		Ensure(len);
		string value = Encoding.UTF8.GetString(_data, _pos, len);
		_pos += len;
		return value;
	}

	private void Ensure(int needed)
	{
		if (_pos + needed > _data.Length)
		{
			throw new InvalidOperationException("Invalid payload length");
		}
	}
}

public sealed class Frame
{
	public ushort Opcode { get; init; }
	public byte Flags { get; init; }
	public uint RequestId { get; init; }
	public ulong SessionId { get; init; }
	public byte[] Payload { get; set; } = Array.Empty<byte>();
}
