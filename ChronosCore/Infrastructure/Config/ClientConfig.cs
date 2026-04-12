using System;

namespace Chronos.Core.Infrastructure.Config;

public sealed class ClientConfig
{
    public string Host              { get; init; } = "127.0.0.1";
    public int    Port              { get; init; } = 14446;
    public bool   UseTls            { get; init; } = true;
    public bool   SkipTlsCertCheck  { get; init; } = true;
    public bool   UseHmac           { get; init; } = true;
    public string HmacSecret        { get; init; } = "";
    public bool   UsePacketEncrypt  { get; init; } = false;
    public string EncryptionSecret  { get; init; } = "";

    public static ClientConfig Default => new();
}
