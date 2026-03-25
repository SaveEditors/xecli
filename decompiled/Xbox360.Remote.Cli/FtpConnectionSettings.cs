using System.ComponentModel;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli;

public class FtpConnectionSettings : CommandSettings
{
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
