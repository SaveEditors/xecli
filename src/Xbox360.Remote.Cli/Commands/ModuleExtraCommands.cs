using System.ComponentModel;
using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmModulesInfoCommand : AsyncCommand<XbdmModulesInfoCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--name <MODULE>")]
        [LocalizedDescription("Module name, e.g. default.xex or xam.xex.")]
        public string? Name { get; init; }

        [CommandOption("--sections")]
        [LocalizedDescription("Include section details.")]
        public bool Sections { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Name)) {
            if (settings.Json) {
                CliOutput.EmitJsonError(new CliErrorEnvelope(
                    "Module name required",
                    "--name is required.",
                    "XBDM_MODULE_NAME_REQUIRED",
                    new[] { "Pass a loaded module name, for example --name default.xex." }));
            }
            else {
                AnsiConsole.MarkupLine("[red]--name is required.[/]");
            }
            return 1;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(settings.Sections, CancellationToken.None);
            XbdmModuleInfo? module = modules.FirstOrDefault(m => string.Equals(m.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
            if (module == null) {
                WriteModuleNotFound(settings);
                return 1;
            }

            if (settings.Json) {
                CliOutput.EmitJson(module);
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold deepskyblue1]{Markup.Escape(module.Name)}[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]Field[/]"));
            table.AddColumn(new TableColumn("[white]Value[/]"));
            table.AddRow("[grey]Base[/]", $"[cyan]0x{module.BaseAddress:X8}[/]");
            table.AddRow("[grey]Size[/]", $"[cyan]0x{module.ModuleSize:X8}[/]");
            table.AddRow("[grey]Entry[/]", module.EntryPoint.HasValue ? $"[gold1]0x{module.EntryPoint.Value:X8}[/]" : "[grey]unknown[/]");
            table.AddRow("[grey]Timestamp[/]", CliOutput.FormatTimestamp(module.Timestamp));
            AnsiConsole.Write(table);
            foreach (string warning in module.Warnings) {
                OperationFeedback.WriteWarning(
                    $"Module partial data: {module.Name}",
                    $"[yellow]{Markup.Escape(warning)}[/]");
            }

            if (settings.Sections && module.Sections.Count > 0) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Sections[/]").RuleStyle("grey"));
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

            return 0;
        }, CancellationToken.None);
    }

    private static void WriteModuleNotFound(Settings settings) {
        if (settings.Json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Module not found",
                $"Loaded module '{settings.Name}' was not found.",
                "XBDM_MODULE_NOT_FOUND",
                new[] { "Run `rgh xbdm modules list --json` and retry with an exact loaded module name." }));
            return;
        }

        AnsiConsole.MarkupLine($"[red]Module not found:[/] {Markup.Escape(settings.Name!)}");
    }
}

