using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ContentListCommand : AsyncCommand<ContentListCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--device <ROOTS>")]
        [LocalizedDescription("Comma-separated content roots, for example Hdd1 or Hdd1,Usb0 (default: Hdd1,Usb0,Usb1,HddX).")]
        public string? Devices { get; init; }

        [CommandOption("--titleid <TITLEID>")]
        [LocalizedDescription("Restrict to one Title ID.")]
        public string? TitleId { get; init; }

        [CommandOption("--show-types")]
        [LocalizedDescription("Include content-type breakdowns per title.")]
        public bool ShowTypes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        uint? titleId = null;
        if (!string.IsNullOrWhiteSpace(settings.TitleId)) {
            if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint parsed)) {
                AnsiConsole.MarkupLine("[red]Invalid --titleid.[/]");
                return 1;
            }
            titleId = parsed;
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        List<ContentHelpers.ContentEntry> entries = await ContentHelpers.ListAsync(ip, port, user, pass, timeout, settings.Devices, titleId, settings.ShowTypes);
        if (settings.Json) {
            CliOutput.EmitJson(entries);
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Installed Content[/]").RuleStyle("grey"));
        if (entries.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No content entries found.[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[cyan]Title ID[/]"));
        table.AddColumn(new TableColumn("[green]Name[/]"));
        table.AddColumn(new TableColumn("[grey]Devices[/]"));
        table.AddColumn(new TableColumn("[cyan]Types[/]"));
        foreach (ContentHelpers.ContentEntry entry in entries.OrderBy(e => e.TitleId)) {
            string typeText = settings.ShowTypes
                ? string.Join(", ", entry.Types.Select(t => $"{t.Kind}:{t.Count}"))
                : entry.Types.Count.ToString(CultureInfo.InvariantCulture);
            table.AddRow(
                $"[cyan]0x{entry.TitleId:X8}[/]",
                $"[green]{Markup.Escape(entry.Name)}[/]",
                $"[grey]{Markup.Escape(string.Join(", ", entry.Devices.OrderBy(d => d, StringComparer.OrdinalIgnoreCase)))}[/]",
                $"[cyan]{Markup.Escape(typeText)}[/]");
        }
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class ContentDeleteCommand : AsyncCommand<ContentDeleteCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--titleid <TITLEID>")]
        [LocalizedDescription("Title ID to remove.")]
        public string? TitleId { get; init; }

        [CommandOption("--device <ROOTS>")]
        [LocalizedDescription("Comma-separated content roots, for example Hdd1 or Hdd1,Usb0.")]
        public string? Devices { get; init; }

        [CommandOption("--yes")]
        [LocalizedDescription("Delete without interactive confirmation.")]
        public bool Yes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint titleId)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "Content delete failed", "--titleid is required.", "CONTENT_DELETE_TITLE_ID_REQUIRED");
        }

        if (settings.Json && !settings.Yes) {
            return FtpHelpers.WriteValidationFailure(
                true,
                "Content delete confirmation required",
                "--yes is required when --json is used for content deletion.",
                "CONTENT_DELETE_CONFIRMATION_REQUIRED",
                "Review the target Title ID and devices, then retry with --yes.");
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        List<ContentHelpers.ContentLocation> matches = await ContentHelpers.FindLocationsAsync(ip, port, user, pass, timeout, settings.Devices, titleId);
        if (matches.Count == 0) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "content-delete",
                    Status = "not-found",
                    TitleId = titleId.ToString("X8", CultureInfo.InvariantCulture),
                    DeletedCount = 0,
                    Paths = Array.Empty<string>()
                });
                return 0;
            }

            AnsiConsole.MarkupLine("[yellow]No matching content folders found.[/]");
            return 0;
        }

        if (!settings.Yes) {
            AnsiConsole.MarkupLine("[yellow]The following paths will be removed:[/]");
            foreach (ContentHelpers.ContentLocation match in matches)
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(match.Path)}[/]");
            if (!ConfirmationHelpers.TryConfirm(
                    "Content delete",
                    "Delete these content folders?",
                    settings.Yes,
                    emitWarning: false)) {
                AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
                return 0;
            }
        }

        foreach (ContentHelpers.ContentLocation match in matches) {
            await ContentHelpers.DeletePathAsync(ip, port, user, pass, timeout, match.Path);
        }

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Operation = "content-delete",
                Status = "completed",
                TitleId = titleId.ToString("X8", CultureInfo.InvariantCulture),
                DeletedCount = matches.Count,
                Paths = matches.Select(match => match.Path).ToArray()
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Deleted[/] {matches.Count} content folder(s).");
        return 0;
    }
}

