using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmInfoCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        return await CliHelpers.WithClientAsync((ip, port, timeout), settings, async client => {
            int probeTimeoutMs = ProbeHelpers.GetProbeTimeoutMs(timeout);
            List<string> warnings = new List<string>();

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

            ProbeHelpers.ProbeResult<string> titleIdProbe = await ProbeHelpers.RunProbeAsync(
                "JRPC2 title ID",
                async ct => {
                    Jrpc2Client jrpc = new Jrpc2Client(client);
                    uint titleId = await jrpc.GetTitleIdAsync(ct);
                    return $"0x{titleId:X8}";
                },
                probeTimeoutMs,
                warnings.Add);
            string? titleId = titleIdProbe.Value;

            ProbeHelpers.ProbeResult<IReadOnlyList<XbdmDriveEntry>> drivesProbe = await ProbeHelpers.RunProbeAsync<IReadOnlyList<XbdmDriveEntry>>(
                "drive list",
                async ct => {
                    IReadOnlyList<XbdmDriveEntry> rawDrives = await client.GetDrivesAsync(includeSize: false, ct);
                    List<XbdmDriveEntry> resolvedDrives = new List<XbdmDriveEntry>(rawDrives.Count);
                    for (int i = 0; i < rawDrives.Count; i++) {
                        XbdmDriveEntry drive = rawDrives[i];
                        ProbeHelpers.ProbeResult<XbdmDriveEntry> driveSizeProbe = await ProbeHelpers.RunProbeAsync(
                            $"drive size for {drive.Name}",
                            async driveCt => {
                                IReadOnlyList<string> sizeLines = await client.SendCommandForLinesAsync($"drivefreespace name=\"{drive.Name}\\\"", driveCt);
                                if (sizeLines.Count == 1) {
                                    TryGetHexValue(sizeLines[0], "totalbyteslo", out uint totalLo);
                                    TryGetHexValue(sizeLines[0], "totalbyteshi", out uint totalHi);
                                    TryGetHexValue(sizeLines[0], "totalfreebyteslo", out uint freeLo);
                                    TryGetHexValue(sizeLines[0], "totalfreebyteshi", out uint freeHi);
                                    return drive with {
                                        TotalBytes = ((ulong) totalHi << 32) | totalLo,
                                        FreeBytes = ((ulong) freeHi << 32) | freeLo
                                    };
                                }

                                return drive;
                            },
                            probeTimeoutMs,
                            warnings.Add,
                            fallback: drive);
                        resolvedDrives.Add(driveSizeProbe.Value ?? drive);
                        if (driveSizeProbe.Unavailable) {
                            for (int j = i + 1; j < rawDrives.Count; j++) {
                                resolvedDrives.Add(rawDrives[j]);
                            }
                            break;
                        }
                    }

                    return resolvedDrives;
                },
                probeTimeoutMs,
                warnings.Add,
                fallback: Array.Empty<XbdmDriveEntry>());
            IReadOnlyList<XbdmDriveEntry> drives = drivesProbe.Value ?? Array.Empty<XbdmDriveEntry>();

            ProbeHelpers.ProbeResult<IReadOnlyList<XbdmUserInfo>> usersProbe = await ProbeHelpers.RunProbeAsync<IReadOnlyList<XbdmUserInfo>>(
                "user list",
                async ct => await client.GetUserListAsync(ct),
                probeTimeoutMs,
                warnings.Add,
                fallback: Array.Empty<XbdmUserInfo>());
            IReadOnlyList<XbdmUserInfo> users = usersProbe.Value ?? Array.Empty<XbdmUserInfo>();

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Console = info,
                    Port = port,
                    DmVersion = dmVersion,
                    TitleId = titleId,
                    Drives = drives,
                    Users = users,
                    Warnings = warnings.Count > 0 ? warnings : null
                });
                return 0;
            }

            if (warnings.Count > 0) {
                OperationFeedback.WriteWarning(
                    "XBDM info completed with unavailable data",
                    string.Join("; ", warnings));
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Console[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]Field[/]"));
            table.AddColumn(new TableColumn("[white]Value[/]"));
            table.AddRow("[grey]Console ID[/]", info.ConsoleId != null ? $"[gold1]{Markup.Escape(info.ConsoleId)}[/]" : "[grey]unknown[/]");
            table.AddRow("[grey]Debug Name[/]", info.DebugName != null ? $"[green]{Markup.Escape(info.DebugName)}[/]" : "[grey]unknown[/]");
            table.AddRow("[grey]Execution State[/]", info.ExecutionState != null ? $"[cyan]{Markup.Escape(info.ExecutionState)}[/]" : "[grey]unknown[/]");
            table.AddRow("[grey]Title IP[/]", info.TitleIp != null ? $"[cyan]{Markup.Escape(info.TitleIp)}[/]" : "[grey]unknown[/]");
            table.AddRow("[grey]Process ID[/]", info.ProcessId?.ToString() ?? "[grey]unknown[/]");
            table.AddRow("[grey]Port[/]", port.ToString());
            table.AddRow("[grey]DM Version[/]", dmVersion != null ? $"[cyan]{Markup.Escape(dmVersion)}[/]" : "[grey]unknown[/]");
            table.AddRow("[grey]Title ID[/]", titleId != null ? $"[deepskyblue1]{Markup.Escape(titleId)}[/]" : "[grey]unknown[/]");
            AnsiConsole.Write(table);

            if (drives.Count > 0) {
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

            if (users.Count > 0) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Users[/]").RuleStyle("grey"));
                Table userTable = CliOutput.CreateTable();
                userTable.AddColumn(new TableColumn("[green]Gamertag[/]"));
                userTable.AddColumn(new TableColumn("[gold1]XUID[/]"));
                userTable.AddColumn(new TableColumn("[grey]Raw[/]"));
                foreach (XbdmUserInfo user in users) {
                    userTable.AddRow(
                        user.Gamertag != null ? $"[green]{Markup.Escape(user.Gamertag)}[/]" : "[grey]unknown[/]",
                        user.Xuid.HasValue ? $"0x{user.Xuid.Value:X16}" : "[grey]unknown[/]",
                        Markup.Escape(user.RawLine));
                }
                AnsiConsole.Write(userTable);
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

    private static bool TryGetHexValue(string text, string key, out uint value) {
        value = 0;
        int keyIndex = text.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0)
            return false;

        int start = keyIndex + key.Length + 1;
        int end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;

        string rawValue = text[start..end];
        if (rawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            rawValue = rawValue[2..];
        }

        return uint.TryParse(rawValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}

public sealed class XbdmModulesListCommand : AsyncCommand<XbdmModulesListCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--sections")]
        [LocalizedDescription("Include section details.")]
        public bool Sections { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(settings.Sections, CancellationToken.None);

            if (settings.Json) {
                CliOutput.EmitJson(modules);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Modules[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]#[/]").Centered());
            table.AddColumn(new TableColumn("[green]Name[/]"));
            table.AddColumn(new TableColumn("[cyan]Base[/]"));
            table.AddColumn(new TableColumn("[cyan]Size[/]"));
            table.AddColumn(new TableColumn("[gold1]Entry[/]"));
            table.AddColumn(new TableColumn("[grey]Timestamp (Local)[/]"));
            int index = 1;
            foreach (XbdmModuleInfo module in modules) {
                uint moduleSize = XbdmAddressResolutionHelpers.GetModuleSize(module);
                table.AddRow(
                    $"[grey]{index}[/]",
                    $"[green]{Markup.Escape(module.Name)}[/]",
                    $"[cyan]0x{module.BaseAddress:X8}[/]",
                    $"[cyan]0x{moduleSize:X8}[/]",
                    module.EntryPoint.HasValue ? $"[gold1]0x{module.EntryPoint.Value:X8}[/]" : "[grey]unknown[/]",
                    CliOutput.FormatTimestamp(module.Timestamp));
                index++;
            }

            AnsiConsole.Write(table);
            foreach (XbdmModuleInfo module in modules) {
                foreach (string warning in module.Warnings) {
                    OperationFeedback.WriteWarning(
                        $"Module partial data: {module.Name}",
                        $"[yellow]{Markup.Escape(warning)}[/]");
                }
            }

            if (settings.Sections) {
                foreach (XbdmModuleInfo module in modules) {
                    if (module.Sections.Count == 0)
                        continue;
                    AnsiConsole.Write(new Rule($"[bold deepskyblue1]Sections for {Markup.Escape(module.Name)}[/]").RuleStyle("grey"));
                    Table secTable = CliOutput.CreateTable();
                    secTable.AddColumn(new TableColumn("[green]Name[/]"));
                    secTable.AddColumn(new TableColumn("[cyan]Base[/]"));
                    secTable.AddColumn(new TableColumn("[cyan]Size[/]"));
                    secTable.AddColumn(new TableColumn("[grey]Index[/]"));
                    secTable.AddColumn(new TableColumn("[grey]Flags[/]"));
                    foreach (XbdmSectionInfo sec in module.Sections) {
                        secTable.AddRow(
                            sec.Name != null ? $"[green]{Markup.Escape(sec.Name)}[/]" : "[grey](unnamed)[/]",
                            $"[cyan]0x{sec.BaseAddress:X8}[/]",
                            $"[cyan]0x{sec.Size:X8}[/]",
                            $"[grey]{sec.Index}[/]",
                            $"[grey]0x{sec.Flags:X8}[/]");
                    }
                    AnsiConsole.Write(secTable);
                }
            }

            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmModulesDumpCommand : AsyncCommand<XbdmModulesDumpCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--name <MODULE>")]
        [LocalizedDescription("Module name, e.g. default.xex or xam.xex.")]
        public string? Name { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Output file path.")]
        public string? Output { get; init; }

        [CommandOption("--all")]
        [LocalizedDescription("Dump all modules to a directory.")]
        public bool All { get; init; }

        [CommandOption("--dir <DIR>")]
        [LocalizedDescription("Output directory for --all.")]
        public string? Directory { get; init; }

        [CommandOption("--session-delay <MS>")]
        [LocalizedDescription("Delay between isolated module transfer sessions (default: 1500 ms; range: 250-10000).")]
        public int? SessionDelayMs { get; init; }

        [CommandOption("--readable-only")]
        [LocalizedDescription("With --all, skip modules whose full memory span is not reported readable by XBDM.")]
        public bool ReadableOnly { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (settings.All) {
            if (string.IsNullOrWhiteSpace(settings.Directory)) {
                return WriteValidationFailure(settings, "--dir is required with --all.");
            }

            if (settings.SessionDelayMs is < 250 or > 10000) {
                return WriteValidationFailure(settings, "--session-delay must be between 250 and 10000 milliseconds.");
            }
        }
        else if (string.IsNullOrWhiteSpace(settings.Name) && string.IsNullOrWhiteSpace(settings.Output)) {
            return WriteValidationFailure(settings, "--name and --out are required.");
        }
        else if (string.IsNullOrWhiteSpace(settings.Name)) {
            return WriteValidationFailure(settings, "--name is required.");
        }
        else if (string.IsNullOrWhiteSpace(settings.Output)) {
            return WriteValidationFailure(settings, "--out is required.");
        }

        if (!settings.All && settings.ReadableOnly)
            return WriteValidationFailure(settings, "--readable-only requires --all.");

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        IReadOnlyList<XbdmModuleInfo> modules;
        IReadOnlyList<XbdmMemoryRegion> memoryRegions;
        using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions { Host = ip, Port = port, TimeoutMs = timeout }, CancellationToken.None)) {
            modules = await client.GetModulesAsync(false, CancellationToken.None);
            memoryRegions = await client.GetMemoryRegionsAsync(CancellationToken.None);
        }
        if (modules.Count == 0)
            return WriteValidationFailure(settings, "XBDM returned an empty module inventory.");
        if (memoryRegions.Count == 0)
            return WriteValidationFailure(settings, "XBDM returned an empty live memory map; module dump coverage cannot be verified.");

        if (settings.All) {
            List<ModuleDumpCoverageGap> coverageGaps = modules
                .Select(module => FindCoverageGap(module, memoryRegions))
                .Where(gap => gap != null)
                .Cast<ModuleDumpCoverageGap>()
                .ToList();
            if (coverageGaps.Count > 0 && !settings.ReadableOnly)
                return WriteUnreadableRangeFailure(settings, coverageGaps);

            HashSet<string> skippedModuleNames = coverageGaps
                .Select(gap => gap.Module)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<XbdmModuleInfo> modulesToDump = modules
                .Where(module => !skippedModuleNames.Contains(module.Name))
                .ToList();
            if (modulesToDump.Count == 0)
                return WriteUnreadableRangeFailure(settings, coverageGaps);

            Directory.CreateDirectory(settings.Directory!);
            string stagingDirectory = Path.Combine(settings.Directory!, $".xecli-modules-{Guid.NewGuid():N}.staging");
            Directory.CreateDirectory(stagingDirectory);
            int sessionDelayMs = settings.SessionDelayMs ?? 1500;
            List<object> dumpedModules = new List<object>(modulesToDump.Count);
            List<(XbdmModuleInfo Module, uint Size, string StagedPath, string OutputPath)> stagedModules = new(modulesToDump.Count);
            HashSet<string> outputNames = new(StringComparer.OrdinalIgnoreCase);

            try {
                for (int index = 0; index < modulesToDump.Count; index++) {
                    XbdmModuleInfo module = modulesToDump[index];
                    uint moduleSize = XbdmAddressResolutionHelpers.GetModuleSize(module);
                    string safeName = SanitizeFileName(module.Name);
                    if (!outputNames.Add(safeName))
                        return WriteValidationFailure(settings, $"Multiple loaded modules map to the same output file name: {safeName}");

                    string stagedPath = Path.Combine(stagingDirectory, safeName);
                    string outputPath = Path.Combine(settings.Directory!, safeName);
                    int dumpExitCode = await CliHelpers.WithClientAsync((ip, port, timeout), settings, async client => {
                        await AtomicFileWriter.WriteAsync(stagedPath, async stream => {
                            if (settings.Json) {
                                await client.ReadMemoryAsync(module.BaseAddress, moduleSize, stream, null, CancellationToken.None);
                            }
                            else {
                                await CliOutput.RunWithProgressAsync($"Capturing {module.Name}", moduleSize, progress =>
                                    client.ReadMemoryAsync(module.BaseAddress, moduleSize, stream, progress, CancellationToken.None));
                            }
                        });
                        return 0;
                    }, CancellationToken.None);
                    if (dumpExitCode != 0)
                        return dumpExitCode;

                    _ = BuildDumpResult(module, moduleSize, stagedPath);
                    stagedModules.Add((module, moduleSize, stagedPath, outputPath));

                    if (index < modulesToDump.Count - 1)
                        await Task.Delay(sessionDelayMs);
                }

                foreach ((XbdmModuleInfo module, uint moduleSize, string stagedPath, string outputPath) in stagedModules) {
                    File.Move(stagedPath, outputPath, overwrite: true);
                    object dumpResult = BuildDumpResult(module, moduleSize, outputPath);
                    dumpedModules.Add(dumpResult);
                    if (!settings.Json)
                        AnsiConsole.MarkupLine($"[green]Dumped[/] {module.Name} -> {GetDisplayName(outputPath)}");
                }
            }
            finally {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Mode = settings.ReadableOnly ? "all-readable" : "all",
                    Directory = settings.Directory,
                    SessionDelayMs = sessionDelayMs,
                    Complete = coverageGaps.Count == 0,
                    Count = dumpedModules.Count,
                    Modules = dumpedModules,
                    SkippedCount = coverageGaps.Count,
                    Skipped = coverageGaps
                });
            }

            return 0;
        }

        XbdmModuleInfo? moduleSingle = modules.FirstOrDefault(m => string.Equals(m.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
        if (moduleSingle == null) {
            return WriteValidationFailure(settings, $"Module not found: {settings.Name}");
        }

        ModuleDumpCoverageGap? singleCoverageGap = FindCoverageGap(moduleSingle, memoryRegions);
        if (singleCoverageGap != null)
            return WriteUnreadableRangeFailure(settings, new[] { singleCoverageGap });

        uint singleModuleSize = XbdmAddressResolutionHelpers.GetModuleSize(moduleSingle);
        int singleDumpExitCode = await CliHelpers.WithClientAsync((ip, port, timeout), settings, async client => {
            await AtomicFileWriter.WriteAsync(settings.Output!, async stream => {
                if (settings.Json) {
                    await client.ReadMemoryAsync(moduleSingle.BaseAddress, singleModuleSize, stream, null, CancellationToken.None);
                }
                else {
                    await CliOutput.RunWithProgressAsync($"Dumping {moduleSingle.Name}", singleModuleSize, progress =>
                        client.ReadMemoryAsync(moduleSingle.BaseAddress, singleModuleSize, stream, progress, CancellationToken.None));
                }
            });
            return 0;
        }, CancellationToken.None);
        if (singleDumpExitCode != 0)
            return singleDumpExitCode;

        object singleDumpResult = BuildDumpResult(moduleSingle, singleModuleSize, settings.Output!);
        if (settings.Json)
            CliOutput.EmitJson(singleDumpResult);
        else
            AnsiConsole.MarkupLine($"[green]Dumped[/] {moduleSingle.Name} to {GetDisplayName(settings.Output!)}");
        return 0;
    }

    private static object BuildDumpResult(XbdmModuleInfo module, uint expectedSize, string outputPath) {
        FileInfo outputFile = new FileInfo(outputPath);
        if (!outputFile.Exists || outputFile.Length != expectedSize) {
            throw new IOException(
                $"Module dump length mismatch for {module.Name}: expected {expectedSize} byte(s), found {(outputFile.Exists ? outputFile.Length : 0)}.");
        }

        return new {
            Module = module.Name,
            BaseAddress = $"0x{module.BaseAddress:X8}",
            Size = expectedSize,
            Output = outputPath,
            FileSize = outputFile.Length,
            Sha256 = FileHashHelpers.ComputeSha256(outputPath)
        };
    }

    private static int WriteValidationFailure(Settings settings, string detail) {
        if (settings.Json) {
            CliValidationOutput.Write(
                settings,
                "Module dump validation failed",
                detail,
                "XBDM_MODULE_DUMP_VALIDATION_FAILED",
                "Select a loaded module and a writable output path, or use --all with --dir.");
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(detail)}[/]");
        }

        return 1;
    }

    private static int WriteUnreadableRangeFailure(Settings settings, IReadOnlyList<ModuleDumpCoverageGap> gaps) {
        ModuleDumpCoverageGap first = gaps[0];
        string detail = gaps.Count == 1
            ? $"{first.Module} contains an unmapped span beginning at {first.FirstUnmappedAddress}; refusing to fabricate a contiguous dump."
            : $"{gaps.Count} loaded modules contain unmapped spans; the first is {first.Module} at {first.FirstUnmappedAddress}.";

        if (settings.Json) {
            CliValidationOutput.Write(
                settings,
                "Module dump range is not fully readable",
                detail,
                "XBDM_MODULE_DUMP_UNREADABLE_RANGE",
                "Use --all --readable-only to export fully covered modules and receive an explicit skipped-module list.",
                "Use `rgh mem regions --json` to inspect the live memory map.");
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(detail)}[/]");
        }

        return 1;
    }

    private static ModuleDumpCoverageGap? FindCoverageGap(XbdmModuleInfo module, IReadOnlyList<XbdmMemoryRegion> regions) {
        ulong start = module.BaseAddress;
        ulong end = start + XbdmAddressResolutionHelpers.GetModuleSize(module);
        ulong cursor = start;

        foreach (XbdmMemoryRegion region in regions.OrderBy(region => region.BaseAddress)) {
            ulong regionStart = region.BaseAddress;
            ulong regionEnd = regionStart + region.Size;
            if (regionEnd <= cursor)
                continue;
            if (regionStart >= end)
                break;
            if (regionStart > cursor)
                break;

            cursor = Math.Min(regionEnd, end);
            if (cursor >= end)
                return null;
        }

        return new ModuleDumpCoverageGap(
            module.Name,
            $"0x{module.BaseAddress:X8}",
            XbdmAddressResolutionHelpers.GetModuleSize(module),
            $"0x{cursor:X8}");
    }

    private sealed record ModuleDumpCoverageGap(
        string Module,
        string BaseAddress,
        uint Size,
        string FirstUnmappedAddress);

    private static string GetDisplayName(string path) {
        string leafName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(leafName) ? path : leafName;
    }

    private static string SanitizeFileName(string name) {
        string fileName = name;
        foreach (char invalid in Path.GetInvalidFileNameChars()) {
            fileName = fileName.Replace(invalid, '_');
        }
        return fileName;
    }
}

