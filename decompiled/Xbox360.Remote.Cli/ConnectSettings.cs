using System.ComponentModel;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli;

public sealed class ConnectSettings : DiscoverySettings
{
	[CommandArgument(0, "[target]")]
	[Description("Console IP or discovery index.")]
	public string? Target { get; init; }
}
