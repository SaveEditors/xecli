using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class StatusCommand : AsyncCommand<StatusCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--quick")]
        [Description("Skip JRPC2, drive, and user checks for a fast snapshot.")]
        public bool Quick { get; init; }

        [CommandOption("--no-jrpc")]
        [Description("Skip JRPC2 queries (temps, CPU key, dashboard, title id).")]
        public bool NoJrpc { get; init; }

        [CommandOption("--no-drives")]
        [Description("Skip drive and USB size reporting.")]
        public bool NoDrives { get; init; }

        [CommandOption("--no-users")]
        [Description("Skip user list and signed-in detection.")]
        public bool NoUsers { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        (string ip, int port, int _) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        int timeout = settings.TimeoutMs ?? 2000;
        using CancellationTokenSource connectCts = new CancellationTokenSource(timeout);
        await using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
            Host = ip,
            Port = port,
            TimeoutMs = timeout
        }, connectCts.Token);

        {
            bool skipJrpc = settings.Quick || settings.NoJrpc;
            bool skipDrives = settings.Quick || settings.NoDrives;
            bool skipUsers = settings.Quick || settings.NoUsers;

            XbdmConsoleInfo info = await client.GetConsoleInfoAsync(CancellationToken.None);

            string? dmVersion = null;
            try {
                dmVersion = await client.GetDmVersionAsync(CancellationToken.None);
                dmVersion = dmVersion?.Trim();
            }
            catch {
                // ignored
            }

            string? xbdmFlavor = null;
            try {
                XbdmResponse flavorResponse = await client.SendCommandAsync("whomadethis", CancellationToken.None);
                xbdmFlavor = flavorResponse.Message.Trim();
            }
            catch {
                // ignored
            }

            string? runningXex = null;
            try {
                runningXex = await client.GetRunningXexPathAsync(null, CancellationToken.None);
            }
            catch {
                // ignored
            }

            bool? jrpcAvailable = null;
            uint? titleId = null;
            uint? dashVersion = null;
            string? motherboard = null;
            string? cpuKey = null;
            uint? cpuTemp = null;
            uint? gpuTemp = null;
            uint? edramTemp = null;
            uint? mbTemp = null;
            IReadOnlyList<XbdmDriveEntry>? drives = null;
            IReadOnlyList<XbdmUserInfo>? users = null;
            List<ProfileHelpers.F3ProfileInfo>? f3Profiles = null;

            if (!skipJrpc) {
                try {
                    Jrpc2Client jrpc = new Jrpc2Client(client);
                    titleId = await jrpc.GetTitleIdAsync(CancellationToken.None);
                    dashVersion = await jrpc.GetDashboardVersionAsync(CancellationToken.None);
                    motherboard = await jrpc.GetMotherboardTypeAsync(CancellationToken.None);
                    cpuKey = await jrpc.GetCpuKeyAsync(CancellationToken.None);
                    cpuTemp = await jrpc.GetTemperatureAsync(SensorType.CPU, CancellationToken.None);
                    gpuTemp = await jrpc.GetTemperatureAsync(SensorType.GPU, CancellationToken.None);
                    edramTemp = await jrpc.GetTemperatureAsync(SensorType.EDRAM, CancellationToken.None);
                    mbTemp = await jrpc.GetTemperatureAsync(SensorType.MotherBoard, CancellationToken.None);
                    jrpcAvailable = true;
                }
                catch {
                    jrpcAvailable = false;
                }
            }

            if (!skipDrives) {
                try {
                    drives = await client.GetDrivesAsync(includeSize: true, CancellationToken.None);
                }
                catch {
                    // ignored
                }
            }

            if (!skipUsers) {
                try {
                    users = await client.GetUserListAsync(CancellationToken.None);
                }
                catch {
                    // ignored
                }
            }
            ProfileHelpers.XamUserInfo? xamUser = null;
            if (!skipUsers && (users == null || users.Count == 0)) {
                try {
                    xamUser = await ProfileHelpers.TryGetSignedInXamUserAsync(ip, port, timeout, CancellationToken.None);
                    if (xamUser != null) {
                        users = new[] {
                            new XbdmUserInfo {
                                Gamertag = xamUser.Gamertag,
                                Xuid = xamUser.Xuid != null && ulong.TryParse(xamUser.Xuid.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsedXuid)
                                    ? parsedXuid
                                    : null,
                                SignInState = xamUser.SignInState,
                                RawLine = $"xam slot={xamUser.Slot}"
                            }
                        };
                    }
                }
                catch {
                    // ignored
                }
            }

            XbdmUserInfo? signedInUserInfo = users?.FirstOrDefault(u =>
                    u.SignInState.HasValue && u.SignInState.Value > 0 && !string.IsNullOrWhiteSpace(u.Gamertag))
                ?? users?.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Gamertag));
            string? signedInUser = signedInUserInfo?.Gamertag;
            string? signedInXuid = signedInUserInfo?.Xuid.HasValue == true ? $"0x{signedInUserInfo.Xuid.Value:X16}" : null;

            if (string.IsNullOrWhiteSpace(signedInUser) && !skipUsers) {
                try {
                    f3Profiles = await ProfileHelpers.TryGetF3ProfilesAsync(ip);
                }
                catch {
                    // ignored
                }
            }

            ProfileHelpers.F3ProfileInfo? signedInF3Profile = f3Profiles?.FirstOrDefault(p => p.SignedIn == 1 && !string.IsNullOrWhiteSpace(p.Gamertag));
            if (string.IsNullOrWhiteSpace(signedInUser) && signedInF3Profile != null) {
                signedInUser = signedInF3Profile.Gamertag;
                signedInXuid ??= signedInF3Profile.Xuid;
            }
            if (string.IsNullOrWhiteSpace(signedInUser) && xamUser != null) {
                signedInUser = xamUser.Gamertag;
                signedInXuid ??= xamUser.Xuid;
            }

            string? titleName = null;
            if (titleId.HasValue && TitleIdDatabase.Instance.TryResolve(titleId.Value, null, out TitleIdEntry? entry)) {
                titleName = entry?.Name;
            }
            titleName = ProfileHelpers.TryGetTitleFallbackName(titleId, runningXex, titleName);

            CliConfig cfg = CliConfig.Load();
            string? pendingModuleText = null;
            if (cfg.PendingModuleOperation != null &&
                !string.IsNullOrWhiteSpace(cfg.PendingModuleOperation.Action) &&
                !string.IsNullOrWhiteSpace(cfg.PendingModuleOperation.ModuleName)) {
                string action = cfg.PendingModuleOperation.Action!;
                string moduleName = cfg.PendingModuleOperation.ModuleName!;
                pendingModuleText = $"{action} {moduleName}";
                bool? satisfied = null;
                try {
                    IReadOnlyList<XbdmModuleInfo> pendingModules = await client.GetModulesAsync(false, CancellationToken.None);
                    bool present = pendingModules.Any(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
                    satisfied = string.Equals(action, "load", StringComparison.OrdinalIgnoreCase) ? present : !present;
                    if (satisfied.Value) {
                        cfg.PendingModuleOperation = null;
                        cfg.Save();
                        pendingModuleText = null;
                    }
                }
                catch {
                    // ignored
                }

                if (pendingModuleText != null) {
                    pendingModuleText = satisfied switch {
                        true => $"{action} {moduleName} (verified)",
                        false => $"{action} {moduleName} (pending)",
                        _ => $"{action} {moduleName}"
                    };
                }
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Target = new { ip, port },
                    Info = info,
                    DmVersion = dmVersion,
                    XbdmFlavor = xbdmFlavor,
                    RunningXex = runningXex,
                    TitleId = titleId.HasValue ? $"0x{titleId.Value:X8}" : null,
                    TitleName = titleName,
                    Dashboard = dashVersion,
                    Motherboard = motherboard,
                    CpuKey = cpuKey,
                    Temps = new { cpuTemp, gpuTemp, edramTemp, mbTemp },
                    JrpcAvailable = jrpcAvailable,
                    Drives = drives,
                    Users = users,
                    SignedIn = signedInUser,
                    SignedInXuid = signedInXuid,
                    PendingModuleOperation = pendingModuleText
                });
                return 0;
            }

            string jrpcStatus = jrpcAvailable.HasValue
                ? (jrpcAvailable.Value ? "[springgreen3_1]online[/]" : "[red1]unavailable[/]")
                : "[grey70]skipped[/]";

            const string Unknown = "[grey70]unknown[/]";
            const string FieldColor = "[white]";
            const string FieldEnd = "[/]";
            static string FormatValue(string? value, string color, string? fallback = null) {
                if (string.IsNullOrWhiteSpace(value))
                    return fallback ?? Unknown;
                return $"[{color}]{Markup.Escape(value)}[/]";
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Status[/]").RuleStyle("silver"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold white]Field[/]"));
            table.AddColumn(new TableColumn("[bold white]Value[/]"));
            table.AddRow($"{FieldColor}IP{FieldEnd}", $"[cyan1]{ip}[/]");
            table.AddRow($"{FieldColor}Port{FieldEnd}", $"[deepskyblue1]{port}[/]");
            table.AddRow($"{FieldColor}Console ID{FieldEnd}", FormatValue(info.ConsoleId, "gold1"));
            table.AddRow($"{FieldColor}Debug Name{FieldEnd}", FormatValue(info.DebugName, "springgreen3_1"));
            table.AddRow($"{FieldColor}Execution State{FieldEnd}", FormatExecutionState(info.ExecutionState));
            table.AddRow($"{FieldColor}Title IP{FieldEnd}", FormatValue(info.TitleIp, "cyan1"));
            table.AddRow($"{FieldColor}Process ID{FieldEnd}", info.ProcessId.HasValue ? $"[mediumpurple3]0x{info.ProcessId.Value:X8}[/]" : Unknown);
            table.AddRow($"{FieldColor}Title ID{FieldEnd}", titleId.HasValue ? $"[deepskyblue1]0x{titleId.Value:X8}[/]" : FormatSkipped(skipJrpc));
            table.AddRow($"{FieldColor}Title Name{FieldEnd}", FormatValue(titleName, "springgreen3_1", FormatSkipped(skipJrpc)));
            table.AddRow($"{FieldColor}Running XEX{FieldEnd}", FormatValue(runningXex, "springgreen3_1"));
            table.AddRow($"{FieldColor}DM Version{FieldEnd}", FormatValue(dmVersion, "cyan1"));
            table.AddRow($"{FieldColor}XBDM Flavor{FieldEnd}", FormatValue(xbdmFlavor, "mediumpurple3"));
            table.AddRow($"{FieldColor}JRPC2{FieldEnd}", jrpcStatus);
            table.AddRow($"{FieldColor}Dashboard{FieldEnd}", dashVersion.HasValue ? $"[gold1]{dashVersion.Value}[/]" : FormatSkipped(skipJrpc));
            table.AddRow($"{FieldColor}Motherboard{FieldEnd}", FormatValue(motherboard, "deepskyblue1", FormatSkipped(skipJrpc)));
            table.AddRow($"{FieldColor}CPU Key{FieldEnd}", FormatValue(cpuKey, "gold1", FormatSkipped(skipJrpc)));
            table.AddRow($"{FieldColor}Signed In{FieldEnd}", FormatValue(signedInUser, "springgreen3_1", skipUsers ? FormatSkipped(true) : "[grey70]none[/]"));
            table.AddRow($"{FieldColor}Signed In XUID{FieldEnd}", FormatValue(signedInXuid, "gold1", skipUsers ? FormatSkipped(true) : "[grey70]none[/]"));
            if (!string.IsNullOrWhiteSpace(pendingModuleText))
                table.AddRow($"{FieldColor}Pending Module{FieldEnd}", FormatValue(pendingModuleText, "gold1"));
            AnsiConsole.Write(table);

            if (cpuTemp.HasValue || gpuTemp.HasValue || edramTemp.HasValue || mbTemp.HasValue) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Temps (C)[/]").RuleStyle("silver"));
                Table tempTable = CliOutput.CreateTable();
                tempTable.AddColumn(new TableColumn("[bold deepskyblue1]CPU[/]"));
                tempTable.AddColumn(new TableColumn("[bold deepskyblue1]GPU[/]"));
                tempTable.AddColumn(new TableColumn("[bold deepskyblue1]EDRAM[/]"));
                tempTable.AddColumn(new TableColumn("[bold deepskyblue1]Board[/]"));
                tempTable.AddRow(
                    FormatTemperature(cpuTemp),
                    FormatTemperature(gpuTemp),
                    FormatTemperature(edramTemp),
                    FormatTemperature(mbTemp));
                AnsiConsole.Write(tempTable);
            }

            if (drives != null && drives.Count > 0) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Drives[/]").RuleStyle("silver"));
                Table driveTable = CliOutput.CreateTable();
                driveTable.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
                driveTable.AddColumn(new TableColumn("[bold white]Aliases[/]"));
                driveTable.AddColumn(new TableColumn("[bold deepskyblue1]Total[/]"));
                driveTable.AddColumn(new TableColumn("[bold cyan1]Free[/]"));
                driveTable.AddColumn(new TableColumn("[bold gold1]Used[/]"));
                foreach (DriveGroup drive in GroupDrives(drives)) {
                    string name = Markup.Escape(drive.DisplayName);
                    string aliases = drive.Aliases.Count > 0
                        ? $"[silver]{Markup.Escape(string.Join(", ", drive.Aliases))}[/]"
                        : "[grey70]-[/]";
                    driveTable.AddRow(
                        FormatDriveName(drive),
                        aliases,
                        $"[deepskyblue1]{FormatBytes(drive.TotalBytes)}[/]",
                        $"[cyan1]{FormatBytes(drive.FreeBytes)}[/]",
                        FormatDriveUsage(drive.TotalBytes, drive.FreeBytes));
                }
                AnsiConsole.Write(driveTable);

                List<XbdmDriveEntry> usb = drives
                    .Where(d => d.Name.StartsWith("Usb", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (usb.Count > 0) {
                    AnsiConsole.Write(new Rule("[bold deepskyblue1]Connected USB[/]").RuleStyle("silver"));
                    Table usbTable = CliOutput.CreateTable();
                    usbTable.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
                    usbTable.AddColumn(new TableColumn("[bold deepskyblue1]Total[/]"));
                    usbTable.AddColumn(new TableColumn("[bold cyan1]Free[/]"));
                    usbTable.AddColumn(new TableColumn("[bold gold1]Used[/]"));
                    foreach (XbdmDriveEntry drive in usb) {
                        usbTable.AddRow(
                            "[deepskyblue1]" + Markup.Escape(drive.Name) + "[/]",
                            $"[deepskyblue1]{FormatBytes(drive.TotalBytes)}[/]",
                            $"[cyan1]{FormatBytes(drive.FreeBytes)}[/]",
                            FormatDriveUsage(drive.TotalBytes, drive.FreeBytes));
                    }
                    AnsiConsole.Write(usbTable);
                }
            }

            return 0;
        }
    }

    private static string FormatBytes(ulong? value) {
        if (!value.HasValue)
            return "unknown";
        double size = value.Value;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static string FormatSkipped(bool skipped) {
        return skipped ? "[grey70]skipped[/]" : "[grey70]unknown[/]";
    }

    private static string FormatExecutionState(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return "[grey70]unknown[/]";

        return value.Trim().ToLowerInvariant() switch {
            "start" or "running" => $"[springgreen3_1]{Markup.Escape(value)}[/]",
            "stop" or "stopped" or "break" => $"[gold1]{Markup.Escape(value)}[/]",
            "reboot_title" or "reboot" => $"[mediumpurple3]{Markup.Escape(value)}[/]",
            _ => $"[cyan]{Markup.Escape(value)}[/]"
        };
    }

    private static string FormatTemperature(uint? value) {
        if (!value.HasValue)
            return "[grey70]unknown[/]";

        string color = value.Value switch {
            < 45 => "springgreen3_1",
            < 60 => "yellow1",
            < 70 => "darkorange",
            _ => "red1"
        };

        return $"[{color}]{value.Value}[/]";
    }

    private static string FormatDriveName(DriveGroup drive) {
        string escaped = Markup.Escape(drive.DisplayName);
        return drive.Family switch {
            "internal" => $"[springgreen3_1]{escaped}[/]",
            "usb" => $"[deepskyblue1]{escaped}[/]",
            "systemext" => $"[gold1]{escaped}[/]",
            "system" => $"[mediumpurple3]{escaped}[/]",
            _ => $"[cyan]{escaped}[/]"
        };
    }

    private static string FormatDriveUsage(ulong? totalBytes, ulong? freeBytes) {
        if (!totalBytes.HasValue || !freeBytes.HasValue || totalBytes.Value == 0 || freeBytes.Value > totalBytes.Value)
            return "[grey70]unknown[/]";

        double usedPercent = ((double) (totalBytes.Value - freeBytes.Value) / totalBytes.Value) * 100d;
        string color = usedPercent switch {
            < 60 => "springgreen3_1",
            < 85 => "gold1",
            _ => "red1"
        };

        return $"[{color}]{usedPercent:0.#}%[/]";
    }

    private static IReadOnlyList<DriveGroup> GroupDrives(IReadOnlyList<XbdmDriveEntry> drives) {
        List<DriveGroup> groups = new List<DriveGroup>();
        foreach (XbdmDriveEntry drive in drives.OrderBy(GetDriveSortKey).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)) {
            string family = GetDriveFamily(drive.Name);
            DriveGroup? existing = groups.FirstOrDefault(g =>
                g.Family == family &&
                g.TotalBytes == drive.TotalBytes &&
                g.FreeBytes == drive.FreeBytes);

            if (existing is null) {
                groups.Add(new DriveGroup(family, GetDriveDisplayName(drive.Name), drive.Name, drive.TotalBytes, drive.FreeBytes));
                continue;
            }

            existing.Aliases.Add(drive.Name);
            existing.Aliases.Sort(StringComparer.OrdinalIgnoreCase);
        }

        foreach (DriveGroup group in groups) {
            group.Aliases.RemoveAll(alias => string.Equals(alias, group.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        return groups;
    }

    private static string GetDriveSortKey(XbdmDriveEntry drive) {
        return GetDriveFamily(drive.Name) switch {
            "internal" => "0",
            "usb" => "1",
            "systemext" => "2",
            "system" => "3",
            _ => "9"
        };
    }

    private static string GetDriveFamily(string name) {
        return NormalizeDriveName(name) switch {
            "hdd" or "hdd1" or "game" or "devkit" or "d" => "internal",
            "sysext" => "systemext",
            "system" => "system",
            _ when name.StartsWith("Usb", StringComparison.OrdinalIgnoreCase) => "usb",
            _ => NormalizeDriveName(name)
        };
    }

    private static string GetDriveDisplayName(string name) {
        return GetDriveFamily(name) switch {
            "internal" => "Internal",
            "systemext" => "SysExt:",
            "system" => "System:",
            _ => name
        };
    }

    private static string NormalizeDriveName(string name) {
        return name.Trim().TrimEnd(':').ToLowerInvariant();
    }

    private sealed class DriveGroup {
        public DriveGroup(string family, string displayName, string primaryAlias, ulong? totalBytes, ulong? freeBytes) {
            Family = family;
            DisplayName = displayName;
            TotalBytes = totalBytes;
            FreeBytes = freeBytes;
            Aliases = new List<string> { primaryAlias };
        }

        public string Family { get; }
        public string DisplayName { get; }
        public ulong? TotalBytes { get; }
        public ulong? FreeBytes { get; }
        public List<string> Aliases { get; }
    }
}

public sealed class TargetCommand : Command<TargetCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--set <IP>")]
        [Description("Set the default console IP.")]
        public string? Ip { get; init; }

        [CommandOption("--port <PORT>")]
        [Description("Set the default port (default: 730).")]
        public int? Port { get; init; }

        [CommandOption("--clear")]
        [Description("Clear the saved target.")]
        public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();

        if (settings.Clear) {
            config.DefaultIp = null;
            config.DefaultPort = null;
            config.Save();
            AnsiConsole.MarkupLine("[green]Default target cleared.[/]");
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(settings.Ip)) {
            config.DefaultIp = settings.Ip;
            if (settings.Port.HasValue)
                config.DefaultPort = settings.Port;
            else
                config.DefaultPort ??= 730;
            config.Save();
            AnsiConsole.MarkupLine($"[green]Default target set to[/] {settings.Ip}:{config.DefaultPort}");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(config.DefaultIp)) {
            AnsiConsole.MarkupLine("[yellow]No default target set. Use --set or `rgh connect`.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Default target:[/] {config.DefaultIp}:{config.DefaultPort ?? 730}");
        return 0;
    }
}

public sealed class PingCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        ConnectionSettings effective = new ConnectionSettings {
            Ip = settings.Ip,
            Port = settings.Port,
            TimeoutMs = settings.TimeoutMs ?? 2000,
            Json = settings.Json
        };

        return await CliHelpers.WithClientOnceAsync(effective, async client => {
            Stopwatch sw = Stopwatch.StartNew();
            _ = await client.GetConsoleInfoAsync(CancellationToken.None);
            sw.Stop();
            AnsiConsole.MarkupLine($"[green]OK[/] {sw.ElapsedMilliseconds} ms");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class RebootCommand : AsyncCommand<RebootCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--title")]
        [Description("Restart the current title instead of a cold reboot.")]
        public bool Title { get; init; }

        [CommandOption("--notify")]
        [Description("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [Description("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [Description("Notification logo id (decimal or 0x hex).")]
        public string? NotifyLogo { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string mode = settings.Title ? "title" : "cold";
        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            await client.SendCommandAsync($"magicboot {mode}", CancellationToken.None);
            OperationFeedback.WriteSuccess(
                settings.Title ? "Title reboot requested" : "Cold reboot requested",
                settings.Title ? "[cyan]Current title restart requested.[/]" : "[cyan]Full console reboot requested.[/]");
            await NotifyHelpers.TrySendOperationNotificationAsync(
                client,
                settings.Notify,
                settings.NotifyIcon,
                settings.NotifyLogo,
                "Success :)",
                CancellationToken.None);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class InstallCommand : Command<InstallCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--uninstall")]
        [Description("Remove the current-user shim or, with --machine-path, remove the machine PATH entry.")]
        public bool Uninstall { get; init; }

        [CommandOption("--machine-path")]
        [Description("Add the rgh.exe directory to the machine PATH (admin required).")]
        public bool MachinePath { get; init; }

        [CommandOption("--path <DIR>")]
        [Description("Override the rgh.exe directory (defaults to the current executable folder).")]
        public string? Path { get; init; }

        [CommandOption("--quiet")]
        [Description("Suppress non-error install output.")]
        public bool Quiet { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!OperatingSystem.IsWindows()) {
            AnsiConsole.MarkupLine("[red]The install command is only supported on Windows.[/]");
            return 1;
        }

        string exeDir = InstallHelpers.NormalizeDirectory(settings.Path ?? AppContext.BaseDirectory);
        string exePath = Path.Combine(exeDir, "rgh.exe");
        if (!File.Exists(exePath)) {
            AnsiConsole.MarkupLine($"[red]rgh.exe not found at[/] {Markup.Escape(exePath)}");
            return 1;
        }

        if (settings.Uninstall) {
            if (settings.MachinePath) {
                if (!InstallHelpers.IsAdministrator()) {
                    AnsiConsole.MarkupLine("[red]Machine PATH uninstall requires an elevated terminal.[/]");
                    return 1;
                }

                if (!InstallHelpers.RemoveMachinePathEntry(exeDir, out string removeMessage)) {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(removeMessage)}[/]");
                    return 1;
                }

                if (!settings.Quiet)
                    AnsiConsole.MarkupLine($"[green]{Markup.Escape(removeMessage)}[/]");
                if (!settings.Quiet)
                    AnsiConsole.MarkupLine("[mediumpurple3]Created by Pew - Se7ensins[/]");
                return 0;
            }

            InstallHelpers.UninstallUserShim();
            if (!settings.Quiet)
                AnsiConsole.MarkupLine("[green]Uninstalled rgh command shim.[/]");
            if (!settings.Quiet)
                AnsiConsole.MarkupLine("[mediumpurple3]Created by Pew - Se7ensins[/]");
            return 0;
        }

        if (settings.MachinePath) {
            if (!InstallHelpers.IsAdministrator()) {
                AnsiConsole.MarkupLine("[red]Machine PATH install requires an elevated terminal.[/]");
                return 1;
            }

            if (!InstallHelpers.AddMachinePathEntry(exeDir, out string addMessage)) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(addMessage)}[/]");
                return 1;
            }

            if (!settings.Quiet)
                AnsiConsole.MarkupLine($"[green]{Markup.Escape(addMessage)}[/]");
            if (!settings.Quiet)
                AnsiConsole.MarkupLine("[mediumpurple3]Created by Pew - Se7ensins[/]");
            return 0;
        }

        InstallHelpers.InstallUserShim(exeDir);
        if (!settings.Quiet)
            AnsiConsole.MarkupLine($"[green]Installed rgh command shim:[/] {Markup.Escape(InstallHelpers.ShimPath)}");
        if (!InstallHelpers.IsDirectoryOnProcessPath(InstallHelpers.WindowsAppsDir) && !settings.Quiet) {
            AnsiConsole.MarkupLine($"[yellow]Note:[/] {Markup.Escape(InstallHelpers.WindowsAppsDir)} is not on PATH. Add it or use `rgh` from its folder.");
        }
        if (!settings.Quiet)
            AnsiConsole.MarkupLine("[mediumpurple3]Created by Pew - Se7ensins[/]");
        return 0;
    }
}
