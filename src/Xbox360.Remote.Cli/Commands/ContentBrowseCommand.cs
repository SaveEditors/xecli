using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using NoDev.Xdbf;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.LocalProfiles;

namespace Xbox360.Remote.Cli.Commands;

internal static class ContentBrowseHelpers
{
	private const string UnsupportedMessage = "Unsupported content file. Use a profile package or dashboard GPD.";

	public static int Execute(string inputPath, bool json)
	{
		if (!LocalContentHelpers.TryResolveExistingFile(inputPath, out string fullPath, out string error))
		{
			return LocalContentHelpers.Fail(error);
		}

		if (TryBrowseProfilePackage(fullPath, json))
		{
			return 0;
		}

		if (TryBrowseDataFile(fullPath, json))
		{
			return 0;
		}

		return LocalContentHelpers.Fail(UnsupportedMessage);
	}

	private static bool TryBrowseProfilePackage(string fullPath, bool json)
	{
		ProfilePackage? profile = null;
		try
		{
			profile = new ProfilePackage(fullPath);
			IReadOnlyList<ProfileBrowseEntry> files = profile.Entries
				.Where(static entry => !entry.IsDirectory)
				.OrderBy(static entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
				.Select(static entry => new ProfileBrowseEntry(
					entry.FullPath,
					entry.Size,
					entry.Contiguous,
					entry.FirstBlockNumber,
					entry.AllocationBlocks))
				.ToList();

			if (json)
			{
				CliOutput.EmitJson(new
				{
					Path = fullPath,
					Format = "profile-package",
					Files = files
				});
				return true;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Content Files[/]").RuleStyle("grey"));
			if (files.Count == 0)
			{
				AnsiConsole.MarkupLine("[yellow]No files found.[/]");
				return true;
			}

			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[green]Path[/]"));
			table.AddColumn(new TableColumn("[cyan]Size[/]"));
			table.AddColumn(new TableColumn("[grey]Blocks[/]"));
			foreach (ProfileBrowseEntry file in files)
			{
				table.AddRow(
					"[green]" + Markup.Escape(file.Path) + "[/]",
					"[cyan]" + Markup.Escape(FtpHelpers.FormatBytes(file.Size)) + "[/]",
					"[grey]" + file.AllocationBlocks.ToString(CultureInfo.InvariantCulture) + "[/]");
			}

			AnsiConsole.Write(table);
			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			profile?.Dispose();
		}
	}

	private static bool TryBrowseDataFile(string fullPath, bool json)
	{
		DataFile? dataFile = null;
		try
		{
			dataFile = new DataFile(fullPath, DataFileOrigin.Profile);
			IReadOnlyList<DataBrowseEntry> records = LocalContentHelpers
				.GetRecords(dataFile, null)
				.Select(record => new DataBrowseEntry(
					record.Namespace.ToString(),
					$"0x{(ushort)record.Namespace:X4}",
					LocalContentHelpers.FormatUInt64(record.ID),
					record.Size,
					record.Offset))
				.ToList();

			if (json)
			{
				CliOutput.EmitJson(new
				{
					Path = fullPath,
					Format = "xdbf",
					Records = records
				});
				return true;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]XDBF Records[/]").RuleStyle("grey"));
			if (records.Count == 0)
			{
				AnsiConsole.MarkupLine("[yellow]No records found.[/]");
				return true;
			}

			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[cyan]Namespace[/]"));
			table.AddColumn(new TableColumn("[green]ID[/]"));
			table.AddColumn(new TableColumn("[grey]Size[/]"));
			table.AddColumn(new TableColumn("[grey]Offset[/]"));
			foreach (DataBrowseEntry record in records)
			{
				table.AddRow(
					"[cyan]" + Markup.Escape(record.Namespace) + $" ([grey]{record.NamespaceId}[/])[/]",
					"[green]" + record.Id + "[/]",
					"[grey]" + record.Size.ToString(CultureInfo.InvariantCulture) + "[/]",
					"[grey]0x" + record.Offset.ToString("X8", CultureInfo.InvariantCulture) + "[/]");
			}

			AnsiConsole.Write(table);
			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			dataFile?.Close();
		}
	}

	internal sealed record ProfileBrowseEntry(string Path, uint Size, bool Contiguous, uint FirstBlockNumber, uint AllocationBlocks);

	internal sealed record DataBrowseEntry(string Namespace, string NamespaceId, string Id, int Size, int Offset);
}

public sealed class ContentBrowseCommand : Command<ContentBrowseCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<FILE>")]
		[LocalizedDescription("Path to a profile package or dashboard GPD file.")]
		public string FilePath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ContentBrowseHelpers.Execute(settings.FilePath, settings.Json);
	}
}
