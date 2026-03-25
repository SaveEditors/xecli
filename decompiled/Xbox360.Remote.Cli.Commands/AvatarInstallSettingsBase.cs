using System.ComponentModel;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public abstract class AvatarInstallSettingsBase : AvatarLibrarySettings
{
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

	[CommandOption("--titleid <TITLEID>")]
	[Description("Install all items for one title when paired with --all.")]
	public string? TitleId { get; init; }

	[CommandOption("--contentid <CONTENTID>")]
	[Description("Install one specific avatar item.")]
	public string? ContentId { get; init; }

	[CommandOption("--all")]
	[Description("Install all items for the selected title.")]
	public bool All { get; init; }

	[CommandOption("--device <ROOT>")]
	[Description("Console storage root (default: Hdd1).")]
	public string? Device { get; init; }

	[CommandOption("--xuid <XUID>")]
	[Description("Explicit target XUID. Defaults to the current signed-in user.")]
	public string? Xuid { get; init; }

	[CommandOption("--gamertag <NAME>")]
	[Description("Label shown in local output when --xuid is provided.")]
	public string? Gamertag { get; init; }

	[CommandOption("--current-user")]
	[Description("Use the current signed-in user explicitly.")]
	public bool CurrentUser { get; init; }

	[CommandOption("--xbdm-port <PORT>")]
	[Description("XBDM port used for current-user resolution (default: saved target port or 730).")]
	public int? XbdmPort { get; init; }

	[CommandOption("--working <DIR>")]
	[Description("Working directory for patched temporary files.")]
	public string? WorkingDirectory { get; init; }

	[CommandOption("--overwrite")]
	[Description("Overwrite remote files that already exist.")]
	public bool Overwrite { get; init; }

	[CommandOption("--dry-run")]
	[Description("Show the install plan without uploading anything.")]
	public bool DryRun { get; init; }
}
