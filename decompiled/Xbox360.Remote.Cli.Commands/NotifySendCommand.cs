using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class NotifySendCommand : AsyncCommand<NotifySendCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandArgument(0, "[message]")]
		[Description("Notification text.")]
		public string? MessageArgument { get; init; }

		[CommandArgument(1, "[logo]")]
		[Description("Optional icon id or built-in icon name.")]
		public string? LogoArgument { get; init; }

		[CommandOption("--message <TEXT>")]
		[Description("Notification text.")]
		public string? Message { get; init; }

		[CommandOption("--logo <ID>")]
		[Description("Notification logo id or built-in icon name.")]
		public string? Logo { get; init; }

		[CommandOption("--icon <NAME>")]
		[Description("Notification icon preset name from config.")]
		public string? Icon { get; init; }

		[CommandOption("--position <POS>")]
		[Description("Notification position (top|bottom|center|left|right|top-left|top-right|bottom-left|bottom-right).")]
		public string? Position { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		string message = ((!string.IsNullOrWhiteSpace(settings.Message)) ? settings.Message : settings.MessageArgument);
		if (string.IsNullOrWhiteSpace(message))
		{
			AnsiConsole.MarkupLine("[red]Notification text is required. Use `rgh notify \"text\" 14` or `--message`.[/]");
			return 1;
		}
		string logoValue = ((!string.IsNullOrWhiteSpace(settings.Logo)) ? settings.Logo : settings.LogoArgument);
		if (!NotifyHelpers.TryResolveLogo(settings.Icon, logoValue, out int logo, out string error))
		{
			AnsiConsole.MarkupLine("[red]" + Markup.Escape(error ?? "Invalid notify options.") + "[/]");
			return 1;
		}
		if (!NotifyHelpers.TryResolvePosition(settings.Position, out int position, out error))
		{
			AnsiConsole.MarkupLine("[red]" + Markup.Escape(error ?? "Invalid notify options.") + "[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			Jrpc2Client jrpc = new Jrpc2Client(client);
			if (!string.IsNullOrWhiteSpace(settings.Position))
			{
				await jrpc.SetNotificationPositionAsync(position, CancellationToken.None);
			}
			await jrpc.ShowNotificationAsync(logo, message, CancellationToken.None);
			string text = (string.IsNullOrWhiteSpace(settings.Position) ? "[grey]default[/]" : ("[aqua]" + Markup.Escape(settings.Position.Trim()) + "[/]"));
			AnsiConsole.MarkupLine("[green]Notification sent.[/] [grey]Icon:[/] [aqua]" + Markup.Escape(NotifyHelpers.DescribeLogo(logo)) + "[/] [grey]Position:[/] " + text);
			return 0;
		}, CancellationToken.None);
	}
}