public sealed class XbdmXexDumpCommand : AsyncCommand<XbdmXexDumpCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--path <XEX>")]
        [LocalizedDescription("Explicit XEX path. If omitted, uses the running title.")]
        public string? Path { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Output file path.")]
        public string? Output { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Output)) {
            WriteValidationFailure(settings, "--out is required.");
            return 1;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            string? path = settings.Path ?? await client.GetRunningXexPathAsync(null, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(path)) {
                WriteValidationFailure(settings, "Unable to resolve running XEX. Use --path.");
                return 1;
            }

            await AtomicFileWriter.WriteAsync(settings.Output, async stream => {
                if (settings.Json) {
                    await client.DownloadFileAsync(path, stream, null, CancellationToken.None);
                }
                else {
                    await CliOutput.RunWithProgressAsync($"Downloading {path}", null, progress =>
                        client.DownloadFileAsync(path, stream, progress, CancellationToken.None));
                }
            });

            FileInfo outputFile = new FileInfo(settings.Output);
            if (!outputFile.Exists || outputFile.Length < 4)
                throw new IOException($"XEX dump was incomplete: expected at least 4 bytes, found {(outputFile.Exists ? outputFile.Length : 0)}.");

            byte[] header = new byte[4];
            await using (FileStream stream = outputFile.OpenRead()) {
                int read = await stream.ReadAsync(header, CancellationToken.None);
                if (read != header.Length)
                    throw new IOException($"XEX dump header was incomplete: expected 4 bytes, read {read}.");
            }

            string magic = System.Text.Encoding.ASCII.GetString(header);
            if (!string.Equals(magic, "XEX1", StringComparison.Ordinal) &&
                !string.Equals(magic, "XEX2", StringComparison.Ordinal)) {
                throw new InvalidDataException($"Downloaded file is not a valid XEX image: expected XEX1 or XEX2 magic, found {Convert.ToHexString(header)}.");
            }

            string sha256 = FileHashHelpers.ComputeSha256(settings.Output);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Path = path,
                    Output = settings.Output,
                    FileSize = outputFile.Length,
                    Sha256 = sha256,
                    Magic = magic
                });
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]Downloaded[/] {path} to {Path.GetFileName(settings.Output)}");
            AnsiConsole.MarkupLine($"[grey]Size:[/] {outputFile.Length} bytes");
            AnsiConsole.MarkupLine($"[grey]SHA-256:[/] {sha256}");
            return 0;
        }, CancellationToken.None);
    }

    private static void WriteValidationFailure(Settings settings, string detail) {
        if (settings.Json) {
            CliValidationOutput.Write(
                settings,
                "XEX dump validation failed",
                detail,
                "XBDM_XEX_DUMP_VALIDATION_FAILED",
                "Pass a writable --out file path and, when auto-detection is unavailable, an explicit --path.");
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(detail)}[/]");
        }
    }
}

