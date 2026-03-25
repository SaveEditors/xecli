using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FtpPutCommand : AsyncCommand<FtpPutCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--path <PATH>")]
		public string? Path { get; init; }

		[CommandOption("--in <FILE>")]
		public string? Input { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Input))
		{
			AnsiConsole.MarkupLine("[red]--path and --in are required.[/]");
			return 1;
		}
		if (!File.Exists(settings.Input))
		{
			AnsiConsole.MarkupLine("[red]Input file not found.[/]");
			return 1;
		}
		(string, int, string, string, int) tuple = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeout = tuple.Item5;
		string remote = FtpHelpers.NormalizePath(settings.Path);
		string local = settings.Input;
		long size = new FileInfo(local).Length;
		await CliOutput.RunWithProgressAsync("FTP upload " + Markup.Escape(remote), (size > 0) ? new long?((uint)size) : ((long?)null), async delegate(IProgress<CliOutput.TransferProgressUpdate> progress)
		{
			Progress<FtpProgress> progress2 = new Progress<FtpProgress>(delegate(FtpProgress p)
			{
				if (p.TransferredBytes > 0)
				{
					progress.Report(new CliOutput.TransferProgressUpdate(p.TransferredBytes, "sending"));
				}
			});
			await FtpHelpers.UploadFileVerifiedAsync(ip, port, user, pass, timeout, local, remote, ensureRemoteDirectory: true, progress2, CancellationToken.None);
		});
		OperationFeedback.WriteSuccess("FTP upload complete", $"[white]{Markup.Escape(local)}[/] -> [cyan]{Markup.Escape(remote)}[/] [silver]({FtpHelpers.FormatBytes(size)})[/]");
		return 0;
	}
}
