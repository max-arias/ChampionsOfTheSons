namespace ChampionsOfTheSons.Core.Networking;

/// <summary>The version check a host runs on every joiner's <see cref="Hello"/>.</summary>
public static class Handshake
{
    /// <summary>Null when the joiner may play; otherwise the reason to send back in a <see cref="HelloReject"/>.</summary>
    public static string? Check(Hello hello, int hostProtocol, string hostModVersion)
    {
        if (hello.ProtocolVersion != hostProtocol)
            return $"Champions of the Sons version mismatch: the host runs {hostModVersion} (protocol {hostProtocol}), you run {hello.ModVersion} (protocol {hello.ProtocolVersion}). Install the same version.";
        if (hello.ModVersion != hostModVersion)
            return $"Champions of the Sons version mismatch: the host runs {hostModVersion}, you run {hello.ModVersion}. Install the same version.";
        return null;
    }
}
