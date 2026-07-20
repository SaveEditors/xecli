using System.ComponentModel;
using System.IO;
using NoDev.Xdbf;
using NoDev.Xdbf.Records;
using Spectre.Console;
using Spectre.Console.Cli;

using XdbfNamespace = NoDev.Xdbf.Namespace;

namespace Xbox360.Remote.Cli.Commands;

internal static class XdbfCommandHelpers
{
	public static int ExecuteWithDataFile(string inputPath, DataFileOrigin origin, Func<string, DataFile, int> action)
	{
		if (!LocalContentHelpers.TryResolveExistingFile(inputPath, out string fullPath, out string error))
		{
			return LocalContentHelpers.Fail(error);
		}

		DataFile? dataFile = null;
		try
		{
			dataFile = new DataFile(fullPath, origin);
			return action(fullPath, dataFile);
		}
		catch (Exception)
		{
			return LocalContentHelpers.Fail("Unable to open XDBF package.");
		}
		finally
		{
			dataFile?.Close();
		}
	}
}

public sealed class XdbfListCommand : Command<XdbfListCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<FILE>")]
		[LocalizedDescription("Path to the GPD/XDBF file.")]
		public string FilePath { get; init; } = string.Empty;

		[CommandOption("--namespace <NAMESPACE>")]
		[LocalizedDescription("Optional namespace filter: achievements, images, settings, titles, strings, avatar, or the numeric namespace ID.")]
		public string? Namespace { get; init; }

		[CommandOption("--origin <ORIGIN>")]
		[LocalizedDescription("Sync record origin: profile or pec (default: profile).")]
		public string? Origin { get; init; }

		[CommandOption("--show-sync")]
		[LocalizedDescription("Include pending-sync status for each record.")]
		public bool ShowSync { get; init; }

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (!LocalContentHelpers.TryParseOrigin(settings.Origin, out DataFileOrigin origin))
		{
			return LocalContentHelpers.Fail("Invalid --origin. Use `profile` or `pec`.");
		}

		XdbfNamespace? filter = null;
		if (!string.IsNullOrWhiteSpace(settings.Namespace))
		{
			if (!LocalContentHelpers.TryParseNamespace(settings.Namespace, out XdbfNamespace parsed))
			{
				return LocalContentHelpers.Fail("Invalid --namespace.");
			}

			filter = parsed;
		}

		return XdbfCommandHelpers.ExecuteWithDataFile(settings.FilePath, origin, (fullPath, dataFile) =>
		{
			IReadOnlyList<DataFileRecord> records = LocalContentHelpers.GetRecords(dataFile, filter);
			if (settings.Json)
			{
				CliOutput.EmitJson(records.Select(record => new
				{
					Namespace = record.Namespace.ToString(),
					NamespaceId = $"0x{(ushort)record.Namespace:X4}",
					Id = LocalContentHelpers.FormatUInt64(record.ID),
					record.Offset,
					record.Size,
					PendingSync = settings.ShowSync ? dataFile.IsPendingSync(record.Namespace, record.ID) : (bool?)null
				}));
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]XDBF Records[/]").RuleStyle("grey"));
			if (records.Count == 0)
			{
				AnsiConsole.MarkupLine("[yellow]No matching records found.[/]");
				return 0;
			}

			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[cyan]Namespace[/]"));
			table.AddColumn(new TableColumn("[green]ID[/]"));
			table.AddColumn(new TableColumn("[grey]Size[/]"));
			table.AddColumn(new TableColumn("[grey]Offset[/]"));
			if (settings.ShowSync)
			{
				table.AddColumn(new TableColumn("[grey]Pending Sync[/]"));
			}

			foreach (DataFileRecord record in records)
			{
				string[] row = settings.ShowSync
					? new[]
					{
						"[cyan]" + record.Namespace + $" (0x{(ushort)record.Namespace:X4})[/]",
						"[green]" + LocalContentHelpers.FormatUInt64(record.ID) + "[/]",
						"[grey]" + record.Size + "[/]",
						"[grey]0x" + record.Offset.ToString("X8") + "[/]",
						dataFile.IsPendingSync(record.Namespace, record.ID) ? "[yellow]yes[/]" : "[grey]no[/]"
					}
					: new[]
					{
						"[cyan]" + record.Namespace + $" (0x{(ushort)record.Namespace:X4})[/]",
						"[green]" + LocalContentHelpers.FormatUInt64(record.ID) + "[/]",
						"[grey]" + record.Size + "[/]",
						"[grey]0x" + record.Offset.ToString("X8") + "[/]"
					};
				table.AddRow(row);
			}

			AnsiConsole.Write(table);
			AnsiConsole.MarkupLine("[grey]Source:[/] " + Markup.Escape(fullPath));
			return 0;
		});
	}
}

