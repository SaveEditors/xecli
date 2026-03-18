using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmInfoCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        return await CliHelpers.WithClientAsync((ip, port, timeout), settings, async client => {
            XbdmConsoleInfo info = await client.GetConsoleInfoAsync(CancellationToken.None);
            string? dmVersion = null;
            try {
                dmVersion = await client.GetDmVersionAsync(CancellationToken.None);
            }
            catch {
                // ignored
            }

            uint? titleId = null;
            try {
                Jrpc2Client jrpc = new Jrpc2Client(client);
                titleId = await jrpc.GetTitleIdAsync(CancellationToken.None);
            }
            catch {
                // JRPC2 not available or failed
            }

            IReadOnlyList<XbdmDriveEntry> drives = Array.Empty<XbdmDriveEntry>();
            try {
                drives = await client.GetDrivesAsync(includeSize: true, CancellationToken.None);
            }
            catch {
                // ignored
            }

            IReadOnlyList<XbdmUserInfo> users = Array.Empty<XbdmUserInfo>();
            try {
                users = await client.GetUserListAsync(CancellationToken.None);
            }
            catch {
                // ignored
            }
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Console = info,
                    Port = port,
                    DmVersion = dmVersion,
                    TitleId = titleId.HasValue ? $"0x{titleId.Value:X8}" : null,
                    Drives = drives,
                    Users = users
                });
                return 0;
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
            table.AddRow("[grey]Title ID[/]", titleId.HasValue ? $"0x{titleId.Value:X8}" : "[grey]unknown[/]");
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
}

