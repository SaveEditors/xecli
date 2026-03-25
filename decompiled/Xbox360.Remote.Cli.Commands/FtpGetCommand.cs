using System;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FtpGetCommand : AsyncCommand<FtpGetCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--path <PATH>")]
		public string? Path { get; init; }

		[CommandOption("--out <FILE>")]
		public string? Output { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Output))
		{
			AnsiConsole.MarkupLine("[red]--path and --out are required.[/]");
			return 1;
		}
		return await FtpHelpers.WithClientAsync(settings, async delegate(AsyncFtpClient client)
		{
			string remote = FtpHelpers.NormalizePath(settings.Path);
			string local = settings.Output;
			long size = (await FtpHelpers.TryGetFileSizeAsync(client, remote)).GetValueOrDefault();
			await CliOutput.RunWithProgressAsync("FTP download " + Markup.Escape(remote), (size > 0) ? new long?((uint)size) : ((long?)null), async delegate(IProgress<CliOutput.TransferProgressUpdate> progress)
			{
				Progress<FtpProgress> progress2 = new Progress<FtpProgress>(delegate(FtpProgress p)
				{
					if (p.TransferredBytes > 0)
					{
						progress.Report(new CliOutput.TransferProgressUpdate(p.TransferredBytes, "receiving"));
					}
				});
				await client.DownloadFile(local, remote, FtpLocalExists.Overwrite, FtpVerify.None, progress2);
			});
			OperationFeedback.WriteSuccess("FTP download complete", $"[cyan]{Markup.Escape(remote)}[/] -> [white]{Markup.Escape(local)}[/] [silver]({FtpHelpers.FormatBytes(size)})[/]");
			return 0;
		}, CancellationToken.None);
	}
}