public sealed class XbdmMemDumpCommand : AsyncCommand<XbdmMemDumpCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        [Description("Absolute console address to dump.")]
        public string? Address { get; init; }

        [CommandOption("--module <NAME>")]
        [Description("Loaded module name used with --rva or --ghidra. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <RVA>")]
        [Description("Module-relative virtual address.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [Description("Address copied from Ghidra for the selected module.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [Description("Image base configured in Ghidra.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }

        [CommandOption("--out <FILE>")]
        public string? Output { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings)) {
            return 1;
        }

        if (!CliHelpers.TryParseUInt32(settings.Size, out uint size) ||
            string.IsNullOrWhiteSpace(settings.Output)) {
            if (settings.Json) {
                CliValidationOutput.Write(
                    settings,
                    "Memory dump validation failed",
                    "--size and --out are required.",
                    "XBDM_MEMORY_DUMP_VALIDATION_FAILED",
                    "Pass a positive --size and a writable --out file path.");
            }
            else {
                AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> or --module <name> (--rva <hex|dec> | --ghidra <addr> --ghidra-base <addr>) --size <hex|dec> --out <file>");
            }
            return 1;
        }

        if (!MemoryRangeValidationHelpers.ValidateRequestedSize(size, settings))
            return 1;

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(client, settings, cts.Token, printResolution: !settings.Json);
            if (resolved == null)
                return 1;

            uint address = resolved.Address;
            if (!MemoryRangeValidationHelpers.ValidateResolvedSpan(address, size, settings))
                return 1;

            await AtomicFileWriter.WriteAsync(settings.Output, async stream => {
                if (settings.Json) {
                    await client.ReadMemoryAsync(address, size, stream, null, cts.Token);
                }
                else {
                    await CliOutput.RunWithProgressAsync($"Dumping 0x{address:X8}", size, progress =>
                        client.ReadMemoryAsync(address, size, stream, progress, cts.Token));
                }
            });

            FileInfo outputFile = new FileInfo(settings.Output);
            if (outputFile.Length != size)
                throw new IOException($"Memory dump size mismatch: expected {size} bytes, wrote {outputFile.Length} bytes.");

            string sha256 = FileHashHelpers.ComputeSha256(settings.Output);

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Address = $"0x{address:X8}",
                    Size = size,
                    Output = settings.Output,
                    FileSize = outputFile.Length,
                    Sha256 = sha256,
                    Resolution = XbdmMemoryJsonOutput.BuildAddressResolutionJson(resolved)
                });
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]Dumped[/] 0x{address:X8} ({size} bytes) to {GetDisplayName(settings.Output)}");
            AnsiConsole.MarkupLine($"[grey]SHA-256:[/] {sha256}");
            return 0;
        }, CancellationToken.None);
    }

    private static string GetDisplayName(string path) {
        string leafName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(leafName) ? path : leafName;
    }
}

