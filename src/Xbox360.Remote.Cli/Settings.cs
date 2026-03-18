using System.ComponentModel;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli;

public class ConnectionSettings : CommandSettings {
    [CommandOption("--ip <IP>")]
    [Description("Console IP address. If omitted, uses the last connected console.")]
    public string? Ip { get; init; }

    [CommandOption("--port <PORT>")]
    [Description("TCP port (default: 730).")]
    public int? Port { get; init; }

    [CommandOption("--timeout <MS>")]
    [Description("Socket timeout in milliseconds (default: 5000).")]
    public int? TimeoutMs { get; init; }

    [CommandOption("--json")]
    [Description("Emit JSON output.")]
    public bool Json { get; init; }
}

public class DiscoverySettings : CommandSettings {
    [CommandOption("--ports <PORTS>")]
    [Description("Comma-separated ports to probe. Default: 730,731.")]
    public string? Ports { get; init; }

    [CommandOption("--timeout <MS>")]
    [Description("TCP timeout in milliseconds (default: 400).")]
    public int? TimeoutMs { get; init; }

    [CommandOption("--no-nap")]
    [Description("Disable NAP broadcast discovery.")]
    public bool NoNap { get; init; }

    [CommandOption("--no-tcp")]
    [Description("Disable TCP scan discovery.")]
    public bool NoTcp { get; init; }

    [CommandOption("--json")]
    [Description("Emit JSON output.")]
    public bool Json { get; init; }
}

public sealed class ConnectSettings : DiscoverySettings {
    [CommandArgument(0, "[target]")]
    [Description("Console IP or discovery index.")]
    public string? Target { get; init; }
}

public class FtpConnectionSettings : CommandSettings {
    [CommandOption("--ip <IP>")]
    [Description("Console IP address. If omitted, uses the last connected console.")]
    public string? Ip { get; init; }

    [CommandOption("--port <PORT>")]
    [Description("FTP port (default: 21).")]
    public int? Port { get; init; }

    [CommandOption("--user <USER>")]
    [Description("FTP username (default: xboxftp).")]
    public string? User { get; init; }

    [CommandOption("--pass <PASS>")]
    [Description("FTP password (default: xboxftp).")]
    public string? Pass { get; init; }

    [CommandOption("--timeout <MS>")]
    [Description("FTP timeout in milliseconds (default: 5000).")]
    public int? TimeoutMs { get; init; }

    [CommandOption("--json")]
    [Description("Emit JSON output.")]
    public bool Json { get; init; }
}