public sealed class XbdmModuleAddressCommand : AsyncCommand<XbdmModuleAddressCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--name <MODULE>")]
        [LocalizedDescription("Module name, e.g. default.xex or xam.xex.")]
        public string? Name { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("Relative virtual address to translate from this module base.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Address copied from Ghidra.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base shown by Ghidra or the XEX loader.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--sections")]
        [LocalizedDescription("Include section details.")]
        public bool Sections { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Name)) {
            AnsiConsole.MarkupLine("[red]--name is required.[/]");
            return 1;
        }
        bool hasRva = !string.IsNullOrWhiteSpace(settings.Rva);
        bool hasGhidraAddress = !string.IsNullOrWhiteSpace(settings.GhidraAddress);
        if (hasRva && hasGhidraAddress) {
            AnsiConsole.MarkupLine("[red]Use either --rva or --ghidra, not both.[/]");
            return 1;
        }
        if (hasRva && !string.IsNullOrWhiteSpace(settings.GhidraBase)) {
            AnsiConsole.MarkupLine("[red]--ghidra-base only applies with --ghidra.[/]");
            return 1;
        }
        if (!hasRva && !hasGhidraAddress && !string.IsNullOrWhiteSpace(settings.GhidraBase)) {
            AnsiConsole.MarkupLine("[red]--ghidra-base requires --ghidra.[/]");
            return 1;
        }
        if (hasRva && !CliHelpers.TryParseUInt32(settings.Rva, out _)) {
            AnsiConsole.MarkupLine("[red]Invalid --rva.[/]");
            return 1;
        }
        if (hasGhidraAddress && !CliHelpers.TryParseUInt32(settings.GhidraAddress, out _)) {
            AnsiConsole.MarkupLine("[red]Invalid --ghidra.[/]");
            return 1;
        }
        if (hasGhidraAddress && !CliHelpers.TryParseUInt32(settings.GhidraBase, out _)) {
            AnsiConsole.MarkupLine("[red]--ghidra-base is required when using --ghidra.[/]");
            return 1;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(settings.Sections, CancellationToken.None);
            XbdmModuleInfo? module;
            try {
                module = XbdmAddressResolutionHelpers.FindModule(modules, settings.Name);
            }
            catch (InvalidOperationException ex) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                return 1;
            }

            if (module == null) {
                AnsiConsole.MarkupLine($"[red]Module not found:[/] {Markup.Escape(settings.Name)}");
                return 1;
            }

            uint? rva = null;
            uint? ghidraAddress = null;
            uint? ghidraBase = null;
            if (hasRva) {
                CliHelpers.TryParseUInt32(settings.Rva, out uint parsedRva);
                rva = parsedRva;
            }
            else if (hasGhidraAddress) {
                CliHelpers.TryParseUInt32(settings.GhidraAddress, out uint parsedGhidraAddress);
                CliHelpers.TryParseUInt32(settings.GhidraBase, out uint parsedGhidraBase);
                ghidraAddress = parsedGhidraAddress;
                ghidraBase = parsedGhidraBase;
            }

            if (!XbdmAddressResolutionHelpers.TryResolveModuleAddress(
                module,
                rva,
                ghidraAddress,
                ghidraBase,
                out uint resolvedRva,
                out uint liveAddress,
                out long? ghidraDelta,
                out XbdmResolvedSection? section,
                out string? rangeWarning,
                out string? errorMessage)) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(errorMessage ?? "Unable to resolve module address.")}[/]");
                return 1;
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Module = module.Name,
                    LiveBase = Hex(module.BaseAddress),
                    ModuleSize = Hex(XbdmAddressResolutionHelpers.GetModuleSize(module)),
                    OriginalSize = Hex(module.OriginalModuleSize),
                    EntryPoint = Hex(module.EntryPoint),
                    EntryPointRva = module.EntryPoint.HasValue && module.EntryPoint.Value >= module.BaseAddress ? Hex(module.EntryPoint.Value - module.BaseAddress) : null,
                    GhidraBase = Hex(ghidraBase),
                    GhidraAddress = Hex(ghidraAddress),
                    Delta = ghidraDelta.HasValue ? FormatSignedDelta(ghidraDelta.Value) : null,
                    Rva = Hex(resolvedRva),
                    LiveAddress = Hex(liveAddress),
                    Section = section == null
                        ? null
                        : new {
                            section.Name,
                            Base = Hex(section.BaseAddress),
                            End = XbdmAddressResolutionHelpers.Hex(section.EndAddress),
                            Size = Hex(section.Size),
                            Rva = Hex(section.Rva),
                            EndRva = section.EndRva.HasValue ? XbdmAddressResolutionHelpers.Hex(section.EndRva.Value) : null,
                            Flags = Hex(section.Flags)
                        },
                    Warning = rangeWarning,
                    Sections = settings.Sections ? module.Sections.Select(sec => new {
                        sec.Name,
                        Base = Hex(sec.BaseAddress),
                        End = XbdmAddressResolutionHelpers.Hex((ulong)sec.BaseAddress + sec.Size),
                        Size = Hex(sec.Size),
                        sec.Index,
                        Flags = Hex(sec.Flags)
                    }).ToArray() : null
                });
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold deepskyblue1]{Markup.Escape(module.Name)} address map[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]Field[/]"));
            table.AddColumn(new TableColumn("[white]Value[/]"));
            table.AddRow("[grey]Live base[/]", $"[cyan]{Hex(module.BaseAddress)}[/]");
            table.AddRow("[grey]Module size[/]", $"[cyan]{Hex(XbdmAddressResolutionHelpers.GetModuleSize(module))}[/]");
            table.AddRow("[grey]Original size[/]", $"[cyan]{Hex(module.OriginalModuleSize)}[/]");
            table.AddRow("[grey]Entry[/]", module.EntryPoint.HasValue ? $"[gold1]{Hex(module.EntryPoint.Value)}[/]" : "[grey]unknown[/]");
            if (module.EntryPoint.HasValue && module.EntryPoint.Value >= module.BaseAddress)
                table.AddRow("[grey]Entry RVA[/]", $"[gold1]{Hex(module.EntryPoint.Value - module.BaseAddress)}[/]");
            if (ghidraDelta.HasValue)
                table.AddRow("[grey]Ghidra base delta[/]", $"[yellow]{FormatSignedDelta(ghidraDelta.Value)}[/]");
            table.AddRow("[grey]RVA[/]", $"[cyan]{Hex(resolvedRva)}[/]");
            table.AddRow("[grey]Live address[/]", $"[springgreen3_1]{Hex(liveAddress)}[/]");
            table.AddRow("[grey]Section[/]", FormatSection(section));
            AnsiConsole.Write(table);

            if (!string.IsNullOrWhiteSpace(rangeWarning))
                OperationFeedback.WriteWarning(XbdmAddressResolutionHelpers.GetWarningTitle(rangeWarning), $"[yellow]{Markup.Escape(rangeWarning)}[/]");

            if (settings.Sections && module.Sections.Count > 0) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Sections[/]").RuleStyle("grey"));
                Table secTable = CliOutput.CreateTable();
                secTable.AddColumn(new TableColumn("[green]Name[/]"));
                secTable.AddColumn(new TableColumn("[cyan]Base[/]"));
                secTable.AddColumn(new TableColumn("[cyan]Size[/]"));
                secTable.AddColumn(new TableColumn("[grey]RVA[/]"));
                secTable.AddColumn(new TableColumn("[grey]Flags[/]"));
                foreach (XbdmSectionInfo sec in module.Sections) {
                    string sectionRva = sec.BaseAddress >= module.BaseAddress ? Hex(sec.BaseAddress - module.BaseAddress) : "unknown";
                    secTable.AddRow(
                        sec.Name != null ? $"[green]{Markup.Escape(sec.Name)}[/]" : "[grey](unnamed)[/]",
                        $"[cyan]{Hex(sec.BaseAddress)}[/]",
                        $"[cyan]{Hex(sec.Size)}[/]",
                        $"[grey]{Markup.Escape(sectionRva)}[/]",
                        $"[grey]{Hex(sec.Flags)}[/]");
                }
                AnsiConsole.Write(secTable);
            }

            return 0;
        }, CancellationToken.None);
    }
    private static string Hex(uint value) => $"0x{value:X8}";

    private static string? Hex(uint? value) => value.HasValue ? Hex(value.Value) : null;

    private static string FormatSection(XbdmResolvedSection? section) {
        if (section == null)
            return "[grey]n/a[/]";

        string name = string.IsNullOrWhiteSpace(section.Name) ? "(unnamed)" : section.Name;
        return $"[green]{Markup.Escape(name)}[/] [grey]{Hex(section.BaseAddress)}..{XbdmAddressResolutionHelpers.Hex(section.EndAddress)} flags {Hex(section.Flags)}[/]";
    }

    private static string FormatSignedDelta(long value) {
        string sign = value < 0 ? "-" : "+";
        ulong magnitude = value < 0 ? (ulong)-value : (ulong)value;
        return $"{sign}0x{magnitude:X}";
    }
}

