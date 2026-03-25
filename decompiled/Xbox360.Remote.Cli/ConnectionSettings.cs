using System.ComponentModel;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli;

public class ConnectionSettings : CommandSettings
{
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
