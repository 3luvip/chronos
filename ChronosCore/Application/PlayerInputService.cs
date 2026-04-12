using System;
using System.Collections.Generic;
using Chronos.Core.Common;
using Chronos.Core.Contracts;
using Chronos.Core.Domain;
using Chronos.Core.Domain.Character;

namespace Chronos.Core.Application;

public enum InputError { NotLoggedIn, Throttled, CharacterDead }

public sealed class PlayerInputService
{
    private readonly ITimeSource _time;
    private readonly ILogger     _log;

    private readonly Queue<(uint seq, InputSnapshot input, long sentMs)> _pending = new();
    private const int MaxPending = 60;

    private uint _sendSeq;

    public PlayerInputService(ITimeSource time, ILogger log)
    {
        _time = time;
        _log  = log;
    }

    public Result<(uint seq, byte[] payload), InputError> PrepareInput(
        InputSnapshot input, CharacterState character)
    {
        if (!character.IsAlive)
            return Result<(uint seq, byte[] payload), InputError>.Fail(InputError.CharacterDead);

        uint seq     = ++_sendSeq;
        byte[] payload = SerializeInput(seq, input);

        _pending.Enqueue((seq, input, _time.TickMs));
        while (_pending.Count > MaxPending) _pending.Dequeue();

        return Result<(uint seq, byte[] payload), InputError>.Ok((seq, payload));
    }

    public Vec2 Reconcile(Vec2 predictedPos, Vec2 serverPos)
    {
        float err = predictedPos.DistanceTo(serverPos);
        return err switch
        {
            > 5f   => serverPos,
            > 1.5f => predictedPos.Lerp(serverPos, 0.5f),
            _      => predictedPos,
        };
    }

    private static byte[] SerializeInput(uint seq, InputSnapshot input)
    {
        var (mx, my) = input.NormalizedMoveDir();
        byte flags = 0;
        if (input.Attack) flags |= 0x01;
        if (input.Jump)   flags |= 0x02;

        byte[] buf = new byte[13];
        WriteU32Be(buf, 0, seq);
        WriteF32Be(buf, 4, mx);
        WriteF32Be(buf, 8, my);
        buf[12] = flags;
        return buf;
    }

    private static void WriteU32Be(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o+1] = (byte)(v >> 16);
        b[o+2] = (byte)(v >> 8); b[o+3] = (byte)v;
    }

    private static void WriteF32Be(byte[] b, int o, float v) =>
        WriteU32Be(b, o, BitConverter.SingleToUInt32Bits(v));
}
