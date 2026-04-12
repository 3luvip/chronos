using System;
using System.Threading;
using System.Threading.Tasks;
using Chronos.Core.Common;
using Chronos.Core.Contracts;

namespace Chronos.Core.Application;

public enum LoginError
{
    NetworkError,
    AuthFailed,
    Banned,
    WrongServer,
    AlreadyOnline,
    Cancelled,
}

public sealed class LoginResult
{
    public int    UserId           { get; init; }
    public bool   IsAdmin          { get; init; }
    public bool   Active           { get; init; }
    public int    Gold             { get; init; }
    public int    Vnd              { get; init; }
    public int    Ruby             { get; init; }
    public int    ServerLogin      { get; init; }
    public int    TotalRecharge    { get; init; }
    public long   LastTimeLoginMs  { get; init; }
    public long   LastTimeLogoutMs { get; init; }
    public string Rewards          { get; init; } = "";
    public ulong  SessionId        { get; init; }
}

/// <summary>
/// Pure login service — không có Godot, không có UI.
/// UI (LoginScreen) gọi service này và phản ứng với kết quả.
/// </summary>
public sealed class LoginService
{
    private readonly ILogger _log;

    // Session state (sau khi login thành công)
    public ulong  SessionId { get; private set; }
    public int    UserId    { get; private set; }
    public bool   IsLoggedIn => SessionId != 0 && UserId != 0;

    public LoginService(ILogger log)
    {
        _log = log;
    }

    /// Gọi sau khi ChronosTcpClient.LoginAsync thành công.
    public void OnLoginSuccess(ulong sessionId, int userId)
    {
        SessionId = sessionId;
        UserId    = userId;
        _log.Info($"[LoginService] Session established: user={userId} session={sessionId:X16}");
    }

    public void OnLogout()
    {
        _log.Info($"[LoginService] Session cleared: user={UserId}");
        SessionId = 0;
        UserId    = 0;
    }

    public void EnsureAuthenticated()
    {
        if (!IsLoggedIn)
            throw new InvalidOperationException("Not logged in.");
    }
}