public sealed class XbdmMemHexDumpCommand : AsyncCommand<XbdmMemHexDumpCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        [Description("Absolute console address to read.")]
        public string? Address { get; init; }

        [CommandOption("--module <NAME>")]
        [Description("Loaded module name used with --rva or --ghidra. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <RVA>")]
        [Description("Module-relative virtual address.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [Description("Address copied from Ghidra for the selected module.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [Description("Image base configured in Ghidra.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings)) {
            return 1;
        }

        if (!CliHelpers.TryParseUInt32(settings.Size, out uint size)) {
            CliValidationOutput.Write(
                settings,
                "Memory hexdump validation failed",
                "--size is required and must be a hexadecimal or decimal integer.",
                "XBDM_MEMORY_HEXDUMP_VALIDATION_FAILED",
                "Pass a positive --size value.");
            return 1;
        }

        if (!MemoryRangeValidationHelpers.ValidateRequestedSize(size, settings))
            return 1;

        uint requestedSize = size;
        bool truncated = false;
        if (size > 0x100000) {
            if (!settings.Json)
                AnsiConsole.MarkupLine("[yellow]Large hexdumps are truncated to 1MB. Use `rgh mem dump` or `mem dump` for larger ranges.[/]");
            size = 0x100000;
            truncated = true;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(client, settings, cts.Token, printResolution: !settings.Json);
            if (resolved == null)
                return 1;

            uint address = resolved.Address;
            if (!MemoryRangeValidationHelpers.ValidateResolvedSpan(address, size, settings))
                return 1;

            byte[] data = await client.ReadMemoryBytesReliableAsync(address, checked((int) size), cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Address = $"0x{address:X8}",
                    Size = data.Length,
                    RequestedSize = requestedSize,
                    Truncated = truncated,
                    DataHex = Convert.ToHexString(data),
                    Lines = XbdmMemoryJsonOutput.BuildHexDumpJsonLines(address, data),
                    Resolution = XbdmMemoryJsonOutput.BuildAddressResolutionJson(resolved)
                });
                return 0;
            }

            CliOutput.RenderHexDump(address, data);
            return 0;
        }, CancellationToken.None);
    }

}

