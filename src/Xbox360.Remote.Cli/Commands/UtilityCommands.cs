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
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        return await CliHelpers.WithClientAsync((ip, port, timeout), settings, async client => {
            bool skipJrpc = settings.Quick || settings.NoJrpc;
            bool skipDrives = settings.Quick || settings.NoDrives;
            bool skipUsers = settings.Quick || settings.NoUsers;

            XbdmConsoleInfo info = await client.GetConsoleInfoAsync(CancellationToken.None);

            string? dmVersion = null;
            try {
                dmVersion = await client.GetDmVersionAsync(CancellationToken.None);
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

            string? titleName = null;
            if (titleId.HasValue && TitleIdDatabase.Instance.TryResolve(titleId.Value, null, out TitleIdEntry? entry)) {
                titleName = entry?.Name;
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Target = new { ip, port },
                    Info = info,
                    DmVersion = dmVersion,
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
                    SignedInXuid = signedInXuid
                });
                return 0;
            }

            string jrpcStatus = jrpcAvailable.HasValue
                ? (jrpcAvailable.Value ? "[green]ok[/]" : "[red]unavailable[/]")
                : "[grey]skipped[/]";

            const string Unknown = "[grey]unknown[/]";
            static string FormatValue(string? value, string color, string? fallback = null) {
                if (string.IsNullOrWhiteSpace(value))
                    return fallback ?? Unknown;
                return $"[{color}]{Markup.Escape(value)}[/]";
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Status[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]Field[/]"));
            table.AddColumn(new TableColumn("[white]Value[/]"));
            table.AddRow("[grey]IP[/]", $"[cyan]{ip}[/]");
            table.AddRow("[grey]Port[/]", port.ToString());
            table.AddRow("[grey]Console ID[/]", FormatValue(info.ConsoleId, "gold1"));
            table.AddRow("[grey]Debug Name[/]", FormatValue(info.DebugName, "green"));
            table.AddRow("[grey]Execution State[/]", FormatValue(info.ExecutionState, "cyan"));
            table.AddRow("[grey]Title IP[/]", FormatValue(info.TitleIp, "cyan"));
            table.AddRow("[grey]Process ID[/]", info.ProcessId.HasValue ? $"0x{info.ProcessId.Value:X8}" : Unknown);
            table.AddRow("[grey]Title ID[/]", titleId.HasValue ? $"0x{titleId.Value:X8}" : Unknown);
            table.AddRow("[grey]Title Name[/]", FormatValue(titleName, "green"));
            table.AddRow("[grey]Running XEX[/]", FormatValue(runningXex, "green"));
            table.AddRow("[grey]DM Version[/]", FormatValue(dmVersion, "cyan"));
            table.AddRow("[grey]JRPC2[/]", jrpcStatus);
            table.AddRow("[grey]Dashboard[/]", dashVersion.HasValue ? dashVersion.Value.ToString() : Unknown);
            table.AddRow("[grey]Motherboard[/]", FormatValue(motherboard, "cyan"));
            table.AddRow("[grey]CPU Key[/]", FormatValue(cpuKey, "gold1"));
            table.AddRow("[grey]Signed In[/]", FormatValue(signedInUser, "green", "[grey]none[/]"));
            table.AddRow("[grey]Signed In XUID[/]", FormatValue(signedInXuid, "gold1", "[grey]none[/]"));
            AnsiConsole.Write(table);

            if (cpuTemp.HasValue || gpuTemp.HasValue || edramTemp.HasValue || mbTemp.HasValue) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Temps (C)[/]").RuleStyle("grey"));
                Table tempTable = CliOutput.CreateTable();
                tempTable.AddColumn(new TableColumn("[cyan]CPU[/]"));
                tempTable.AddColumn(new TableColumn("[cyan]GPU[/]"));
                tempTable.AddColumn(new TableColumn("[cyan]EDRAM[/]"));
                tempTable.AddColumn(new TableColumn("[cyan]Board[/]"));
                tempTable.AddRow(
                    cpuTemp?.ToString() ?? "unknown",
                    gpuTemp?.ToString() ?? "unknown",
                    edramTemp?.ToString() ?? "unknown",
                    mbTemp?.ToString() ?? "unknown");
                AnsiConsole.Write(tempTable);
            }

            if (drives != null && drives.Count > 0) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Drives[/]").RuleStyle("grey"));
                Table driveTable = CliOutput.CreateTable();
                driveTable.AddColumn(new TableColumn("[green]Name[/]"));
                driveTable.AddColumn(new TableColumn("[cyan]Total[/]"));
                driveTable.AddColumn(new TableColumn("[cyan]Free[/]"));
                foreach (XbdmDriveEntry drive in drives) {
                    string name = Markup.Escape(drive.Name);
                    driveTable.AddRow(
                        $"[green]{name}[/]",
                        $"[cyan]{FormatBytes(drive.TotalBytes)}[/]",
                        $"[cyan]{FormatBytes(drive.FreeBytes)}[/]");
                }
                AnsiConsole.Write(driveTable);

                List<XbdmDriveEntry> usb = drives
                    .Where(d => d.Name.StartsWith("Usb", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (usb.Count > 0) {
                    AnsiConsole.Write(new Rule("[bold deepskyblue1]Connected USB[/]").RuleStyle("grey"));
                    Table usbTable = CliOutput.CreateTable();
                    usbTable.AddColumn(new TableColumn("[green]Name[/]"));
                    usbTable.AddColumn(new TableColumn("[cyan]Total[/]"));
                    usbTable.AddColumn(new TableColumn("[cyan]Free[/]"));
                    foreach (XbdmDriveEntry drive in usb) {
                        string name = Markup.Escape(drive.Name);
                        usbTable.AddRow(
                            $"[green]{name}[/]",
                            $"[cyan]{FormatBytes(drive.TotalBytes)}[/]",
                            $"[cyan]{FormatBytes(drive.FreeBytes)}[/]");
                    }
                    AnsiConsole.Write(usbTable);
                }
            }

            return 0;
        }, CancellationToken.None);
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
        return await CliHelpers.WithClientAsync(settings, async client => {
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
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string mode = settings.Title ? "title" : "cold";
        return await CliHelpers.WithClientAsync(settings, async client => {
            await client.SendCommandAsync($"magicboot {mode}", CancellationToken.None);
            AnsiConsole.MarkupLine(settings.Title
                ? "[green]Title reboot requested.[/]"
                : "[green]Cold reboot requested.[/]");
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
                return 0;
            }

            InstallHelpers.UninstallUserShim();
            if (!settings.Quiet)
                AnsiConsole.MarkupLine("[green]Uninstalled rgh command shim.[/]");
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
            return 0;
        }

        InstallHelpers.InstallUserShim(exeDir);
        if (!settings.Quiet)
            AnsiConsole.MarkupLine($"[green]Installed rgh command shim:[/] {Markup.Escape(InstallHelpers.ShimPath)}");
        if (!InstallHelpers.IsDirectoryOnProcessPath(InstallHelpers.WindowsAppsDir) && !settings.Quiet) {
            AnsiConsole.MarkupLine($"[yellow]Note:[/] {Markup.Escape(InstallHelpers.WindowsAppsDir)} is not on PATH. Add it or use `rgh` from its folder.");
        }
        return 0;
    }
}
