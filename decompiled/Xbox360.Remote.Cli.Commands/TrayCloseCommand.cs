using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class TrayCloseCommand : AsyncCommand<TrayCloseCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--notify")]
		[Description("Send a default success notification to the console.")]
		public bool Notify { get; init; }

		[CommandOption("--notify-icon <NAME>")]
		[Description("Notification icon preset name.")]
		public string? NotifyIcon { get; init; }

		[CommandOption("--notify-logo <ID>")]
		[Description("Notification logo id (decimal or 0x hex).")]
		public string? NotifyLogo { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 15000));
			await HardwareHelpers.ExecuteXamShortcutAsync(client, XamShortcutOrdinal.CloseTray, cts.Token);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Tray = "close",
					Status = "requested"
				});
				return 0;
			}
			OperationFeedback.WriteSuccess("Disc tray closed", "[deepskyblue1]Close request sent to the console.[/]");
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, settings.NotifyLogo, "Success :)", CancellationToken.None);
			return 0;
		}, CancellationToken.None);
	}
}