public sealed class XbdmModuleLoadCommand : AsyncCommand<XbdmModuleLoadCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--path <PATH>")]
        [LocalizedDescription("Remote XEX path to load, e.g. Hdd:\\XDRPC.xex.")]
        public string? Path { get; init; }

        [CommandOption("--flags <N>")]
        [LocalizedDescription("Kernel load flags (default 8).")]
        public int? Flags { get; init; }

        [CommandOption("--system")]
        [LocalizedDescription("Run the load RPC on a system thread instead of the default title thread.")]
        public bool SystemThread { get; init; }

        [CommandOption("--reboot-expected")]
        [LocalizedDescription("Treat a console disconnect/reboot as an expected part of the load and persist pending verification.")]
        public bool RebootExpected { get; init; }

        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the resolved call without writing anything.")]
        public bool DryRun { get; init; }

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
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            CliValidationOutput.Write(
                settings.Json,
                "Module load validation failed",
                "--path is required.",
                "MODULE_PATH_REQUIRED",
                "Provide the console path to the module XEX with --path.");
            return 1;
        }

        string modulePath = settings.Path.Trim();
        int flags = settings.Flags ?? 8;
        string moduleName = System.IO.Path.GetFileName(modulePath.Replace('\\', System.IO.Path.DirectorySeparatorChar));

        if (settings.DryRun) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "module-load",
                    Mode = "dry-run",
                    Status = "planned",
                    Path = modulePath,
                    Module = moduleName,
                    Flags = flags,
                    SystemThread = settings.SystemThread,
                    RebootExpected = settings.RebootExpected,
                    RequestSent = false
                });
                return 0;
            }
            AnsiConsole.MarkupLine($"[grey]Would load[/] [cyan]{Markup.Escape(modulePath)}[/] [grey]with flags[/] [cyan]{flags}[/]");
            return 0;
        }

        CancellationToken cancellationToken = CliHelpers.ConsoleCancellationToken;
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, cancellationToken);
        return await CliHelpers.WithClientOnceAsync((ip, port, timeout), settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            uint status = 0;
            Exception? rpcError = null;
            try {
                string result = await jrpc.CallAsync(
                RpcDataType.Int,
                null,
                "xboxkrnl.exe",
                409,
                settings.SystemThread,
                false,
                new[] {
                    new RpcArgument(RpcArgType.Bytes, ModuleCommandHelpers.CreateNullTerminatedAscii(modulePath)),
                    new RpcArgument(RpcArgType.Int, flags),
                    new RpcArgument(RpcArgType.Int, 0),
                    new RpcArgument(RpcArgType.Int, 0)
                    },
                    cancellationToken);
                status = ModuleCommandHelpers.ParseRpcUInt32(result);
            }
            catch (Exception ex) when (ModuleCommandHelpers.IsAmbiguousRpcCompletion(ex)) {
                rpcError = ex;
            }

            if (rpcError != null && settings.RebootExpected) {
                ModuleCommandHelpers.StorePendingLoad(moduleName, modulePath, settings.SystemThread);
                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Operation = "module-load",
                        Mode = "live",
                        Status = "pending",
                        Path = modulePath,
                        Module = moduleName,
                        Flags = flags,
                        SystemThread = settings.SystemThread,
                        RebootExpected = true,
                        RequestSent = true,
                        PendingVerification = true,
                        Detail = CommandLogRedactor.RedactFreeText(rpcError.Message)
                    });
                }
                else {
                    OperationFeedback.WriteWarning(
                        "Module load pending verification",
                        $"[yellow]{Markup.Escape(moduleName)}[/] [grey]triggered a disconnect/reboot-expected path. Reboot the console if needed, then run[/] [cyan]rgh modules pending[/] [grey]or[/] [cyan]rgh status[/] [grey]to verify.[/]");
                }
                return 0;
            }

            XbdmModuleInfo? loaded = await ModuleCommandHelpers.SafeWaitForModuleStateAsync(
                client,
                moduleName,
                shouldExist: true,
                cancellationToken);
            if (loaded == null && rpcError != null) {
                loaded = await ModuleCommandHelpers.WaitForModuleStateWithReconnectAsync(
                    ip,
                    port,
                    timeout,
                    moduleName,
                    shouldExist: true,
                    cancellationToken);
            }

            if (status != 0) {
                ModuleCommandHelpers.WriteFailure(settings, "Load failed", $"NTSTATUS=0x{status:X8}", "MODULE_LOAD_NTSTATUS_FAILED");
                return 1;
            }
            if (loaded == null && rpcError != null) {
                ModuleCommandHelpers.WriteFailure(settings, "Load failed", rpcError.Message, "MODULE_LOAD_RPC_FAILED");
                return 1;
            }
            if (loaded == null) {
                ModuleCommandHelpers.WriteFailure(
                    settings,
                    "Load failed",
                    "The module load RPC returned zero, but the module was not present in the live module list.",
                    "MODULE_LOAD_NOT_OBSERVED");
                return 1;
            }
            XbdmModuleInfo verifiedLoaded = loaded;

            {
                XbdmModuleInfo? confirmedLoaded = await ModuleCommandHelpers.ConfirmModulePresenceFreshAsync(
                    ip,
                    port,
                    timeout,
                    moduleName,
                    cancellationToken);
                if (confirmedLoaded == null) {
                    ModuleCommandHelpers.WriteFailure(settings, "Load failed", "module did not remain loaded after the initial success response", "MODULE_LOAD_NOT_STABLE");
                    return 1;
                }

                verifiedLoaded = confirmedLoaded;
            }

            uint resolvedHandle = 0;
            if (rpcError == null) {
                resolvedHandle = await ModuleCommandHelpers.ResolveModuleHandleAsync(client, jrpc, moduleName, cancellationToken);
            }
            else if (loaded != null) {
                resolvedHandle = await ModuleCommandHelpers.TryResolveHandleAfterReconnectAsync(
                    ip,
                    port,
                    timeout,
                    moduleName,
                    cancellationToken);
            }
            if (resolvedHandle != 0)
                ModuleCommandHelpers.StoreHandle(moduleName, resolvedHandle);
            ModuleCommandHelpers.ClearPendingIfMatch("load", moduleName);

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "module-load",
                    Mode = "live",
                    Status = "completed",
                    Path = modulePath,
                    Module = verifiedLoaded.Name,
                    Flags = flags,
                    SystemThread = settings.SystemThread,
                    RebootExpected = settings.RebootExpected,
                    RequestSent = true,
                    RpcStatus = status,
                    ModuleListVerified = true,
                    BaseAddress = $"0x{verifiedLoaded.BaseAddress:X8}",
                    Handle = resolvedHandle == 0 ? null : $"0x{resolvedHandle:X8}",
                    AmbiguousRpcCompletion = rpcError != null,
                    NotifyRequested = settings.Notify
                });
            }
            else {
                string handleText = resolvedHandle != 0
                    ? $" [grey]handle[/] [gold1]0x{resolvedHandle:X8}[/]"
                    : string.Empty;
                string verifiedText = rpcError != null
                    ? " [grey](verified after ambiguous RPC completion)[/]"
                    : string.Empty;
                OperationFeedback.WriteSuccess(
                    "Module loaded",
                    $"[green]{Markup.Escape(verifiedLoaded.Name)}[/] [grey]at[/] [cyan]0x{verifiedLoaded.BaseAddress:X8}[/]{handleText}{verifiedText}");
            }

            await NotifyHelpers.TrySendOperationNotificationAsync(
                client,
                settings.Notify,
                settings.NotifyIcon,
                settings.NotifyLogo,
                "Success :)",
                cancellationToken);

            return 0;
        }, cancellationToken);
    }
}

