using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ShutdownCommand : AsyncCommand<ShutdownCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--notify")]
		[LocalizedDescription("Send a default success notification to the console before shutdown.")]
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
		return await CliHelpers.WithClientOnceAsync(settings, async delegate(XbdmClient client)
		{
			if (settings.Notify)
			{
				await NotifyHelpers.TrySendOperationNotificationAsync(client, enabled: true, settings.NotifyIcon, settings.NotifyLogo, "Success :)", CancellationToken.None);
				await Task.Delay(300, CancellationToken.None);
			}
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 10000));
			await new Jrpc2Client(client).ShutdownAsync(cts.Token);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Power = "off",
					Status = "requested"
				});
				return 0;
			}
			OperationFeedback.WriteSuccess("Shutdown requested", "[deepskyblue1]Power-off request sent to the console.[/]");
			return 0;
		}, CancellationToken.None);
	}
}
