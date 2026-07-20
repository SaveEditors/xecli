using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using XeCli.Localization;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Homebrew;
using Xbox360.Remote.Cli.Logging;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Xbox360.Remote.Cli.Commands;

public sealed class StatusCommand : AsyncCommand<StatusCommand.Settings> {
    private const int LargeBannerMinimumWidth = 220;
    private const int FourCardLayoutMinimumWidth = 230;
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--quick")]
        [LocalizedDescription("Skip JRPC2, drive, and user checks for a fast snapshot.")]
        public bool Quick { get; init; }

        [CommandOption("--no-jrpc")]
        [LocalizedDescription("Skip JRPC2 queries (temps, CPU key, dashboard, title id).")]
        public bool NoJrpc { get; init; }

        [CommandOption("--no-drives")]
        [LocalizedDescription("Skip drive and USB size reporting.")]
        public bool NoDrives { get; init; }

        [CommandOption("--no-users")]
        [LocalizedDescription("Skip user list and signed-in detection.")]
        public bool NoUsers { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        CliConfig config;
        try {
            config = CliConfig.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
            return WriteConfigFailure(settings.Json, ex.Message);
        }

        (string ip, int port, int _) = await CliHelpers.ResolveTargetAsync(settings, config, CancellationToken.None, persistDefaultTarget: true);
        int timeout = settings.TimeoutMs ?? 2000;
        using CancellationTokenSource connectCts = new CancellationTokenSource(timeout);
        await using XbdmClient client = await CliHelpers.ConnectResolvedAsync(ip, port, timeout, connectCts.Token);

        int probeTimeoutMs = ProbeHelpers.GetProbeTimeoutMs(timeout);
        List<string> warnings = new List<string>();
        bool skipJrpc = settings.Quick || settings.NoJrpc;
        bool skipDrives = settings.Quick || settings.NoDrives;
        bool skipUsers = settings.Quick || settings.NoUsers;

        if (!settings.Json) {
            AnsiConsole.MarkupLine("[grey]Collecting live status snapshot...[/]");
        }

        ProbeHelpers.ProbeResult<XbdmConsoleInfo> infoProbe = await ProbeHelpers.RunProbeAsync<XbdmConsoleInfo>(
            "console info",
            async ct => await client.GetConsoleInfoAsync(ct),
            probeTimeoutMs,
            warnings.Add,
            fallback: new XbdmConsoleInfo());
        XbdmConsoleInfo info = infoProbe.Value ?? new XbdmConsoleInfo();

        ProbeHelpers.ProbeResult<string> dmVersionProbe = await ProbeHelpers.RunProbeAsync(
            "DM version",
            ct => client.GetDmVersionAsync(ct),
            probeTimeoutMs,
            warnings.Add);
        string? dmVersion = dmVersionProbe.Value?.Trim();

        ProbeHelpers.ProbeResult<string> xbdmFlavorProbe = await ProbeHelpers.RunProbeAsync(
            "XBDM flavor",
            async ct => (await client.SendCommandAsync("whomadethis", ct)).Message.Trim(),
            probeTimeoutMs,
            warnings.Add);
        string? xbdmFlavor = xbdmFlavorProbe.Value;

        ProbeHelpers.ProbeResult<string> runningXexProbe = await ProbeHelpers.RunProbeAsync(
            "running XEX",
            ct => client.GetRunningXexPathAsync(null, ct),
            probeTimeoutMs,
            warnings.Add);
        string? runningXex = runningXexProbe.Value;

        bool? jrpcAvailable = null;
        uint? titleId = null;
        uint? dashVersion = null;
        string? motherboard = null;
        string? smcVersion = null;
        string? cpuKey = null;
        uint? cpuTemp = null;
        uint? gpuTemp = null;
        uint? edramTemp = null;
        uint? mbTemp = null;

        if (!skipJrpc) {
            ProbeHelpers.ProbeResult<JrpcStatusSnapshot> jrpcProbe = await ProbeHelpers.RunProbeAsync(
                "JRPC2",
                async ct => {
                    Jrpc2Client jrpc = new Jrpc2Client(client);
                    uint probeTitleId = await jrpc.GetTitleIdAsync(ct);
                    uint probeDashVersion = await jrpc.GetDashboardVersionAsync(ct);
                    string probeMotherboard = await jrpc.GetMotherboardTypeAsync(ct);
                    string probeCpuKey = await jrpc.GetCpuKeyAsync(ct);
                    uint probeCpuTemp = await jrpc.GetTemperatureAsync(SensorType.CPU, ct);
                    uint probeGpuTemp = await jrpc.GetTemperatureAsync(SensorType.GPU, ct);
                    uint probeEdramTemp = await jrpc.GetTemperatureAsync(SensorType.EDRAM, ct);
                    uint probeMbTemp = await jrpc.GetTemperatureAsync(SensorType.MotherBoard, ct);
                    string? probeSmcVersion = null;
                    try {
                        probeSmcVersion = await HardwareHelpers.GetSmcVersionAsync(client, ct);
                    }
                    catch {
                        // ignored
                    }

                    return new JrpcStatusSnapshot(
                        probeTitleId,
                        probeDashVersion,
                        probeMotherboard,
                        probeCpuKey,
                        probeCpuTemp,
                        probeGpuTemp,
                        probeEdramTemp,
                        probeMbTemp,
                        probeSmcVersion);
                },
                probeTimeoutMs,
                warnings.Add);

            if (jrpcProbe.Value != null) {
                jrpcAvailable = true;
                titleId = jrpcProbe.Value.TitleId;
                dashVersion = jrpcProbe.Value.DashboardVersion;
                motherboard = jrpcProbe.Value.Motherboard;
                cpuKey = jrpcProbe.Value.CpuKey;
                cpuTemp = jrpcProbe.Value.CpuTemp;
                gpuTemp = jrpcProbe.Value.GpuTemp;
                edramTemp = jrpcProbe.Value.EdramTemp;
                mbTemp = jrpcProbe.Value.MbTemp;
                smcVersion = jrpcProbe.Value.SmcVersion;
            }
            else {
                jrpcAvailable = false;
            }
        }

        ProbeHelpers.ProbeResult<IReadOnlyList<XbdmDriveEntry>> drivesProbe = skipDrives
            ? new ProbeHelpers.ProbeResult<IReadOnlyList<XbdmDriveEntry>>(null, TimedOut: false, Failed: false)
            : await ProbeHelpers.RunProbeAsync<IReadOnlyList<XbdmDriveEntry>>(
                "drive list",
                async ct => await client.GetDrivesAsync(includeSize: true, ct),
                probeTimeoutMs,
                warnings.Add);
        IReadOnlyList<XbdmDriveEntry>? drives = drivesProbe.Value;

        ProbeHelpers.ProbeResult<ProfileHelpers.ResolvedIdentityInfo> usersProbe = skipUsers
            ? new ProbeHelpers.ProbeResult<ProfileHelpers.ResolvedIdentityInfo>(null, TimedOut: false, Failed: false)
            : await ProbeHelpers.RunProbeAsync<ProfileHelpers.ResolvedIdentityInfo>(
                "user list",
                async ct => await ProfileHelpers.ResolveSignedInIdentityAsync(
                    client,
                    ip,
                    port,
                    timeout,
                    config,
                    allowF3: true,
                    allowProfilePackage: true,
                    ct),
                probeTimeoutMs,
                warnings.Add);
        ProfileHelpers.ResolvedIdentityInfo? resolvedIdentity = usersProbe.Value;
        IReadOnlyList<XbdmUserInfo>? users = null;
        if (resolvedIdentity != null) {
            users = resolvedIdentity.Users;
            if ((users == null || users.Count == 0) && resolvedIdentity.XamUser != null) {
                users = new[] {
                    new XbdmUserInfo {
                        Gamertag = resolvedIdentity.XamUser.Gamertag,
                        Xuid = resolvedIdentity.XamUser.Xuid != null &&
                               ulong.TryParse(resolvedIdentity.XamUser.Xuid.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsedXuid)
                            ? parsedXuid
                            : null,
                        SignInState = resolvedIdentity.XamUser.SignInState,
                        RawLine = $"xam slot={resolvedIdentity.XamUser.Slot}"
                    }
                };
            }
        }

        bool jrpcUnavailable = !skipJrpc && jrpcAvailable == false;
        bool drivesUnavailable = !skipDrives && drivesProbe.Unavailable;
        bool usersUnavailable = !skipUsers && usersProbe.Unavailable;
        bool infoUnavailable = infoProbe.Unavailable;
        bool dmVersionUnavailable = dmVersionProbe.Unavailable;
        bool xbdmFlavorUnavailable = xbdmFlavorProbe.Unavailable;
        bool runningXexUnavailable = runningXexProbe.Unavailable;

        string? signedInUser = usersUnavailable ? null : resolvedIdentity?.Gamertag;
        string? signedInXuid = usersUnavailable ? null : resolvedIdentity?.Xuid;
        bool? isSignedIn = skipUsers ? false : (usersUnavailable ? null : resolvedIdentity?.IsSignedIn ?? false);
        string signInStateText = skipUsers
            ? "Not signed in"
            : usersUnavailable
                ? "unavailable"
                : resolvedIdentity?.SignInStateText ?? "Not signed in";

        string? titleName = null;
        TitleIdDatabase titleDatabase = TitleIdDatabase.Instance;
        if (titleId.HasValue && titleDatabase.TryResolve(titleId.Value, null, out TitleIdEntry? entry)) {
            titleName = entry?.Name;
        }
        else if (titleId.HasValue && titleDatabase.TryRememberDiscoveredTitle(titleId.Value, runningXex, out string? discoveredName)) {
            titleName = discoveredName;
        }
        titleName = ProfileHelpers.TryGetTitleFallbackName(titleId, runningXex, titleName);
        string? runningXexName = SimplifyRunningXexPath(runningXex);
        IReadOnlyList<DriveGroup>? driveGroups = drives is { Count: > 0 }
            ? GroupDrives(drives)
            : null;

        FtpEndpointResolution ftpEndpoint = await FtpEndpointHelpers.ResolveStatusEndpointAsync(
            ip,
            config,
            allowProbe: !settings.Quick,
            Math.Clamp(timeout, 800, 3000),
            CancellationToken.None);
        int ftpPort = ftpEndpoint.Port;
        bool? ftpReachable = ftpEndpoint.Reachable;
        string? pendingModuleText = null;
        if (config.PendingModuleOperation != null &&
            !string.IsNullOrWhiteSpace(config.PendingModuleOperation.Action) &&
            !string.IsNullOrWhiteSpace(config.PendingModuleOperation.ModuleName)) {
            string action = config.PendingModuleOperation.Action!;
            string moduleName = config.PendingModuleOperation.ModuleName!;
            pendingModuleText = $"{action} {moduleName}";
            bool? satisfied = null;

            ProbeHelpers.ProbeResult<IReadOnlyList<XbdmModuleInfo>> modulesProbe = await ProbeHelpers.RunProbeAsync<IReadOnlyList<XbdmModuleInfo>>(
                "pending module check",
                async ct => await client.GetModulesAsync(false, ct),
                probeTimeoutMs,
                warnings.Add);
            if (modulesProbe.Value != null) {
                bool present = modulesProbe.Value.Any(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
                satisfied = string.Equals(action, "load", StringComparison.OrdinalIgnoreCase) ? present : !present;
                if (satisfied.Value) {
                    config.PendingModuleOperation = null;
                    config.Save();
                    pendingModuleText = null;
                }
            }

            if (pendingModuleText != null) {
                pendingModuleText = modulesProbe.Unavailable
                    ? $"{action} {moduleName} (unavailable)"
                    : satisfied switch {
                        true => $"{action} {moduleName} (verified)",
                        false => $"{action} {moduleName} (pending)",
                        _ => $"{action} {moduleName}"
                    };
            }
        }

        if (warnings.Count > 0 && !settings.Json) {
            OperationFeedback.WriteWarning(
                "Status completed with unavailable data",
                string.Join("; ", warnings));
        }

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Target = new { ip, port },
                Info = info,
                DmVersion = dmVersion,
                XbdmFlavor = xbdmFlavor,
                RunningXex = runningXex,
                RunningXexName = runningXexName,
                TitleId = titleId.HasValue ? $"0x{titleId.Value:X8}" : null,
                TitleName = titleName,
                Dashboard = dashVersion,
                Motherboard = motherboard,
                SmcVersion = smcVersion,
                CpuKey = cpuKey,
                Temps = new { cpuTemp, gpuTemp, edramTemp, mbTemp },
                JrpcAvailable = jrpcAvailable,
                Drives = drives,
                Users = users,
                SignedIn = isSignedIn,
                SignInState = signInStateText,
                Gamertag = signedInUser,
                SignedInXuid = signedInXuid,
                PendingModuleOperation = pendingModuleText,
                Warnings = warnings.Count > 0 ? warnings : null,
                Ftp = new {
                    Host = ip,
                    Port = ftpPort,
                    Reachable = ftpReachable
                }
            });
            return 0;
        }

        string jrpcStatus = skipJrpc
            ? "[grey70]skipped[/]"
            : jrpcUnavailable
                ? "[yellow]unavailable[/]"
                : "[springgreen3_1]online[/]";

        const string Unknown = "[grey70]unknown[/]";
        const string Unavailable = "[yellow]unavailable[/]";
        static string FormatValue(string? value, string color, string? fallback = null) {
            if (string.IsNullOrWhiteSpace(value))
                return fallback ?? Unknown;
            return $"[{color}]{Markup.Escape(value)}[/]";
        }

        string infoFallback = infoUnavailable ? Unavailable : Unknown;
        string dmVersionFallback = dmVersionUnavailable ? Unavailable : Unknown;
        string flavorFallback = xbdmFlavorUnavailable ? Unavailable : Unknown;
        string runningXexFallback = runningXexUnavailable ? Unavailable : Unknown;
        string jrpcValueFallback = skipJrpc ? FormatSkipped(true) : (jrpcUnavailable ? Unavailable : Unknown);
        string driveFallback = skipDrives ? FormatSkipped(true) : (drivesUnavailable ? Unavailable : Unknown);
        string userFallback = GetUserValueFallback(skipUsers, usersUnavailable, isSignedIn);

        string statusIdentity = BuildStatusIdentity(signedInUser, motherboard, info.DebugName, ip, AnsiConsole.Profile.Width);

        WriteStatusBanner();
        WriteStatusIdentity(statusIdentity);

        AnsiConsole.WriteLine();
        Table table = CreateCardTable("Field", "Value", 12);
        AddStatusRow(table, "IP", "[cyan1]" + ip + "[/]");
        AddStatusRow(table, "Port", $"[deepskyblue1]{port}[/]");
        AddStatusRow(table, "Console ID", FormatValue(info.ConsoleId, "gold1", infoFallback));
        AddStatusRow(table, "Debug Name", FormatValue(info.DebugName, "springgreen3_1", infoFallback));
        AddStatusRow(table, "Motherboard", FormatValue(motherboard, "deepskyblue1", jrpcValueFallback));
        AddStatusRow(table, "Dashboard", dashVersion.HasValue ? $"[gold1]{dashVersion.Value}[/]" : jrpcValueFallback);
        AddStatusRow(table, "DM Version", FormatValue(dmVersion, "cyan1", dmVersionFallback));
        if (skipJrpc || !IsEffectivelyUnknown(smcVersion)) {
            AddStatusRow(table, "SMC Version", FormatValue(smcVersion, "deepskyblue1", jrpcValueFallback));
        }
        Table table2 = CreateCardTable("Field", "Value", 14);
        AddStatusRow(table2, "State", FormatExecutionState(info.ExecutionState));
        if (!IsSameValue(info.TitleIp, ip)) {
            AddStatusRow(table2, "Title IP", FormatValue(info.TitleIp, "cyan1", infoFallback));
        }
        AddStatusRow(table2, "Process ID", info.ProcessId.HasValue ? $"[mediumpurple3]0x{info.ProcessId.Value:X8}[/]" : infoFallback);
        AddStatusRow(table2, "Title ID", titleId.HasValue ? $"[deepskyblue1]0x{titleId.Value:X8}[/]" : jrpcValueFallback);
        AddStatusRow(table2, "Title Name", FormatValue(titleName, "springgreen3_1", jrpcValueFallback));
        AddStatusRow(table2, "Running XEX", FormatValue(runningXexName, "springgreen3_1", runningXexFallback));
        AddStatusRow(table2, "Reported by XBDM", FormatValue(xbdmFlavor, "mediumpurple3", flavorFallback));
        AddStatusRow(table2, "JRPC2", jrpcStatus);
        AddStatusRow(table2, "CPU Key", FormatValue(cpuKey, "gold1", jrpcValueFallback));
        AddStatusRow(table2, "Signed In", FormatSignedInStatus(isSignedIn, skipUsers, usersUnavailable));
        AddStatusRow(table2, "Sign-In State", skipUsers ? FormatSkipped(true) : (usersUnavailable ? Unavailable : ("[gold1]" + Markup.Escape(TrimOrNull(signInStateText) ?? signInStateText) + "[/]")));
        AddStatusRow(table2, "Gamertag", FormatValue(signedInUser, "springgreen3_1", userFallback));
        AddStatusRow(table2, "Signed In XUID", FormatValue(signedInXuid, "gold1", userFallback));
        if (!string.IsNullOrWhiteSpace(pendingModuleText)) {
            AddStatusRow(table2, "Pending Module", FormatValue(pendingModuleText, "gold1"));
        }
        Table table3 = CreateCardTable("Sensor", "Reading", 9);
        AddStatusRow(table3, "CPU", FormatTemperatureBar(cpuTemp));
        AddStatusRow(table3, "GPU", FormatTemperatureBar(gpuTemp));
        AddStatusRow(table3, "EDRAM", FormatTemperatureBar(edramTemp));
        AddStatusRow(table3, "Board", FormatTemperatureBar(mbTemp));
        Table table4 = CreateCardTable("Drive", "Usage", 13);
        if (drives != null && drives.Count > 0) {
            foreach (DriveGroup item in GroupDrives(drives)) {
                table4.AddRow(FormatDriveName(item), FormatDriveCardValue(item));
            }
        }
        else {
            AddStatusRow(table4, "Storage", driveFallback);
        }

        Grid grid = new Grid();
        if (AnsiConsole.Profile.Width >= FourCardLayoutMinimumWidth) {
            grid.AddColumn();
            grid.AddColumn();
            grid.AddColumn();
            grid.AddColumn();
            grid.AddRow(table, table2, table3, table4);
            AnsiConsole.Write(grid);
        }
        else {
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(table2);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(table3);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(table4);
        }

        return 0;
    }

    private sealed record JrpcStatusSnapshot(
        uint TitleId,
        uint DashboardVersion,
        string Motherboard,
        string CpuKey,
        uint CpuTemp,
        uint GpuTemp,
        uint EdramTemp,
        uint MbTemp,
        string? SmcVersion);

    private static string GetUserValueFallback(bool skipped, bool unavailable, bool? signedIn) {
        if (skipped) {
            return FormatSkipped(skipped: true);
        }

        if (unavailable) {
            return "[yellow]unavailable[/]";
        }

        if (signedIn == false) {
            return "[grey70]none[/]";
        }

        return "[grey70]not detected[/]";
    }

    private static string FormatSignedInStatus(bool? signedIn, bool skipped, bool unavailable) {
        if (skipped) {
            return "[grey70]skipped[/]";
        }

        if (unavailable) {
            return "[yellow]unavailable[/]";
        }

        if (!signedIn.HasValue) {
            return "[grey70]not detected[/]";
        }

        if (!signedIn.Value) {
            return "[red1]No[/]";
        }

        return "[springgreen3_1]Yes[/]";
    }

    private static void AddStatusRow(Table table, string field, string value) {
        table.AddRow("[white]" + Markup.Escape(field) + "[/]", value);
    }

    private static Table CreateCardTable(string leftHeader, string rightHeader, int leftWidth) {
        Table table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey35).Expand();
        table.AddColumn(new TableColumn("[bold grey70]" + Markup.Escape(leftHeader) + "[/]").Width(leftWidth));
        table.AddColumn(new TableColumn("[bold white]" + Markup.Escape(rightHeader) + "[/]"));
        return table;
    }

    private static void WriteStatusBanner() {
        if (AnsiConsole.Profile.Width < LargeBannerMinimumWidth) {
            AnsiConsole.Write(new Align(new Markup("[bold deepskyblue1]XeCLI[/]"), Spectre.Console.HorizontalAlignment.Center));
            AnsiConsole.WriteLine();
            return;
        }

        AnsiConsole.Write(new Align(new Markup("[bold deepskyblue1]XeCLI[/]"), Spectre.Console.HorizontalAlignment.Center));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    private static void WriteStatusIdentity(string statusIdentity) {
        string text = "XeCLI Status";
        if (!string.IsNullOrWhiteSpace(statusIdentity)) {
            text = text + " · " + statusIdentity;
        }

        AnsiConsole.Write(new Align(new Markup("[bold #ff79c6]" + Markup.Escape(text) + "[/]"), Spectre.Console.HorizontalAlignment.Center));
        AnsiConsole.WriteLine();
    }

    private static string BuildStatusIdentity(string? signedInUser, string? motherboard, string? debugName, string ip, int width) {
        List<string> parts = new string?[] {
            TrimOrNull(signedInUser),
            TrimOrNull(motherboard),
            TrimOrNull(debugName),
            ip
        }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        int maxLength = Math.Max(32, width - 20);
        while (parts.Count > 1 && string.Join(" | ", parts).Length > maxLength) {
            parts.RemoveAt(0);
        }

        return string.Join(" | ", parts);
    }

    private static string FormatTemperatureBar(uint? value) {
        if (!value.HasValue) {
            return "[grey70]unknown[/]";
        }

        int filled = (int) Math.Clamp(Math.Round((double) value.Value / 12.5), 0.0, 8.0);
        string bars = new string('▰', filled);
        string empty = new string('▱', 8 - filled);
        string color = value.Value >= 85 ? "red1" : value.Value >= 70 ? "gold1" : "springgreen3_1";
        return $"[bold white]{value.Value}°C[/]  [{color}]{bars}[/][grey35]{empty}[/]";
    }

    private static string FormatDriveCardValue(DriveGroup drive) {
        string text = "[cyan1]" + FormatBytes(drive.FreeBytes) + "[/] [grey58]free of[/] [deepskyblue1]" + FormatBytes(drive.TotalBytes) + "[/]";
        return text + "\n" + FormatDriveUsage(drive.TotalBytes, drive.FreeBytes);
    }

    private static string FormatDriveName(DriveGroup drive) {
        string escaped = Markup.Escape(drive.DisplayName);
        return drive.Family switch {
            "internal" => "[springgreen3_1]" + escaped + "[/]",
            "memoryunit" => "[mediumpurple3]" + escaped + "[/]",
            "usb" => "[deepskyblue1]" + escaped + "[/]",
            "systemext" => "[gold1]" + escaped + "[/]",
            "system" => "[mediumpurple3]" + escaped + "[/]",
            _ => "[cyan]" + escaped + "[/]",
        };
    }

    private static bool IsEffectivelyUnknown(string? value) {
        string? text = TrimOrNull(value);
        if (!string.IsNullOrWhiteSpace(text)) {
            return string.Equals(text, "unknown", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool IsSameValue(string? left, string? right) {
        return string.Equals(TrimOrNull(left), TrimOrNull(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string? TrimOrNull(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim();
    }

    private static int WriteConfigFailure(bool json, string message) {
        string detail = $"Config file is present but could not be parsed: {CommandLogRedactor.RedactFreeText(message)}";
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Status failed",
                detail,
                "STATUS_CONFIG_INVALID",
                new[] {
                    "Fix or remove config.json, then run rgh status again.",
                    "Run rgh health to inspect local configuration issues."
                }));
        }
        else {
            OperationFeedback.WriteFailure("Status failed", detail);
        }

        return 1;
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

    private static void WriteStatusHeader(string ip, int port, string? executionState, string? motherboard, string? gamertag, string? titleName) {
        List<string> segments = new List<string> {
            "[bold deepskyblue1]XeCLI Status[/]",
            $"[cyan1]{Markup.Escape(ip)}:{port}[/]",
            FormatExecutionState(executionState)
        };

        if (!string.IsNullOrWhiteSpace(motherboard))
            segments.Add($"[deepskyblue1]{Markup.Escape(motherboard.Trim())}[/]");
        if (!string.IsNullOrWhiteSpace(gamertag))
            segments.Add($"[springgreen3_1]{Markup.Escape(gamertag.Trim())}[/]");
        if (!string.IsNullOrWhiteSpace(titleName))
            segments.Add($"[grey70]{Markup.Escape(titleName.Trim())}[/]");

        Panel headerPanel = new Panel(string.Join(" [grey35]|[/] ", segments))
            .Header("[bold deeppink3]Live Session[/]")
            .BorderColor(Color.Grey);
        AnsiConsole.Write(headerPanel);
    }

    private static void WriteStatusFooter(string storageSummaryMarkup, string ftpSummaryMarkup) {
        Panel footerPanel = new Panel($"{storageSummaryMarkup}\n{ftpSummaryMarkup}")
            .Header("[bold deeppink3]Quick Access[/]")
            .BorderColor(Color.Grey);
        AnsiConsole.Write(footerPanel);
    }

    private static string BuildStorageSummaryMarkup(IReadOnlyList<DriveGroup>? driveGroups, bool skipped) {
        if (skipped)
            return "[bold deepskyblue1]Storage[/]: [grey70]skipped (--quick/--no-drives)[/]";
        if (driveGroups == null || driveGroups.Count == 0)
            return "[bold deepskyblue1]Storage[/]: [grey70]unavailable[/]";

        List<DriveGroup> top = driveGroups
            .OrderByDescending(d => d.TotalBytes ?? 0)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        List<string> entries = new List<string>(top.Count);
        foreach (DriveGroup drive in top) {
            entries.Add(
                $"[white]{Markup.Escape(GetDriveSummaryLabel(drive))}[/] " +
                $"[cyan1]{Markup.Escape(FormatBytes(drive.FreeBytes))}[/]/[deepskyblue1]{Markup.Escape(FormatBytes(drive.TotalBytes))}[/] " +
                $"{FormatDriveUsage(drive.TotalBytes, drive.FreeBytes)}");
        }

        int remaining = driveGroups.Count - top.Count;
        string more = remaining > 0 ? $" [grey50](+{remaining} more)[/]" : string.Empty;
        return $"[bold deepskyblue1]Storage[/]: {string.Join(" [grey35]|[/] ", entries)}{more}";
    }

    private static string BuildFtpSummaryMarkup(string ip, int ftpPort, bool? ftpReachable, bool probeSkipped) {
        string state = probeSkipped
            ? "[grey70]not probed[/]"
            : ftpReachable switch {
                true => "[springgreen3_1]available[/]",
                false => "[red1]service not running[/]",
                _ => "[gold1]timeout[/]"
            };

        return $"[bold deepskyblue1]FTP[/]: [cyan1]{Markup.Escape(ip)}:{ftpPort}[/] {state}";
    }

    private static string GetDriveSummaryLabel(DriveGroup drive) {
        if (drive.Family == "internal") {
            bool gameMount = drive.Aliases.Any(alias => NormalizeDriveName(alias) is "game" or "d");
            return gameMount ? "Game Mount" : "Internal";
        }

        return drive.Family switch {
            "systemext" => "SysExt",
            "system" => "System",
            _ => drive.DisplayName.TrimEnd(':')
        };
    }

    private static string? SimplifyRunningXexPath(string? runningXex) {
        if (string.IsNullOrWhiteSpace(runningXex))
            return null;

        string trimmed = runningXex.Trim();
        int separator = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return separator >= 0 && separator < trimmed.Length - 1
            ? trimmed[(separator + 1)..]
            : trimmed;
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
        [LocalizedDescription("Set the default console IP.")]
        public string? Ip { get; init; }

        [CommandOption("--port <PORT>")]
        [LocalizedDescription("Set the default port (default: 730).")]
        public int? Port { get; init; }

        [CommandOption("--clear")]
        [LocalizedDescription("Clear the saved target.")]
        public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!CliConfig.TryLoad(out CliConfig config)) {
            AnsiConsole.MarkupLine("[red]Config file is present but could not be parsed. Fix or remove config.json, then run rgh target again.[/]");
            return 1;
        }

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

        if (!TargetProfileStore.TryValidateTarget(config.DefaultIp, config.DefaultPort, out string normalizedIp, out int normalizedPort, out string validationError)) {
            AnsiConsole.MarkupLine($"[yellow]Saved default target is invalid:[/] {validationError}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Default target:[/] {normalizedIp}:{normalizedPort}");
        return 0;
    }
}

public sealed class PingCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        if (settings.TimeoutMs.HasValue && settings.TimeoutMs.Value <= 0) {
            AnsiConsole.MarkupLine("[red]--timeout must be greater than zero.[/]");
            return 1;
        }

        ConnectionSettings effective = new ConnectionSettings {
            Ip = settings.Ip,
            Port = settings.Port,
            TimeoutMs = settings.TimeoutMs ?? 2000,
            Json = settings.Json
        };

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(effective, CancellationToken.None);
        timeout = Math.Max(timeout, 250);
        using CancellationTokenSource timeoutCts = new CancellationTokenSource(timeout);
        Stopwatch sw = Stopwatch.StartNew();
        XbdmClient client;
        try {
            client = await CliHelpers.ConnectResolvedAsync(ip, port, timeout, timeoutCts.Token);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or IOException || ConnectionFailureModel.IsXbdmConnectionFailure(ex)) {
            return WritePingConnectionFailure(settings.Json, ip, port, timeout, ex);
        }

        await using (client) {
            try {
                _ = await client.GetConsoleInfoAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
                return WritePingTimeout(settings.Json, ip, port, timeout);
            }
            catch (TimeoutException) {
                return WritePingTimeout(settings.Json, ip, port, timeout);
            }

            sw.Stop();
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Target = $"{ip}:{port}",
                    Status = "ok",
                    ElapsedMs = sw.ElapsedMilliseconds
                });
            }
            else {
                AnsiConsole.MarkupLine($"[green]OK[/] {sw.ElapsedMilliseconds} ms");
            }

            return 0;
        }
    }

    private static int WritePingConnectionFailure(bool json, string ip, int port, int timeout, Exception ex) {
        string target = $"{ip}:{port}";
        string message = ConnectionFailureModel.ClassifyXbdmFailure(ex) switch {
            ConnectionFailureModel.XbdmFailureKind.ConnectTimeout =>
                $"XBDM ping to {target} timed out before the handshake completed after {timeout} ms. Check that XBDM is running and responsive, then retry with a higher --timeout if the console is just slow.",
            ConnectionFailureModel.XbdmFailureKind.HandshakeStalled =>
                $"XBDM handshake at {target} stalled after the TCP connection was accepted; no greeting arrived within {timeout} ms.",
            ConnectionFailureModel.XbdmFailureKind.HandshakeRejected =>
                ex.Message,
            ConnectionFailureModel.XbdmFailureKind.PortClosedOrRefused =>
                $"XBDM connection to {target} was refused or closed before XBDM greeted the client.",
            _ =>
                $"XBDM ping to {target} failed: {ex.Message}"
        };

        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                ConnectionFailureModel.ClassifyXbdmFailure(ex) switch {
                    ConnectionFailureModel.XbdmFailureKind.ConnectTimeout => "XBDM connection timed out",
                    ConnectionFailureModel.XbdmFailureKind.HandshakeStalled => "XBDM handshake stalled",
                    ConnectionFailureModel.XbdmFailureKind.HandshakeRejected => "XBDM handshake rejected",
                    ConnectionFailureModel.XbdmFailureKind.PortClosedOrRefused => "XBDM connection refused",
                    _ => "XBDM connection failed"
                },
                message,
                ConnectionFailureModel.XbdmConnectionFailedCode,
                new[] {
                    $"Check that XBDM at {target} is running and responsive.",
                    "Confirm the console did not freeze after the TCP connection was accepted.",
                    "Retry with a higher --timeout if the console is just slow."
                }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        }

        return 1;
    }

    private static int WritePingTimeout(bool json, string ip, int port, int timeout) {
        string target = $"{ip}:{port}";
        string message = $"XBDM ping to {target} timed out after {timeout} ms. Check that XBDM is running and responsive, then retry with a higher --timeout if the console is just slow.";

        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "XBDM ping timed out",
                message,
                ConnectionFailureModel.XbdmConnectionFailedCode,
                new[] {
                    $"Check that XBDM at {target} is running and responsive.",
                    "Confirm the console did not freeze after the TCP connection was accepted.",
                    "Retry with a higher --timeout if the console is just slow."
                }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        }

        return 1;
    }
}

