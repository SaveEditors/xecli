using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class PopupMessageBoxCommand : AsyncCommand<PopupMessageBoxCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--title <TEXT>")]
		[Description("Popup title text.")]
		public string? Title { get; init; }

		[CommandOption("--body <TEXT>")]
		[Description("Popup body text.")]
		public string? Body { get; init; }

		[CommandOption("--button <TEXT>")]
		[Description("Button label. Repeat to add multiple buttons.")]
		public string[]? Buttons { get; init; }

		[CommandOption("--focus <INDEX>")]
		[Description("Focused button index (default: 0).")]
		public uint? FocusedButtonIndex { get; init; }

		[CommandOption("--preset <NAME>")]
		[Description("Popup icon preset: none|error|warning|question (default: none).")]
		public string? Preset { get; init; }

		[CommandOption("--style <ID>")]
		[Description("Raw popup style id. Overrides --preset when provided.")]
		public uint? MessageBoxType { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		string title = (string.IsNullOrWhiteSpace(settings.Title) ? "XeCLI" : settings.Title.Trim());
		string body = settings.Body;
		if (string.IsNullOrWhiteSpace(body))
		{
			AnsiConsole.MarkupLine("[red]--body is required.[/]");
			return 1;
		}
		string[] buttons = settings.Buttons;
		string[] buttons2 = ((buttons != null && buttons.Length > 0) ? (from x in settings.Buttons
			where !string.IsNullOrWhiteSpace(x)
			select x.Trim()).ToArray() : new string[1] { "Continue" });
		if (buttons2.Length == 0 || buttons2.Length > 4)
		{
			AnsiConsole.MarkupLine("[red]Use between 1 and 4 --button values.[/]");
			return 1;
		}
		uint focus = settings.FocusedButtonIndex.GetValueOrDefault();
		if (focus >= buttons2.Length)
		{
			AnsiConsole.MarkupLine("[red]--focus must point to an existing button index.[/]");
			return 1;
		}
		uint messageBoxType;
		if (settings.MessageBoxType.HasValue)
		{
			messageBoxType = settings.MessageBoxType.Value;
		}
		else if (!string.IsNullOrWhiteSpace(settings.Preset))
		{
			if (!HardwareHelpers.TryParsePopupStylePreset(settings.Preset, out var preset))
			{
				AnsiConsole.MarkupLine("[red]Unknown popup preset. Use none, error, warning, or question.[/]");
				return 1;
			}
			messageBoxType = (uint)preset;
		}
		else
		{
			messageBoxType = 0u;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 15000));
			await HardwareHelpers.ShowMessageBoxAsync(client, title, body, buttons2, messageBoxType, focus, cts.Token);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Title = title,
					Body = body,
					Buttons = buttons2,
					FocusedButtonIndex = focus,
					MessageBoxType = messageBoxType,
					Status = "requested"
				});
				return 0;
			}
			OperationFeedback.WriteSuccess("Popup requested", $"[deepskyblue1]{Markup.Escape(title)}[/] [grey]with[/] [springgreen3_1]{buttons2.Length}[/] [grey]button(s)[/]");
			return 0;
		}, CancellationToken.None);
	}
}
