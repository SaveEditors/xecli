using System.ComponentModel;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli;

public class DiscoverySettings : CommandSettings
{
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
