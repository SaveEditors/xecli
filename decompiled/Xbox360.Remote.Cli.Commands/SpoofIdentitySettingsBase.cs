using System.ComponentModel;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public abstract class SpoofIdentitySettingsBase : ConnectionSettings
{
	[CommandOption("--current-user")]
	[Description("Use the currently signed-in user value.")]
	public bool CurrentUser { get; init; }

	[CommandOption("--notify")]
	[Description("Send a default success notification to the console.")]
	public bool Notify { get; init; } = true;

	[CommandOption("--notify-icon <NAME>")]
	[Description("Notification icon preset name.")]
	public string? NotifyIcon { get; init; }

	[CommandOption("--notify-logo <ID>")]
	[Description("Notification logo id (decimal or 0x hex).")]
	public string? NotifyLogo { get; init; }
}
