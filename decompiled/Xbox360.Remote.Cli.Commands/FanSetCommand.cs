using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FanSetCommand : AsyncCommand<FanSetCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--speed <PERCENT>")]
		[LocalizedDescription("Manual fan speed percentage (10-100).")]
		public int? SpeedPercent { get; init; }

		[CommandOption("--channel <CHANNEL>")]
		[LocalizedDescription("primary|secondary|both (default: both).")]
		public string? Channel { get; init; }

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
		if (!settings.SpeedPercent.HasValue)
		{
			AnsiConsole.MarkupLine("[red]--speed is required.[/]");
			return 1;
		}
		string channel = (string.IsNullOrWhiteSpace(settings.Channel) ? "both" : settings.Channel.Trim().ToLowerInvariant());
		bool flag;
		switch (channel)
		{
		case "primary":
		case "secondary":
		case "both":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			AnsiConsole.MarkupLine("[red]--channel must be primary, secondary, or both.[/]");
			return 1;
		}
		int speed = Math.Clamp(settings.SpeedPercent.Value, 10, 100);
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 30000));
			await HardwareHelpers.SetFanSpeedAsync(client, channel, speed, cts.Token);
			HardwareHelpers.SaveFanState(speed, channel);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					SpeedPercent = speed,
					Channel = channel,
					Source = "manual"
				});
				return 0;
			}
			OperationFeedback.WriteSuccess("Fan command sent", $"[green]{speed}%[/] [grey]requested for[/] [deepskyblue1]{Markup.Escape(channel)}[/]");
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, settings.NotifyLogo, "Success :)", CancellationToken.None);
			return 0;
		}, CancellationToken.None);
	}
}