public sealed class PluginListCommand : AsyncCommand<PluginListCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--ini <PATH>")]
        [LocalizedDescription("DashLaunch config path (default: /Hdd1/launch.ini).")]
        public string? IniPath { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        PluginHelpers.PluginConfig config = await PluginHelpers.LoadAsync(ip, port, user, pass, timeout, settings.IniPath);
        PluginHelpers.PluginListDisplay display = PluginHelpers.BuildListDisplay(config);
        if (settings.Json) {
            CliOutput.EmitJson(display);
            return 0;
        }

        AnsiConsole.Write(new Rule($"[bold deepskyblue1]Plugins[/] [grey]{Markup.Escape(display.Path)}[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[cyan]Slot[/]"));
        table.AddColumn(new TableColumn("[green]Path[/]"));
        foreach (PluginHelpers.PluginListEntry plugin in display.Plugins) {
            table.AddRow($"[cyan]plugin{plugin.Slot}[/]", string.IsNullOrWhiteSpace(plugin.Path) ? "[grey]disabled[/]" : $"[green]{Markup.Escape(plugin.Path)}[/]");
        }
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class PluginEnableCommand : AsyncCommand<PluginEnableCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--slot <N>")]
        [LocalizedDescription("Plugin slot number, 1-5.")]
        public string? Slot { get; init; }

        [CommandOption("--path <PATH>")]
        [LocalizedDescription("Plugin XEX path.")]
        public string? PluginPath { get; init; }

        [CommandOption("--ini <PATH>")]
        [LocalizedDescription("DashLaunch config path (default: /Hdd1/launch.ini).")]
        public string? IniPath { get; init; }

        [CommandOption("--backup")]
        [LocalizedDescription("Create a .bak copy before writing.")]
        public bool Backup { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!PluginHelpers.TryParseSlot(settings.Slot, out int slot)) {
            CliValidationOutput.Write(
                settings.Json,
                "Plugin enable validation failed",
                "--slot must be a whole number between 1 and 5.",
                "PLUGIN_SLOT_INVALID",
                "Provide --slot with a value from 1 through 5.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.PluginPath)) {
            CliValidationOutput.Write(
                settings.Json,
                "Plugin enable validation failed",
                "--path is required.",
                "PLUGIN_PATH_REQUIRED",
                "Provide the console path to the plugin XEX with --path.");
            return 1;
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        PluginHelpers.PluginConfig config = await PluginHelpers.LoadAsync(ip, port, user, pass, timeout, settings.IniPath);
        string previousPath = config.Slots.GetValueOrDefault(slot, string.Empty);
        config.SetSlot(slot, settings.PluginPath);
        await PluginHelpers.SaveAsync(ip, port, user, pass, timeout, config, settings.Backup);
        PluginHelpers.WriteMutationResult(
            new PluginHelpers.PluginMutationResult(
                Operation: "plugin-enable",
                Status: "completed",
                Slot: slot,
                PreviousPath: previousPath,
                Path: settings.PluginPath,
                ConfigPath: PluginHelpers.GetDisplayIniPath(config.Path),
                BackupCreated: settings.Backup),
            settings.Json);
        return 0;
    }
}

public sealed class PluginDisableCommand : AsyncCommand<PluginDisableCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--slot <N>")]
        [LocalizedDescription("Plugin slot number, 1-5.")]
        public string? Slot { get; init; }

        [CommandOption("--ini <PATH>")]
        [LocalizedDescription("DashLaunch config path (default: /Hdd1/launch.ini).")]
        public string? IniPath { get; init; }

        [CommandOption("--backup")]
        [LocalizedDescription("Create a .bak copy before writing.")]
        public bool Backup { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!PluginHelpers.TryParseSlot(settings.Slot, out int slot)) {
            CliValidationOutput.Write(
                settings.Json,
                "Plugin disable validation failed",
                "--slot must be a whole number between 1 and 5.",
                "PLUGIN_SLOT_INVALID",
                "Provide --slot with a value from 1 through 5.");
            return 1;
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        PluginHelpers.PluginConfig config = await PluginHelpers.LoadAsync(ip, port, user, pass, timeout, settings.IniPath);
        string previousPath = config.Slots.GetValueOrDefault(slot, string.Empty);
        config.SetSlot(slot, string.Empty);
        await PluginHelpers.SaveAsync(ip, port, user, pass, timeout, config, settings.Backup);
        PluginHelpers.WriteMutationResult(
            new PluginHelpers.PluginMutationResult(
                Operation: "plugin-disable",
                Status: "completed",
                Slot: slot,
                PreviousPath: previousPath,
                Path: string.Empty,
                ConfigPath: PluginHelpers.GetDisplayIniPath(config.Path),
                BackupCreated: settings.Backup),
            settings.Json);
        return 0;
    }
}

