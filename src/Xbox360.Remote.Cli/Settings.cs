using System.ComponentModel;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli;

public class ConnectionSettings : CommandSettings {
    [CommandOption("--ip <IP>")]
    [LocalizedDescription("Console IP address. If omitted, uses the last connected console.")]
    public string? Ip { get; init; }

    [CommandOption("--port <PORT>")]
    [LocalizedDescription("TCP port (default: 730).")]
    public int? Port { get; init; }

    [CommandOption("--timeout <MS>")]
    [LocalizedDescription("Socket timeout in milliseconds (default: 5000).")]
    public int? TimeoutMs { get; init; }

    [CommandOption("--json")]
    [LocalizedDescription("Emit JSON output.")]
    public bool Json { get; init; }
}

public class DiscoverySettings : CommandSettings {
    [CommandOption("--ports <PORTS>")]
    [LocalizedDescription("Comma-separated ports to probe. Default: 730,731.")]
    public string? Ports { get; init; }

    [CommandOption("--timeout <MS>")]
    [LocalizedDescription("TCP timeout in milliseconds (default: 400).")]
    public int? TimeoutMs { get; init; }

    [CommandOption("--no-nap")]
    [LocalizedDescription("Disable NAP broadcast discovery.")]
    public bool NoNap { get; init; }

    [CommandOption("--no-tcp")]
    [LocalizedDescription("Disable TCP scan discovery.")]
    public bool NoTcp { get; init; }

    [CommandOption("--json")]
    [LocalizedDescription("Emit JSON output.")]
    public bool Json { get; init; }
}

public sealed class ConnectSettings : DiscoverySettings {
    [CommandArgument(0, "[target]")]
    [LocalizedDescription("Console IP or discovery index.")]
    public string? Target { get; init; }
}

public class FtpConnectionSettings : CommandSettings {
    [CommandOption("--ip <IP>")]
    [LocalizedDescription("Console IP address. If omitted, uses the last connected console.")]
    public string? Ip { get; init; }

    [CommandOption("--port <PORT>")]
    [LocalizedDescription("FTP port (default: 21).")]
    public int? Port { get; init; }

    [CommandOption("--user <USER>")]
    [LocalizedDescription("FTP username (default: xboxftp).")]
    public string? User { get; init; }

    [CommandOption("--pass <PASS>")]
    [LocalizedDescription("FTP password (default: xboxftp).")]
    public string? Pass { get; init; }

    [CommandOption("--timeout <MS>")]
    [LocalizedDescription("FTP timeout in milliseconds (default: 5000).")]
    public int? TimeoutMs { get; init; }

    [CommandOption("--json")]
    [LocalizedDescription("Emit JSON output.")]
    public bool Json { get; init; }
}

