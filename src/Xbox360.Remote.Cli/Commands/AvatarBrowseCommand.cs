using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarBrowseCommand : AsyncCommand<AvatarBrowseCommand.Settings> {
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(8);

    public class Settings : AvatarInstallCommand.Settings {
        [CommandOption("--search <TEXT>")]
        [LocalizedDescription("Initial item search text.")]
        public string? Search { get; init; }

        [CommandOption("--game <TEXT>")]
        [LocalizedDescription("Initial game/title filter text.")]
        public string? Game { get; init; }

        [CommandOption("--publisher <TEXT>")]
        [LocalizedDescription("Initial publisher filter.")]
        public string? Publisher { get; init; }

        [CommandOption("--tag <TEXT>")]
        [LocalizedDescription("Initial derived tag filter.")]
        public string? Tag { get; init; }

        [CommandOption("--limit <N>")]
        [LocalizedDescription("Maximum titles/items to preload (default: 100, 0 = no limit).")]
        public int? Limit { get; init; }

        [CommandOption("--no-cache")]
        [LocalizedDescription("Force a fresh library scan instead of using the cache.")]
        public bool NoCache { get; init; }

        [CommandOption("--show-sensitive")]
        [LocalizedDescription("Show raw gamertag, XUID, and source path details in the browser.")]
        public bool ShowSensitive { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        AvatarCommandHelpers.ValidateTitleIdIfPresent(settings.TitleId);

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
        IReadOnlyList<AvatarItemRecord>? selectedItems;
        try {
            selectedItems = await RunOnStaThreadAsync(() => {
                using AvatarBrowserForm form = new AvatarBrowserForm(
                    paths,
                    settings,
                    null,
                    Array.Empty<AvatarItemRecord>(),
                    token => AvatarCommandHelpers.RunWithTimeoutAsync(
                        innerToken => AvatarCommandHelpers.LoadIndexAsync(paths, !settings.NoCache, innerToken),
                        OperationTimeout));
                DialogResult result = form.ShowDialog();
                return result == DialogResult.OK ? form.SelectedItems : null;
            });
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("Avatar browser", ex.Message);
            return 1;
        }

        if (selectedItems == null)
            return 0;
        if (selectedItems.Count == 0) {
            OperationFeedback.WriteWarning("Avatar browser", "No avatar items were selected.");
            return 0;
        }

        try {
            return await AvatarCommandHelpers.RunWithTimeoutAsync(
                token => AvatarInstallFlow.RunAsync(paths, settings, selectedItems, token),
                OperationTimeout);
        }
        catch (OperationCanceledException) {
            OperationFeedback.WriteFailure("Avatar browser", $"Avatar install timed out after {(int)OperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (TimeoutException ex) {
            OperationFeedback.WriteFailure("Avatar browser", ex.Message);
            return 1;
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("Avatar browser", ex.Message);
            return 1;
        }
    }

    internal static Task<T> RunOnStaThreadAsync<T>(Func<T> action) {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return Task.FromResult(action());

        TaskCompletionSource<T> taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => {
            try {
                taskCompletionSource.TrySetResult(action());
            }
            catch (Exception exception) {
                taskCompletionSource.TrySetException(exception);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return taskCompletionSource.Task;
    }

}