internal static class XbdmMemoryJsonOutput {
    public static object BuildAddressResolutionJson(XbdmResolvedAddress resolved) {
        return new {
            resolved.Source,
            Address = $"0x{resolved.Address:X8}",
            Module = resolved.Module?.Name,
            ModuleBase = resolved.Module != null ? $"0x{resolved.Module.BaseAddress:X8}" : null,
            Rva = resolved.Rva.HasValue ? $"0x{resolved.Rva.Value:X8}" : null,
            GhidraAddress = resolved.GhidraAddress.HasValue ? $"0x{resolved.GhidraAddress.Value:X8}" : null,
            GhidraBase = resolved.GhidraBase.HasValue ? $"0x{resolved.GhidraBase.Value:X8}" : null,
            resolved.Warning
        };
    }

    public static object[] BuildHexDumpJsonLines(uint baseAddress, byte[] data, int width = 16) {
        List<object> lines = new List<object>();
        for (int offset = 0; offset < data.Length; offset += width) {
            int lineCount = Math.Min(width, data.Length - offset);
            byte[] row = new byte[lineCount];
            Array.Copy(data, offset, row, 0, lineCount);
            char[] ascii = new char[lineCount];
            for (int i = 0; i < lineCount; i++) {
                byte value = row[i];
                ascii[i] = value >= 32 && value <= 126 ? (char)value : '.';
            }

            lines.Add(new {
                Address = $"0x{baseAddress + (uint)offset:X8}",
                Hex = Convert.ToHexString(row),
                Ascii = new string(ascii)
            });
        }

