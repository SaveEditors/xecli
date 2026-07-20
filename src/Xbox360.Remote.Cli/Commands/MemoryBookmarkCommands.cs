using System.ComponentModel;
using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class MemoryBookmarkAddCommand : Command<MemoryBookmarkAddCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--name <NAME>")]
        [LocalizedDescription("Bookmark name.")]
        public string? Name { get; init; }

        [CommandOption("--addr <ADDR>")]
        [LocalizedDescription("Live runtime address to store as entered.")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Module name or path to store as entered.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("Relative virtual address to store with --module.")]
        public string? Rva { get; init; }

        [CommandOption("--note <TEXT>")]
        [LocalizedDescription("Optional note.")]
        public string? Note { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TryLoadStore(settings.Json, out MemoryBookmarkStoreData data))
            return 1;

        if (!MemoryBookmarkStore.TryValidateName(settings.Name, out string name, out string error))
            return WriteError(settings.Json, "Memory bookmark validation failed", error, "MEM_BOOKMARKS_VALIDATION_FAILED");

        string? module = string.IsNullOrWhiteSpace(settings.Module) ? null : settings.Module;
        bool hasModule = !string.IsNullOrWhiteSpace(module);
        bool hasAddress = !string.IsNullOrWhiteSpace(settings.Address);
        bool hasRva = !string.IsNullOrWhiteSpace(settings.Rva);

        if (hasRva && !hasModule)
            return WriteError(settings.Json, "Memory bookmark validation failed", "Provide --module <MODULE> when using --rva <ADDR>.", "MEM_BOOKMARKS_VALIDATION_FAILED");

        if (!hasAddress && !hasModule)
            return WriteError(settings.Json, "Memory bookmark validation failed", "Provide --addr <ADDR> or --module <MODULE>.", "MEM_BOOKMARKS_VALIDATION_FAILED");

        uint? liveAddress = null;
        if (hasAddress) {
            if (!CliHelpers.TryParseUInt32(settings.Address?.Trim(), out uint parsedAddress))
                return WriteError(settings.Json, "Memory bookmark validation failed", "Provide valid --addr <ADDR>.", "MEM_BOOKMARKS_VALIDATION_FAILED");
            liveAddress = parsedAddress;
        }

        uint? rva = null;
        if (hasRva) {
            if (!CliHelpers.TryParseUInt32(settings.Rva?.Trim(), out uint parsedRva))
                return WriteError(settings.Json, "Memory bookmark validation failed", "Provide valid --rva <ADDR>.", "MEM_BOOKMARKS_VALIDATION_FAILED");
            rva = parsedRva;
        }

        MemoryBookmarkRecord bookmark = MemoryBookmarkStore.CreateBookmark(name, module, rva, liveAddress, settings.Note, DateTimeOffset.UtcNow);
        if (!MemoryBookmarkStore.TryAddBookmark(data, bookmark, out string addError))
            return WriteError(settings.Json, "Memory bookmark validation failed", addError, "MEM_BOOKMARKS_DUPLICATE_NAME");

        MemoryBookmarkStore.Save(data);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Bookmark = BuildJsonBookmark(bookmark)
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Added bookmark[/] {Markup.Escape(bookmark.Name)} [grey]({DescribeBookmark(bookmark)})[/]");
        return 0;
    }

    internal static object BuildJsonBookmark(MemoryBookmarkRecord bookmark) {
        return new {
            bookmark.Name,
            bookmark.Module,
            Rva = MemoryBookmarkStore.FormatHex(bookmark.Rva),
            LiveAddress = MemoryBookmarkStore.FormatHex(bookmark.LiveAddress),
            bookmark.Type,
            bookmark.Note,
            bookmark.CreatedUtc,
            bookmark.UpdatedUtc
        };
    }

    private static string DescribeBookmark(MemoryBookmarkRecord bookmark) {
        List<string> parts = [];
        if (bookmark.LiveAddress.HasValue)
            parts.Add($"addr {MemoryBookmarkStore.FormatHex(bookmark.LiveAddress)}");
        if (!string.IsNullOrWhiteSpace(bookmark.Module))
            parts.Add($"module {Markup.Escape(bookmark.Module)}");
        if (bookmark.Rva.HasValue)
            parts.Add($"rva {MemoryBookmarkStore.FormatHex(bookmark.Rva)}");
        if (parts.Count == 0)
            parts.Add("no coordinates");
        return string.Join(", ", parts);
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the bookmark arguments and try again."
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

public sealed class MemoryBookmarkListCommand : Command<MemoryBookmarkListCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TryLoadStore(settings.Json, out MemoryBookmarkStoreData data))
            return 1;

        IReadOnlyList<MemoryBookmarkRecord> bookmarks = data.Bookmarks;
        if (settings.Json) {
            CliOutput.EmitJson(bookmarks.Select(MemoryBookmarkAddCommand.BuildJsonBookmark));
            return 0;
        }

        if (bookmarks.Count == 0) {
            AnsiConsole.MarkupLine("[grey]No memory bookmarks saved.[/]");
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Memory Bookmarks[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn("Name");
        table.AddColumn("Type");
        table.AddColumn("Live");
        table.AddColumn("Module");
        table.AddColumn("RVA");
        table.AddColumn("Note");
        table.AddColumn("Created");
        table.AddColumn("Updated");

        foreach (MemoryBookmarkRecord bookmark in bookmarks) {
            table.AddRow(
                $"[white]{Markup.Escape(bookmark.Name)}[/]",
                $"[gold1]{Markup.Escape(bookmark.Type)}[/]",
                bookmark.LiveAddress.HasValue ? $"[cyan]{MemoryBookmarkStore.FormatHex(bookmark.LiveAddress)}[/]" : "[grey]n/a[/]",
                string.IsNullOrWhiteSpace(bookmark.Module) ? "[grey]n/a[/]" : $"[green]{Markup.Escape(bookmark.Module)}[/]",
                bookmark.Rva.HasValue ? $"[cyan]{MemoryBookmarkStore.FormatHex(bookmark.Rva)}[/]" : "[grey]n/a[/]",
                string.IsNullOrWhiteSpace(bookmark.Note) ? "[grey]n/a[/]" : $"[white]{Markup.Escape(bookmark.Note)}[/]",
                CliOutput.FormatTimestamp(bookmark.CreatedUtc.UtcDateTime),
                CliOutput.FormatTimestamp(bookmark.UpdatedUtc.UtcDateTime));
        }

        AnsiConsole.Write(table);
        return 0;
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

public sealed class MemoryBookmarkRemoveCommand : Command<MemoryBookmarkRemoveCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--name <NAME>")]
        [LocalizedDescription("Bookmark name.")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TryLoadStore(settings.Json, out MemoryBookmarkStoreData data))
            return 1;

        if (!MemoryBookmarkStore.TryValidateName(settings.Name, out string name, out string error))
            return WriteError(settings.Json, "Memory bookmark validation failed", error, "MEM_BOOKMARKS_VALIDATION_FAILED");

        if (!MemoryBookmarkStore.TryRemoveBookmark(data, name, out MemoryBookmarkRecord? removed, out string removeError))
            return WriteError(settings.Json, "Memory bookmark remove failed", removeError, "MEM_BOOKMARKS_NOT_FOUND");

        MemoryBookmarkStore.Save(data);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Removed = removed!.Name
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Removed bookmark[/] {Markup.Escape(removed!.Name)}");
        return 0;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the bookmark name and try again."
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

public sealed class MemoryBookmarkRenameCommand : Command<MemoryBookmarkRenameCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--name <NAME>")]
        [LocalizedDescription("Current bookmark name.")]
        public string? Name { get; init; }

        [CommandOption("--to <NAME>")]
        [LocalizedDescription("New bookmark name.")]
        public string? To { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TryLoadStore(settings.Json, out MemoryBookmarkStoreData data))
            return 1;

        if (!MemoryBookmarkStore.TryValidateName(settings.Name, out string name, out string error))
            return WriteError(settings.Json, "Memory bookmark validation failed", error, "MEM_BOOKMARKS_VALIDATION_FAILED");

        if (!MemoryBookmarkStore.TryValidateName(settings.To, out string to, out string toError))
            return WriteError(settings.Json, "Memory bookmark validation failed", toError, "MEM_BOOKMARKS_VALIDATION_FAILED");

        if (!MemoryBookmarkStore.TryRenameBookmark(data, name, to, out MemoryBookmarkRecord? renamed, out string renameError)) {
            string code = string.Equals(renameError, "Bookmark name already exists.", StringComparison.Ordinal) ? "MEM_BOOKMARKS_DUPLICATE_NAME" : "MEM_BOOKMARKS_NOT_FOUND";
            return WriteError(settings.Json, "Memory bookmark rename failed", renameError, code);
        }

        MemoryBookmarkStore.Save(data);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                From = name,
                To = renamed!.Name,
                Bookmark = MemoryBookmarkAddCommand.BuildJsonBookmark(renamed)
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Renamed bookmark[/] {Markup.Escape(name)} [grey]->[/] {Markup.Escape(renamed!.Name)}");
        return 0;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the bookmark name and try again."
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

public sealed class MemoryBookmarkShowCommand : Command<MemoryBookmarkShowCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--name <NAME>")]
        [LocalizedDescription("Bookmark name.")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TryLoadStore(settings.Json, out MemoryBookmarkStoreData data))
            return 1;

        if (!MemoryBookmarkStore.TryValidateName(settings.Name, out string name, out string validationError))
            return WriteError(settings.Json, "Memory bookmark validation failed", validationError, "MEM_BOOKMARKS_VALIDATION_FAILED");

        if (!MemoryBookmarkStore.TryFindBookmark(data, name, out MemoryBookmarkRecord? bookmark, out string error))
            return WriteError(settings.Json, "Memory bookmark lookup failed", error, "MEM_BOOKMARKS_NOT_FOUND");

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Bookmark = MemoryBookmarkAddCommand.BuildJsonBookmark(bookmark!)
            });
            return 0;
        }

        AnsiConsole.Write(new Rule($"[bold deepskyblue1]Memory Bookmark[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("Name", $"[white]{Markup.Escape(bookmark!.Name)}[/]");
        table.AddRow("Type", $"[gold1]{Markup.Escape(bookmark.Type)}[/]");
        table.AddRow("Live", bookmark.LiveAddress.HasValue ? $"[cyan]{MemoryBookmarkStore.FormatHex(bookmark.LiveAddress)}[/]" : "[grey]n/a[/]");
        table.AddRow("Module", string.IsNullOrWhiteSpace(bookmark.Module) ? "[grey]n/a[/]" : $"[green]{Markup.Escape(bookmark.Module)}[/]");
        table.AddRow("RVA", bookmark.Rva.HasValue ? $"[cyan]{MemoryBookmarkStore.FormatHex(bookmark.Rva)}[/]" : "[grey]n/a[/]");
        table.AddRow("Note", string.IsNullOrWhiteSpace(bookmark.Note) ? "[grey]n/a[/]" : $"[white]{Markup.Escape(bookmark.Note)}[/]");
        table.AddRow("Created", CliOutput.FormatTimestamp(bookmark.CreatedUtc.UtcDateTime));
        table.AddRow("Updated", CliOutput.FormatTimestamp(bookmark.UpdatedUtc.UtcDateTime));
        AnsiConsole.Write(table);
        return 0;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the bookmark name and try again."
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

public sealed class MemoryBookmarkExportCommand : Command<MemoryBookmarkExportCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Output file path.")]
        public string? Output { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TryLoadStore(settings.Json, out MemoryBookmarkStoreData data))
            return 1;

        if (string.IsNullOrWhiteSpace(settings.Output))
            return WriteError(settings.Json, "Memory bookmark export failed", "--out is required.", "MEM_BOOKMARKS_VALIDATION_FAILED");

        string fullPath;
        try {
            fullPath = Path.GetFullPath(settings.Output);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            AtomicFileWriter.WriteAllText(fullPath, MemoryBookmarkStore.Serialize(data));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            string leafName = XexInfoCommand.GetDisplayFileName(settings.Output);
            string detail = string.IsNullOrWhiteSpace(leafName)
                ? "Failed to write memory bookmark export. Check the output path, parent directory, and permissions."
                : $"Failed to write memory bookmark export file '{leafName}'. Check the output path, parent directory, and permissions.";
            return WriteError(settings.Json, "Memory bookmark export failed", detail, "MEM_BOOKMARKS_EXPORT_FAILED");
        }

        IReadOnlyList<MemoryBookmarkRecord> bookmarks = data.Bookmarks;
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Exported = bookmarks.Count,
                Store = MemoryBookmarkStore.StorePath,
                Output = fullPath,
                Bookmarks = bookmarks.Select(MemoryBookmarkAddCommand.BuildJsonBookmark).ToList()
            });
            return 0;
        }

        AnsiConsole.MarkupLine(
            $"[green]Exported[/] [cyan]{bookmarks.Count}[/] [green]bookmark{(bookmarks.Count == 1 ? string.Empty : "s")} to[/] [white]{Markup.Escape(XexInfoCommand.GetDisplayFileName(fullPath) ?? fullPath)}[/]");
        return 0;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the output path and try again."
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
