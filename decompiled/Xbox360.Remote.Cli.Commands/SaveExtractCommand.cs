using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class SaveExtractCommand : AsyncCommand<SaveExtractCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--titleid <TITLEID>")]
		[LocalizedDescription("Title ID in hex (for example 4D530805).")]
		public string? TitleId { get; init; }

		[CommandOption("--profile <PROFILE>")]
		[LocalizedDescription("Restrict to one 16-character profile ID.")]
		public string? ProfileId { get; init; }

		[CommandOption("--out <DIR>")]
		[LocalizedDescription("Destination directory.")]
		public string? OutputDirectory { get; init; }

		[CommandOption("--overwrite")]
		[LocalizedDescription("Overwrite files that already exist.")]
		public bool Overwrite { get; init; }

		[CommandOption("--device <ROOTS>")]
		[LocalizedDescription("Optional comma-separated storage roots, for example Hdd1 or Hdd1,Usb0.")]
		public string? Devices { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!SaveHelpers.TryParseTitleId(settings.TitleId, out var titleId))
		{
			AnsiConsole.MarkupLine("[red]--titleid is required.[/]");
			return 1;
		}
		if (string.IsNullOrWhiteSpace(settings.OutputDirectory))
		{
			AnsiConsole.MarkupLine("[red]--out is required.[/]");
			return 1;
		}
		(string, int, string, string, int) tuple = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeout = tuple.Item5;
		List<SaveHelpers.SaveFileRecord> list = await SaveHelpers.EnumerateSaveFilesAsync(ip, port, user, pass, timeout, titleId, settings.ProfileId, settings.Devices);
		if (list.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No save files found for this title.[/]");
			return 0;
		}
		string path = SaveHelpers.BuildOutputFolderName(titleId);
		string outputRoot = Path.Combine(settings.OutputDirectory, path);
		Directory.CreateDirectory(outputRoot);
		List<(SaveHelpers.SaveFileRecord File, string LocalPath)> plan = new List<(SaveHelpers.SaveFileRecord, string)>();
		int skipped = 0;
		foreach (SaveHelpers.SaveFileRecord item in list)
		{
			string text = Path.Combine(outputRoot, item.Device, item.ProfileId, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
			string directoryName = Path.GetDirectoryName(text);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			if (!settings.Overwrite && File.Exists(text))
			{
				skipped++;
			}
			else
			{
				plan.Add((item, text));
			}
		}
		if (plan.Count == 0)
		{
			OperationFeedback.WriteWarning("Save extract skipped", "all matching files already exist in [grey]" + Markup.Escape(outputRoot) + "[/]");
			return 0;
		}
		await CliOutput.RunBatchProgressAsync("Save extract " + SaveHelpers.FormatTitleLabel(titleId), plan.Select<(SaveHelpers.SaveFileRecord, string), CliOutput.TransferBatchItem>(((SaveHelpers.SaveFileRecord File, string LocalPath) item) => new CliOutput.TransferBatchItem(item.File.RemotePath, item.File.Size)).ToList(), async delegate(TransferBatchScope batch)
		{
			foreach (var (saveFileRecord, localPath) in plan)
			{
				batch.StartFile(saveFileRecord.RemotePath, saveFileRecord.Size);
				Progress<long> progress = new Progress<long>(delegate(long value)
				{
					batch.ReportFileProgress(value);
				});
				await SaveHelpers.DownloadFileAsync(ip, port, user, pass, timeout, saveFileRecord.RemotePath, localPath, progress);
				batch.CompleteFile();
			}
		});
		OperationFeedback.WriteSuccess("Save extract complete", $"[green]{plan.Count}[/] file(s)  [silver]{FtpHelpers.FormatBytes(plan.Sum<(SaveHelpers.SaveFileRecord, string)>(((SaveHelpers.SaveFileRecord File, string LocalPath) item) => item.File.Size))}[/] -> [white]{Markup.Escape(outputRoot)}[/]");
		if (skipped > 0)
		{
			OperationFeedback.WriteWarning("Save extract skipped existing files", skipped.ToString(CultureInfo.InvariantCulture));
		}
		return 0;
	}
}
