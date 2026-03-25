using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarItemsCommand : AsyncCommand<AvatarItemsCommand.Settings>
{
	public sealed class Settings : AvatarLibrarySettings
	{
		[CommandOption("--titleid <TITLEID>")]
		[LocalizedDescription("Restrict items to a Title ID.")]
		public string? TitleId { get; init; }

		[CommandOption("--game <TEXT>")]
		[LocalizedDescription("Restrict items to a game name match.")]
		public string? Game { get; init; }

		[CommandOption("--search <TEXT>")]
		[LocalizedDescription("Search by item name, game name, or content id.")]
		public string? Search { get; init; }

		[CommandOption("--publisher <TEXT>")]
		[LocalizedDescription("Restrict items to one publisher.")]
		public string? Publisher { get; init; }

		[CommandOption("--tag <TEXT>")]
		[LocalizedDescription("Restrict items to one derived tag.")]
		public string? Tag { get; init; }

		[CommandOption("--limit <N>")]
		[LocalizedDescription("Maximum items to show (default: 100, 0 = no limit).")]
		public int? Limit { get; init; }

		[CommandOption("--no-cache")]
		[LocalizedDescription("Force a fresh library scan instead of using the cache.")]
		public bool NoCache { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(settings.LibraryRoot, settings.CachePath, settings.Remote, settings.ManifestUrl, settings.TitleMapUrl, settings.ContentBaseUrl, settings.DownloadCachePath);
		IReadOnlyList<AvatarItemRecord> readOnlyList = AvatarCommandHelpers.FilterItems(await AvatarCommandHelpers.LoadIndexAsync(paths, !settings.NoCache, CancellationToken.None), settings);
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
				Items = readOnlyList
			});
			return 0;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]Avatar Items[/]").RuleStyle("grey"));
		if (readOnlyList.Count == 0)
		{
			OperationFeedback.WriteWarning("Avatar items", "No matching avatar items were found.");
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[cyan]Title ID[/]"));
		table.AddColumn(new TableColumn("[green]Game[/]"));
		table.AddColumn(new TableColumn("[springgreen3_1]Item[/]"));
		table.AddColumn(new TableColumn("[gold1]Content ID[/]"));
		table.AddColumn(new TableColumn("[deepskyblue1]Layout[/]"));
		table.AddColumn(new TableColumn("[grey]Size[/]"));
		foreach (AvatarItemRecord item in readOnlyList)
		{
			table.AddRow($"[cyan]0x{item.TitleId:X8}[/]", "[green]" + Markup.Escape(item.TitleName) + "[/]", "[springgreen3_1]" + Markup.Escape(AvatarCommandHelpers.ResolveItemDisplayName(item)) + "[/]", "[gold1]" + Markup.Escape(item.ContentId) + "[/]", "[deepskyblue1]" + Markup.Escape(AvatarCommandHelpers.DescribeLayout(item)) + "[/]", "[grey]" + FtpHelpers.FormatBytes(item.SizeBytes) + "[/]");
		}
		AnsiConsole.Write(table);
		return 0;
	}
}
