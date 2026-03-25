using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class RebootCommand : AsyncCommand<RebootCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--title")]
		[LocalizedDescription("Restart the current title instead of a cold reboot.")]
		public bool Title { get; init; }

		[CommandOption("--notify")]
		[LocalizedDescription("Send a default success notification to the console.")]
		public bool Notify { get; init; }

		[CommandOption("--notify-icon <NAME>")]
		[LocalizedDescription("Notification icon preset name.")]
		public string? NotifyIcon { get; init; }

		[CommandOption("--notify-logo <ID>")]
		[LocalizedDescription("Notification logo id (decimal or 0x hex).")]
		public string? NotifyLogo { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		string mode = (settings.Title ? "title" : "cold");
		return await CliHelpers.WithClientOnceAsync(settings, async delegate(XbdmClient client)
		{
			await client.SendCommandAsync("magicboot " + mode, CancellationToken.None);
			OperationFeedback.WriteSuccess(settings.Title ? "Title reboot requested" : "Cold reboot requested", settings.Title ? "[cyan]Current title restart requested.[/]" : "[cyan]Full console reboot requested.[/]");
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, settings.NotifyLogo, "Success :)", CancellationToken.None);
			return 0;
		}, CancellationToken.None);
	}
}