public sealed class XdbfGetCommand : Command<XdbfGetCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<FILE>")]
		[LocalizedDescription("Path to the GPD/XDBF file.")]
		public string FilePath { get; init; } = string.Empty;

		[CommandArgument(1, "<NAMESPACE>")]
		[LocalizedDescription("Namespace name or numeric ID.")]
		public string Namespace { get; init; } = string.Empty;

		[CommandArgument(2, "<ID>")]
		[LocalizedDescription("Record ID, in decimal or hex.")]
		public string Id { get; init; } = string.Empty;

		[CommandOption("--origin <ORIGIN>")]
		[LocalizedDescription("Sync record origin: profile or pec (default: profile).")]
		public string? Origin { get; init; }

		[CommandOption("--out <FILE>")]
		[LocalizedDescription("Write the record payload to a file instead of rendering a hex preview.")]
		public string? OutFile { get; init; }

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (!LocalContentHelpers.TryParseOrigin(settings.Origin, out DataFileOrigin origin))
		{
			return LocalContentHelpers.Fail("Invalid --origin. Use `profile` or `pec`.");
		}

		if (!LocalContentHelpers.TryParseNamespace(settings.Namespace, out XdbfNamespace ns))
		{
			return LocalContentHelpers.Fail("Invalid --namespace.");
		}

		if (!LocalContentHelpers.TryParseUlong(settings.Id, out ulong id))
		{
			return LocalContentHelpers.Fail("Invalid record ID.");
		}

		return XdbfCommandHelpers.ExecuteWithDataFile(settings.FilePath, origin, (_, dataFile) =>
		{
			DataFileRecord? record = dataFile.GetRecord(ns, id);
			if (record == null)
			{
				return LocalContentHelpers.Fail("Record not found.");
			}

			byte[] data = dataFile.GetData(record);
			if (!string.IsNullOrWhiteSpace(settings.OutFile))
			{
				string outputPath = Path.GetFullPath(settings.OutFile);
				string? directory = Path.GetDirectoryName(outputPath);
				if (!string.IsNullOrWhiteSpace(directory))
				{
					Directory.CreateDirectory(directory);
				}

				File.WriteAllBytes(outputPath, data);

				if (settings.Json)
				{
					CliOutput.EmitJson(new
					{
						Namespace = ns.ToString(),
						Id = LocalContentHelpers.FormatUInt64(id),
						Size = data.Length,
						OutFile = outputPath,
						PendingSync = dataFile.IsPendingSync(ns, id)
					});
					return 0;
				}

				AnsiConsole.MarkupLine("[green]Record exported.[/]");
				AnsiConsole.MarkupLine("[grey]Output:[/] " + Markup.Escape(outputPath));
				return 0;
			}

			int previewSize = Math.Min(data.Length, LocalContentHelpers.HexPreviewLimit);
			string preview = LocalContentHelpers.Hex(data[..previewSize]);
			bool truncated = data.Length > previewSize;

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Namespace = ns.ToString(),
					NamespaceId = $"0x{(ushort)ns:X4}",
					Id = LocalContentHelpers.FormatUInt64(id),
					record.Offset,
					record.Size,
					PendingSync = dataFile.IsPendingSync(ns, id),
					PreviewBytes = previewSize,
					PreviewHex = preview,
					Truncated = truncated
				});
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]XDBF Record[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Field[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			table.AddRow("[grey]Namespace[/]", "[cyan]" + ns + $" (0x{(ushort)ns:X4})[/]");
			table.AddRow("[grey]ID[/]", "[green]" + LocalContentHelpers.FormatUInt64(id) + "[/]");
			table.AddRow("[grey]Size[/]", "[grey]" + data.Length + " bytes[/]");
			table.AddRow("[grey]Offset[/]", "[grey]0x" + record.Offset.ToString("X8") + "[/]");
			table.AddRow("[grey]Pending Sync[/]", dataFile.IsPendingSync(ns, id) ? "[yellow]yes[/]" : "[grey]no[/]");
			AnsiConsole.Write(table);
			LocalContentHelpers.RenderHexPreview(data);
			return 0;
		});
	}
}

