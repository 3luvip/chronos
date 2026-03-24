using Godot;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

public sealed class ChronosTcpClient : IDisposable
{
	private TcpClient? _client;
	private Stream? _stream;
	private uint _requestId = 1;
	private readonly ClientOptions _options;
	private int _lastClientId;

	public ulong SessionId { get; private set; }
	public int UserId { get; private set; }

	public ChronosTcpClient(ClientOptions options)
	{
		_options = options;
	}

	public async Task ConnectAsync(string host, int port, CancellationToken ct)
	{
		if (_client is { Connected: true })
		{
			return;
		}

		_client = new TcpClient();
		await _client.ConnectAsync(host, port, ct);
		Stream baseStream = _client.GetStream();
		if (_options.UseTls)
		{
			var ssl = new SslStream(
				baseStream,
				false,
				(sender, cert, chain, errors) => _options.SkipTlsCertValidation || errors == SslPolicyErrors.None
			);
			var authOptions = new SslClientAuthenticationOptions
			{
				TargetHost = host
			};
			await ssl.AuthenticateAsClientAsync(authOptions, ct);
			_stream = ssl;
		}
		else
		{
			_stream = baseStream;
		}
	}

	public async Task<LoginResult> LoginAsync(int serverId, int clientId, string username, string password, CancellationToken ct)
	{
		EnsureConnected();

		var writer = new PacketWriter();
		writer.WriteInt32(serverId);
		writer.WriteInt32(clientId);
		writer.WriteUtf(username);
		writer.WriteUtf(password);

		uint requestId = _requestId++;
		var loginFrame = new Frame
		{
			Opcode = Protocol.OpLogin,
			Flags = _options.UseHmac ? Protocol.FlagIntegrity : (byte)0,
			RequestId = requestId,
			SessionId = 0,
			Payload = writer.ToArray()
		};
		if (_options.UseHmac)
		{
			loginFrame = WithHmac(loginFrame);
		}
		_lastClientId = clientId;

		await WriteFrameAsync(loginFrame, ct);

		while (true)
		{
			Frame frame = await ReadFrameAsync(ct);

			if (frame.Opcode == Protocol.OpServerMessage)
			{
				if (_options.UseHmac)
				{
					VerifyAndStripHmac(frame);
				}
				var msgReader = new PacketReader(frame.Payload);
				_ = msgReader.ReadInt32();
				string text = msgReader.ReadUtf();
				return new LoginResult { Ok = false, Error = text };
			}
			if (_options.UseHmac)
			{
				VerifyAndStripHmac(frame);
			}

			if (frame.Opcode != Protocol.OpLogin || frame.RequestId != requestId)
			{
				continue;
			}

			var rd = new PacketReader(frame.Payload);
			int resultClientId = rd.ReadInt32();
			byte status = rd.ReadByte();
			if (resultClientId != clientId)
			{
				return new LoginResult { Ok = false, Error = "Client ID mismatch in response" };
			}

			if (status == 1)
			{
				string err = rd.ReadUtf();
				return new LoginResult { Ok = false, Error = err };
			}

			var result = new LoginResult
			{
				Ok = true,
				UserId = rd.ReadInt32(),
				IsAdmin = rd.ReadByte() == 1,
				Active = rd.ReadByte() == 1,
				Gold = rd.ReadInt32(),
				LastTimeLoginMs = rd.ReadInt64(),
				LastTimeLogoutMs = rd.ReadInt64(),
				Rewards = rd.ReadUtf(),
				Ruby = rd.ReadInt32(),
				MocNap = rd.ReadInt32(),
				ServerLogin = rd.ReadInt32(),
				IsUseMaBaoVe = rd.ReadInt32(),
				MaBaoVe = rd.ReadInt32(),
				TotalRecharge = rd.ReadInt32(),
				Vnd = rd.ReadInt32(),
				SessionIdEcho = rd.ReadUInt64()
			};

			SessionId = frame.SessionId;
			UserId = result.UserId;
			return result;
		}
	}

	public async Task LogoutAsync(CancellationToken ct)
	{
		EnsureConnected();
		if (SessionId == 0 || UserId == 0)
		{
			return;
		}

		var writer = new PacketWriter();
		writer.WriteInt32(UserId);
		var logoutFrame = new Frame
		{
			Opcode = Protocol.OpLogout,
			Flags = _options.UseHmac ? Protocol.FlagIntegrity : (byte)0,
			RequestId = _requestId++,
			SessionId = SessionId,
			Payload = writer.ToArray()
		};
		if (_options.UseHmac)
		{
			logoutFrame = WithHmac(logoutFrame);
		}
		await WriteFrameAsync(logoutFrame, ct);
		// Server hiện tại không trả ACK cho OP_LOGOUT.
		SessionId = 0;
		UserId = 0;
	}

