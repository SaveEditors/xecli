using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class LedSetCommand : AsyncCommand<LedSetCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--preset <NAME>")]
		[Description("all-green|all-red|all-orange|all-off|quadrant1|quadrant2|quadrant3|quadrant4")]
		public string? Preset { get; init; }

		[CommandOption("--tl <COLOR>")]
		[Description("Top-left color: off|green|red|orange")]
		public string? TopLeft { get; init; }

		[CommandOption("--tr <COLOR>")]
		[Description("Top-right color: off|green|red|orange")]
		public string? TopRight { get; init; }

		[CommandOption("--bl <COLOR>")]
		[Description("Bottom-left color: off|green|red|orange")]
		public string? BottomLeft { get; init; }

		[CommandOption("--br <COLOR>")]
		[Description("Bottom-right color: off|green|red|orange")]
		public string? BottomRight { get; init; }

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
		if (!HardwareHelpers.TryResolveLedState(CliConfig.Load().LastLedState, settings.Preset, settings.TopLeft, settings.TopRight, settings.BottomLeft, settings.BottomRight, out CliConfig.LedStateInfo state, out string error))
		{
			AnsiConsole.MarkupLine("[red]" + Markup.Escape(error ?? "Invalid LED settings.") + "[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			HardwareHelpers.TryParseLedColor(state.TopLeft, out var color);
			HardwareHelpers.TryParseLedColor(state.TopRight, out var color2);
			HardwareHelpers.TryParseLedColor(state.BottomLeft, out var color3);
			HardwareHelpers.TryParseLedColor(state.BottomRight, out var color4);
			await HardwareHelpers.SetLedsAsync(client, color, color2, color3, color4, CancellationToken.None);
			HardwareHelpers.SaveLedState(state);
			if (settings.Json)
			{
				CliOutput.EmitJson(new { state.Preset, state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight });
				return 0;
			}
			OperationFeedback.WriteSuccess("Ring light updated", "[green]" + Markup.Escape(HardwareHelpers.FormatLedSummary(state)) + "[/]");
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, settings.NotifyLogo, "Success :)", CancellationToken.None);
			return 0;
		}, CancellationToken.None);
	}
}