public sealed class XbdmModuleUnloadCommand : AsyncCommand<XbdmModuleUnloadCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--name <MODULE>")]
        [LocalizedDescription("Loaded module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Name { get; init; }

        [CommandOption("--handle <HANDLE>")]
        [LocalizedDescription("Explicit module handle as hex or decimal.")]
        public string? Handle { get; init; }

        [CommandOption("--skip-mark")]
        [LocalizedDescription("Do not set the sysdll unload marker at handle+0x40 before unloading.")]
        public bool SkipMark { get; init; }

        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the resolved unload target without writing anything.")]
        public bool DryRun { get; init; }

        [CommandOption("--force")]
        [LocalizedDescription("Required. Module unload can wedge the console if the target rejects live unload.")]
        public bool Force { get; init; }

        [CommandOption("--allow-critical")]
        [LocalizedDescription("Also allow unloading a known system or boot-plugin module. Requires --force.")]
        public bool AllowCritical { get; init; }

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
        if (string.IsNullOrWhiteSpace(settings.Name) && string.IsNullOrWhiteSpace(settings.Handle)) {
            CliValidationOutput.Write(
                settings.Json,
                "Module unload validation failed",
                "--name or --handle is required.",
                "MODULE_UNLOAD_TARGET_REQUIRED",
                "Provide an exact loaded module name or an explicit module handle.");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(settings.Handle) && !CliHelpers.TryParseUInt32(settings.Handle, out _)) {
            CliValidationOutput.Write(
                settings.Json,
                "Module unload validation failed",
                "Invalid --handle.",
                "MODULE_HANDLE_INVALID",
                "Provide a decimal or 0x-prefixed 32-bit module handle.");
            return 1;
        }

        if (settings.DryRun) {
            if (!string.IsNullOrWhiteSpace(settings.Handle)) {
                CliHelpers.TryParseUInt32(settings.Handle, out uint handle);
                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Operation = "module-unload",
                        Mode = "dry-run",
                        Status = "planned",
                        Module = settings.Name,
                        Handle = $"0x{handle:X8}",
                        HandleSource = "explicit",
                        CriticalModule = ModuleCommandHelpers.IsProtectedModuleName(settings.Name),
                        SkipUnloadMarker = settings.SkipMark,
                        RequestSent = false
                    });
                    return 0;
                }
                AnsiConsole.MarkupLine($"[grey]Would unload module handle[/] [cyan]0x{handle:X8}[/]");
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(settings.Name) && ModuleCommandHelpers.TryGetStoredHandle(settings.Name, out uint cachedHandle)) {
                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Operation = "module-unload",
                        Mode = "dry-run",
                        Status = "planned",
                        Module = settings.Name,
                        Handle = $"0x{cachedHandle:X8}",
                        HandleSource = "cache",
                        CriticalModule = ModuleCommandHelpers.IsProtectedModuleName(settings.Name),
                        SkipUnloadMarker = settings.SkipMark,
                        RequestSent = false
                    });
                    return 0;
                }
                AnsiConsole.MarkupLine($"[grey]Would unload module handle[/] [cyan]0x{cachedHandle:X8}[/]");
                return 0;
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "module-unload",
                    Mode = "dry-run",
                    Status = "planned",
                    Module = settings.Name,
                    Handle = (string?)null,
                    HandleSource = "resolve-live",
                    CriticalModule = ModuleCommandHelpers.IsProtectedModuleName(settings.Name),
                    SkipUnloadMarker = settings.SkipMark,
                    RequestSent = false
                });
                return 0;
            }
            AnsiConsole.MarkupLine($"[grey]Would unload module[/] [cyan]{Markup.Escape(settings.Name!)}[/]");
            return 0;
        }

        if (!settings.DryRun && !settings.Force) {
            CliValidationOutput.Write(
                settings.Json,
                "Module unload validation failed",
                "--force is required for live unloads.",
                "MODULE_UNLOAD_FORCE_REQUIRED",
                "Review the target, then rerun with --force.");
            return 1;
        }

        if (ModuleCommandHelpers.IsProtectedModuleName(settings.Name) && !settings.AllowCritical) {
            CliValidationOutput.Write(
                settings.Json,
                "Protected module unload blocked",
                $"{settings.Name} is a known system or boot-plugin module. Live unload can freeze the console.",
                "MODULE_UNLOAD_PROTECTED",
                "Use a disposable title module for lifecycle testing.",
                "Only rerun with both --force and --allow-critical when a recovery plan is available.");
            return 1;
        }

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        return await CliHelpers.WithClientOnceAsync((ip, port, timeout), settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            uint handle;
            string? moduleName = settings.Name;

            if (!string.IsNullOrWhiteSpace(settings.Handle)) {
                CliHelpers.TryParseUInt32(settings.Handle, out handle);
            }
            else {
                handle = await ModuleCommandHelpers.ResolveModuleHandleAsync(client, jrpc, settings.Name!, CancellationToken.None);
                if (handle == 0) {
                    ModuleCommandHelpers.WriteFailure(settings, "Module handle not found", settings.Name!, "MODULE_HANDLE_NOT_FOUND");
                    return 1;
                }
            }

            if (!settings.SkipMark)
                await client.WriteMemoryAsync(handle + 0x40, new byte[] { 0x00, 0x01 }, CancellationToken.None);

            uint status = 0;
            Exception? rpcError = null;
            try {
                status = await ModuleCommandHelpers.UnloadModuleAsync(jrpc, handle, CancellationToken.None);
            }
            catch (Exception ex) when (ModuleCommandHelpers.IsAmbiguousRpcCompletion(ex)) {
                rpcError = ex;
            }

            if (status != 0) {
                ModuleCommandHelpers.WriteFailure(settings, "Unload failed", $"NTSTATUS=0x{status:X8}", "MODULE_UNLOAD_NTSTATUS_FAILED");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(moduleName)) {
                XbdmModuleInfo? remaining = await ModuleCommandHelpers.SafeWaitForModuleStateAsync(
                    client,
                    moduleName,
                    shouldExist: false,
                    CancellationToken.None);
                if (remaining != null && rpcError != null) {
                    remaining = await ModuleCommandHelpers.WaitForModuleStateWithReconnectAsync(
                        ip,
                        port,
                        timeout,
                        moduleName,
                        shouldExist: false,
                        CancellationToken.None);
                }
                if (remaining != null) {
                    string reason = rpcError?.Message ?? "module is still present in the module list";
                    ModuleCommandHelpers.WriteFailure(settings, "Unload failed", reason, "MODULE_UNLOAD_NOT_OBSERVED");
                    return 1;
                }
            }
            else if (rpcError != null) {
                ModuleCommandHelpers.WriteFailure(settings, "Unload failed", rpcError.Message, "MODULE_UNLOAD_RPC_FAILED");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(moduleName))
                ModuleCommandHelpers.RemoveHandle(moduleName);

            (bool healthVerified, string healthDetail) = await ModuleCommandHelpers.VerifyFreshXbdmHealthAsync(
                ip,
                port,
                timeout,
                CancellationToken.None);
            if (!healthVerified) {
                ModuleCommandHelpers.WriteFailure(
                    settings,
                    "Unload destabilized XBDM",
                    healthDetail,
                    "MODULE_UNLOAD_POST_HEALTH_FAILED");
                return 1;
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "module-unload",
                    Mode = "live",
                    Status = "completed",
                    Module = moduleName,
                    Handle = $"0x{handle:X8}",
                    RequestSent = true,
                    RpcStatus = status,
                    ModuleListVerified = !string.IsNullOrWhiteSpace(moduleName),
                    PostOperationHealthVerified = true,
                    UnloadMarkerWritten = !settings.SkipMark,
                    AmbiguousRpcCompletion = rpcError != null,
                    NotifyRequested = settings.Notify
                });
            }
            else {
                string unloadVerifiedText = rpcError != null
                    ? "[grey](verified after ambiguous RPC completion)[/]"
                    : string.Empty;
                OperationFeedback.WriteSuccess("Module unloaded", $"[cyan]0x{handle:X8}[/] {unloadVerifiedText}".TrimEnd());
            }
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

