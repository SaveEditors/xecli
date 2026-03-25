using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarChooseCommand : AsyncCommand<AvatarChooseCommand.Settings>
{
	public class Settings : AvatarBrowseCommand.Settings
	{
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(settings.LibraryRoot, settings.CachePath, settings.Remote, settings.ManifestUrl, settings.TitleMapUrl, settings.ContentBaseUrl, settings.DownloadCachePath);
		AvatarLibraryIndex index = await AvatarCommandHelpers.LoadIndexAsync(paths, !settings.NoCache, CancellationToken.None);
		AvatarTitleSummary avatarTitleSummary = ResolveTitle(index, settings);
		if (avatarTitleSummary == null)
		{
			OperationFeedback.WriteWarning("Avatar choose", "No matching avatar titles were found.");
			return 0;
		}
		IReadOnlyList<AvatarItemRecord> readOnlyList = ResolveItems(index, settings, avatarTitleSummary);
		if (readOnlyList.Count == 0)
		{
			OperationFeedback.WriteWarning("Avatar choose", "No avatar items were found for " + avatarTitleSummary.TitleName + ".");
			return 0;
		}
		IReadOnlyList<AvatarItemRecord> readOnlyList3;
		if (!settings.All)
		{
			IReadOnlyList<AvatarItemRecord> readOnlyList2 = AnsiConsole.Prompt(new MultiSelectionPrompt<AvatarItemRecord>().Title("[bold deepskyblue1]Select avatar items for [green]" + Markup.Escape(avatarTitleSummary.TitleName) + "[/][/]").InstructionsText("[grey]Press [green]space[/] to toggle, [green]enter[/] to confirm.[/]").PageSize(Math.Min(15, Math.Max(6, readOnlyList.Count)))
				.NotRequired()
				.UseConverter((AvatarItemRecord item) => $"{AvatarCommandHelpers.ResolveItemDisplayName(item)} [grey]({item.ContentId}, {AvatarCommandHelpers.DescribeLayout(item)}, {FtpHelpers.FormatBytes(item.SizeBytes)})[/]")
				.AddChoices(readOnlyList));
			readOnlyList3 = readOnlyList2;
		}
		else
		{
			readOnlyList3 = readOnlyList;
		}
		IReadOnlyList<AvatarItemRecord> readOnlyList4 = readOnlyList3;
		if (readOnlyList4.Count == 0)
		{
			OperationFeedback.WriteWarning("Avatar choose", "No avatar items were selected.");
			return 0;
		}
		return await AvatarInstallFlow.RunAsync(paths, settings, readOnlyList4, CancellationToken.None);
	}

	private static AvatarTitleSummary? ResolveTitle(AvatarLibraryIndex index, Settings settings)
	{
		if (!string.IsNullOrWhiteSpace(settings.TitleId) && SaveHelpers.TryParseTitleId(settings.TitleId, out var parsedTitleId))
		{
			AvatarTitleSummary avatarTitleSummary = index.Titles.FirstOrDefault((AvatarTitleSummary title) => title.TitleId == parsedTitleId);
			if (avatarTitleSummary != null)
			{
				return avatarTitleSummary;
			}
		}
		string search = ((!string.IsNullOrWhiteSpace(settings.Search)) ? settings.Search : settings.Game);
		IReadOnlyList<AvatarTitleSummary> readOnlyList = AvatarCommandHelpers.FilterTitles(index.Titles, search, AvatarCommandHelpers.ParseLimit(settings.Limit, 50)).ToArray();
		if (readOnlyList.Count == 0)
		{
			return null;
		}
		if (readOnlyList.Count == 1)
		{
			return readOnlyList[0];
		}
		return AnsiConsole.Prompt(new SelectionPrompt<AvatarTitleSummary>().Title("[bold deepskyblue1]Select a game[/]").PageSize(Math.Min(15, Math.Max(6, readOnlyList.Count))).UseConverter((AvatarTitleSummary title) => $"{title.TitleName} [grey](0x{title.TitleId:X8}, {title.ItemCount} items, {FtpHelpers.FormatBytes(title.TotalBytes)})[/]")
			.AddChoices(readOnlyList));
	}

	private static IReadOnlyList<AvatarItemRecord> ResolveItems(AvatarLibraryIndex index, Settings settings, AvatarTitleSummary selectedTitle)
	{
		AvatarItemQuery query = new AvatarItemQuery(selectedTitle.TitleId, settings.Search, settings.Publisher, settings.Tag, AvatarCommandHelpers.ParseLimit(settings.Limit, int.MaxValue));
		IEnumerable<AvatarItemRecord> source = AvatarLibraryService.SearchItems(index, query);
		if (!string.IsNullOrWhiteSpace(settings.Game))
		{
			string game = settings.Game.Trim();
			source = source.Where((AvatarItemRecord item) => item.TitleName.Contains(game, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(item.GameName) && item.GameName.Contains(game, StringComparison.OrdinalIgnoreCase)));
		}
		if (!string.IsNullOrWhiteSpace(settings.ContentId))
		{
			AvatarItemRecord avatarItemRecord = AvatarLibraryService.FindByContentId(index, settings.ContentId, selectedTitle.TitleId);
			if (!(avatarItemRecord == null))
			{
				return new AvatarItemRecord[1] { avatarItemRecord };
			}
			return Array.Empty<AvatarItemRecord>();
		}
		return source.ToArray();
	}
}