        return lines.ToArray();
    }
}

public sealed class XbdmFsListCommand : AsyncCommand<XbdmFsListCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--path <DIR>")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            IReadOnlyList<XbdmFileEntry> entries = await client.GetDirectoryAsync(settings.Path, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(entries);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Directory[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[green]Name[/]"));
            table.AddColumn(new TableColumn("[grey]Type[/]"));
            table.AddColumn(new TableColumn("[cyan]Size[/]"));
            table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
            foreach (XbdmFileEntry entry in entries) {
                string name = Markup.Escape(entry.Name);
                string type = entry.IsDirectory ? "[yellow]Dir[/]" : "[grey]File[/]";
                table.AddRow(
                    entry.IsDirectory ? $"[yellow]{name}[/]" : $"[green]{name}[/]",
                    type,
                    $"[cyan]{entry.Size}[/]",
                    CliOutput.FormatTimestamp(entry.ModifiedUtc));
            }

            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmFsGetCommand : AsyncCommand<XbdmFsGetCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--path <FILE>")]
        public string? Path { get; init; }

        [CommandOption("--out <FILE>")]
        public string? Output { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Output)) {
            AnsiConsole.MarkupLine("[red]--out is required.[/]");
            return 1;
        }

        if (ContainsLineBreak(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path must be a single line.[/]");
            return 1;
        }

        if (ContainsLineBreak(settings.Output)) {
            AnsiConsole.MarkupLine("[red]--out must be a single line.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            await AtomicFileWriter.WriteAsync(settings.Output, async stream => {
                if (settings.Json) {
                    await client.DownloadFileAsync(settings.Path, stream, null, CancellationToken.None);
                }
                else {
                    await CliOutput.RunWithProgressAsync($"Downloading {settings.Path}", null, progress =>
                        client.DownloadFileAsync(settings.Path, stream, progress, CancellationToken.None));
                }
            });

            FileInfo outputFile = new FileInfo(settings.Output);
            string sha256 = FileHashHelpers.ComputeSha256(settings.Output);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Path = settings.Path,
                    Output = settings.Output,
                    FileSize = outputFile.Length,
                    Sha256 = sha256
                });
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]Downloaded[/] {settings.Path} to {GetDisplayName(settings.Output)}");
            AnsiConsole.MarkupLine($"[grey]SHA-256:[/] {sha256}");
            return 0;
        }, CancellationToken.None);
    }

    private static string GetDisplayName(string path) {
        string leafName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(leafName) ? path : leafName;
    }

    private static bool ContainsLineBreak(string value) {
        return value.Contains('\r') || value.Contains('\n');
    }
}

