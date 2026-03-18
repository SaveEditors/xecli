using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli;
using Xbox360.Remote.Cli.Commands;

internal static class Program {
    public static async Task<int> Main(string[] args) {
        args = NormalizeArgs(args);
        await HandleFirstRunPathPromptAsync(args);
        CommandApp app = new CommandApp();
        app.Configure(config => {
            config.SetApplicationName("rgh");
            config.ValidateExamples();
            config.SetExceptionHandler((ex, _) => {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            });

            config.AddCommand<StatusCommand>("status").WithDescription("Show a compact console status snapshot.");
            config.AddCommand<ProfilesCommand>("profiles").WithAlias("users").WithDescription("List profiles and signed-in users.");
            config.AddCommand<TitleLookupCommand>("title").WithAlias("titles").WithDescription("Look up a Title ID in the local database.");
            config.AddCommand<TargetCommand>("target").WithDescription("Show or set the default target.");
            config.AddCommand<PingCommand>("ping").WithDescription("Ping the current console.");
            config.AddCommand<RebootCommand>("reboot").WithAlias("restart").WithDescription("Reboot the console (cold by default).");
            config.AddCommand<InstallCommand>("install").WithDescription("Install rgh for the current user or add it to the machine PATH.");
            config.AddCommand<StartCommand>("start").WithAlias("s").WithDescription("Discover consoles and set the default target.");
            config.AddCommand<ConnectCommand>("connect").WithAlias("c").WithDescription("Set or select the default target.");
            config.AddCommand<ScanCommand>("scan").WithAlias("discover").WithDescription("Scan the network for consoles.");
            config.AddCommand<XbdmScreenshotCommand>("screenshot").WithAlias("shot").WithDescription("Capture a live screenshot.");

            config.AddBranch("xbdm", xbdm => {
                xbdm.SetDescription("XBDM commands.");
                xbdm.AddCommand<XbdmInfoCommand>("info").WithDescription("Show XBDM console info.");
                xbdm.AddCommand<XbdmRawCommand>("raw").WithDescription("Send a raw XBDM command.");
                xbdm.AddCommand<XbdmScreenshotCommand>("screenshot").WithDescription("Capture a screenshot via XBDM.");

                xbdm.AddBranch("modules", modules => {
                    modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
                    modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
                    modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
                });

                xbdm.AddBranch("mem", mem => {
                    mem.AddCommand<XbdmMemDumpCommand>("dump").WithDescription("Dump a memory range to a file.");
                    mem.AddCommand<XbdmMemHexDumpCommand>("hexdump").WithDescription("Hex dump a memory range.");
                    mem.AddCommand<XbdmMemRegionsCommand>("regions").WithAlias("map").WithDescription("List memory regions.");
                    mem.AddCommand<XbdmMemPeekCommand>("peek").WithAlias("read").WithDescription("Read a value from memory.");
                    mem.AddCommand<XbdmMemPokeCommand>("poke").WithAlias("write").WithDescription("Write a value to memory.");
                    mem.AddCommand<XbdmMemWatchCommand>("watch").WithDescription("Stream memory changes.");
                    mem.AddCommand<XbdmMemStringsCommand>("strings").WithDescription("Extract strings from memory.");
                    mem.AddCommand<XbdmMemFindCommand>("find").WithDescription("Search memory for a pattern.");
                });

                xbdm.AddBranch("xex", xex => {
                    xex.AddCommand<XbdmXexDumpCommand>("dump").WithDescription("Dump the active XEX image.");
                    xex.AddCommand<XexStringsCommand>("strings").WithDescription("Extract strings from a XEX (local/FTP/running).");
                    xex.AddCommand<GhidraDecompileCommand>("decompile").WithAlias("decode").WithDescription("Decompile a XEX to C via Ghidra.");
                });

                xbdm.AddBranch("fs", fs => {
                    fs.AddCommand<XbdmFsListCommand>("list").WithAlias("ls").WithDescription("List files in a directory.");
                    fs.AddCommand<XbdmFsGetCommand>("get").WithDescription("Download a file from the console.");
                    fs.AddCommand<XbdmFsPutCommand>("put").WithDescription("Upload a file to the console.");
                    fs.AddCommand<XbdmFsCatCommand>("cat").WithDescription("Print a file from the console.");
                    fs.AddCommand<XbdmFsDeleteCommand>("rm").WithAlias("del").WithDescription("Delete a file on the console.");
                    fs.AddCommand<XbdmFsMkdirCommand>("mkdir").WithDescription("Create a directory on the console.");
                    fs.AddCommand<XbdmFsMoveCommand>("mv").WithAlias("move").WithDescription("Move or rename a file on the console.");
                });

                xbdm.AddBranch("threads", threads => {
                    threads.AddCommand<XbdmThreadsListCommand>("list").WithAlias("ls").WithDescription("List threads.");
                    threads.AddCommand<XbdmThreadContextCommand>("context").WithDescription("Read thread registers.");
                    threads.AddCommand<XbdmThreadSuspendCommand>("suspend").WithDescription("Suspend a thread.");
                    threads.AddCommand<XbdmThreadResumeCommand>("resume").WithDescription("Resume a thread.");
                });

                xbdm.AddBranch("debug", debug => {
                    debug.AddCommand<XbdmDebugStopCommand>("stop").WithDescription("Break execution.");
                    debug.AddCommand<XbdmDebugGoCommand>("go").WithDescription("Resume execution.");
                    debug.AddBranch("break", brk => {
                        brk.SetDescription("Code breakpoints.");
                        brk.AddCommand<XbdmBreakpointAddCommand>("add").WithDescription("Add a code breakpoint.");
                        brk.AddCommand<XbdmBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a code breakpoint.");
                    });
                    debug.AddBranch("databreak", brk => {
                        brk.SetDescription("Data breakpoints.");
                        brk.AddCommand<XbdmDataBreakpointAddCommand>("add").WithDescription("Add a data breakpoint.");
                        brk.AddCommand<XbdmDataBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a data breakpoint.");
                    });
                });
            });

            config.AddBranch("modules", modules => {
                modules.SetDescription("XBDM module commands (shortcut).");
                modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
                modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
                modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
            });

            config.AddBranch("mem", mem => {
                mem.SetDescription("XBDM memory commands (shortcut).");
                mem.AddCommand<XbdmMemDumpCommand>("dump").WithDescription("Dump a memory range to a file.");
                mem.AddCommand<XbdmMemHexDumpCommand>("hexdump").WithDescription("Hex dump a memory range.");
                mem.AddCommand<XbdmMemRegionsCommand>("regions").WithAlias("map").WithDescription("List memory regions.");
                mem.AddCommand<XbdmMemPeekCommand>("peek").WithAlias("read").WithDescription("Read a value from memory.");
                mem.AddCommand<XbdmMemPokeCommand>("poke").WithAlias("write").WithDescription("Write a value to memory.");
                mem.AddCommand<XbdmMemWatchCommand>("watch").WithDescription("Stream memory changes.");
                mem.AddCommand<XbdmMemStringsCommand>("strings").WithDescription("Extract strings from memory.");
                mem.AddCommand<XbdmMemFindCommand>("find").WithDescription("Search memory for a pattern.");
            });

            config.AddBranch("xex", xex => {
                xex.SetDescription("XBDM XEX commands (shortcut).");
                xex.AddCommand<XbdmXexDumpCommand>("dump").WithDescription("Dump the active XEX image.");
                xex.AddCommand<XexStringsCommand>("strings").WithDescription("Extract strings from a XEX (local/FTP/running).");
                xex.AddCommand<GhidraDecompileCommand>("decompile").WithAlias("decode").WithDescription("Decompile a XEX to C via Ghidra.");
            });

            config.AddBranch("fs", fs => {
                fs.SetDescription("XBDM file system commands (shortcut).");
                fs.AddCommand<XbdmFsListCommand>("list").WithAlias("ls").WithDescription("List files in a directory.");
                fs.AddCommand<XbdmFsGetCommand>("get").WithDescription("Download a file from the console.");
                fs.AddCommand<XbdmFsPutCommand>("put").WithDescription("Upload a file to the console.");
                fs.AddCommand<XbdmFsCatCommand>("cat").WithDescription("Print a file from the console.");
                fs.AddCommand<XbdmFsDeleteCommand>("rm").WithAlias("del").WithDescription("Delete a file on the console.");
                fs.AddCommand<XbdmFsMkdirCommand>("mkdir").WithDescription("Create a directory on the console.");
                fs.AddCommand<XbdmFsMoveCommand>("mv").WithAlias("move").WithDescription("Move or rename a file on the console.");
            });

            config.AddBranch("threads", threads => {
                threads.SetDescription("XBDM thread commands (shortcut).");
                threads.AddCommand<XbdmThreadsListCommand>("list").WithAlias("ls").WithDescription("List threads.");
                threads.AddCommand<XbdmThreadContextCommand>("context").WithDescription("Read thread registers.");
                threads.AddCommand<XbdmThreadSuspendCommand>("suspend").WithDescription("Suspend a thread.");
                threads.AddCommand<XbdmThreadResumeCommand>("resume").WithDescription("Resume a thread.");
            });

            config.AddBranch("debug", debug => {
                debug.SetDescription("XBDM debug control (shortcut).");
                debug.AddCommand<XbdmDebugStopCommand>("stop").WithDescription("Break execution.");
                debug.AddCommand<XbdmDebugGoCommand>("go").WithDescription("Resume execution.");
                debug.AddBranch("break", brk => {
                    brk.SetDescription("Code breakpoints.");
                    brk.AddCommand<XbdmBreakpointAddCommand>("add").WithDescription("Add a code breakpoint.");
                    brk.AddCommand<XbdmBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a code breakpoint.");
                });
                debug.AddBranch("databreak", brk => {
                    brk.SetDescription("Data breakpoints.");
                    brk.AddCommand<XbdmDataBreakpointAddCommand>("add").WithDescription("Add a data breakpoint.");
                    brk.AddCommand<XbdmDataBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a data breakpoint.");
                });
            });

            config.AddBranch("jrpc2", rpc => {
                rpc.SetDescription("JRPC2 (XDRPC-style) commands.");
                rpc.AddCommand<Jrpc2CpuKeyCommand>("cpu-key").WithDescription("Read the CPU key.");
                rpc.AddCommand<Jrpc2TempsCommand>("temps").WithDescription("Read temperature sensors.");
                rpc.AddCommand<Jrpc2TitleIdCommand>("title-id").WithDescription("Read the current Title ID.");
                rpc.AddCommand<Jrpc2DashboardCommand>("dashboard").WithDescription("Read the dashboard version.");
                rpc.AddCommand<Jrpc2MotherboardCommand>("motherboard").WithDescription("Read motherboard type.");
                rpc.AddCommand<Jrpc2ResolveCommand>("resolve").WithDescription("Resolve a function by module/ordinal.");
                rpc.AddCommand<Jrpc2NotifyCommand>("notify").WithDescription("Send a notification via JRPC2.");
                rpc.AddCommand<Jrpc2CallCommand>("call").WithDescription("Call a function with RPC.");
            });

            config.AddCommand<NotifySendCommand>("notify").WithAlias("xnotify").WithDescription("Send an on-screen notification.");
            config.AddBranch("notify-icons", icons => {
                icons.SetDescription("Manage notify icon presets.");
                icons.AddCommand<NotifyIconsListCommand>("list").WithDescription("List icon presets.");
                icons.AddCommand<NotifyIconsAddCommand>("add").WithDescription("Add an icon preset.");
                icons.AddCommand<NotifyIconsRemoveCommand>("remove").WithAlias("del").WithDescription("Remove an icon preset.");
            });

            config.AddBranch("ftp", ftp => {
                ftp.SetDescription("FTP commands (alternate access).");
                ftp.AddCommand<FtpTargetCommand>("target").WithDescription("Show or set FTP target.");
                ftp.AddCommand<FtpListCommand>("list").WithAlias("ls").WithDescription("List files via FTP.");
                ftp.AddCommand<FtpFindCommand>("find").WithDescription("Find files via FTP.");
                ftp.AddCommand<FtpGetCommand>("get").WithDescription("Download a file via FTP.");
                ftp.AddCommand<FtpPutCommand>("put").WithDescription("Upload a file via FTP.");
                ftp.AddCommand<FtpCatCommand>("cat").WithDescription("Print a file via FTP.");
                ftp.AddCommand<FtpDeleteCommand>("rm").WithAlias("del").WithDescription("Delete a file via FTP.");
                ftp.AddCommand<FtpMkdirCommand>("mkdir").WithDescription("Create a directory via FTP.");
                ftp.AddCommand<FtpMoveCommand>("mv").WithAlias("move").WithDescription("Move or rename a file via FTP.");
            });

            config.AddBranch("god", god => {
                god.SetDescription("ISO to Games on Demand conversion.");
                god.AddCommand<GodInfoCommand>("info").WithDescription("Inspect an ISO and show title metadata.");
                god.AddCommand<GodBuildCommand>("build").WithAlias("make").WithAlias("convert").WithDescription("Convert an ISO into GOD parts.");
                god.AddCommand<GodWatchCommand>("watch").WithAlias("watchdog").WithDescription("Watch a folder and auto-convert ISOs.");
            });

            config.AddBranch("ghidra", ghidra => {
                ghidra.SetDescription("Ghidra headless helpers.");
                ghidra.AddCommand<GhidraConfigCommand>("config").WithDescription("Configure Ghidra paths.");
                ghidra.AddCommand<GhidraAnalyzeCommand>("analyze").WithDescription("Run headless analysis.");
                ghidra.AddCommand<GhidraDecompileCommand>("decompile").WithDescription("Decompile a module or XEX.");
                ghidra.AddCommand<GhidraVerifyCommand>("verify").WithDescription("Verify decompiler output for bad-instruction placeholders.");
            });
        });

        return await app.RunAsync(args);
    }