internal static class ContentHelpers {
    private static readonly HashSet<string> DefaultRoots = new HashSet<string>(new[] { "Hdd1", "Usb0", "Usb1", "HddX" }, StringComparer.OrdinalIgnoreCase);

    internal sealed record ContentTypeSummary(string Code, string Kind, int Count);
    internal sealed record ContentEntry(uint TitleId, string Name, List<string> Devices, List<ContentTypeSummary> Types);
    internal sealed record ContentLocation(string Device, string Path);

    public static async Task<List<ContentEntry>> ListAsync(string ip, int port, string user, string pass, int timeoutMs, string? devices, uint? titleId, bool showTypes) {
        Dictionary<uint, ContentEntry> map = new Dictionary<uint, ContentEntry>();
        foreach (string root in ParseRoots(devices)) {
            string basePath = $"/{root}/Content/0000000000000000";
            (FtpListItem[] items, bool rootListing)? listing = await SaveHelpersTryGetListingAsync(ip, port, user, pass, timeoutMs, basePath);
            if (listing == null || listing.Value.rootListing)
                continue;

            foreach (FtpListItem item in listing.Value.items) {
                if (item.Type != FtpObjectType.Directory)
                    continue;
                if (!SaveHelpers.TryParseTitleId(item.Name, out uint parsedTitleId))
                    continue;
                if (titleId.HasValue && parsedTitleId != titleId.Value)
                    continue;

                if (!map.TryGetValue(parsedTitleId, out ContentEntry? entry)) {
                    string name = ResolveTitleName(parsedTitleId);
                    entry = new ContentEntry(parsedTitleId, name, new List<string>(), new List<ContentTypeSummary>());
                    map[parsedTitleId] = entry;
                }

                if (!entry.Devices.Contains(root, StringComparer.OrdinalIgnoreCase))
                    entry.Devices.Add(root);

                if (!showTypes)
                    continue;

                string titlePath = $"{basePath}/{item.Name}";
                (FtpListItem[] types, bool typeRoot)? typeListing = await SaveHelpersTryGetListingAsync(ip, port, user, pass, timeoutMs, titlePath);
                if (typeListing == null || typeListing.Value.typeRoot)
                    continue;

                foreach (FtpListItem type in typeListing.Value.types.Where(i => i.Type == FtpObjectType.Directory)) {
                    string kind = DescribeContentType(type.Name);
                    ContentTypeSummary? existing = entry.Types.FirstOrDefault(t => t.Code.Equals(type.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) {
                        entry.Types.Add(new ContentTypeSummary(type.Name, kind, 1));
                    }
                    else {
                        entry.Types.Remove(existing);
                        entry.Types.Add(existing with { Count = existing.Count + 1 });
                    }
                }
            }
        }

        return map.Values.OrderBy(e => e.TitleId).ToList();
    }

    public static async Task<List<ContentLocation>> FindLocationsAsync(string ip, int port, string user, string pass, int timeoutMs, string? devices, uint titleId) {
        List<ContentLocation> locations = new List<ContentLocation>();
        foreach (string root in ParseRoots(devices)) {
            string path = $"/{root}/Content/0000000000000000/{titleId:X8}";
            (FtpListItem[] items, bool rootListing)? listing = await SaveHelpersTryGetListingAsync(ip, port, user, pass, timeoutMs, path);
            if (listing == null)
                continue;
            if (listing.Value.rootListing)
                continue;
            locations.Add(new ContentLocation(root, path));
        }
        return locations;
    }

    public static async Task DeletePathAsync(string ip, int port, string user, string pass, int timeoutMs, string path) {
        await using AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port);
        client.Config.ConnectTimeout = timeoutMs;
        client.Config.ReadTimeout = timeoutMs;
        client.Config.DataConnectionConnectTimeout = timeoutMs;
        client.Config.DataConnectionReadTimeout = timeoutMs;
        await client.Connect();
        await client.DeleteDirectory(path);
    }