public sealed class XbdmModulesPendingCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        CliConfig cfg = CliConfig.Load();
        CliConfig.PendingModuleOperationInfo? pending = cfg.PendingModuleOperation;
        if (pending == null) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "module-pending",
                    Status = "none",
                    Pending = false
                });
                return 0;
            }

            AnsiConsole.MarkupLine("[grey]No pending module operation.[/]");
            return 0;
        }

        if (!TryValidatePendingOperation(pending, out string validationError)) {
            cfg.PendingModuleOperation = null;
            cfg.Save();
            if (settings.Json) {
                CliOutput.EmitJsonError(new CliErrorEnvelope(
                    "Pending module operation invalid",
                    validationError,
                    "MODULE_PENDING_INVALID",
                    new[] { "The invalid pending operation was cleared; issue a new module load or unload request." }));
                return 1;
            }

            OperationFeedback.WriteFailure("Pending module operation invalid", validationError);
            return 1;
        }

        string action = pending.Action!;
        string moduleName = pending.ModuleName!;

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        return await CliHelpers.WithClientOnceAsync((ip, port, timeout), settings, async client => {
            IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(false, CancellationToken.None);
            bool present = modules.Any(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
            bool success = string.Equals(action, "load", StringComparison.OrdinalIgnoreCase) ? present : !present;

            if (success) {
                XbdmModuleInfo? loadedModule = null;
                if (string.Equals(action, "load", StringComparison.OrdinalIgnoreCase)) {
                    loadedModule = modules.First(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
                    ModuleCommandHelpers.ClearPendingIfMatch(action, moduleName);
                }
                else {
                    ModuleCommandHelpers.ClearPendingIfMatch(action, moduleName);
                }

                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Operation = "module-pending",
                        Status = "verified",
                        Pending = false,
                        Action = action,
                        Module = moduleName,
                        Present = present,
                        BaseAddress = loadedModule == null ? null : $"0x{loadedModule.BaseAddress:X8}"
                    });
                    return 0;
                }

                if (loadedModule != null) {
                    OperationFeedback.WriteSuccess(
                        "Pending module load verified",
                        $"[green]{Markup.Escape(loadedModule.Name)}[/] [grey]at[/] [cyan]0x{loadedModule.BaseAddress:X8}[/]");
                }
                else {
                    OperationFeedback.WriteSuccess(
                        "Pending module unload verified",
                        $"[green]{Markup.Escape(moduleName)}[/] [grey]is no longer loaded.[/]");
                }

                return 0;
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "module-pending",
                    Status = "pending",
                    Pending = true,
                    Action = action,
                    Module = moduleName,
                    Present = present
                });
                return 1;
            }

            OperationFeedback.WriteWarning(
                "Module operation still pending",
                $"[yellow]{Markup.Escape(moduleName)}[/] [grey]has not reached the expected state yet.[/]");
            return 1;
        }, CancellationToken.None);
    }

    private static bool TryValidatePendingOperation(CliConfig.PendingModuleOperationInfo pending, out string validationError) {
        if (string.IsNullOrWhiteSpace(pending.ModuleName)) {
            validationError = "The saved pending module operation was missing a module name and has been cleared.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(pending.Action) ||
            !(string.Equals(pending.Action, "load", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(pending.Action, "unload", StringComparison.OrdinalIgnoreCase))) {
            validationError = "The saved pending module operation had an invalid action and has been cleared.";
            return false;
        }

        if (string.Equals(pending.Action, "load", StringComparison.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(pending.ModulePath)) {
                validationError = "The saved pending load was missing a module path and has been cleared.";
                return false;
            }

            if (pending.SystemThread == null) {
                validationError = "The saved pending load was missing a system thread flag and has been cleared.";
                return false;
            }
        }

        validationError = string.Empty;
        return true;
    }
}