    private static Task HandleFirstRunPathPromptAsync(string[] args) {
        if (!ShouldShowPathPrompt(args))
            return Task.CompletedTask;

        CliConfig config = CliConfig.Load();
        if (config.PathPromptHandled)
            return Task.CompletedTask;

        string exeDir = InstallHelpers.NormalizeDirectory(AppContext.BaseDirectory);
        if (InstallHelpers.IsCommandAvailable(exeDir)) {
            config.PathPromptHandled = true;
            config.Save();
            return Task.CompletedTask;
        }

        string currentExe = Environment.ProcessPath ?? Path.Combine(exeDir, "rgh.exe");
        Panel panel = new Panel(
            "Add [green]rgh[/] to the machine PATH so it can be used from any terminal.\n[grey]This triggers a UAC prompt and requires administrator approval.[/]")
            .Header("[bold deepskyblue1]First-Run Setup[/]")
            .BorderColor(Color.Grey);
        AnsiConsole.Write(panel);

        string choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Install global terminal access now?")
                .AddChoices("Yes", "Not now", "Never ask again"));

        if (choice == "Yes") {
            try {
                int exitCode = InstallHelpers.RunElevatedMachinePathInstall(currentExe, exeDir);
                if (exitCode == 0) {
                    config.PathPromptHandled = true;
                    config.Save();
                    AnsiConsole.MarkupLine("[green]Global PATH install completed.[/] Open a new terminal to use `rgh` from anywhere.");
                }
                else {
                    AnsiConsole.MarkupLine($"[red]PATH install exited with code {exitCode}.[/]");
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) {
                AnsiConsole.MarkupLine("[yellow]PATH install was cancelled at the UAC prompt.[/]");
            }

            return Task.CompletedTask;
        }

        if (choice == "Not now") {
            config.PathPromptHandled = true;
            config.Save();
            AnsiConsole.MarkupLine("[grey]Skipped. You can install later with `rgh install --machine-path`.[/]");
            return Task.CompletedTask;
        }

        config.PathPromptHandled = true;
        config.Save();
        AnsiConsole.MarkupLine("[grey]Path prompt disabled. You can install later with `rgh install --machine-path`.[/]");
        return Task.CompletedTask;
    }

    private static string[] NormalizeArgs(string[] args) {
        if (args.Length == 0)
            return args;

        if (IsHelpToken(args[0])) {
            if (args.Length == 1)
                return new[] { "--help" };
            return args.Skip(1).Concat(new[] { "--help" }).ToArray();
        }

        int last = args.Length - 1;
        if (IsHelpToken(args[last]))
            return args.Take(last).Concat(new[] { "--help" }).ToArray();

        return args;
    }

    private static bool IsHelpToken(string arg) {
        return arg.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldShowPathPrompt(string[] args) {
        if (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected)
            return false;

        if (args.Any(IsHelpToken) || args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (args.Length > 0 && args[0].Equals("install", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
