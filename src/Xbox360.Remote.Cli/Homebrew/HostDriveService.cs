using System.Globalization;
using Spectre.Console;
using Xbox360.Remote.Cli.Commands;

namespace Xbox360.Remote.Cli.Homebrew;

internal sealed record HostDriveInfo(
    string Key,
    string RootPath,
    string DisplayName,
    string? VolumeLabel,
    string? FileSystem,
    long TotalBytes,
    long FreeBytes,
    bool IsRemovable);

internal static class HostDriveService {
    public static IReadOnlyList<HostDriveInfo> GetRemovableDrives() {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<HostDriveInfo>();

        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Removable)
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .Select(drive => new HostDriveInfo(
                Key: NormalizeDriveKey(drive.RootDirectory.FullName),
                RootPath: drive.RootDirectory.FullName,
                DisplayName: BuildDisplayName(drive),
                VolumeLabel: SafeGet(() => drive.VolumeLabel),
                FileSystem: SafeGet(() => drive.DriveFormat),
                TotalBytes: SafeGet(() => drive.TotalSize),
                FreeBytes: SafeGet(() => drive.AvailableFreeSpace),
                IsRemovable: true))
            .ToArray();
    }

    public static string ResolveTargetRoot(string? token, bool autoConfirm) {
        if (!string.IsNullOrWhiteSpace(token))
            return ResolveExplicitTarget(token.Trim());

        IReadOnlyList<HostDriveInfo> drives = GetRemovableDrives();
        if (drives.Count == 0)
            throw new InvalidOperationException("No removable USB drives were detected. Use --usb <drive-or-path> to target a folder manually.");

        if (drives.Count == 1 && autoConfirm)
            return drives[0].RootPath;

        if (!ConfirmationHelpers.CanPromptForConfirmation())
            throw new InvalidOperationException("Interactive USB target selection is required. Use --usb <drive-or-path> or --auto-confirm.");

        AnsiConsole.Write(new Rule("[bold deepskyblue1]USB Targets[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]#[/]"));
        table.AddColumn(new TableColumn("[springgreen3_1]Drive[/]"));
        table.AddColumn(new TableColumn("[cyan]Label[/]"));
        table.AddColumn(new TableColumn("[deepskyblue1]File System[/]"));
        table.AddColumn(new TableColumn("[gold1]Capacity[/]"));
        table.AddColumn(new TableColumn("[grey]Free[/]"));

        for (int i = 0; i < drives.Count; i++) {
            HostDriveInfo drive = drives[i];
            table.AddRow(
                $"[white]{i + 1}[/]",
                $"[springgreen3_1]{Markup.Escape(drive.Key)}[/]",
                string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "[grey]unnamed[/]" : $"[cyan]{Markup.Escape(drive.VolumeLabel)}[/]",
                string.IsNullOrWhiteSpace(drive.FileSystem) ? "[grey]unknown[/]" : $"[deepskyblue1]{Markup.Escape(drive.FileSystem)}[/]",
                $"[gold1]{FormatBytes(drive.TotalBytes)}[/]",
                $"[grey]{FormatBytes(drive.FreeBytes)}[/]");
        }
        AnsiConsole.Write(table);

        HostDriveInfo selected = AnsiConsole.Prompt(
            new SelectionPrompt<HostDriveInfo>()
                .Title("[bold white]Select the USB drive to use[/]")
                .HighlightStyle(new Style(Spectre.Console.Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .UseConverter(drive => $"{drive.Key} [{FormatBytes(drive.TotalBytes)}] {(string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "unnamed" : drive.VolumeLabel)}")
                .AddChoices(drives));
        return selected.RootPath;
    }

    private static string ResolveExplicitTarget(string token) {
        IReadOnlyList<HostDriveInfo> removable = GetRemovableDrives();
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
            index >= 1 &&
            index <= removable.Count) {
            return removable[index - 1].RootPath;
        }

        string original = token.Trim();
        bool driveOnlyToken = false;
        string normalized = original;
        if (normalized.Length == 1 && char.IsLetter(normalized[0])) {
            normalized = $"{char.ToUpperInvariant(normalized[0])}:\\";
            driveOnlyToken = true;
        }
        else if (normalized.Length == 2 && char.IsLetter(normalized[0]) && normalized[1] == ':') {
            normalized = $"{char.ToUpperInvariant(normalized[0])}:\\";
            driveOnlyToken = true;
        }
        else if (normalized.Length == 3 &&
                 char.IsLetter(normalized[0]) &&
                 normalized[1] == ':' &&
                 (normalized[2] == '\\' || normalized[2] == '/')) {
            normalized = $"{char.ToUpperInvariant(normalized[0])}:\\";
            driveOnlyToken = true;
        }

        string rooted = Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(normalized);

        if (driveOnlyToken) {
            string driveKey = NormalizeDriveKey(rooted);
            HostDriveInfo? match = removable.FirstOrDefault(drive => string.Equals(drive.Key, driveKey, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match.RootPath;
        }

        Directory.CreateDirectory(rooted);
        return rooted;
    }

    private static string NormalizeDriveKey(string path) {
        string root = Path.GetPathRoot(path) ?? path;
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return root.EndsWith(":", StringComparison.Ordinal) ? root : $"{root}:";
    }

    private static string BuildDisplayName(DriveInfo drive) {
        string key = NormalizeDriveKey(drive.RootDirectory.FullName);
        string label = SafeGet(() => drive.VolumeLabel);
        if (string.IsNullOrWhiteSpace(label))
            return $"{key} [{FormatBytes(SafeGet(() => drive.TotalSize))}]";
        return $"{key} [{FormatBytes(SafeGet(() => drive.TotalSize))}] {label}";
    }

    private static T SafeGet<T>(Func<T> getter) {
        try {
            return getter();
        }
        catch {
            return default!;
        }
    }

    private static string FormatBytes(long bytes) {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) {
            value /= 1024d;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }
}