public sealed class XdbfExtractCommand : Command<XdbfExtractCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<FILE>")]
		[LocalizedDescription("Path to the GPD/XDBF file.")]
		public string FilePath { get; init; } = string.Empty;

		[CommandArgument(1, "<OUTDIR>")]
		[LocalizedDescription("Directory to write extracted records into.")]
		public string OutDir { get; init; } = string.Empty;

		[CommandOption("--namespace <NAMESPACE>")]
		[LocalizedDescription("Optional namespace filter: achievements, images, settings, titles, strings, avatar, or the numeric namespace ID.")]
		public string? Namespace { get; init; }

		[CommandOption("--origin <ORIGIN>")]
		[LocalizedDescription("Sync record origin: profile or pec (default: profile).")]
		public string? Origin { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (!LocalContentHelpers.TryParseOrigin(settings.Origin, out DataFileOrigin origin))
		{
			return LocalContentHelpers.Fail("Invalid --origin. Use `profile` or `pec`.");
		}

		XdbfNamespace? filter = null;
		if (!string.IsNullOrWhiteSpace(settings.Namespace))
		{
			if (!LocalContentHelpers.TryParseNamespace(settings.Namespace, out XdbfNamespace parsed))
			{
				return LocalContentHelpers.Fail("Invalid --namespace.");
			}

			filter = parsed;
		}

		return XdbfCommandHelpers.ExecuteWithDataFile(settings.FilePath, origin, (_, dataFile) =>
		{
			string outputDir = Path.GetFullPath(settings.OutDir);
			Directory.CreateDirectory(outputDir);
			IReadOnlyList<DataFileRecord> records = LocalContentHelpers.GetRecords(dataFile, filter);
			foreach (DataFileRecord record in records)
			{
				string extension = record.Namespace switch
				{
					XdbfNamespace.Images => ".png",
					XdbfNamespace.Strings => ".txt",
					_ => ".bin"
				};
				string fileName = $"{record.Namespace} - {record.ID:X16}{extension}";
				string destination = Path.Combine(outputDir, fileName);
				File.WriteAllBytes(destination, dataFile.GetData(record));
			}

			AnsiConsole.MarkupLine("[green]Records extracted.[/]");
			AnsiConsole.MarkupLine("[grey]Count:[/] " + records.Count);
			AnsiConsole.MarkupLine("[grey]Output:[/] " + Markup.Escape(outputDir));
			return 0;
		});
	}
}

public sealed class XdbfSyncStatusCommand : Command<XdbfSyncStatusCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<FILE>")]
		[LocalizedDescription("Path to the GPD/XDBF file.")]
		public string FilePath { get; init; } = string.Empty;

		[CommandOption("--namespace <NAMESPACE>")]
		[LocalizedDescription("Optional namespace filter: achievements, images, settings, titles, strings, avatar, or the numeric namespace ID.")]
		public string? Namespace { get; init; }

		[CommandOption("--origin <ORIGIN>")]
		[LocalizedDescription("Sync record origin: profile or pec (default: profile).")]
		public string? Origin { get; init; }

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (!LocalContentHelpers.TryParseOrigin(settings.Origin, out DataFileOrigin origin))
		{
			return LocalContentHelpers.Fail("Invalid --origin. Use `profile` or `pec`.");
		}

		XdbfNamespace? filter = null;
		if (!string.IsNullOrWhiteSpace(settings.Namespace))
		{
			if (!LocalContentHelpers.TryParseNamespace(settings.Namespace, out XdbfNamespace parsed))
			{
				return LocalContentHelpers.Fail("Invalid --namespace.");
			}

			filter = parsed;
		}

		return XdbfCommandHelpers.ExecuteWithDataFile(settings.FilePath, origin, (_, dataFile) =>
		{
			var pending = LocalContentHelpers
				.GetRecords(dataFile, filter)
				.Where(record => dataFile.IsPendingSync(record.Namespace, record.ID))
				.ToList();

			if (settings.Json)
			{
				CliOutput.EmitJson(pending.Select(record => new
				{
					Namespace = record.Namespace.ToString(),
					NamespaceId = $"0x{(ushort)record.Namespace:X4}",
					Id = LocalContentHelpers.FormatUInt64(record.ID),
					record.Size,
					record.Offset
				}));
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]XDBF Sync Status[/]").RuleStyle("grey"));
			if (pending.Count == 0)
			{
				AnsiConsole.MarkupLine("[green]No pending sync records.[/]");
				return 0;
			}

			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[cyan]Namespace[/]"));
			table.AddColumn(new TableColumn("[green]ID[/]"));
			table.AddColumn(new TableColumn("[grey]Size[/]"));
			table.AddColumn(new TableColumn("[grey]Offset[/]"));
			foreach (DataFileRecord record in pending)
			{
				table.AddRow(
					"[cyan]" + record.Namespace + $" (0x{(ushort)record.Namespace:X4})[/]",
					"[green]" + LocalContentHelpers.FormatUInt64(record.ID) + "[/]",
					"[grey]" + record.Size + "[/]",
					"[grey]0x" + record.Offset.ToString("X8") + "[/]");
			}

			AnsiConsole.Write(table);
			return 0;
		});
	}
}