public sealed class XbdmFsPutCommand : AsyncCommand<XbdmFsPutCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--path <FILE>")]
        public string? Path { get; init; }

        [CommandOption("--in <FILE>")]
        public string? Input { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Input)) {
            AnsiConsole.MarkupLine("[red]--in is required.[/]");
            return 1;
        }

        if (Directory.Exists(settings.Input)) {
            AnsiConsole.MarkupLine("[red]Input path is a directory.[/]");
            return 1;
        }

        if (!File.Exists(settings.Input)) {
            AnsiConsole.MarkupLine("[red]Input file not found.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using FileStream stream = new FileStream(settings.Input, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            string inputSha256 = FileHashHelpers.ComputeSha256(stream);
            stream.Position = 0;
            if (settings.Json) {
                await client.UploadFileAsync(settings.Path, stream, length, null, CancellationToken.None);
                CliOutput.EmitJson(new {
                    Path = settings.Path,
                    Input = settings.Input,
                    FileSize = length,
                    InputSha256 = inputSha256
                });
                return 0;
            }

            await CliOutput.RunWithProgressAsync($"Uploading {settings.Input}", (uint) stream.Length, progress =>
                client.UploadFileAsync(settings.Path, stream, stream.Length, progress, CancellationToken.None));
            AnsiConsole.MarkupLine($"[green]Uploaded[/] {settings.Input} to {settings.Path}");
            AnsiConsole.MarkupLine($"[grey]Input SHA-256:[/] {inputSha256}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmRawCommand : AsyncCommand<XbdmRawCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--cmd <COMMAND>")]
        public string? Command { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Command)) {
            AnsiConsole.MarkupLine("[red]--cmd is required.[/]");
            return 1;
        }

        if (ContainsLineBreak(settings.Command)) {
            AnsiConsole.MarkupLine("[red]--cmd must be a single line.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            (XbdmResponse response, IReadOnlyList<string>? lines) = await client.SendRawAsync(settings.Command, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Command = settings.Command,
                    StatusCode = response.StatusCode,
                    ResponseType = response.ResponseType.ToString(),
                    Message = response.Message,
                    RawMessage = response.RawMessage,
                    Lines = lines ?? Array.Empty<string>()
                });
                return 0;
            }

            AnsiConsole.MarkupLine($"[grey]({response.StatusCode})[/] {response.Message}");
            if (lines != null) {
                foreach (string line in lines) {
                    AnsiConsole.WriteLine(line);
                }
            }
            return 0;
        }, CancellationToken.None);
    }

    private static bool ContainsLineBreak(string value) {
        return value.Contains('\r') || value.Contains('\n');
    }
}

