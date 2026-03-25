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

public sealed class SaveInjectCommand : AsyncCommand<SaveInjectCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--titleid <TITLEID>")]
		[Description("Title ID in hex (for example 4D530805).")]
		public string? TitleId { get; init; }

		[CommandOption("--profile <PROFILE>")]
		[Description("Destination 16-character profile ID.")]
		public string? ProfileId { get; init; }

		[CommandOption("--device <ROOT>")]
		[Description("Destination storage root, for example Hdd1 or Usb0 (default: Hdd1).")]
		public string? Device { get; init; }

		[CommandOption("--in <PATH>")]
		[Description("Local file or directory to upload.")]
		public string? InputPath { get; init; }

		[CommandOption("--remote-path <RELATIVE>")]
		[Description("Relative save path to use when --in points to a single file.")]
		public string? RemotePath { get; init; }

		[CommandOption("--overwrite")]
		[Description("Overwrite remote files that already exist.")]
		public bool Overwrite { get; init; }

		[CommandOption("--dry-run")]
		[Description("Show the remote paths that would be uploaded without writing anything.")]
		public bool DryRun { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!SaveHelpers.TryParseTitleId(settings.TitleId, out var titleId))
		{
			AnsiConsole.MarkupLine("[red]--titleid is required.[/]");
			return 1;
		}
		if (string.IsNullOrWhiteSpace(settings.ProfileId) || !SaveHelpers.IsProfileId(settings.ProfileId))
		{
			AnsiConsole.MarkupLine("[red]--profile must be a 16-character profile id.[/]");
			return 1;
		}
		if (string.IsNullOrWhiteSpace(settings.InputPath))
		{
			AnsiConsole.MarkupLine("[red]--in is required.[/]");
			return 1;
		}
		string fullPath = Path.GetFullPath(settings.InputPath);
		bool flag = Directory.Exists(fullPath);
		bool flag2 = File.Exists(fullPath);
		if (!flag && !flag2)
		{
			AnsiConsole.MarkupLine("[red]Input path was not found.[/]");
			return 1;
		}
		string device = (string.IsNullOrWhiteSpace(settings.Device) ? "Hdd1" : settings.Device.Trim());
		string titleRoot = SaveHelpers.BuildTitleRoot(device, settings.ProfileId, titleId);
		List<SaveHelpers.SaveUploadRecord> uploads = (flag ? SaveHelpers.BuildUploadPlanFromDirectory(fullPath, titleRoot) : SaveHelpers.BuildUploadPlanFromFile(fullPath, titleRoot, settings.RemotePath));
		if (uploads.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No files found to upload.[/]");
			return 0;
		}
		if (settings.Json || settings.DryRun)
		{
			object value = uploads.Select((SaveHelpers.SaveUploadRecord u) => new { u.LocalPath, u.RemotePath, u.Size }).ToList();
			if (settings.Json)
			{
				CliOutput.EmitJson(value);
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Save Inject Preview[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[green]Local[/]"));
			table.AddColumn(new TableColumn("[cyan]Remote[/]"));
			table.AddColumn(new TableColumn("[grey]Size[/]"));
			foreach (SaveHelpers.SaveUploadRecord item in uploads)
			{
				table.AddRow("[green]" + Markup.Escape(item.LocalPath) + "[/]", "[cyan]" + Markup.Escape(item.RemotePath) + "[/]", "[grey]" + FtpHelpers.FormatBytes(item.Size) + "[/]");
			}
			AnsiConsole.Write(table);
			return 0;
		}
		(string, int, string, string, int) tuple = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeout = tuple.Item5;
		List<SaveHelpers.SaveUploadRecord> plan = new List<SaveHelpers.SaveUploadRecord>();
		int skipped = 0;
		foreach (SaveHelpers.SaveUploadRecord upload in uploads)
		{
			if (!settings.Overwrite && await SaveHelpers.RemoteFileExistsAsync(ip, port, user, pass, timeout, upload.RemotePath))
			{
				skipped++;
			}
			else
			{
				plan.Add(upload);
			}
		}
		if (plan.Count == 0)
		{
			OperationFeedback.WriteWarning("Save inject skipped", "all target files already exist in [grey]" + Markup.Escape(titleRoot) + "[/]");
			return 0;
		}
		await CliOutput.RunBatchProgressAsync("Save inject " + SaveHelpers.FormatTitleLabel(titleId), plan.Select((SaveHelpers.SaveUploadRecord item) => new CliOutput.TransferBatchItem(item.RemotePath, item.Size)).ToList(), async delegate(TransferBatchScope batch)
		{
			foreach (SaveHelpers.SaveUploadRecord item2 in plan)
			{
				batch.StartFile(item2.RemotePath, item2.Size);
				Progress<long> progress = new Progress<long>(delegate(long fileBytes)
				{
					batch.ReportFileProgress(fileBytes);
				});
				await SaveHelpers.UploadFileAsync(ip, port, user, pass, timeout, item2.LocalPath, item2.RemotePath, progress);
				batch.CompleteFile();
			}
		});
		OperationFeedback.WriteSuccess("Save inject complete", $"[green]{plan.Count}[/] file(s)  [silver]{FtpHelpers.FormatBytes(plan.Sum((SaveHelpers.SaveUploadRecord item) => item.Size))}[/] -> [white]{Markup.Escape(titleRoot)}[/]");
		if (skipped > 0)
		{
			OperationFeedback.WriteWarning("Save inject skipped existing files", skipped.ToString(CultureInfo.InvariantCulture));
		}
		return 0;
	}
}