    private static async Task<(FtpListItem[] items, bool rootListing)?> SaveHelpersTryGetListingAsync(string ip, int port, string user, string pass, int timeoutMs, string path) {
        try {
            await using AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port);
            client.Config.ConnectTimeout = timeoutMs;
            client.Config.ReadTimeout = timeoutMs;
            client.Config.DataConnectionConnectTimeout = timeoutMs;
            client.Config.DataConnectionReadTimeout = timeoutMs;
            await client.Connect();
            return await FtpHelpers.GetListingWithFallbackAsync(client, path);
        }
        catch {
            return null;
        }
    }

    private static IEnumerable<string> ParseRoots(string? devices) {
        if (string.IsNullOrWhiteSpace(devices))
            return DefaultRoots;

        return devices
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveTitleName(uint titleId) {
        return TitleIdDatabase.Instance.ResolveNameOrHex(titleId);
    }

    private static string DescribeContentType(string code) {
        return code.ToUpperInvariant() switch {
            "00007000" => "Game",
            "000D0000" => "XBLA",
            "000B0000" => "Title Update",
            "00000002" => "DLC",
            "00010000" => "Profile Data",
            "00004000" => "Installed Game",
            "00000001" => "Save",
            _ => code
        };
    }
}

internal static class PluginHelpers {
    private const int MinPluginSlot = 1;
    private const int MaxPluginSlot = 5;

    internal sealed class PluginConfig {
        public required string Path { get; init; }
        public required List<string> Lines { get; init; }
        public required Dictionary<int, string> Slots { get; init; }

        public void SetSlot(int slot, string value) {
            Slots[slot] = value;
        }
    }

    internal sealed record PluginListEntry(int Slot, string Path);
    internal sealed record PluginListDisplay(string Path, List<PluginListEntry> Plugins);
    internal sealed record PluginMutationResult(
        string Operation,
        string Status,
        int Slot,
        string PreviousPath,
        string Path,
        string ConfigPath,
        bool BackupCreated);

    public static async Task<PluginConfig> LoadAsync(string ip, int port, string user, string pass, int timeoutMs, string? iniPath, CancellationToken cancellationToken = default) {
        string path = NormalizeIniPath(iniPath);
        string text = await DownloadTextAsync(ip, port, user, pass, timeoutMs, path, cancellationToken);
        List<string> lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        Dictionary<int, string> slots = ParseSlots(lines);
        return new PluginConfig {
            Path = path,
            Lines = lines,
            Slots = slots
        };
    }

    internal static PluginListDisplay BuildListDisplay(PluginConfig config) {
        return BuildListDisplay(config.Path, config.Slots);
    }

    internal static PluginListDisplay BuildListDisplay(string path, Dictionary<int, string> slots) {
        return new PluginListDisplay(
            GetDisplayIniPath(path),
            slots.OrderBy(s => s.Key).Select(s => new PluginListEntry(s.Key, s.Value)).ToList());
    }

    internal static void WriteMutationResult(PluginMutationResult result, bool json) {
        if (json) {
            CliOutput.EmitJson(result);
            return;
        }

        if (string.Equals(result.Operation, "plugin-enable", StringComparison.Ordinal)) {
            AnsiConsole.MarkupLine($"[green]Enabled[/] plugin{result.Slot} = {Markup.Escape(result.Path)}");
            return;
        }

        AnsiConsole.MarkupLine($"[green]Disabled[/] plugin{result.Slot}");
    }