public sealed class RebootCommand : AsyncCommand<RebootCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the reboot request without connecting or writing anything.")]
        public bool DryRun { get; init; }

        [CommandOption("--yes")]
        [LocalizedDescription("Skip the reboot confirmation prompt.")]
        public bool Yes { get; init; }

        [CommandOption("--title")]
        [LocalizedDescription("Restart the current title instead of a cold reboot.")]
        public bool Title { get; init; }

        [CommandOption("--wait")]
        [LocalizedDescription("Stop execution and require a changed process or XBDM disconnect and responsive reconnect.")]
        public bool Wait { get; init; }

        [CommandOption("--wait-timeout <SEC>")]
        [LocalizedDescription("Maximum seconds to wait for a verified reboot transition (default: 120).")]
        [DefaultValue(120)]
        public int WaitTimeoutSeconds { get; init; } = 120;

        [CommandOption("--notify")]
        [LocalizedDescription("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [LocalizedDescription("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [LocalizedDescription("Notification logo id (decimal or 0x hex).")]
        public string? NotifyLogo { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string mode = settings.Title ? "title" : "cold";
        string command = $"magicboot {mode}";
        if (settings.DryRun) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "reboot",
                    Mode = mode,
                    Status = "dry-run",
                    DryRun = true,
                    RequestSent = false,
                    RequestAcknowledged = false,
                    WaitRequested = settings.Wait,
                    NotifyRequested = settings.Notify,
                    Command = command
                });
                return 0;
            }

            OperationFeedback.WriteWarning(
                settings.Title ? "Title reboot preview" : "Cold reboot preview",
                $"Would send a {mode} reboot request to the console.");
            return 0;
        }

        if (settings.Json && !settings.Yes) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Reboot confirmation required",
                "Reboot was not requested because explicit confirmation is required.",
                "CONFIRMATION_REQUIRED",
                new[] { "Re-run with --yes after confirming that the console can be safely rebooted." }));
            return 1;
        }

        if (!HardwareHelpers.TryConfirmHardwareAction(
                settings.Title ? "Title reboot" : "Cold reboot",
                settings.Title ? "Restart the current title now?" : "Reboot the console now?",
                settings.Yes)) {
            return 1;
        }

        if (settings.WaitTimeoutSeconds is < 1 or > 600) {
            WriteRebootValidationFailure(settings, "--wait-timeout must be between 1 and 600 seconds.");
            return 1;
        }
        if (settings.Wait)
            return await ExecuteAndWaitAsync(settings, mode, command);

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            try {
                XbdmResponse response = await client.SendCommandAsync(command, CancellationToken.None);
                string acknowledgement = response.ExpectMessage(
                    command,
                    200,
                    XbdmResponseType.SingleResponse,
                    XbdmResponseBodyRequirement.RequiredNonEmpty);

                await NotifyHelpers.TrySendOperationNotificationAsync(
                    client,
                    settings.Notify,
                    settings.NotifyIcon,
                    settings.NotifyLogo,
                    "Success :)",
                    CancellationToken.None);

                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Operation = "reboot",
                        Mode = mode,
                        Status = "requested",
                        DryRun = false,
                        RequestSent = true,
                        RequestAcknowledged = true,
                        WaitRequested = false,
                        TransitionVerified = false,
                        NotifyRequested = settings.Notify,
                        Command = command,
                        ResponseStatusCode = response.StatusCode,
                        ResponseType = response.ResponseType.ToString(),
                        Acknowledgement = acknowledgement
                    });
                    return 0;
                }

                OperationFeedback.WriteSuccess(settings.Title ? "Title reboot requested" : "Cold reboot requested");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) {
                WriteRebootFailure(settings, ex);
                return 1;
            }
        }, CancellationToken.None);
    }

    private static async Task<int> ExecuteAndWaitAsync(Settings settings, string mode, string command) {
        (string ip, int port, int timeout) target;
        try {
            target = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) {
            WriteRebootFailure(settings, ex);
            return 1;
        }

        bool executionStopped = false;
        uint? previousProcessId = null;
        XbdmResponse response = default;
        string? acknowledgement = null;
        try {
            using XbdmClient client = await CliHelpers.ConnectResolvedAsync(target.ip, target.port, target.timeout, CancellationToken.None);
            previousProcessId = await client.GetCurrentProcessIdAsync(CancellationToken.None);
            if (!previousProcessId.HasValue)
                throw new IOException("XBDM did not return the active process ID before reboot.");

            await NotifyHelpers.TrySendOperationNotificationAsync(
                client,
                settings.Notify,
                settings.NotifyIcon,
                settings.NotifyLogo,
                "Success :)",
                CancellationToken.None);
            await client.DebugStopAsync(CancellationToken.None);
            executionStopped = true;
            response = await client.SendCommandAsync(command, CancellationToken.None);
            acknowledgement = response.ExpectMessage(
                command,
                200,
                XbdmResponseType.SingleResponse,
                XbdmResponseBodyRequirement.RequiredNonEmpty);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) {
            if (executionStopped)
                await XbdmTransitionVerifier.TryResumeAsync(target, CancellationToken.None);
            WriteRebootFailure(settings, ex);
            return 1;
        }

        XbdmTransitionObservation transition = await XbdmTransitionVerifier.WaitForProcessChangeAsync(
            target,
            previousProcessId,
            previousRunningXex: null,
            expectedXex: null,
            settings.WaitTimeoutSeconds,
            CancellationToken.None);
        if (!transition.Succeeded) {
            bool resumeSucceeded = await XbdmTransitionVerifier.TryResumeAsync(target, CancellationToken.None);
            WriteRebootTransitionFailure(settings, transition, resumeSucceeded);
            return 1;
        }

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Operation = "reboot",
                Mode = mode,
                Status = "completed",
                DryRun = false,
                RequestSent = true,
                RequestAcknowledged = true,
                WaitRequested = true,
                TransitionVerified = true,
                ExecutionStoppedBeforeRequest = executionStopped,
                transition.DisconnectObserved,
                transition.PreviousProcessId,
                transition.CurrentProcessId,
                TransitionEvidence = transition.Evidence,
                TransitionElapsedMs = transition.ElapsedMilliseconds,
                NotifyRequested = settings.Notify,
                Command = command,
                ResponseStatusCode = response.StatusCode,
                ResponseType = response.ResponseType.ToString(),
                Acknowledgement = acknowledgement
            });
            return 0;
        }

        OperationFeedback.WriteSuccess(
            settings.Title ? "Title reboot completed" : "Cold reboot completed",
            $"[green]Process transition verified in {transition.ElapsedMilliseconds} ms.[/]");
        return 0;
    }

    private static void WriteRebootValidationFailure(Settings settings, string message) {
        if (settings.Json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Reboot validation failed",
                message,
                "REBOOT_WAIT_TIMEOUT_INVALID",
                new[] { "Use a wait timeout between 1 and 600 seconds." }));
            return;
        }

        OperationFeedback.WriteFailure("Reboot validation failed", message);
    }

    private static void WriteRebootTransitionFailure(
        Settings settings,
        XbdmTransitionObservation transition,
        bool resumeSucceeded) {
        string message = $"The reboot request was acknowledged, but XBDM did not verify a changed process or disconnect and responsive reconnect within {settings.WaitTimeoutSeconds} seconds.";
        if (settings.Json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Reboot transition not observed",
                message,
                "REBOOT_TRANSITION_NOT_OBSERVED",
                new[] {
                    transition.LastError ?? "Inspect the console state before retrying.",
                    resumeSucceeded ? "XeCLI resumed execution after the failed transition check." : "XeCLI could not confirm that execution resumed; inspect the console immediately."
                }));
            return;
        }

        OperationFeedback.WriteFailure("Reboot transition not observed", message);
    }

    private static void WriteRebootFailure(Settings settings, Exception exception) {
        if (settings.Json) {
            string? targetDisplay = string.IsNullOrWhiteSpace(settings.Ip)
                ? null
                : $"{settings.Ip}:{settings.Port ?? 730}";
            if (ConnectionFailureModel.TryBuildXbdmError(
                    exception,
                    exception.Message,
                    targetDisplay,
                    out CliErrorEnvelope protocolError)) {
                CliOutput.EmitJsonError(protocolError);
                return;
            }

            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Reboot request failed",
                exception.Message,
                "REBOOT_FAILED",
                new[] {
                    "Confirm that XBDM is responsive before retrying.",
                    "Check the console state manually before sending another reboot request."
                }));
            return;
        }

        string message = exception is XbdmProtocolViolationException violation && violation.StatusCode != 200
            ? $"Reboot request rejected: {violation.RawResponse}"
            : exception.Message;
        OperationFeedback.WriteFailure("Reboot failed", message);
    }
}

