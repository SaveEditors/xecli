using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarGamesCommand : AsyncCommand<AvatarGamesCommand.Settings>
{
	public sealed class Settings : AvatarLibrarySettings
	{
		[CommandOption("--search <TEXT>")]
		[LocalizedDescription("Filter titles by name, publisher, or title id.")]
		public string? Search { get; init; }

		[CommandOption("--limit <N>")]
		[LocalizedDescription("Maximum titles to show (default: 100, 0 = no limit).")]
		public int? Limit { get; init; }

		[CommandOption("--no-cache")]
		[LocalizedDescription("Force a fresh library scan instead of using the cache.")]
		public bool NoCache { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(settings.LibraryRoot, settings.CachePath, settings.Remote, settings.ManifestUrl, settings.TitleMapUrl, settings.ContentBaseUrl, settings.DownloadCachePath);
		IReadOnlyList<AvatarTitleSummary> readOnlyList = AvatarCommandHelpers.FilterTitles((await AvatarCommandHelpers.LoadIndexAsync(paths, !settings.NoCache, CancellationToken.None)).Titles, settings.Search, AvatarCommandHelpers.ParseLimit(settings.Limit)).ToArray();
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				Mode = (paths.RemoteMode ? "remote" : "local"),
				EffectiveLibraryRoot = paths.EffectiveLibraryRoot,
				EffectiveCachePath = paths.EffectiveCachePath,
				EffectiveManifestUrl = paths.EffectiveManifestUrl,
				EffectiveContentBaseUrl = paths.EffectiveContentBaseUrl,
				Count = readOnlyList.Count,
				Titles = readOnlyList
			});
			return 0;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]Avatar Games[/]").RuleStyle("grey"));
		if (readOnlyList.Count == 0)
		{
			OperationFeedback.WriteWarning("Avatar games", "No matching avatar titles were found.");
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[cyan]Title ID[/]"));
		table.AddColumn(new TableColumn("[green]Game[/]"));
		table.AddColumn(new TableColumn("[deepskyblue1]Items[/]"));
		table.AddColumn(new TableColumn("[gold1]Size[/]"));
		table.AddColumn(new TableColumn("[grey]Publishers[/]"));
		foreach (AvatarTitleSummary item in readOnlyList)
		{
			string text = ((item.Publishers.Count == 0) ? "-" : string.Join(", ", item.Publishers.Take(3)));
			table.AddRow($"[cyan]0x{item.TitleId:X8}[/]", "[green]" + Markup.Escape(item.TitleName) + "[/]", $"[deepskyblue1]{item.ItemCount}[/]", "[gold1]" + FtpHelpers.FormatBytes(item.TotalBytes) + "[/]", "[grey]" + Markup.Escape(text) + "[/]");
		}
		AnsiConsole.Write(table);
		return 0;
	}
}