public sealed class XbdmModulesListCommand : AsyncCommand<XbdmModulesListCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--sections")]
        [Description("Include section details.")]
        public bool Sections { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
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
                table.AddRow(
                    $"[grey]{index}[/]",
                    $"[green]{Markup.Escape(module.Name)}[/]",
                    $"[cyan]0x{module.BaseAddress:X8}[/]",
                    $"[cyan]0x{module.ModuleSize:X8}[/]",
                    module.EntryPoint.HasValue ? $"[gold1]0x{module.EntryPoint.Value:X8}[/]" : "[grey]unknown[/]",
                    CliOutput.FormatTimestamp(module.Timestamp));
                index++;
            }

            AnsiConsole.Write(table);

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
        [Description("Module name, e.g. default.xex or xam.xex.")]
        public string? Name { get; init; }

        [CommandOption("--out <FILE>")]
        [Description("Output file path.")]
        public string? Output { get; init; }

        [CommandOption("--all")]
        [Description("Dump all modules to a directory.")]
        public bool All { get; init; }

        [CommandOption("--dir <DIR>")]
        [Description("Output directory for --all.")]
        public string? Directory { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (settings.All) {
            if (string.IsNullOrWhiteSpace(settings.Directory)) {
                AnsiConsole.MarkupLine("[red]--dir is required with --all.[/]");
                return 1;
            }
        }
        else if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Output)) {
            AnsiConsole.MarkupLine("[red]--name and --out are required.[/]");
            return 1;
        }

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        IReadOnlyList<XbdmModuleInfo> modules;
        using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions { Host = ip, Port = port, TimeoutMs = timeout }, CancellationToken.None)) {
            modules = await client.GetModulesAsync(false, CancellationToken.None);
        }

        if (settings.All) {
            Directory.CreateDirectory(settings.Directory!);
            foreach (XbdmModuleInfo module in modules) {
                string safeName = SanitizeFileName(module.Name);
                string outputPath = Path.Combine(settings.Directory!, safeName);
                await CliHelpers.WithClientAsync((ip, port, timeout), settings, async client => {
                    using FileStream stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await CliOutput.RunWithProgressAsync($"Dumping {module.Name}", module.ModuleSize, progress =>
                        client.ReadMemoryAsync(module.BaseAddress, module.ModuleSize, stream, progress, CancellationToken.None));
                    return 0;
                }, CancellationToken.None);
                AnsiConsole.MarkupLine($"[green]Dumped[/] {module.Name} -> {outputPath}");
            }
            return 0;
        }

        XbdmModuleInfo? moduleSingle = modules.FirstOrDefault(m => string.Equals(m.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
        if (moduleSingle == null) {
            AnsiConsole.MarkupLine($"[red]Module not found:[/] {settings.Name}");
            return 1;
        }

        await CliHelpers.WithClientAsync((ip, port, timeout), settings, async client => {
            using FileStream streamSingle = new FileStream(settings.Output!, FileMode.Create, FileAccess.Write, FileShare.None);
            await CliOutput.RunWithProgressAsync($"Dumping {moduleSingle.Name}", moduleSingle.ModuleSize, progress =>
                client.ReadMemoryAsync(moduleSingle.BaseAddress, moduleSingle.ModuleSize, streamSingle, progress, CancellationToken.None));
            return 0;
        }, CancellationToken.None);

        AnsiConsole.MarkupLine($"[green]Dumped[/] {moduleSingle.Name} to {settings.Output}");
        return 0;
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
        [Description("Explicit XEX path. If omitted, uses the running title.")]
        public string? Path { get; init; }

        [CommandOption("--out <FILE>")]
        [Description("Output file path.")]
        public string? Output { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Output)) {
            AnsiConsole.MarkupLine("[red]--out is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            string? path = settings.Path ?? await client.GetRunningXexPathAsync(null, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(path)) {
                AnsiConsole.MarkupLine("[red]Unable to resolve running XEX. Use --path.[/]");
                return 1;
            }

            using FileStream stream = new FileStream(settings.Output, FileMode.Create, FileAccess.Write, FileShare.None);
            await CliOutput.RunWithProgressAsync($"Downloading {path}", null, progress =>
                client.DownloadFileAsync(path, stream, progress, CancellationToken.None));

            AnsiConsole.MarkupLine($"[green]Downloaded[/] {path} to {settings.Output}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmMemDumpCommand : AsyncCommand<XbdmMemDumpCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }

        [CommandOption("--out <FILE>")]
        public string? Output { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address) ||
            !CliHelpers.TryParseUInt32(settings.Size, out uint size) ||
            string.IsNullOrWhiteSpace(settings.Output)) {
            AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec> --out <file>");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using FileStream stream = new FileStream(settings.Output, FileMode.Create, FileAccess.Write, FileShare.None);
            await CliOutput.RunWithProgressAsync($"Dumping 0x{address:X8}", size, progress =>
                client.ReadMemoryAsync(address, size, stream, progress, CancellationToken.None));

            AnsiConsole.MarkupLine($"[green]Dumped[/] 0x{address:X8} ({size} bytes) to {settings.Output}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmMemHexDumpCommand : AsyncCommand<XbdmMemHexDumpCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address) ||
            !CliHelpers.TryParseUInt32(settings.Size, out uint size)) {
            AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec>");
            return 1;
        }

        if (size > 0x100000) {
            AnsiConsole.MarkupLine("[yellow]Large hexdumps are truncated to 1MB. Use mem-dump for larger ranges.[/]");
            size = 0x100000;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using MemoryStream ms = new MemoryStream((int) size);
            await client.ReadMemoryAsync(address, size, ms, null, CancellationToken.None);
            CliOutput.RenderHexDump(address, ms.ToArray());
            return 0;
        }, CancellationToken.None);
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
        if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Output)) {
            AnsiConsole.MarkupLine("[red]--path and --out are required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using FileStream stream = new FileStream(settings.Output, FileMode.Create, FileAccess.Write, FileShare.None);
            await CliOutput.RunWithProgressAsync($"Downloading {settings.Path}", null, progress =>
                client.DownloadFileAsync(settings.Path, stream, progress, CancellationToken.None));
            AnsiConsole.MarkupLine($"[green]Downloaded[/] {settings.Path} to {settings.Output}");
            return 0;
        }, CancellationToken.None);
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
        if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Input)) {
            AnsiConsole.MarkupLine("[red]--path and --in are required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using FileStream stream = new FileStream(settings.Input, FileMode.Open, FileAccess.Read, FileShare.Read);
            await CliOutput.RunWithProgressAsync($"Uploading {settings.Input}", (uint) stream.Length, progress =>
                client.UploadFileAsync(settings.Path, stream, stream.Length, progress, CancellationToken.None));
            AnsiConsole.MarkupLine($"[green]Uploaded[/] {settings.Input} to {settings.Path}");
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

        return await CliHelpers.WithClientAsync(settings, async client => {
            (XbdmResponse response, IReadOnlyList<string>? lines) = await client.SendRawAsync(settings.Command, CancellationToken.None);
            AnsiConsole.MarkupLine($"[grey]({response.StatusCode})[/] {response.Message}");
            if (lines != null) {
                foreach (string line in lines) {
                    AnsiConsole.WriteLine(line);
                }
            }
            return 0;
        }, CancellationToken.None);
    }
}
