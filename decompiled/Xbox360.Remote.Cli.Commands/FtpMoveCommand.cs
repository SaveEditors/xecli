using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FtpMoveCommand : AsyncCommand<FtpMoveCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--from <PATH>")]
		public string? From { get; init; }

		[CommandOption("--to <PATH>")]
		public string? To { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.From) || string.IsNullOrWhiteSpace(settings.To))
		{
			AnsiConsole.MarkupLine("[red]--from and --to are required.[/]");
			return 1;
		}
		return await FtpHelpers.WithClientAsync(settings, async delegate(AsyncFtpClient client)
		{
			string from = FtpHelpers.NormalizePath(settings.From);
			string to = FtpHelpers.NormalizePath(settings.To);
			if (await client.DirectoryExists(from))
			{
				await client.MoveDirectory(from, to);
				AnsiConsole.MarkupLine("[green]Moved directory[/] " + Markup.Escape(from) + " -> " + Markup.Escape(to));
				return 0;
			}
			await client.MoveFile(from, to);
			AnsiConsole.MarkupLine("[green]Moved file[/] " + Markup.Escape(from) + " -> " + Markup.Escape(to));
			return 0;
		}, CancellationToken.None);
	}
}
