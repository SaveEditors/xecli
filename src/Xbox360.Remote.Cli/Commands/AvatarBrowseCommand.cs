using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarBrowseCommand : AsyncCommand<AvatarBrowseCommand.Settings> {
    public class Settings : AvatarInstallCommand.Settings {
        [CommandOption("--search <TEXT>")]
        [Description("Initial item search text.")]
        public string? Search { get; init; }

        [CommandOption("--game <TEXT>")]
        [Description("Initial game/title filter text.")]
        public string? Game { get; init; }

        [CommandOption("--publisher <TEXT>")]
        [Description("Initial publisher filter.")]
        public string? Publisher { get; init; }

        [CommandOption("--tag <TEXT>")]
        [Description("Initial derived tag filter.")]
        public string? Tag { get; init; }

        [CommandOption("--limit <N>")]
        [Description("Maximum titles/items to preload (default: 100, 0 = no limit).")]
        public int? Limit { get; init; }

        [CommandOption("--no-cache")]
        [Description("Force a fresh library scan instead of using the cache.")]
        public bool NoCache { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!OperatingSystem.IsWindows()) {
            AnsiConsole.MarkupLine("[red]The avatar browser is only available on Windows.[/]");
            return 1;
        }

        AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(
            settings.LibraryRoot,
            settings.CachePath,
            settings.Remote,
            settings.ManifestUrl,
            settings.TitleMapUrl,
            settings.ContentBaseUrl,
            settings.DownloadCachePath);
        AvatarLibraryIndex index = await AvatarCommandHelpers.LoadIndexAsync(paths, !settings.NoCache, CancellationToken.None);

        AvatarCommandHelpers.AvatarResolvedOwnership? ownership = null;
        try {
            (string xbdmIp, int xbdmPort, int xbdmTimeout) = await AvatarCommandHelpers.ResolveXbdmTargetAsync(settings, CancellationToken.None);
            ownership = await AvatarCommandHelpers.ResolveOwnershipAsync(settings, xbdmIp, CancellationToken.None);
        }
        catch {
            ownership = null;
        }

        IReadOnlyList<AvatarItemRecord> initialSelection = ResolveInitialSelection(index, settings);
        using AvatarBrowserForm form = new AvatarBrowserForm(index, paths, settings, ownership, initialSelection);
        DialogResult result = form.ShowDialog();
        if (result != DialogResult.OK)
            return 0;

        IReadOnlyList<AvatarItemRecord> selectedItems = form.SelectedItems;
        if (selectedItems.Count == 0) {
            OperationFeedback.WriteWarning("Avatar browser", "No avatar items were selected.");
            return 0;
        }

        return await AvatarInstallFlow.RunAsync(paths, settings, selectedItems, CancellationToken.None);
    }

    private static IReadOnlyList<AvatarItemRecord> ResolveInitialSelection(AvatarLibraryIndex index, Settings settings) {
        List<AvatarItemRecord> selected = new List<AvatarItemRecord>();

        if (!string.IsNullOrWhiteSpace(settings.ContentId)) {
            uint? titleId = null;
            if (!string.IsNullOrWhiteSpace(settings.TitleId) && SaveHelpers.TryParseTitleId(settings.TitleId, out uint parsedTitleId))
                titleId = parsedTitleId;

            AvatarItemRecord? item = AvatarLibraryService.FindByContentId(index, settings.ContentId, titleId);
            if (item != null)
                selected.Add(item);
        }
        else if (settings.All && !string.IsNullOrWhiteSpace(settings.TitleId) && SaveHelpers.TryParseTitleId(settings.TitleId, out uint parsedTitleId)) {
            selected.AddRange(AvatarLibraryService.SearchItems(index, new AvatarItemQuery(TitleId: parsedTitleId, Limit: int.MaxValue)));
        }

        return selected;
    }
}