    public static async Task SaveAsync(string ip, int port, string user, string pass, int timeoutMs, PluginConfig config, bool backup, CancellationToken cancellationToken = default) {
        List<string> lines = UpdatePluginLines(config.Lines, config.Slots);
        string content = string.Join("\r\n", lines);
        byte[] bytes = Encoding.UTF8.GetBytes(content);

        if (backup) {
            string backupPath = config.Path + ".bak";
            await FtpHelpers.UploadBytesVerifiedAsync(
                ip,
                port,
                user,
                pass,
                timeoutMs,
                Encoding.UTF8.GetBytes(string.Join("\r\n", config.Lines)),
                backupPath,
                ensureRemoteDirectory: true,
                progress: null,
                cancellationToken);
        }

        await FtpHelpers.UploadBytesVerifiedAsync(
            ip,
            port,
            user,
            pass,
            timeoutMs,
            bytes,
            config.Path,
            ensureRemoteDirectory: true,
            progress: null,
            cancellationToken);
    }

    internal static bool TryParseSlot(string? text, out int slot) {
        slot = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) &&
               slot >= MinPluginSlot &&
               slot <= MaxPluginSlot;
    }

    private static Dictionary<int, string> ParseSlots(List<string> lines) {
        Dictionary<int, string> slots = new Dictionary<int, string>();
        bool inPlugins = false;
        foreach (string raw in lines) {
            string line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) {
                inPlugins = line.Equals("[Plugins]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inPlugins || line.StartsWith(';') || !line.Contains('=', StringComparison.Ordinal))
                continue;

            int split = line.IndexOf('=');
            string key = line.Substring(0, split).Trim();
            string value = line.Substring(split + 1).Trim();
            if (!key.StartsWith("plugin", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(key.Substring("plugin".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot))
                continue;
            slots[slot] = value;
        }

        for (int slot = 1; slot <= Math.Max(5, slots.Keys.DefaultIfEmpty(0).Max()); slot++) {
            slots.TryAdd(slot, string.Empty);
        }

        return slots;
    }

    private static List<string> UpdatePluginLines(List<string> sourceLines, Dictionary<int, string> slots) {
        List<string> lines = new List<string>(sourceLines);
        bool inPlugins = false;
        bool foundPluginsSection = false;
        HashSet<int> updated = new HashSet<int>();

        for (int i = 0; i < lines.Count; i++) {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) {
                if (inPlugins && updated.Count < slots.Count) {
                    foreach ((int pendingSlot, string pendingValue) in slots.OrderBy(s => s.Key)) {
                        if (updated.Contains(pendingSlot))
                            continue;
                        lines.Insert(i++, $"plugin{pendingSlot} = {pendingValue}");
                    }
                }

                inPlugins = trimmed.Equals("[Plugins]", StringComparison.OrdinalIgnoreCase);
                foundPluginsSection = foundPluginsSection || inPlugins;
                continue;
            }

            if (!inPlugins || trimmed.StartsWith(';') || !trimmed.Contains('=', StringComparison.Ordinal))
                continue;

            int split = trimmed.IndexOf('=');
            string key = trimmed.Substring(0, split).Trim();
            if (!key.StartsWith("plugin", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(key.Substring("plugin".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot))
                continue;
            if (!slots.TryGetValue(slot, out string? value))
                continue;

            lines[i] = $"plugin{slot} = {value}";
            updated.Add(slot);
        }

        if (!foundPluginsSection) {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);
            lines.Add("[Plugins]");
            foreach ((int slot, string value) in slots.OrderBy(s => s.Key))
                lines.Add($"plugin{slot} = {value}");
            return lines;
        }

        if (inPlugins && updated.Count < slots.Count) {
            foreach ((int slot, string value) in slots.OrderBy(s => s.Key)) {
                if (updated.Contains(slot))
                    continue;
                lines.Add($"plugin{slot} = {value}");
            }
        }

        return lines;
    }

    private static async Task<string> DownloadTextAsync(string ip, int port, string user, string pass, int timeoutMs, string path, CancellationToken cancellationToken) {
        await using AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port);
        client.Config.ConnectTimeout = timeoutMs;
        client.Config.ReadTimeout = timeoutMs;
        client.Config.DataConnectionConnectTimeout = timeoutMs;
        client.Config.DataConnectionReadTimeout = timeoutMs;
        await client.Connect(cancellationToken);
        using MemoryStream ms = new MemoryStream();
        await client.DownloadStream(ms, path, 0, null, cancellationToken);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string NormalizeIniPath(string? path) {
        return FtpHelpers.NormalizePath(string.IsNullOrWhiteSpace(path) ? "/Hdd1/launch.ini" : path);
    }

    internal static string GetDisplayIniPath(string path) {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? path : leaf;
    }
}

