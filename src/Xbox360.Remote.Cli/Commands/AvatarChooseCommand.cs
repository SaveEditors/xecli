using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarChooseCommand : AsyncCommand<AvatarChooseCommand.Settings> {
    public class Settings : AvatarBrowseCommand.Settings {
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        AvatarCommandHelpers.ValidateTitleIdIfPresent(settings.TitleId);

        AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(
            settings.LibraryRoot,
            settings.CachePath,
            settings.Remote,
            settings.ManifestUrl,
            settings.TitleMapUrl,
            settings.ContentBaseUrl,
            settings.DownloadCachePath);
        AvatarLibraryIndex index;
        try {
            index = await AvatarCommandHelpers.RunWithTimeoutAsync(
                token => AvatarCommandHelpers.LoadIndexAsync(paths, !settings.NoCache, token),
                AvatarCommandHelpers.DefaultOperationTimeout);
        }
        catch (OperationCanceledException) {
            OperationFeedback.WriteFailure("Avatar choose", $"Avatar loading timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (TimeoutException) {
            OperationFeedback.WriteFailure("Avatar choose", $"Avatar loading timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (Exception) {
            OperationFeedback.WriteFailure("Avatar choose", "Avatar loading failed. Check the avatar library and try again.");
            return 1;
        }

        AvatarTitleSummary? selectedTitle = ResolveTitle(index, settings);
        if (selectedTitle == null) {
            OperationFeedback.WriteWarning("Avatar choose", "No matching avatar titles were found.");
            return 0;
        }

        IReadOnlyList<AvatarItemRecord> items = ResolveItems(index, settings, selectedTitle);
        if (items.Count == 0) {
            OperationFeedback.WriteWarning("Avatar choose", $"No avatar items were found for {selectedTitle.TitleName}.");
            return 0;
        }

        IReadOnlyList<AvatarItemRecord> selectedItems = settings.All
            ? items
            : AnsiConsole.Prompt(new MultiSelectionPrompt<AvatarItemRecord>()
                .Title($"[bold deepskyblue1]Select avatar items for [green]{Markup.Escape(selectedTitle.TitleName)}[/][/]")
                .InstructionsText("[grey]Press [green]space[/] to toggle, [green]enter[/] to confirm.[/]")
                .PageSize(Math.Min(15, Math.Max(6, items.Count)))
                .NotRequired()
                .UseConverter(item => $"{AvatarCommandHelpers.ResolveItemDisplayName(item)} [grey]({item.ContentId}, {AvatarCommandHelpers.DescribeLayout(item)}, {FtpHelpers.FormatBytes(item.SizeBytes)})[/]")
                .AddChoices(items));

        if (selectedItems.Count == 0) {
            OperationFeedback.WriteWarning("Avatar choose", "No avatar items were selected.");
            return 0;
        }

        try {
            return await AvatarCommandHelpers.RunWithTimeoutAsync(
                token => AvatarInstallFlow.RunAsync(paths, settings, selectedItems, token),
                AvatarCommandHelpers.DefaultOperationTimeout);
        }
        catch (OperationCanceledException) {
            OperationFeedback.WriteFailure("Avatar choose", $"Avatar install timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (TimeoutException) {
            OperationFeedback.WriteFailure("Avatar choose", $"Avatar install timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (Exception) {
            OperationFeedback.WriteFailure("Avatar choose", "Avatar install failed. Check available storage and try again.");
            return 1;
        }
    }

    private static AvatarTitleSummary? ResolveTitle(AvatarLibraryIndex index, Settings settings) {
        if (!string.IsNullOrWhiteSpace(settings.TitleId) &&
            SaveHelpers.TryParseTitleId(settings.TitleId, out uint parsedTitleId)) {
            AvatarTitleSummary? exact = index.Titles.FirstOrDefault(title => title.TitleId == parsedTitleId);
            if (exact != null)
                return exact;
        }

        string? search = !string.IsNullOrWhiteSpace(settings.Search) ? settings.Search : settings.Game;
        IReadOnlyList<AvatarTitleSummary> titles = AvatarCommandHelpers
            .FilterTitles(index.Titles, search, AvatarCommandHelpers.ParseLimit(settings.Limit, 50))
            .ToArray();
        if (titles.Count == 0)
            return null;

        if (titles.Count == 1)
            return titles[0];

        return AnsiConsole.Prompt(new SelectionPrompt<AvatarTitleSummary>()
            .Title("[bold deepskyblue1]Select a game[/]")
            .PageSize(Math.Min(15, Math.Max(6, titles.Count)))
            .UseConverter(title => $"{title.TitleName} [grey](0x{title.TitleId:X8}, {title.ItemCount} items, {FtpHelpers.FormatBytes(title.TotalBytes)})[/]")
            .AddChoices(titles));
    }

    private static IReadOnlyList<AvatarItemRecord> ResolveItems(AvatarLibraryIndex index, Settings settings, AvatarTitleSummary selectedTitle) {
        AvatarItemQuery query = new AvatarItemQuery(
            TitleId: selectedTitle.TitleId,
            Search: settings.Search,
            Publisher: settings.Publisher,
            Tag: settings.Tag,
            Limit: AvatarCommandHelpers.ParseLimit(settings.Limit, int.MaxValue));

        IEnumerable<AvatarItemRecord> items = AvatarLibraryService.SearchItems(index, query);
        if (!string.IsNullOrWhiteSpace(settings.Game)) {
            string game = settings.Game.Trim();
            items = items.Where(item =>
                item.TitleName.Contains(game, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(item.GameName) && item.GameName.Contains(game, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(settings.ContentId)) {
            AvatarItemRecord? match = AvatarLibraryService.FindByContentId(index, settings.ContentId, selectedTitle.TitleId);
            return match == null ? Array.Empty<AvatarItemRecord>() : new[] { match };
        }

        return items.ToArray();
    }
}