	private async Task WriteFrameAsync(Frame frame, CancellationToken ct)
	{
		EnsureConnected();
		Stream stream = _stream!;
		int payloadLen = frame.Payload.Length;

		byte[] header = new byte[24];
		BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0, 2), Protocol.FrameMagic);
		BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2, 2), Protocol.Version);
		BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4, 2), frame.Opcode);
		header[6] = frame.Flags;
		header[7] = 0;
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), (uint)payloadLen);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12, 4), frame.RequestId);
		BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(16, 8), frame.SessionId);

		await stream.WriteAsync(header, ct);
		if (payloadLen > 0)
		{
			await stream.WriteAsync(frame.Payload, ct);
		}
		await stream.FlushAsync(ct);
	}

	private async Task<Frame> ReadFrameAsync(CancellationToken ct)
	{
		EnsureConnected();
		Stream stream = _stream!;
		byte[] header = await ReadExactAsync(stream, 24, ct);

		ushort magic = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
		if (magic != Protocol.FrameMagic)
		{
			throw new InvalidDataException($"Invalid frame magic: 0x{magic:X4}");
		}

		ushort version = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
		if (version != Protocol.Version)
		{
			throw new InvalidDataException($"Unsupported protocol version: {version}");
		}

		ushort opcode = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
		byte flags = header[6];
		byte reserved = header[7];
		if (reserved != 0)
		{
			throw new InvalidDataException("Reserved byte must be zero");
		}

		uint payloadLen = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
		uint requestId = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12, 4));
		ulong sessionId = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(16, 8));
		byte[] payload = payloadLen > 0
			? await ReadExactAsync(stream, (int)payloadLen, ct)
			: Array.Empty<byte>();

		return new Frame
		{
			Opcode = opcode,
			Flags = flags,
			RequestId = requestId,
			SessionId = sessionId,
			Payload = payload
		};
	}

	private static async Task<byte[]> ReadExactAsync(Stream stream, int size, CancellationToken ct)
	{
		byte[] buffer = new byte[size];
		int offset = 0;
		while (offset < size)
		{
			int read = await stream.ReadAsync(buffer.AsMemory(offset, size - offset), ct);
			if (read <= 0)
			{
				throw new IOException("Connection closed while reading frame");
			}
			offset += read;
		}
		return buffer;
	}

	private void EnsureConnected()
	{
		if (_client is null || _stream is null || !_client.Connected)
		{
			throw new InvalidOperationException("Client is not connected");
		}
	}

	private Frame WithHmac(Frame frame)
	{
		if (string.IsNullOrEmpty(_options.HmacSecret))
		{
			throw new InvalidOperationException("HMAC enabled but secret is empty");
		}
		byte[] tag = ComputeHmac(frame, frame.Payload, _options.HmacSecret);
		byte[] payload = new byte[frame.Payload.Length + tag.Length];
		Buffer.BlockCopy(frame.Payload, 0, payload, 0, frame.Payload.Length);
		Buffer.BlockCopy(tag, 0, payload, frame.Payload.Length, tag.Length);
		return new Frame
		{
			Opcode = frame.Opcode,
			Flags = (byte)(frame.Flags | Protocol.FlagIntegrity),
			RequestId = frame.RequestId,
			SessionId = frame.SessionId,
			Payload = payload
		};
	}

	private void VerifyAndStripHmac(Frame frame)
	{
		if ((frame.Flags & Protocol.FlagIntegrity) == 0)
		{
			throw new InvalidDataException("Expected integrity flag");
		}
		if (string.IsNullOrEmpty(_options.HmacSecret))
		{
			throw new InvalidOperationException("HMAC enabled but secret is empty");
		}
		if (frame.Payload.Length < 32)
		{
			throw new InvalidDataException("Invalid HMAC payload");
		}
		int split = frame.Payload.Length - 32;
		byte[] body = new byte[split];
		byte[] recvTag = new byte[32];
		Buffer.BlockCopy(frame.Payload, 0, body, 0, split);
		Buffer.BlockCopy(frame.Payload, split, recvTag, 0, 32);
		byte[] expected = ComputeHmac(frame, body, _options.HmacSecret);
		if (!CryptographicOperations.FixedTimeEquals(expected, recvTag))
		{
			throw new InvalidDataException("HMAC mismatch");
		}
		frame.Payload = body;
	}

	private static byte[] ComputeHmac(Frame frame, byte[] payload, string secret)
	{
		using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
		Span<byte> op = stackalloc byte[2];
		Span<byte> req = stackalloc byte[4];
		Span<byte> sess = stackalloc byte[8];
		BinaryPrimitives.WriteUInt16BigEndian(op, frame.Opcode);
		BinaryPrimitives.WriteUInt32BigEndian(req, frame.RequestId);
		BinaryPrimitives.WriteUInt64BigEndian(sess, frame.SessionId);
		hmac.TransformBlock(op.ToArray(), 0, 2, null, 0);
		hmac.TransformBlock(req.ToArray(), 0, 4, null, 0);
		hmac.TransformBlock(sess.ToArray(), 0, 8, null, 0);
		hmac.TransformFinalBlock(payload, 0, payload.Length);
		return hmac.Hash ?? Array.Empty<byte>();
	}

	public void Dispose()
	{
		try
		{
			_stream?.Dispose();
			_client?.Close();
		}
		catch (Exception e)
		{
			GD.PrintErr($"Error disposing ChronosTcpClient: {e.Message}");
		}
	}
}

public sealed class ClientOptions
{
	public bool UseTls { get; init; }
	public bool SkipTlsCertValidation { get; init; } = true;
	public bool UseHmac { get; init; }
	public string HmacSecret { get; init; } = "";
}

public sealed class LoginResult
{
	public bool Ok { get; init; }
	public string Error { get; init; } = "";
	public int UserId { get; init; }
	public bool IsAdmin { get; init; }
	public bool Active { get; init; }
	public int Gold { get; init; }
	public long LastTimeLoginMs { get; init; }
	public long LastTimeLogoutMs { get; init; }
	public string Rewards { get; init; } = "";
	public int Ruby { get; init; }
	public int MocNap { get; init; }
	public int ServerLogin { get; init; }
	public int IsUseMaBaoVe { get; init; }
	public int MaBaoVe { get; init; }
	public int TotalRecharge { get; init; }
	public int Vnd { get; init; }
	public ulong SessionIdEcho { get; init; }
}
