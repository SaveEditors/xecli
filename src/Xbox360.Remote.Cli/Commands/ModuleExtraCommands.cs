using System.ComponentModel;
using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmModulesInfoCommand : AsyncCommand<XbdmModulesInfoCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--name <MODULE>")]
        [Description("Module name, e.g. default.xex or xam.xex.")]
        public string? Name { get; init; }

        [CommandOption("--sections")]
        [Description("Include section details.")]
        public bool Sections { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Name)) {
            AnsiConsole.MarkupLine("[red]--name is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(settings.Sections, CancellationToken.None);
            XbdmModuleInfo? module = modules.FirstOrDefault(m => string.Equals(m.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
            if (module == null) {
                AnsiConsole.MarkupLine($"[red]Module not found:[/] {settings.Name}");
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
}

public sealed class XbdmModuleLoadCommand : AsyncCommand<XbdmModuleLoadCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--path <PATH>")]
        [Description("Remote XEX path to load, e.g. Hdd:\\XDRPC.xex.")]
        public string? Path { get; init; }

        [CommandOption("--flags <N>")]
        [Description("Kernel load flags (default 8).")]
        public int? Flags { get; init; }

        [CommandOption("--system")]
        [Description("Run the load RPC on a system thread instead of the default title thread.")]
        public bool SystemThread { get; init; }

        [CommandOption("--reboot-expected")]
        [Description("Treat a console disconnect/reboot as an expected part of the load and persist pending verification.")]
        public bool RebootExpected { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show the resolved call without writing anything.")]
        public bool DryRun { get; init; }

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
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        string modulePath = settings.Path.Trim();
        int flags = settings.Flags ?? 8;
        string moduleName = System.IO.Path.GetFileName(modulePath.Replace('\\', System.IO.Path.DirectorySeparatorChar));

        if (settings.DryRun) {
            AnsiConsole.MarkupLine($"[grey]Would load[/] [cyan]{Markup.Escape(modulePath)}[/] [grey]with flags[/] [cyan]{flags}[/]");
            return 0;
        }

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
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
                    CancellationToken.None);
                status = ModuleCommandHelpers.ParseRpcUInt32(result);
            }
            catch (Exception ex) when (ModuleCommandHelpers.IsAmbiguousRpcCompletion(ex)) {
                rpcError = ex;
            }

            if (rpcError != null && settings.RebootExpected) {
                ModuleCommandHelpers.StorePendingLoad(moduleName, modulePath, settings.SystemThread);
                OperationFeedback.WriteWarning(
                    "Module load pending verification",
                    $"[yellow]{Markup.Escape(moduleName)}[/] [grey]triggered a disconnect/reboot-expected path. Reboot the console if needed, then run[/] [cyan]rgh modules pending[/] [grey]or[/] [cyan]rgh status[/] [grey]to verify.[/]");
                return 0;
            }

            XbdmModuleInfo? loaded = await ModuleCommandHelpers.SafeWaitForModuleStateAsync(
                client,
                moduleName,
                shouldExist: true,
                CancellationToken.None);
            if (loaded == null && rpcError != null) {
                loaded = await ModuleCommandHelpers.WaitForModuleStateWithReconnectAsync(
                    ip,
                    port,
                    timeout,
                    moduleName,
                    shouldExist: true,
                    CancellationToken.None);
            }

            if (status != 0) {
                OperationFeedback.WriteFailure("Load failed", $"NTSTATUS=0x{status:X8}");
                return 1;
            }
            if (loaded == null && rpcError != null) {
                OperationFeedback.WriteFailure("Load failed", rpcError.Message);
                return 1;
            }

            if (loaded != null) {
                XbdmModuleInfo? confirmedLoaded = await ModuleCommandHelpers.ConfirmModulePresenceFreshAsync(
                    ip,
                    port,
                    timeout,
                    moduleName,
                    CancellationToken.None);
                if (confirmedLoaded == null) {
                    OperationFeedback.WriteFailure("Load failed", "module did not remain loaded after the initial success response");
                    return 1;
                }

                loaded = confirmedLoaded;
            }

            uint resolvedHandle = 0;
            if (rpcError == null) {
                resolvedHandle = await ModuleCommandHelpers.ResolveModuleHandleAsync(client, jrpc, moduleName, CancellationToken.None);
            }
            else if (loaded != null) {
                resolvedHandle = await ModuleCommandHelpers.TryResolveHandleAfterReconnectAsync(
                    ip,
                    port,
                    timeout,
                    moduleName,
                    CancellationToken.None);
            }
            if (resolvedHandle != 0)
                ModuleCommandHelpers.StoreHandle(moduleName, resolvedHandle);
            ModuleCommandHelpers.ClearPendingIfMatch("load", moduleName);

            if (loaded != null) {
                string handleText = resolvedHandle != 0
                    ? $" [grey]handle[/] [gold1]0x{resolvedHandle:X8}[/]"
                    : string.Empty;
                string verifiedText = rpcError != null
                    ? " [grey](verified after ambiguous RPC completion)[/]"
                    : string.Empty;
                OperationFeedback.WriteSuccess(
                    "Module loaded",
                    $"[green]{Markup.Escape(loaded.Name)}[/] [grey]at[/] [cyan]0x{loaded.BaseAddress:X8}[/]{handleText}{verifiedText}");
            }
            else {
                string handleText = resolvedHandle != 0
                    ? $" [grey]handle[/] [gold1]0x{resolvedHandle:X8}[/]"
                    : string.Empty;
                OperationFeedback.WriteWarning(
                    "Module load returned success",
                    $"[yellow]{Markup.Escape(moduleName)}[/] [grey]was not visible in the module list yet.[/]{handleText}");
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

public sealed class XbdmModuleUnloadCommand : AsyncCommand<XbdmModuleUnloadCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--name <MODULE>")]
        [Description("Loaded module name, e.g. XDRPC.xex.")]
        public string? Name { get; init; }

        [CommandOption("--handle <HANDLE>")]
        [Description("Explicit module handle as hex or decimal.")]
        public string? Handle { get; init; }

        [CommandOption("--skip-mark")]
        [Description("Do not set the sysdll unload marker at handle+0x40 before unloading.")]
        public bool SkipMark { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show the resolved unload target without writing anything.")]
        public bool DryRun { get; init; }

        [CommandOption("--force")]
        [Description("Required. Module unload can wedge the console if the target rejects live unload.")]
        public bool Force { get; init; }

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
        if (string.IsNullOrWhiteSpace(settings.Name) && string.IsNullOrWhiteSpace(settings.Handle)) {
            AnsiConsole.MarkupLine("[red]--name or --handle is required.[/]");
            return 1;
        }

        if (!settings.DryRun && !settings.Force) {
            AnsiConsole.MarkupLine("[red]--force is required for live unloads.[/]");
            return 1;
        }

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        return await CliHelpers.WithClientOnceAsync((ip, port, timeout), settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            uint handle;
            string? moduleName = settings.Name;

            if (!string.IsNullOrWhiteSpace(settings.Handle)) {
                if (!CliHelpers.TryParseUInt32(settings.Handle, out handle)) {
                    AnsiConsole.MarkupLine("[red]Invalid --handle.[/]");
                    return 1;
                }
            }
            else {
                handle = await ModuleCommandHelpers.ResolveModuleHandleAsync(client, jrpc, settings.Name!, CancellationToken.None);
                if (handle == 0) {
                    OperationFeedback.WriteFailure("Module handle not found", Markup.Escape(settings.Name!));
                    return 1;
                }
            }

            if (settings.DryRun) {
                AnsiConsole.MarkupLine($"[grey]Would unload module handle[/] [cyan]0x{handle:X8}[/]");
                return 0;
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
                OperationFeedback.WriteFailure("Unload failed", $"NTSTATUS=0x{status:X8}");
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
                    OperationFeedback.WriteFailure("Unload failed", reason);
                    return 1;
                }
            }
            else if (rpcError != null) {
                OperationFeedback.WriteFailure("Unload failed", rpcError.Message);
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(moduleName))
                ModuleCommandHelpers.RemoveHandle(moduleName);

            string unloadVerifiedText = rpcError != null
                ? "[grey](verified after ambiguous RPC completion)[/]"
                : string.Empty;
            OperationFeedback.WriteSuccess("Module unloaded", $"[cyan]0x{handle:X8}[/] {unloadVerifiedText}".TrimEnd());
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
        if (pending == null || string.IsNullOrWhiteSpace(pending.ModuleName) || string.IsNullOrWhiteSpace(pending.Action)) {
            AnsiConsole.MarkupLine("[grey]No pending module operation.[/]");
            return 0;
        }

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        return await CliHelpers.WithClientOnceAsync((ip, port, timeout), settings, async client => {
            IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(false, CancellationToken.None);
            bool present = modules.Any(m => string.Equals(m.Name, pending.ModuleName, StringComparison.OrdinalIgnoreCase));
            bool success = string.Equals(pending.Action, "load", StringComparison.OrdinalIgnoreCase) ? present : !present;

            if (success) {
                if (string.Equals(pending.Action, "load", StringComparison.OrdinalIgnoreCase)) {
                    XbdmModuleInfo module = modules.First(m => string.Equals(m.Name, pending.ModuleName, StringComparison.OrdinalIgnoreCase));
                    ModuleCommandHelpers.ClearPendingIfMatch(pending.Action, pending.ModuleName);
                    OperationFeedback.WriteSuccess(
                        "Pending module load verified",
                        $"[green]{Markup.Escape(module.Name)}[/] [grey]at[/] [cyan]0x{module.BaseAddress:X8}[/]");
                }
                else {
                    ModuleCommandHelpers.ClearPendingIfMatch(pending.Action, pending.ModuleName);
                    OperationFeedback.WriteSuccess(
                        "Pending module unload verified",
                        $"[green]{Markup.Escape(pending.ModuleName)}[/] [grey]is no longer loaded.[/]");
                }

                return 0;
            }

            OperationFeedback.WriteWarning(
                "Module operation still pending",
                $"[yellow]{Markup.Escape(pending.ModuleName)}[/] [grey]has not reached the expected state yet.[/]");
            return 1;
        }, CancellationToken.None);
    }
}

internal static class ModuleCommandHelpers {
    public static uint ParseRpcUInt32(string value) {
        string text = value.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(2);
        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex)
            ? hex
            : uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
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
        uint handle = ParseRpcUInt32(resolved);
        if (handle != 0)
            return handle;

        IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(false, cancellationToken);
        XbdmModuleInfo? module = modules.FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (module == null)
            return 0;

        if (TryGetStoredHandle(module.Name, out cachedHandle))
            return cachedHandle;

        return 0;
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
        return ParseRpcUInt32(result);
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
            catch when (attempt + 1 < reconnectAttempts) {
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
            catch when (attempt + 1 < reconnectAttempts) {
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
            catch when (attempt + 1 < attempts) {
                // ignored
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

    private static bool TryGetStoredHandle(string moduleName, out uint handle) {
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
    public static void WriteSuccess(string title, string detail) {
        AnsiConsole.MarkupLine($"[bold springgreen3_1]SUCCESS[/] [grey]{Markup.Escape(title)}[/]");
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