public sealed class InstallCommand : Command<InstallCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--machine")]
        [LocalizedDescription("Install for all users (administrator approval required).")]
        public bool Machine { get; init; }

        [CommandOption("--uninstall")]
        [LocalizedDescription("Remove the command registration. With --machine, remove the all-users PATH entry.")]
        public bool Uninstall { get; init; }

        [CommandOption("--machine-path")]
        [LocalizedDescription("Legacy path-only install for the current executable directory (admin required).")]
        public bool MachinePath { get; init; }

        [CommandOption("--path <DIR>")]
        [LocalizedDescription("Install directory for XeCLI.")]
        public string? Path { get; init; }

        [CommandOption("--source <DIR>")]
        [LocalizedDescription("Source release directory containing rgh.exe and its runtime files.")]
        public string? Source { get; init; }

        [CommandOption("--no-path")]
        [LocalizedDescription("Do not add the install directory to PATH.")]
        public bool NoPath { get; init; }

        [CommandOption("--quiet")]
        [LocalizedDescription("Suppress non-error install output.")]
        public bool Quiet { get; init; }

        public override ValidationResult Validate() {
            if (Uninstall && (!string.IsNullOrWhiteSpace(Source) || NoPath))
                return ValidationResult.Error("Use --uninstall without --source or --no-path.");

            if (MachinePath && (Machine || Uninstall || !string.IsNullOrWhiteSpace(Path) || NoPath))
                return ValidationResult.Error("Use --machine-path without --machine, --uninstall, --path, or --no-path. Optional --source and --quiet are supported.");

            return ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!OperatingSystem.IsWindows()) {
            AnsiConsole.MarkupLine("[red]The install command is only supported on Windows.[/]");
            return 1;
        }

        ValidationResult validation = settings.Validate();
        if (!validation.Successful) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(validation.Message ?? "Validation failed.")}[/]");
            return 1;
        }

        bool interactiveInstall = IsInteractiveInstall(settings);

        string sourceDir;
        try {
            sourceDir = Path.GetFullPath(InstallHelpers.NormalizeDirectory(settings.Source ?? AppContext.BaseDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) {
            AnsiConsole.MarkupLine($"[red]Invalid install source:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        if (settings.Uninstall) {
            if (settings.MachinePath) {
                if (!InstallHelpers.IsAdministrator()) {
                    AnsiConsole.MarkupLine("[red]Machine PATH uninstall requires an elevated terminal.[/]");
                    return 1;
                }

                if (!InstallHelpers.RemoveMachinePathEntry(sourceDir, out string removeMessage)) {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(removeMessage)}[/]");
                    return 1;
                }

                if (!settings.Quiet)
                    RenderInstallSummary(
                        "PATH uninstall complete",
                        ("Scope", "Machine PATH"),
                        ("Directory", sourceDir),
                        ("Next Step", "Open a new terminal so the PATH change is picked up."));
                return 0;
            }

            string uninstallDir = ResolveUninstallDirectory(settings, sourceDir);
            if (settings.Machine) {
                if (!InstallHelpers.IsAdministrator()) {
                    AnsiConsole.MarkupLine("[red]All-users uninstall requires an elevated terminal.[/]");
                    return 1;
                }

                if (!InstallHelpers.RemoveMachinePathEntry(uninstallDir, out string removeMessage)) {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(removeMessage)}[/]");
                    return 1;
                }

                if (!settings.Quiet)
                    RenderInstallSummary(
                        "Uninstall complete",
                        ("Scope", "All users"),
                        ("Install Directory", uninstallDir),
                        ("Command Access", "Removed from machine PATH"),
                        ("Next Step", "You can delete the install folder manually if you no longer need it."));
                return 0;
            }

            bool shimRemoved = InstallHelpers.TryUninstallUserShim(out string shimMessage);
            bool pathRemoved = InstallHelpers.RemoveUserPathEntry(uninstallDir, out string pathMessage);
            if (!shimRemoved || !pathRemoved) {
                if (!shimRemoved)
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(shimMessage)}[/]");
                if (!pathRemoved)
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(pathMessage)}[/]");
                return 1;
            }
            if (!settings.Quiet)
                RenderInstallSummary(
                    "Uninstall complete",
                    ("Scope", "Current user"),
                    ("Install Directory", uninstallDir),
                    ("Command Access", "Removed from user registration"),
                    ("Next Step", "You can delete the install folder manually if you no longer need it."));
            return 0;
        }

        string sourceExePath = Path.Combine(sourceDir, "rgh.exe");
        if (!File.Exists(sourceExePath)) {
            AnsiConsole.MarkupLine($"[red]rgh.exe not found at[/] {Markup.Escape(sourceExePath)}");
            return 1;
        }

        InstallSourceValidation sourceValidation = InstallHelpers.InspectPublishedInstallSource(sourceDir);
        if (!sourceValidation.IsValid) {
            AnsiConsole.MarkupLine("[red]Install source must be a published self-contained XeCLI release.[/]");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(sourceValidation.Message)}[/]");
            if (!string.IsNullOrWhiteSpace(sourceValidation.PublishCommandHint))
                AnsiConsole.MarkupLine($"[grey]Valid install sources:[/] [white]{Markup.Escape(sourceValidation.PublishCommandHint)}[/]");
            return 1;
        }

        if (interactiveInstall && !PromptToReinstallIfNeeded(sourceDir))
            return 0;

        if (settings.MachinePath) {
            if (!InstallHelpers.IsAdministrator()) {
                AnsiConsole.MarkupLine("[red]Machine PATH install requires an elevated terminal.[/]");
                return 1;
            }

            if (!InstallHelpers.AddMachinePathEntry(sourceDir, out string addMessage)) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(addMessage)}[/]");
                return 1;
            }

            if (!settings.Quiet)
                RenderInstallSummary(
                    "PATH install complete",
                    ("Scope", "Machine PATH"),
                    ("Directory", sourceDir),
                    ("Command", "rgh"),
                    ("Next Step", "Open a new terminal and run `rgh --help`."));
            return 0;
        }

        InstallPlan plan;
        try {
            plan = BuildInstallPlan(settings, sourceDir);
        }
        catch (OperationCanceledException) {
            AnsiConsole.MarkupLine("[yellow]Installation cancelled.[/]");
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) {
            AnsiConsole.MarkupLine($"[red]Invalid install directory:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        string targetExePath = Path.Combine(plan.InstallDirectory, "rgh.exe");
        InstallDestinationValidation destinationValidation = InstallHelpers.InspectInstallDestination(
            sourceDir,
            plan.InstallDirectory,
            sourceValidation.OwnedFiles);
        if (!destinationValidation.IsValid) {
            AnsiConsole.MarkupLine("[red]Install directory cannot be updated safely.[/]");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(destinationValidation.Message)}[/]");
            return 1;
        }

        if (plan.AllUsers && !InstallHelpers.IsAdministrator()) {
            int exitCode = InstallHelpers.RunElevatedInstall(sourceExePath, sourceDir, plan.InstallDirectory, plan.AddToPath);
            if (exitCode != 0) {
                AnsiConsole.MarkupLine($"[red]Installer exited with code {exitCode}.[/]");
                return exitCode;
            }

            if (!settings.Quiet)
                RenderInstallSummary(
                    "Install complete",
                    ("Scope", "All users"),
                    ("Install Directory", plan.InstallDirectory),
                    ("Command", "rgh"),
                    ("Command Access", GetCommandAccessSummary(plan)),
                    ("Next Step", GetInstallNextStep(plan)));
            return 0;
        }

        InstallCopyResult copyResult;
        try {
            copyResult = InstallHelpers.InstallOwnedRelease(
                sourceDir,
                plan.InstallDirectory,
                sourceValidation.OwnedFiles);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) {
            AnsiConsole.MarkupLine("[red]Install copy failed safely.[/]");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        if (plan.AllUsers) {
            if (plan.AddToPath && !InstallHelpers.AddMachinePathEntry(plan.InstallDirectory, out string addMachineMessage)) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(addMachineMessage)}[/]");
                return 1;
            }
        }
        else {
            if (plan.AddToPath) {
                InstallHelpers.UninstallUserShim();
                if (!InstallHelpers.AddUserPathEntry(plan.InstallDirectory, out string addUserMessage)) {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(addUserMessage)}[/]");
                    return 1;
                }
            }
            else {
                InstallHelpers.InstallUserShim(plan.InstallDirectory);
            }
        }

        if (!settings.Quiet)
            RenderInstallSummary(
                "Install complete",
                ("Scope", plan.AllUsers ? "All users" : "Current user"),
                ("Install Directory", plan.InstallDirectory),
                ("Target EXE", targetExePath),
                ("Command", "rgh"),
                ("Command Access", GetCommandAccessSummary(plan)),
                ("Files", copyResult.WasUpgrade
                    ? $"Updated {copyResult.CopiedFileCount}; removed {copyResult.RemovedStaleFileCount} stale owned file(s)"
                    : $"Installed {copyResult.CopiedFileCount} owned file(s)"),
                ("Next Step", GetInstallNextStep(plan)));
        return 0;
    }

    private static string GetInstallNextStep(InstallPlan plan) {
        if (plan.AddToPath)
            return "Open a new terminal and run `rgh --help`.";

        if (!plan.AllUsers)
            return "Run `rgh --help` through the per-user command shim, or use the installed executable path.";

        string exePath = Path.Combine(InstallHelpers.NormalizeDirectory(plan.InstallDirectory), "rgh.exe");
        return $"Run `{exePath} --help` or reinstall with PATH enabled.";
    }

    private static string GetCommandAccessSummary(InstallPlan plan) {
        if (plan.AddToPath)
            return plan.AllUsers ? "Machine PATH updated" : "User PATH updated";
        return plan.AllUsers ? "PATH unchanged" : "Per-user rgh.cmd shim created";
    }

    private static string ResolveUninstallDirectory(Settings settings, string sourceDirectory) {
        if (!string.IsNullOrWhiteSpace(settings.Path))
            return Path.GetFullPath(InstallHelpers.NormalizeDirectory(settings.Path));

        string sourceOwnedManifest = Path.Combine(sourceDirectory, "xecli-owned-files.txt");
        string sourcePortableMarker = Path.Combine(sourceDirectory, "xecli.portable");
        if (File.Exists(sourceOwnedManifest) && !File.Exists(sourcePortableMarker))
            return InstallHelpers.NormalizeDirectory(sourceDirectory);

        string? registeredDirectory = InstallHelpers.TryResolveInstalledDirectory();
        if (!string.IsNullOrWhiteSpace(registeredDirectory))
            return InstallHelpers.NormalizeDirectory(registeredDirectory);

        return settings.Machine
            ? InstallHelpers.DefaultMachineInstallDir
            : InstallHelpers.DefaultUserInstallDir;
    }

    private static InstallPlan BuildInstallPlan(Settings settings, string sourceDir) {
        bool interactive = IsInteractiveInstall(settings);

        if (!interactive) {
            return new InstallPlan(
                settings.Machine,
                Path.GetFullPath(InstallHelpers.NormalizeDirectory(settings.Path ?? (settings.Machine ? InstallHelpers.DefaultMachineInstallDir : InstallHelpers.DefaultUserInstallDir))),
                !settings.NoPath);
        }

        AnsiConsole.Write(new Panel(
                "[bold white]XeCLI Installer[/]\n" +
                "[grey]Copies the current release into an install folder and registers `rgh`.[/]\n\n" +
                $"[white]Source:[/] [deepskyblue1]{Markup.Escape(sourceDir)}[/]\n" +
                $"[white]User default:[/] [springgreen3_1]{Markup.Escape(InstallHelpers.DefaultUserInstallDir)}[/]\n" +
                $"[white]All-users default:[/] [gold1]{Markup.Escape(InstallHelpers.DefaultMachineInstallDir)}[/]\n\n" +
                "[mediumpurple3]@SaveEditors[/]")
            .BorderColor(Color.Silver)
            .Header("[bold deepskyblue1]Install[/]"));

        AnsiConsole.MarkupLine("[bold white]1.[/] Current user [grey](Recommended)[/]");
        AnsiConsole.MarkupLine("[bold white]2.[/] All users [grey](Administrator approval required)[/]");
        string scopeChoice = ReadInstallerResponse("Choose an install scope [1]:", "1");
        bool allUsers = scopeChoice.Trim() == "2";

        string defaultDirectory = allUsers ? InstallHelpers.DefaultMachineInstallDir : InstallHelpers.DefaultUserInstallDir;
        string installDirectory = ReadInstallerResponse("Install directory:", defaultDirectory).Trim();
        if (string.IsNullOrWhiteSpace(installDirectory))
            installDirectory = defaultDirectory;

        bool addToPath = ReadInstallerYesNo("Add `rgh` to PATH for new terminals? [Y/n]:", true);

        AnsiConsole.Write(new Panel(
                $"[white]Scope:[/] {(allUsers ? "[gold1]All users[/]" : "[springgreen3_1]Current user[/]")}\n" +
                $"[white]Install directory:[/] [deepskyblue1]{Markup.Escape(InstallHelpers.NormalizeDirectory(installDirectory))}[/]\n" +
                $"[white]PATH update:[/] {(addToPath ? "[springgreen3_1]Yes[/]" : "[grey]No[/]")}")
            .BorderColor(Color.Grey)
            .Header("[bold deepskyblue1]Plan[/]"));

        if (!ReadInstallerYesNo("Continue with installation? [Y/n]:", true))
            throw new OperationCanceledException("Installer cancelled by user.");

        return new InstallPlan(allUsers, Path.GetFullPath(InstallHelpers.NormalizeDirectory(installDirectory)), addToPath);
    }

    private static void RenderInstallSummary(string title, params (string Label, string Value)[] rows) {
        OperationFeedback.WriteSuccess(title, "[grey]@SaveEditors[/]");
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Field[/]"));
        table.AddColumn(new TableColumn("[bold white]Value[/]"));
        foreach ((string label, string value) in rows) {
            table.AddRow(
                $"[white]{Markup.Escape(label)}[/]",
                $"[springgreen3_1]{Markup.Escape(value)}[/]");
        }

        AnsiConsole.Write(table);
    }

    private static bool IsInteractiveInstall(Settings settings) {
        return !settings.Quiet &&
               !Console.IsInputRedirected &&
               string.IsNullOrWhiteSpace(settings.Path) &&
               !settings.Machine &&
               !settings.NoPath;
    }

    private static bool PromptToReinstallIfNeeded(string sourceDir) {
        string? installedDirectory = InstallHelpers.TryResolveInstalledDirectory();
        if (string.IsNullOrWhiteSpace(installedDirectory))
            return true;

        string state = InstallHelpers.IsSameDirectory(installedDirectory, sourceDir)
            ? "[grey]This copy is already the registered XeCLI install.[/]"
            : "[grey]A registered XeCLI install is already available on this system.[/]";

        AnsiConsole.Write(new Panel(
                $"[bold white]XeCLI is already installed[/]\n{state}\n\n" +
                $"[white]Registered location:[/] [springgreen3_1]{Markup.Escape(installedDirectory)}[/]\n" +
                $"[white]Current source:[/] [deepskyblue1]{Markup.Escape(sourceDir)}[/]")
            .BorderColor(Color.Silver)
            .Header("[bold deepskyblue1]Reinstall[/]"));

        return ReadInstallerYesNo("Would you like to reinstall or update it? [y/N]:", false);
    }

    private static string ReadInstallerResponse(string prompt, string? defaultValue = null) {
        AnsiConsole.Markup($"[white]{Markup.Escape(prompt)}[/] ");
        string? value = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue ?? string.Empty;
        return value.Trim();
    }

    private static bool ReadInstallerYesNo(string prompt, bool defaultYes) {
        string defaultValue = defaultYes ? "y" : "n";
        while (true) {
            string answer = ReadInstallerResponse(prompt, defaultValue);
            if (string.IsNullOrWhiteSpace(answer))
                return defaultYes;

            answer = answer.Trim().ToLowerInvariant();
            if (LocalizedText.IsAffirmative(answer))
                return true;
            if (LocalizedText.IsNegative(answer))
                return false;

            AnsiConsole.MarkupLine("[red]Please enter Y or N.[/]");
        }
    }

    private sealed record InstallPlan(bool AllUsers, string InstallDirectory, bool AddToPath);
}

