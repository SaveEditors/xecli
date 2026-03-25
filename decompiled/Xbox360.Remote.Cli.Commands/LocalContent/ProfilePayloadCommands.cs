using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.LocalProfiles;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ProfileAccountExtractCommand : Command<ProfileAccountExtractCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandArgument(1, "<OUT>")]
		[LocalizedDescription("Path to write the raw Account payload.")]
		public string OutputPath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasAccount)
			{
				return LocalContentHelpers.Fail("Profile package does not contain an Account file.");
			}

			byte[] data = profile.ReadAccountBytes();
			string outputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.OutputPath));
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
					File = "Account",
					Size = data.Length,
					OutFile = outputPath
				});
				return 0;
			}

			OperationFeedback.WriteSuccess(
				"Account extracted",
				"[grey]Size:[/] [cyan]" + data.Length.ToString(CultureInfo.InvariantCulture) + "[/] bytes [grey]|[/] [white]" + Markup.Escape(outputPath) + "[/]");
			return 0;
		});
	}
}

public sealed class ProfileGpdListCommand : Command<ProfileGpdListCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			var titleRecords = profile.HasDashboardData
				? profile.ReadTitles().ToDictionary(static title => title.TitleId)
				: new Dictionary<uint, ProfileTitleInfo>();

			var gpds = profile.Entries
				.Where(entry =>
					!entry.IsDirectory &&
					entry.Name.EndsWith(".gpd", StringComparison.OrdinalIgnoreCase) &&
					entry.Name.Length == 12 &&
					uint.TryParse(entry.Name[..^4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint _))
				.Select(entry =>
				{
					uint titleId = uint.Parse(entry.Name[..^4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
					titleRecords.TryGetValue(titleId, out ProfileTitleInfo? titleRecord);
					string titleName = titleRecord?.TitleName ?? string.Empty;
					string resolvedTitleName = ResolveGpdTitleName(titleId, titleName);
					return new
					{
						entry.FullPath,
						entry.Size,
						TitleId = LocalContentHelpers.FormatUInt32(titleId),
						Kind = titleId == ProfilePackage.DashboardTitleId ? "Dashboard" : "Title",
						TitleName = titleName,
						ResolvedTitleName = resolvedTitleName,
						HasTitleRecord = titleRecord != null
					};
				})
				.OrderBy(static item => item.Kind, StringComparer.OrdinalIgnoreCase)
				.ThenBy(static item => item.TitleId, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (settings.Json)
			{
				CliOutput.EmitJson(gpds);
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Profile GPD Files[/]").RuleStyle("grey"));
			if (gpds.Count == 0)
			{
				AnsiConsole.MarkupLine("[yellow]No embedded GPD files found.[/]");
				return 0;
			}

			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[cyan]Type[/]"));
			table.AddColumn(new TableColumn("[green]Title ID[/]"));
			table.AddColumn(new TableColumn("[white]Name[/]"));
			table.AddColumn(new TableColumn("[grey]Size[/]"));
			table.AddColumn(new TableColumn("[grey]Path[/]"));
			foreach (var gpd in gpds)
			{
				table.AddRow(
					"[cyan]" + gpd.Kind + "[/]",
					"[green]" + gpd.TitleId + "[/]",
					"[white]" + Markup.Escape(gpd.ResolvedTitleName) + "[/]",
					"[grey]" + FtpHelpers.FormatBytes(gpd.Size) + "[/]",
					"[grey]" + Markup.Escape(gpd.FullPath) + "[/]");
			}

			AnsiConsole.Write(table);
			return 0;
		});
	}

	private static string ResolveGpdTitleName(uint titleId, string titleName)
	{
		if (!string.IsNullOrWhiteSpace(titleName))
		{
			return titleName;
		}

		if (titleId == ProfilePackage.DashboardTitleId)
		{
			return "Xbox Dashboard";
		}

		if (TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? entry) &&
			entry != null &&
			!string.IsNullOrWhiteSpace(entry.Name))
		{
			return entry.Name;
		}

		return LocalContentHelpers.FormatUInt32(titleId);
	}
}

public sealed class ProfileGpdExtractCommand : Command<ProfileGpdExtractCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandArgument(1, "<OUT>")]
		[LocalizedDescription("Path to write the selected GPD.")]
		public string OutputPath { get; init; } = string.Empty;

		[CommandOption("--dashboard")]
		[LocalizedDescription("Extract the dashboard GPD (FFFE07D1.gpd).")]
		public bool Dashboard { get; init; }

		[CommandOption("--titleid <TITLEID>")]
		[LocalizedDescription("Extract a specific title GPD by title ID.")]
		public string? TitleId { get; init; }

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		uint titleId = 0;
		bool hasTitleId = !string.IsNullOrWhiteSpace(settings.TitleId);
		if (hasTitleId && !ProfileCommandHelpers.TryParseTitleId(settings.TitleId, out titleId))
		{
			return LocalContentHelpers.Fail("Invalid --titleid.");
		}

		if (settings.Dashboard == hasTitleId)
		{
			return LocalContentHelpers.Fail("Specify exactly one target: `--dashboard` or `--titleid`.");
		}

		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			byte[] data;
			string sourceName;
			uint resolvedTitleId;
			if (settings.Dashboard)
			{
				if (!profile.HasDashboardData)
				{
					return LocalContentHelpers.Fail("Profile package does not contain FFFE07D1.gpd.");
				}

				resolvedTitleId = ProfilePackage.DashboardTitleId;
				sourceName = $"{ProfilePackage.DashboardTitleId:X8}.gpd";
				data = profile.ReadDashboardDataBytes();
			}
			else
			{
				if (!profile.HasTitleDataFile(titleId))
				{
					return LocalContentHelpers.Fail("Profile package does not contain " + titleId.ToString("X8") + ".gpd.");
				}

				resolvedTitleId = titleId;
				sourceName = $"{titleId:X8}.gpd";
				data = profile.ReadTitleDataBytes(titleId);
			}

			string outputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.OutputPath));
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
					TitleId = LocalContentHelpers.FormatUInt32(resolvedTitleId),
					File = sourceName,
					Size = data.Length,
					OutFile = outputPath
				});
				return 0;
			}

			OperationFeedback.WriteSuccess(
				"GPD extracted",
				"[green]" + sourceName + "[/] [grey]|[/] [cyan]" + data.Length.ToString(CultureInfo.InvariantCulture) + "[/] bytes [grey]|[/] [white]" + Markup.Escape(outputPath) + "[/]");
			return 0;
		});
	}
}
