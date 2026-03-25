using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FtpDeleteCommand : AsyncCommand<FtpDeleteCommand.Settings>
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
			try
			{
				if (await client.DirectoryExists(remote))
				{
					await client.DeleteDirectory(remote);
					AnsiConsole.MarkupLine("[green]Deleted directory[/] " + Markup.Escape(remote));
					return 0;
				}
			}
			catch
			{
			}
			try
			{
				if (await client.FileExists(remote))
				{
					await client.DeleteFile(remote);
					AnsiConsole.MarkupLine("[green]Deleted file[/] " + Markup.Escape(remote));
					return 0;
				}
			}
			catch
			{
			}
			if ((await client.Execute("RMD " + remote)).Success)
			{
				AnsiConsole.MarkupLine("[green]Deleted directory[/] " + Markup.Escape(remote));
				return 0;
			}
			FtpReply ftpReply = await client.Execute("DELE " + remote);
			if (ftpReply.Success)
			{
				AnsiConsole.MarkupLine("[green]Deleted file[/] " + Markup.Escape(remote));
				return 0;
			}
			throw new IOException("FTP delete failed: " + ftpReply.Code + " " + ftpReply.Message);
		}, CancellationToken.None);
	}
}