internal static class ModuleCommandHelpers {
    private static readonly HashSet<string> ProtectedModuleNames = new(StringComparer.OrdinalIgnoreCase) {
        "xboxkrnl.exe",
        "xam.xex",
        "launch.xex",
        "ximecore.xex",
        "Xam.Community.xex",
        "hud.xex",
        "xbdm.xex",
        "JRPC2.xex",
        "XDRPC.xex",
        "xbGuard.xex",
        "SNet.xex",
        "xosc9v2.xex"
    };

    public static bool IsProtectedModuleName(string? moduleName) {
        if (string.IsNullOrWhiteSpace(moduleName))
            return false;

        string? leaf = Path.GetFileName(moduleName.Replace('\\', Path.DirectorySeparatorChar));
        return leaf != null && ProtectedModuleNames.Contains(leaf);
    }

    public static async Task<(bool Verified, string Detail)> VerifyFreshXbdmHealthAsync(
        string ip,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken) {
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        try {
            await using XbdmClient verificationClient = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
                Host = ip,
                Port = port,
                TimeoutMs = timeoutMs
            }, cancellationToken);
            string? dmVersion = await verificationClient.GetDmVersionAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(dmVersion))
                return (false, "A fresh XBDM session connected but returned an empty DM version after the unload.");
            return (true, $"Fresh XBDM session verified with DM version {dmVersion.Trim()}.");
        }
        catch (Exception ex) {
            return (false, "A fresh XBDM session failed after the unload: " + CommandLogRedactor.RedactFreeText(ex.Message));
        }
    }

    public static void WriteFailure(ConnectionSettings settings, string title, string message, string code) {
        string safeMessage = CommandLogRedactor.RedactFreeText(message);
        if (settings.Json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                safeMessage,
                code,
                new[] { "Run `rgh modules list --json` to confirm the current module state before retrying." }));
            return;
        }

        OperationFeedback.WriteFailure(title, Markup.Escape(safeMessage));
    }

    public static uint ParseRpcUInt32(string value) {
        return TryParseRpcUInt32(value, out uint parsed) ? parsed : 0;
    }

    public static bool TryParseRpcUInt32(string value, out uint parsed) {
        string text = value.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            parsed = 0;
            return false;
        }
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(2);
        if (uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
            return true;
        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    public static async Task<uint> ResolveModuleHandleAsync(XbdmClient client, Jrpc2Client jrpc, string moduleName, CancellationToken cancellationToken) {
        if (TryGetStoredHandle(moduleName, out uint cachedHandle))
            return cachedHandle;

        string resolved = await jrpc.CallAsync(
            RpcDataType.Int,
            null,
            "xam.xex",
            1102,
            false,
            false,
            new[] { new RpcArgument(RpcArgType.Bytes, CreateNullTerminatedAscii(moduleName)) },
            cancellationToken);
        if (TryParseRpcUInt32(resolved, out uint handle) && handle != 0)
            return handle;

        IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(false, cancellationToken);
        XbdmModuleInfo? module = modules.FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (module == null)
            return 0;

        if (TryGetStoredHandle(module.Name, out cachedHandle))
            return cachedHandle;

        return module.BaseAddress;
    }

    public static async Task<uint> UnloadModuleAsync(Jrpc2Client jrpc, uint handle, CancellationToken cancellationToken) {
        string result = await jrpc.CallAsync(
            RpcDataType.Int,
            null,
            "xboxkrnl.exe",
            417,
            true,
            false,
            new[] { new RpcArgument(RpcArgType.UInt, handle) },
            cancellationToken);
        return TryParseRpcUInt32(result, out uint status) ? status : 0;
    }

    public static byte[] CreateNullTerminatedAscii(string value) {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        byte[] terminated = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, terminated, 0, bytes.Length);
        terminated[^1] = 0x00;
        return terminated;
    }

    public static bool IsAmbiguousRpcCompletion(Exception ex) {
        if (ex is TimeoutException)
            return true;
        if (ex is IOException io &&
            io.Message.Contains("potential infinite loop", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static async Task<XbdmModuleInfo?> SafeWaitForModuleStateAsync(
        XbdmClient client,
        string moduleName,
        bool shouldExist,
        CancellationToken cancellationToken) {
        try {
            return await WaitForModuleStateAsync(client, moduleName, shouldExist, cancellationToken);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception) {
            return shouldExist ? null : new XbdmModuleInfo { Name = moduleName };
        }
    }

    public static async Task<XbdmModuleInfo?> WaitForModuleStateAsync(
        XbdmClient client,
        string moduleName,
        bool shouldExist,
        CancellationToken cancellationToken) {
        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++) {
            IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(false, cancellationToken);
            XbdmModuleInfo? module = modules.FirstOrDefault(m =>
                string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
            if (shouldExist) {
                if (module != null)
                    return module;
            }
            else if (module == null) {
                return null;
            }

            if (attempt + 1 < maxAttempts)
                await Task.Delay(400, cancellationToken);
        }

        IReadOnlyList<XbdmModuleInfo> finalModules = await client.GetModulesAsync(false, cancellationToken);
        return finalModules.FirstOrDefault(m =>
            string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<XbdmModuleInfo?> WaitForModuleStateWithReconnectAsync(
        string ip,
        int port,
        int timeoutMs,
        string moduleName,
        bool shouldExist,
        CancellationToken cancellationToken) {
        const int reconnectAttempts = 72;
        for (int attempt = 0; attempt < reconnectAttempts; attempt++) {
            try {
                if (attempt > 0) {
                    int elapsedSeconds = attempt * 5;
                    AnsiConsole.MarkupLine($"[yellow]Waiting for console reconnect {attempt + 1}/{reconnectAttempts} ({elapsedSeconds}s elapsed)...[/]");
                }
                await using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
                    Host = ip,
                    Port = port,
                    TimeoutMs = timeoutMs
                }, cancellationToken);
                return await WaitForModuleStateAsync(client, moduleName, shouldExist, cancellationToken);
            }
            catch (Exception ex) when (attempt + 1 < reconnectAttempts && ex is not OperationCanceledException) {
                await Task.Delay(5000, cancellationToken);
            }
        }

        return shouldExist ? null : new XbdmModuleInfo { Name = moduleName };
    }

    public static async Task<uint> TryResolveHandleAfterReconnectAsync(
        string ip,
        int port,
        int timeoutMs,
        string moduleName,
        CancellationToken cancellationToken) {
        const int reconnectAttempts = 24;
        for (int attempt = 0; attempt < reconnectAttempts; attempt++) {
            try {
                await using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
                    Host = ip,
                    Port = port,
                    TimeoutMs = timeoutMs
                }, cancellationToken);
                Jrpc2Client jrpc = new Jrpc2Client(client);
                return await ResolveModuleHandleAsync(client, jrpc, moduleName, cancellationToken);
            }
            catch (Exception ex) when (attempt + 1 < reconnectAttempts && ex is not OperationCanceledException) {
                await Task.Delay(5000, cancellationToken);
            }
        }

        return 0;
    }

    public static async Task<XbdmModuleInfo?> ConfirmModulePresenceFreshAsync(
        string ip,
        int port,
        int timeoutMs,
        string moduleName,
        CancellationToken cancellationToken) {
        const int attempts = 4;
        for (int attempt = 0; attempt < attempts; attempt++) {
            if (attempt > 0)
                await Task.Delay(1500, cancellationToken);

            try {
                await using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
                    Host = ip,
                    Port = port,
                    TimeoutMs = timeoutMs
                }, cancellationToken);
                IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(false, cancellationToken);
                XbdmModuleInfo? module = modules.FirstOrDefault(m =>
                    string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
                if (module != null)
                    return module;
            }
            catch (Exception ex) when (attempt + 1 < attempts && ex is not OperationCanceledException) {
            }
        }

        return null;
    }

    public static void StoreHandle(string moduleName, uint handle) {
        CliConfig cfg = CliConfig.Load();
        cfg.ModuleHandles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        cfg.ModuleHandles[moduleName] = $"0x{handle:X8}";
        cfg.Save();
    }

    public static void RemoveHandle(string moduleName) {
        CliConfig cfg = CliConfig.Load();
        cfg.ModuleHandles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (cfg.ModuleHandles.Remove(moduleName))
            cfg.Save();
    }

    public static void StorePendingLoad(string moduleName, string modulePath, bool systemThread) {
        CliConfig cfg = CliConfig.Load();
        cfg.PendingModuleOperation = new CliConfig.PendingModuleOperationInfo {
            Action = "load",
            ModuleName = moduleName,
            ModulePath = modulePath,
            SystemThread = systemThread,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        cfg.Save();
    }

    public static void ClearPendingIfMatch(string action, string moduleName) {
        CliConfig cfg = CliConfig.Load();
        if (cfg.PendingModuleOperation == null)
            return;
        if (!string.Equals(cfg.PendingModuleOperation.Action, action, StringComparison.OrdinalIgnoreCase))
            return;
        if (!string.Equals(cfg.PendingModuleOperation.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
            return;
        cfg.PendingModuleOperation = null;
        cfg.Save();
    }

    public static bool TryGetStoredHandle(string moduleName, out uint handle) {
        CliConfig cfg = CliConfig.Load();
        if (cfg.ModuleHandles != null &&
            cfg.ModuleHandles.TryGetValue(moduleName, out string? raw) &&
            CliHelpers.TryParseUInt32(raw, out handle)) {
            return true;
        }

        handle = 0;
        return false;
    }
}

internal static class OperationFeedback {
    public static void WriteSuccess(string title) {
        AnsiConsole.MarkupLine($"[bold springgreen3_1]SUCCESS[/] [grey]{Markup.Escape(title)}[/]");
    }

    public static void WriteSuccess(string title, string detail) {
        WriteSuccess(title);
        AnsiConsole.MarkupLine(detail);
    }

    public static void WriteFailure(string title, string detail) {
        AnsiConsole.MarkupLine($"[bold red1]FAILED[/] [grey]{Markup.Escape(title)}[/]");
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(detail)}[/]");
    }

    public static void WriteWarning(string title, string detail) {
        AnsiConsole.MarkupLine($"[bold gold1]NOTICE[/] [grey]{Markup.Escape(title)}[/]");
        AnsiConsole.MarkupLine(detail);
    }
}

