using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FtpMkdirCommand : AsyncCommand<FtpMkdirCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--path <PATH>")]
		public string? Path { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Path))
		{
			AnsiConsole.MarkupLine("[red]--path is required.[/]");
			return 1;
		}
		return await FtpHelpers.WithClientAsync(settings, async delegate(AsyncFtpClient client)
		{
			string remote = FtpHelpers.NormalizePath(settings.Path);
			await client.CreateDirectory(remote);
			AnsiConsole.MarkupLine("[green]Created directory[/] " + Markup.Escape(remote));
			return 0;
		}, CancellationToken.None);
	}
}
