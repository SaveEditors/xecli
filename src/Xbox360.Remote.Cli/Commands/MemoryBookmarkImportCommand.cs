using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class MemoryBookmarkImportCommand : Command<MemoryBookmarkImportCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--file <FILE>")]
        [LocalizedDescription("Path to a symbol sidecar JSON file.")]
        public string? File { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TryLoadStore(settings.Json, out MemoryBookmarkStoreData data))
            return 1;

        if (string.IsNullOrWhiteSpace(settings.File))
            return WriteError(settings.Json, "Memory bookmark import failed", "--file is required.", "MEM_BOOKMARKS_VALIDATION_FAILED");

        string filePath = Path.GetFullPath(settings.File);
        IdaSymbolSidecarLoadResult load = IdaSymbolSidecarStore.LoadRaw(filePath);
        if (!string.IsNullOrWhiteSpace(load.Issue)) {
            string loadCode = load.Issue.StartsWith("Symbol sidecar not found", StringComparison.OrdinalIgnoreCase)
                ? "MEM_SYMBOLS_NOT_FOUND"
                : "MEM_SYMBOLS_STORE_MALFORMED";
            return WriteError(settings.Json, "Memory bookmark import failed", load.Issue!, loadCode);
        }

        if (!IdaSymbolSidecarStore.TryBuildBookmarks(load.Data, DateTimeOffset.UtcNow, out List<MemoryBookmarkRecord> bookmarks, out string buildError))
            return WriteError(settings.Json, "Memory bookmark import failed", buildError, "MEM_SYMBOLS_VALIDATION_FAILED");

        if (!TryImportBookmarks(data, bookmarks, out string importError))
            return WriteError(settings.Json, "Memory bookmark import failed", importError, "MEM_BOOKMARKS_DUPLICATE_NAME");

        MemoryBookmarkStore.Save(data);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Imported = bookmarks.Count,
                Source = load.Data.Source,
                Module = load.Data.Module,
                Store = MemoryBookmarkStore.StorePath,
                Bookmarks = bookmarks.Select(MemoryBookmarkAddCommand.BuildJsonBookmark).ToList()
            });
            return 0;
        }

        AnsiConsole.MarkupLine(
            $"[green]Imported[/] {bookmarks.Count} bookmark(s) from [white]{Markup.Escape(Path.GetFileName(filePath))}[/]");
        return 0;
    }

    private static bool TryImportBookmarks(MemoryBookmarkStoreData data, IReadOnlyList<MemoryBookmarkRecord> incoming, out string error) {
        error = string.Empty;

        HashSet<string> existingNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (MemoryBookmarkRecord bookmark in data.Bookmarks) {
            if (!MemoryBookmarkStore.TryValidateName(bookmark.Name, out string normalizedName, out _))
                continue;
            existingNames.Add(normalizedName);
        }

        HashSet<string> incomingNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (MemoryBookmarkRecord bookmark in incoming) {
            if (bookmark == null) {
                error = "Imported bookmark entry is missing.";
                return false;
            }

            if (!MemoryBookmarkStore.TryValidateName(bookmark.Name, out string normalizedName, out string nameError)) {
                error = nameError;
                return false;
            }

            if (!incomingNames.Add(normalizedName)) {
                error = "Bookmark name already exists.";
                return false;
            }

            if (existingNames.Contains(normalizedName)) {
                error = "Bookmark name already exists.";
                return false;
            }

            existingNames.Add(normalizedName);
        }

        data.Bookmarks ??= [];
        data.Bookmarks.AddRange(incoming);
        return true;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the sidecar file and try again."
            }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        return 1;
    }

    private static bool TryLoadStore(bool json, out MemoryBookmarkStoreData data) {
        MemoryBookmarkLoadResult load = MemoryBookmarkStore.LoadRaw();
        data = load.Data;
        if (string.IsNullOrWhiteSpace(load.Issue))
            return true;

        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Memory bookmark store failed",
                load.Issue,
                "MEM_BOOKMARKS_STORE_MALFORMED",
                new[] { "Fix or remove the bookmarks file, then try again." }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(load.Issue)}[/]");
        }

        return false;
    }
}
