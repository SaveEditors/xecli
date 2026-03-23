using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Xbox360.Remote.Cli;
using Xbox360.Remote.Cli.Commands;
using Xbox360.Remote.Cli.Homebrew;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

internal static class Program {
    [STAThread]
    public static async Task<int> Main(string[] args) {
        args = NormalizeArgs(args);
        if (IsVersionRequest(args)) {
            AnsiConsole.MarkupLine($"[deepskyblue1]XeCLI[/] [white]{Markup.Escape(GetApplicationVersion())}[/]");
            return 0;
        }
        await HandleFirstRunPathPromptAsync(args);
        CommandApp app = new CommandApp();
        app.Configure(config => {
            config.SetApplicationName("rgh");
            config.Settings.ShowOptionDefaultValues = true;
            config.Settings.MaximumIndirectExamples = 2;
            config.Settings.HelpProviderStyles = new HelpProviderStyle {
                Description = new DescriptionStyle {
                    Header = new Style(Color.Gold1, decoration: Decoration.Bold)
                },
                Usage = new UsageStyle {
                    Header = new Style(Color.Gold1, decoration: Decoration.Bold),
                    CurrentCommand = new Style(Color.SpringGreen3_1, decoration: Decoration.Bold),
                    Command = new Style(Color.White, decoration: Decoration.Bold),
                    Options = new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold),
                    RequiredArgument = new Style(Color.Aqua, decoration: Decoration.Bold),
                    OptionalArgument = new Style(Color.White)
                },
                Examples = new ExampleStyle {
                    Header = new Style(Color.Gold1, decoration: Decoration.Bold),
                    Arguments = new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold)
                },
                Arguments = new ArgumentStyle {
                    Header = new Style(Color.Gold1, decoration: Decoration.Bold),
                    RequiredArgument = new Style(Color.Aqua, decoration: Decoration.Bold),
                    OptionalArgument = new Style(Color.White)
                },
                Options = new OptionStyle {
                    Header = new Style(Color.Gold1, decoration: Decoration.Bold),
                    DefaultValueHeader = new Style(Color.White, decoration: Decoration.Bold),
                    DefaultValue = new Style(Color.MediumPurple3, decoration: Decoration.Bold),
                    RequiredOption = new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold),
                    OptionalOption = new Style(Color.White, decoration: Decoration.Bold)
                },
                Commands = new CommandStyle {
                    Header = new Style(Color.Gold1, decoration: Decoration.Bold),
                    ChildCommand = new Style(Color.SpringGreen3_1, decoration: Decoration.Bold),
                    RequiredArgument = new Style(Color.Aqua, decoration: Decoration.Bold)
                }
            };
            config.SetExceptionHandler((ex, _) => {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            });
            config.AddExample(new[] { "status" });
            config.AddExample(new[] { "title" });
            config.AddExample(new[] { "modules", "list" });
            config.AddExample(new[] { "mem", "hexdump", "--addr", "0x30000000", "--size", "0x40" });
            config.AddExample(new[] { "notify", "XeCLI connected", "14" });
            config.AddExample(new[] { "smc", "version" });
            config.AddExample(new[] { "fan", "set", "--speed", "55", "--channel", "both" });
            config.AddExample(new[] { "led", "set", "--preset", "quadrant1" });
            config.AddExample(new[] { "tray", "open" });
            config.AddExample(new[] { "popup", "show", "--title", "XeCLI", "--body", "Connected to console", "--preset", "none" });
            config.AddExample(new[] { "avatar", "games", "--search", "Black Ops" });
            config.AddExample(new[] { "avatar", "install", "--contentid", "000000080DF3B242CAE65A52415608C3", "--current-user" });
            config.AddExample(new[] { "avatar", "browse", "--remote" });
            config.AddExample(new[] { "homebrew", "install", "aurora", "--usb", "E:" });
            config.AddExample(new[] { "homebrew", "install", "all", "--usb", "E:", "--auto-confirm" });
            config.AddExample(new[] { "ogxbox", "install", "hacked", "--include-fixer", "--usb", "E:" });
            config.AddExample(new[] { "fatman", "disks" });
            config.AddExample(new[] { "fatman", "partitions", "--image", ".\\hdd.img" });
            config.AddExample(new[] { "fatman", "partitions", "--disk", "2" });
            config.AddExample(new[] { "fatman", "scan", "--image", ".\\hdd.img" });
            config.AddExample(new[] { "fatman", "info", "--image", ".\\hdd.img", "--offset", "0xB6600000" });
            config.AddExample(new[] { "fatman", "list", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/" });
            config.AddExample(new[] { "fatman", "list", "--image", ".\\hdd.img", "--offset", "0xB6600000", "--path", "/" });
            config.AddExample(new[] { "fatman", "mkdir", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI" });
            config.AddExample(new[] { "fatman", "put", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--in", ".\\readme.txt", "--overwrite" });
            config.AddExample(new[] { "fatman", "mv", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--to", "/XeCLI/readme-old.txt" });
            config.AddExample(new[] { "fatman", "rm", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI", "--recursive" });
            config.AddExample(new[] { "fatman", "format", "--image", ".\\hdd.img", "--partition", "Content", "--auto-confirm" });
            config.AddExample(new[] { "fatman", "check", "--image", ".\\hdd.img", "--partition", "Content" });
            config.AddExample(new[] { "fatman", "repair", "--image", ".\\hdd.img", "--partition", "Content", "--auto-confirm" });
            config.AddExample(new[] { "fatman", "metadata", "backup", "--image", ".\\hdd.img", "--out", ".\\fatman-meta" });
            config.AddExample(new[] { "fatman", "get", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/Content/file.bin", "--out", ".\\file.bin" });
            config.AddExample(new[] { "fatman", "dump", "--image", ".\\hdd.img", "--out", ".\\fatman-dump" });
            config.AddExample(new[] { "ghidra", "decompile", "--running", "--out", ".\\decomp" });
            config.AddExample(new[] { "ida", "decompile", "--running", "--out", ".\\ida-decomp", "--max", "25" });
            config.AddExample(new[] { "ida", "decompile", "--running", "--out", ".\\ida-decomp", "--max", "25" });

            config.AddCommand<StatusCommand>("status").WithDescription("Show a compact console status snapshot.");
            config.AddCommand<ProfilesCommand>("profiles").WithAlias("users").WithDescription("List profiles and signed-in users.");
            config.AddCommand<TitleLookupCommand>("title").WithAlias("titles").WithDescription("Resolve the active title or look up a Title ID.");
            config.AddCommand<TargetCommand>("target").WithDescription("Show or set the default target.");
            config.AddCommand<PingCommand>("ping").WithDescription("Ping the current console.");
            config.AddCommand<RebootCommand>("reboot").WithAlias("restart").WithDescription("Reboot the console (cold by default).");
            config.AddCommand<ShutdownCommand>("shutdown").WithAlias("poweroff").WithDescription("Power off the console.");
            config.AddCommand<LaunchCommand>("launch").WithAlias("run").WithDescription("Launch a XEX with optional arguments.");
            config.AddCommand<InstallCommand>("install").WithDescription("Launch the XeCLI installer.");
            config.AddCommand<StartCommand>("start").WithAlias("s").WithDescription("Discover consoles and set the default target.");
            config.AddCommand<ConnectCommand>("connect").WithAlias("c").WithDescription("Set or select the default target.");
            config.AddCommand<ScanCommand>("scan").WithAlias("discover").WithDescription("Scan the network for consoles.");
            config.AddCommand<XbdmScreenshotCommand>("screenshot").WithAlias("shot").WithDescription("Capture a live screenshot.");
            config.AddBranch("homebrew", homebrew => {
                homebrew.SetDescription("Download public homebrew packages to USB, a folder, or a detected console drive.");
                homebrew.AddExample(new[] { "homebrew", "list" });
                homebrew.AddExample(new[] { "homebrew", "install", "aurora", "--usb", "E:" });
                homebrew.AddExample(new[] { "homebrew", "install", "all", "--usb", "E:", "--auto-confirm" });
                homebrew.AddExample(new[] { "homebrew", "install", "aurora", "--device", "Hdd1", "--ini-mode", "merge" });
                homebrew.AddCommand<HomebrewListCommand>("list").WithAlias("ls").WithDescription("List the built-in package catalog.");
                homebrew.AddCommand<HomebrewInstallCommand>("install").WithAlias("stage").WithDescription("Download one or more public homebrew packages to USB, a folder, or the console.");
            }).WithAlias("hb");

            config.AddBranch("ogxbox", ogxbox => {
                ogxbox.SetDescription("Original Xbox compatibility pack installer for HddX:\\Compatibility.");
                ogxbox.AddExample(new[] { "ogxbox", "list" });
                ogxbox.AddExample(new[] { "ogxbox", "install", "hacked", "--usb", "E:" });
                ogxbox.AddExample(new[] { "ogxbox", "install", "hud", "--include-fixer" });
                ogxbox.AddCommand<OriginalXboxCompatibilityListCommand>("list").WithAlias("ls").WithDescription("List the built-in XeFu compatibility sets and partition fixer.");
                ogxbox.AddCommand<OriginalXboxCompatibilityInstallCommand>("install").WithAlias("stage").WithDescription("Stage or install an Original Xbox compatibility set.");
            }).WithAlias("xefu");

            config.AddBranch("fatman", fatman => {
                fatman.SetDescription("Fatman is XeCLI's FATX image and storage manager.");
                fatman.AddExample(new[] { "fatman", "devices" });
                fatman.AddExample(new[] { "fatman", "disks" });
                fatman.AddExample(new[] { "fatman", "partitions", "--image", ".\\hdd.img" });
                fatman.AddExample(new[] { "fatman", "partitions", "--disk", "2" });
                fatman.AddExample(new[] { "fatman", "scan", "--image", ".\\hdd.img" });
                fatman.AddExample(new[] { "fatman", "info", "--image", ".\\hdd.img", "--partition", "Content" });
                fatman.AddExample(new[] { "fatman", "info", "--image", ".\\hdd.img", "--offset", "0xB6600000" });
                fatman.AddExample(new[] { "fatman", "list", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/" });
                fatman.AddExample(new[] { "fatman", "list", "--image", ".\\hdd.img", "--offset", "0xB6600000", "--path", "/" });
                fatman.AddExample(new[] { "fatman", "mkdir", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI" });
                fatman.AddExample(new[] { "fatman", "put", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--in", ".\\readme.txt", "--overwrite" });
                fatman.AddExample(new[] { "fatman", "mv", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--to", "/XeCLI/readme-old.txt" });
                fatman.AddExample(new[] { "fatman", "rm", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI", "--recursive" });
                fatman.AddExample(new[] { "fatman", "format", "--disk", "2", "--auto-confirm" });
                fatman.AddExample(new[] { "fatman", "check", "--image", ".\\hdd.img", "--partition", "Content" });
                fatman.AddExample(new[] { "fatman", "repair", "--image", ".\\hdd.img", "--partition", "Content", "--auto-confirm" });
                fatman.AddExample(new[] { "fatman", "metadata", "backup", "--image", ".\\hdd.img", "--out", ".\\fatman-meta" });
                fatman.AddExample(new[] { "fatman", "extract", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/Content", "--out", ".\\Content" });
                fatman.AddExample(new[] { "fatman", "dump", "--image", ".\\hdd.img", "--offset", "0xB6600000", "--length", "0x10000000", "--out", ".\\partition-dump" });
                fatman.AddExample(new[] { "fatman", "dump", "--image", ".\\hdd.img", "--out", ".\\fatman-dump" });
                fatman.AddCommand<FatmanDevicesCommand>("devices").WithDescription("List host storage devices visible to Fatman.");
                fatman.AddCommand<FatmanDisksCommand>("disks").WithDescription("List Windows physical disks that Fatman can open directly.");
                fatman.AddCommand<FatmanPartitionsCommand>("partitions").WithAlias("parts").WithDescription("Detect partitions inside a raw Xbox 360 HDD image or physical disk.");
                fatman.AddCommand<FatmanScanCommand>("scan").WithAlias("probe").WithDescription("Scan an image or physical disk for plausible FATX/XTAF volume headers.");
                fatman.AddCommand<FatmanInfoCommand>("info").WithDescription("Inspect one detected FATX partition.");
                fatman.AddCommand<FatmanListCommand>("list").WithAlias("ls").WithDescription("List entries inside a FATX partition.");
                fatman.AddCommand<FatmanFindCommand>("find").WithDescription("Search a FATX partition for matching paths.");
                fatman.AddCommand<FatmanGetCommand>("get").WithDescription("Extract a single file from a FATX partition.");
                fatman.AddCommand<FatmanCatCommand>("cat").WithDescription("Print a text file from a FATX partition.");
                fatman.AddCommand<FatmanMkdirCommand>("mkdir").WithDescription("Create a directory inside a FATX image.");
                fatman.AddCommand<FatmanPutCommand>("put").WithAlias("inject").WithDescription("Write a host file into a FATX image.");
                fatman.AddCommand<FatmanMoveCommand>("mv").WithAlias("rename").WithDescription("Move or rename a FATX entry.");
                fatman.AddCommand<FatmanDeleteCommand>("rm").WithAlias("del").WithDescription("Remove a FATX entry from the image.");
                fatman.AddCommand<FatmanFormatCommand>("format").WithAlias("initialize").WithDescription("Format one or more FATX partitions inside an image or physical disk.");
                fatman.AddCommand<FatmanCheckCommand>("check").WithAlias("verify").WithDescription("Scan a FATX partition for orphaned and invalid chain-map state.");
                fatman.AddCommand<FatmanRepairCommand>("repair").WithDescription("Repair safe FATX chain-map issues such as orphaned allocations.");
                fatman.AddCommand<FatmanExtractCommand>("extract").WithDescription("Extract a directory tree from a FATX partition.");
                fatman.AddCommand<FatmanDumpCommand>("dump").WithDescription("Dump one or more raw FATX partitions to host files.");
                fatman.AddBranch("metadata", metadata => {
                    metadata.SetDescription("Back up or restore low-level FATX and partition metadata regions.");
                    metadata.AddCommand<FatmanMetadataBackupCommand>("backup").WithDescription("Back up disk-prefix and partition-header metadata regions.");
                    metadata.AddCommand<FatmanMetadataRestoreCommand>("restore").WithDescription("Restore metadata regions from a prior Fatman metadata backup.");
                }).WithAlias("meta");
            }).WithAlias("fatx");

            config.AddBranch("xbdm", xbdm => {
                xbdm.SetDescription("XBDM commands.");
                xbdm.AddCommand<XbdmInfoCommand>("info").WithDescription("Show XBDM console info.");
                xbdm.AddCommand<XbdmRawCommand>("raw").WithDescription("Send a raw XBDM command.");
                xbdm.AddCommand<XbdmScreenshotCommand>("screenshot").WithDescription("Capture a screenshot via XBDM.");

                xbdm.AddBranch("modules", modules => {
                    modules.SetDescription("Manage loaded modules through XBDM and kernel RPC.");
                    modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
                    modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
                modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
                modules.AddCommand<XbdmModuleLoadCommand>("load").WithDescription("Load a module via kernel RPC.");
                modules.AddCommand<XbdmModuleUnloadCommand>("unload").WithAlias("remove").WithDescription("Unload a module via kernel RPC.");
                modules.AddCommand<XbdmModulesPendingCommand>("pending").WithAlias("verify").WithDescription("Verify a pending module operation after reboot.");
                });

                xbdm.AddBranch("mem", mem => {
                    mem.SetDescription("Inspect, search, and modify live memory.");
                    mem.AddCommand<XbdmMemDumpCommand>("dump").WithDescription("Dump a memory range to a file.");
                    mem.AddCommand<XbdmMemHexDumpCommand>("hexdump").WithDescription("Hex dump a memory range.");
                    mem.AddCommand<XbdmMemRegionsCommand>("regions").WithAlias("map").WithDescription("List memory regions.");
                    mem.AddCommand<XbdmMemPeekCommand>("peek").WithAlias("read").WithDescription("Read a value from memory.");
                    mem.AddCommand<XbdmMemPokeCommand>("poke").WithAlias("write").WithDescription("Write a value to memory.");
                    mem.AddCommand<XbdmMemWatchCommand>("watch").WithDescription("Stream memory changes.");
                    mem.AddCommand<XbdmMemStringsCommand>("strings").WithDescription("Extract strings from memory.");
                    mem.AddCommand<XbdmMemFindCommand>("find").WithAlias("search").WithDescription("Search memory for a pattern.");
                });

                xbdm.AddBranch("xex", xex => {
                    xex.SetDescription("Dump, launch, and analyze XEX images.");
                    xex.AddCommand<XbdmXexDumpCommand>("dump").WithDescription("Dump the active XEX image.");
                    xex.AddCommand<LaunchCommand>("launch").WithAlias("run").WithDescription("Launch a XEX with optional arguments.");
                    xex.AddCommand<XexStringsCommand>("strings").WithDescription("Extract strings from a XEX (local/FTP/running).");
                    xex.AddCommand<GhidraDecompileCommand>("decompile").WithAlias("decode").WithDescription("Decompile a XEX to C via Ghidra.");
                    xex.AddCommand<IdaDecompileCommand>("ida-decompile").WithDescription("Decompile a XEX to C via IDA Pro.");
                });

                xbdm.AddBranch("fs", fs => {
                    fs.SetDescription("Browse and transfer files over XBDM.");
                    fs.AddCommand<XbdmFsListCommand>("list").WithAlias("ls").WithDescription("List files in a directory.");
                    fs.AddCommand<XbdmFsGetCommand>("get").WithDescription("Download a file from the console.");
                    fs.AddCommand<XbdmFsPutCommand>("put").WithDescription("Upload a file to the console.");
                    fs.AddCommand<XbdmFsCatCommand>("cat").WithDescription("Print a file from the console.");
                    fs.AddCommand<XbdmFsDeleteCommand>("rm").WithAlias("del").WithDescription("Delete a file on the console.");
                    fs.AddCommand<XbdmFsMkdirCommand>("mkdir").WithDescription("Create a directory on the console.");
                    fs.AddCommand<XbdmFsMoveCommand>("mv").WithAlias("move").WithDescription("Move or rename a file on the console.");
                });

                xbdm.AddBranch("threads", threads => {
                    threads.SetDescription("Inspect and control threads.");
                    threads.AddCommand<XbdmThreadsListCommand>("list").WithAlias("ls").WithDescription("List threads.");
                    threads.AddCommand<XbdmThreadContextCommand>("context").WithDescription("Read thread registers.");
                    threads.AddCommand<XbdmThreadSuspendCommand>("suspend").WithDescription("Suspend a thread.");
                    threads.AddCommand<XbdmThreadResumeCommand>("resume").WithDescription("Resume a thread.");
                });

                xbdm.AddBranch("debug", debug => {
                    debug.SetDescription("Break, resume, and watch live debug state.");
                    debug.AddCommand<XbdmDebugStopCommand>("stop").WithDescription("Break execution.");
                    debug.AddCommand<XbdmDebugGoCommand>("go").WithDescription("Resume execution.");
                    debug.AddCommand<XbdmDebugWatchCommand>("watch").WithAlias("events").WithDescription("Stream live XBDM debug notifications.");
                    debug.AddBranch("break", brk => {
                        brk.SetDescription("Code breakpoints.");
                        brk.AddCommand<XbdmBreakpointAddCommand>("add").WithDescription("Add a code breakpoint.");
                        brk.AddCommand<XbdmBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a code breakpoint.");
                        brk.AddCommand<XbdmBreakpointClearAllCommand>("clearall").WithAlias("clear").WithDescription("Clear all code and data breakpoints.");
                    });
                    debug.AddBranch("databreak", brk => {
                        brk.SetDescription("Data breakpoints.");
                        brk.AddCommand<XbdmDataBreakpointAddCommand>("add").WithDescription("Add a data breakpoint.");
                        brk.AddCommand<XbdmDataBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a data breakpoint.");
                    });
                });
            });

            config.AddBranch("modules", modules => {
                modules.SetDescription("Shortcut for `rgh xbdm modules`.");
                modules.AddExample(new[] { "modules", "list" });
                modules.AddExample(new[] { "modules", "load", "--path", "Hdd:\\HvP2.xex", "--system", "--reboot-expected" });
                modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
                modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
                modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
                modules.AddCommand<XbdmModuleLoadCommand>("load").WithDescription("Load a module via kernel RPC.");
                modules.AddCommand<XbdmModuleUnloadCommand>("unload").WithAlias("remove").WithDescription("Unload a module via kernel RPC.");
                modules.AddCommand<XbdmModulesPendingCommand>("pending").WithAlias("verify").WithDescription("Verify a pending module operation after reboot.");
            });

            config.AddBranch("module", modules => {
                modules.SetDescription("Alias of `rgh modules` and `rgh xbdm modules`.");
                modules.AddExample(new[] { "module", "info", "--name", "Aurora.xex" });
                modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
                modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
                modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
                modules.AddCommand<XbdmModuleLoadCommand>("load").WithDescription("Load a module via kernel RPC.");
                modules.AddCommand<XbdmModuleUnloadCommand>("unload").WithAlias("remove").WithDescription("Unload a module via kernel RPC.");
                modules.AddCommand<XbdmModulesPendingCommand>("pending").WithAlias("verify").WithDescription("Verify a pending module operation after reboot.");
            });

            config.AddBranch("mem", mem => {
                mem.SetDescription("Shortcut for `rgh xbdm mem`.");
                mem.AddExample(new[] { "mem", "hexdump", "--addr", "0x30000000", "--size", "0x40" });
                mem.AddExample(new[] { "mem", "search", "--addr", "0x30000000", "--size", "0x1000", "--pattern", "DEADBEEF" });
                mem.AddCommand<XbdmMemDumpCommand>("dump").WithDescription("Dump a memory range to a file.");
                mem.AddCommand<XbdmMemHexDumpCommand>("hexdump").WithDescription("Hex dump a memory range.");
                mem.AddCommand<XbdmMemRegionsCommand>("regions").WithAlias("map").WithDescription("List memory regions.");
                mem.AddCommand<XbdmMemPeekCommand>("peek").WithAlias("read").WithDescription("Read a value from memory.");
                mem.AddCommand<XbdmMemPokeCommand>("poke").WithAlias("write").WithDescription("Write a value to memory.");
                mem.AddCommand<XbdmMemWatchCommand>("watch").WithDescription("Stream memory changes.");
                mem.AddCommand<XbdmMemStringsCommand>("strings").WithDescription("Extract strings from memory.");
                mem.AddCommand<XbdmMemFindCommand>("find").WithAlias("search").WithDescription("Search memory for a pattern.");
            });

            config.AddBranch("xex", xex => {
                xex.SetDescription("Shortcut for `rgh xbdm xex`.");
                xex.AddExample(new[] { "xex", "dump", "--out", ".\\title.xex" });
                xex.AddExample(new[] { "xex", "strings", "--running", "--unicode", "--min", "6" });
                xex.AddExample(new[] { "xex", "ida-decompile", "--running", "--out", ".\\ida-decomp", "--max", "10" });
                xex.AddCommand<XbdmXexDumpCommand>("dump").WithDescription("Dump the active XEX image.");
                xex.AddCommand<LaunchCommand>("launch").WithAlias("run").WithDescription("Launch a XEX with optional arguments.");
                xex.AddCommand<XexStringsCommand>("strings").WithDescription("Extract strings from a XEX (local/FTP/running).");
                xex.AddCommand<GhidraDecompileCommand>("decompile").WithAlias("decode").WithDescription("Decompile a XEX to C via Ghidra.");
                xex.AddCommand<IdaDecompileCommand>("ida-decompile").WithDescription("Decompile a XEX to C via IDA Pro.");
            });

            config.AddBranch("fs", fs => {
                fs.SetDescription("Shortcut for `rgh xbdm fs`.");
                fs.AddExample(new[] { "fs", "list", "--path", "Hdd:\\" });
                fs.AddCommand<XbdmFsListCommand>("list").WithAlias("ls").WithDescription("List files in a directory.");
                fs.AddCommand<XbdmFsGetCommand>("get").WithDescription("Download a file from the console.");
                fs.AddCommand<XbdmFsPutCommand>("put").WithDescription("Upload a file to the console.");
                fs.AddCommand<XbdmFsCatCommand>("cat").WithDescription("Print a file from the console.");
                fs.AddCommand<XbdmFsDeleteCommand>("rm").WithAlias("del").WithDescription("Delete a file on the console.");
                fs.AddCommand<XbdmFsMkdirCommand>("mkdir").WithDescription("Create a directory on the console.");
                fs.AddCommand<XbdmFsMoveCommand>("mv").WithAlias("move").WithDescription("Move or rename a file on the console.");
            });

            config.AddBranch("threads", threads => {
                threads.SetDescription("Shortcut for `rgh xbdm threads`.");
                threads.AddExample(new[] { "threads", "list" });
                threads.AddExample(new[] { "threads", "context", "--id", "0xFB000008" });
                threads.AddCommand<XbdmThreadsListCommand>("list").WithAlias("ls").WithDescription("List threads.");
                threads.AddCommand<XbdmThreadContextCommand>("context").WithDescription("Read thread registers.");
                threads.AddCommand<XbdmThreadSuspendCommand>("suspend").WithDescription("Suspend a thread.");
                threads.AddCommand<XbdmThreadResumeCommand>("resume").WithDescription("Resume a thread.");
            });

            config.AddBranch("debug", debug => {
                debug.SetDescription("Shortcut for `rgh xbdm debug`.");
                debug.AddExample(new[] { "debug", "stop" });
                debug.AddExample(new[] { "debug", "watch" });
                debug.AddCommand<XbdmDebugStopCommand>("stop").WithDescription("Break execution.");
                debug.AddCommand<XbdmDebugGoCommand>("go").WithDescription("Resume execution.");
                debug.AddCommand<XbdmDebugWatchCommand>("watch").WithAlias("events").WithDescription("Stream live XBDM debug notifications.");
                debug.AddBranch("break", brk => {
                    brk.SetDescription("Code breakpoints.");
                    brk.AddCommand<XbdmBreakpointAddCommand>("add").WithDescription("Add a code breakpoint.");
                    brk.AddCommand<XbdmBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a code breakpoint.");
                    brk.AddCommand<XbdmBreakpointClearAllCommand>("clearall").WithAlias("clear").WithDescription("Clear all code and data breakpoints.");
                });
                debug.AddBranch("databreak", brk => {
                    brk.SetDescription("Data breakpoints.");
                    brk.AddCommand<XbdmDataBreakpointAddCommand>("add").WithDescription("Add a data breakpoint.");
                    brk.AddCommand<XbdmDataBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a data breakpoint.");
                });
            });

            config.AddBranch("jrpc2", rpc => {
                rpc.SetDescription("JRPC2 (XDRPC-style) commands.");
                rpc.AddExample(new[] { "jrpc2", "temps" });
                rpc.AddExample(new[] { "jrpc2", "notify", "XeCLI connected", "14" });
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
                icons.SetDescription("Browse XNotify icons and manage preset aliases.");
                icons.AddExample(new[] { "notify-icons", "list" });
                icons.AddExample(new[] { "notify-icons", "show", "14" });
                icons.AddExample(new[] { "notify-icons", "add", "--name", "success", "--logo", "14" });
                icons.AddCommand<NotifyIconsListCommand>("list").WithDescription("List built-in XNotify icons and preset aliases.");
                icons.AddCommand<NotifyIconsShowCommand>("show").WithAlias("resolve").WithDescription("Resolve an icon id, built-in name, or preset alias.");
                icons.AddCommand<NotifyIconsAddCommand>("add").WithDescription("Add an icon preset.");
                icons.AddCommand<NotifyIconsRemoveCommand>("remove").WithAlias("del").WithDescription("Remove an icon preset.");
            });

            config.AddBranch("smc", smc => {
                smc.SetDescription("System Management Controller helpers.");
                smc.AddExample(new[] { "smc", "version" });
                smc.AddCommand<SmcVersionCommand>("version").WithAlias("ver").WithDescription("Probe the console SMC version.");
            });

            config.AddBranch("fan", fan => {
                fan.SetDescription("Fan speed helpers.");
                fan.AddExample(new[] { "fan", "set", "--speed", "55", "--channel", "both" });
                fan.AddExample(new[] { "fan", "show" });
                fan.AddCommand<FanSetCommand>("set").WithAlias("speed").WithDescription("Send a manual fan speed command.");
                fan.AddCommand<FanShowCommand>("show").WithAlias("state").WithDescription("Show the last XeCLI-applied manual fan setting.");
            });

            config.AddBranch("led", led => {
                led.SetDescription("Ring-of-light LED helpers.");
                led.AddExample(new[] { "led", "set", "--preset", "quadrant1" });
                led.AddExample(new[] { "led", "set", "--tl", "green", "--tr", "off", "--bl", "off", "--br", "off" });
                led.AddCommand<LedSetCommand>("set").WithAlias("apply").WithDescription("Set the ring-of-light LEDs.");
                led.AddCommand<LedStateCommand>("state").WithAlias("show").WithDescription("Show the last XeCLI-applied ring-light state.");
            });

            config.AddBranch("signin", signin => {
                signin.SetDescription("Signed-in user helpers.");
                signin.AddExample(new[] { "signin", "state" });
                signin.AddCommand<SignInStateCommand>("state").WithAlias("status").WithDescription("Read the active sign-in state, gamertag, and XUID.");
            });

            config.AddBranch("tray", tray => {
                tray.SetDescription("Disc tray helpers.");
                tray.AddExample(new[] { "tray", "open" });
                tray.AddExample(new[] { "tray", "close" });
                tray.AddCommand<TrayOpenCommand>("open").WithDescription("Open the disc tray.");
                tray.AddCommand<TrayCloseCommand>("close").WithDescription("Close the disc tray.");
            });

            config.AddBranch("popup", popup => {
                popup.SetDescription("Trainer-style console popup helpers.");
                popup.AddExample(new[] { "popup", "show", "--title", "XeCLI", "--body", "Connected to console" });
                popup.AddCommand<PopupMessageBoxCommand>("show").WithAlias("open").WithDescription("Show a native Xbox 360 popup.");
            });

            config.AddBranch("spoof", spoof => {
                spoof.SetDescription("Private title-aware spoof helpers. BO2 supports local GT, local XUID, and remote spoofing.");
                spoof.AddExample(new[] { "spoof", "reset", "--current-user", "--clear-remote" });

                spoof.AddBranch("gt", gt => {
                    gt.SetDescription("Spoof the current in-game gamertag for supported titles. BO2 GT spoof is supported.");
                    gt.AddExample(new[] { "spoof", "gt", "show" });
                    gt.AddExample(new[] { "spoof", "gt", "set", "--value", "ExampleTag", "--notify" });
                    gt.AddExample(new[] { "spoof", "gt", "set", "--current-user" });
                    gt.AddExample(new[] { "spoof", "gt", "set", "--value", "ExampleTag" });
                    gt.AddCommand<GamertagSpoofShowCommand>("show").WithAlias("state").WithDescription("Show the current in-game gamertag.");
                    gt.AddCommand<GamertagSpoofSetCommand>("set").WithAlias("apply").WithDescription("Apply a gamertag spoof to the running title.");
                });

                spoof.AddBranch("xuid", xuid => {
                    xuid.SetDescription("Spoof the current in-game XUID for supported titles. BO2 XUID spoof is supported once the title is ready.");
                    xuid.AddExample(new[] { "spoof", "xuid", "show" });
                    xuid.AddExample(new[] { "spoof", "xuid", "set", "--current-user" });
                    xuid.AddExample(new[] { "spoof", "xuid", "set", "--value", "<xuid>" });
                    xuid.AddCommand<XuidSpoofShowCommand>("show").WithAlias("state").WithDescription("Show the current in-game XUID.");
                    xuid.AddCommand<XuidSpoofSetCommand>("set").WithAlias("apply").WithDescription("Apply an XUID spoof to the running title.");
                });

                spoof.AddBranch("remote", remote => {
                    remote.SetDescription("Remote-player text spoofing for supported titles. BO2 remote slots are exposed as 2-12.");
                    remote.AddExample(new[] { "spoof", "remote", "list" });
                    remote.AddExample(new[] { "spoof", "remote", "apply", "--slot", "1", "--text", "XeCLI" });
                    remote.AddExample(new[] { "spoof", "remote", "apply", "--all", "--text", "ExampleRemote", "--notify" });
                    remote.AddExample(new[] { "spoof", "remote", "apply", "--all", "--text", "XeCLI-{slot}" });
                    remote.AddCommand<RemoteSpoofListCommand>("list").WithAlias("show").WithDescription("List spoofable remote slots for the current title.");
                    remote.AddCommand<RemoteSpoofApplyCommand>("apply").WithAlias("set").WithDescription("Overwrite one or more remote slot names.");
                });

                spoof.AddCommand<SpoofResetCommand>("reset").WithAlias("restore").WithDescription("Restore the in-game identity from the signed-in user or explicit values.");
            });

            config.AddBranch("ftp", ftp => {
                ftp.SetDescription("FTP commands (alternate access).");
                ftp.AddExample(new[] { "ftp", "list", "--path", "/Hdd1/" });
                ftp.AddExample(new[] { "ftp", "target", "--set", "<console-ip>", "--user", "<ftp-user>", "--pass", "<ftp-pass>" });
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

            config.AddBranch("save", save => {
                save.SetDescription("Profile and save-data helpers over FTP.");
                save.AddExample(new[] { "save", "list", "--titleid", "FFFE07D1", "--device", "Hdd1" });
                save.AddExample(new[] { "save", "extract", "--titleid", "415608C3", "--out", ".\\saves" });
                save.AddCommand<SaveListCommand>("list").WithAlias("ls").WithDescription("List save files for a title.");
                save.AddCommand<SaveExtractCommand>("extract").WithAlias("pull").WithDescription("Extract save files for a title.");
                save.AddCommand<SaveInjectCommand>("inject").WithAlias("push").WithAlias("put").WithDescription("Upload save files for a title.");
            });

            config.AddBranch("content", content => {
                content.SetDescription("Installed content management over FTP.");
                content.AddExample(new[] { "content", "list", "--device", "Hdd1", "--show-types" });
                content.AddCommand<ContentListCommand>("list").WithAlias("ls").WithDescription("List installed titles and content types.");
                content.AddCommand<ContentDeleteCommand>("delete").WithAlias("rm").WithDescription("Delete installed content for a title.");
            });

            config.AddBranch("avatar", avatar => {
                avatar.SetDescription("Avatar item library and install helpers.");
                avatar.AddExample(new[] { "avatar", "library", "show" });
                avatar.AddExample(new[] { "avatar", "games", "--search", "Black Ops" });
                avatar.AddExample(new[] { "avatar", "items", "--titleid", "415608C3", "--limit", "10" });
                avatar.AddExample(new[] { "avatar", "choose", "--search", "Black Ops" });
                avatar.AddExample(new[] { "avatar", "browse", "--remote" });
                avatar.AddExample(new[] { "avatar", "install", "--contentid", "000000080DF3B242CAE65A52415608C3", "--current-user" });
                avatar.AddExample(new[] { "avatar", "apply", "--titleid", "415608C3", "--all", "--current-user" });

                avatar.AddBranch("library", library => {
                    library.SetDescription("Show or set avatar collection paths.");
                    library.AddCommand<AvatarLibraryShowCommand>("show").WithDescription("Show the current avatar library and cache paths.");
                    library.AddCommand<AvatarLibrarySetCommand>("set").WithDescription("Set the default avatar library and cache paths.");
                });

                avatar.AddCommand<AvatarGamesCommand>("games").WithDescription("List games with available avatar items.");
                avatar.AddCommand<AvatarItemsCommand>("items").WithDescription("List avatar items from the collection.");
                avatar.AddCommand<AvatarChooseCommand>("choose").WithAlias("pick").WithDescription("Interactively choose a game and avatar items in the terminal.");
                avatar.AddCommand<AvatarBrowseCommand>("browse").WithAlias("gui").WithDescription("Browse avatar items in a Windows picker and install selected entries.");
                avatar.AddCommand<AvatarInstallCommand>("install").WithAlias("apply").WithDescription("Patch avatar items for a user and install them to the console.");
            });

            config.AddBranch("plugin", plugin => {
                plugin.SetDescription("DashLaunch plugin management.");
                plugin.AddExample(new[] { "plugin", "list" });
                plugin.AddExample(new[] { "plugin", "enable", "--slot", "5", "--path", "Hdd:\\XDRPC.xex", "--backup" });
                plugin.AddCommand<PluginListCommand>("list").WithAlias("ls").WithDescription("List configured DashLaunch plugins.");
                plugin.AddCommand<PluginEnableCommand>("enable").WithDescription("Set a DashLaunch plugin slot.");
                plugin.AddCommand<PluginDisableCommand>("disable").WithDescription("Clear a DashLaunch plugin slot.");
            });

            config.AddBranch("god", god => {
                god.SetDescription("ISO to Games on Demand conversion.");
                god.AddExample(new[] { "god", "info", ".\\game.iso" });
                god.AddExample(new[] { "god", "watch", ".\\incoming", "--dest", ".\\god", "--once" });
                god.AddCommand<GodInfoCommand>("info").WithDescription("Inspect an ISO and show title metadata.");
                god.AddCommand<GodBuildCommand>("build").WithAlias("make").WithAlias("convert").WithDescription("Convert an ISO into GOD parts.");
                god.AddCommand<GodWatchCommand>("watch").WithAlias("watchdog").WithDescription("Watch a folder and auto-convert ISOs.");
            });

            config.AddBranch("ghidra", ghidra => {
                ghidra.SetDescription("Ghidra headless helpers (Free, external install required).");
                ghidra.AddExample(new[] { "ghidra", "config", "--path", "C:\\Tools\\ghidra", "--java", "C:\\Java" });
                ghidra.AddExample(new[] { "ghidra", "install-loader" });
                ghidra.AddExample(new[] { "ghidra", "decompile", "--running", "--out", ".\\decomp" });
                ghidra.AddCommand<GhidraConfigCommand>("config").WithDescription("Configure Ghidra paths.");
                ghidra.AddCommand<GhidraInstallLoaderCommand>("install-loader").WithDescription("Download or install XEXLoaderWV into the configured Ghidra install.");
                ghidra.AddCommand<GhidraAnalyzeCommand>("analyze").WithDescription("Run headless analysis.");
                ghidra.AddCommand<GhidraDecompileCommand>("decompile").WithDescription("Decompile a module or XEX.");
                ghidra.AddCommand<GhidraVerifyCommand>("verify").WithDescription("Verify decompiler output for bad-instruction placeholders.");
            });

            config.AddBranch("ida", ida => {
                ida.SetDescription("IDA Pro headless helpers (IDA Pro 9.1.250226 required, external install required).");
                ida.AddExample(new[] { "ida", "config", "--path", "C:\\Program Files\\IDA Professional 9.1", "--python", "python" });
                ida.AddExample(new[] { "ida", "install-loader" });
                ida.AddExample(new[] { "ida", "decompile", "--running", "--out", ".\\ida-decomp", "--max", "25" });
                ida.AddCommand<IdaConfigCommand>("config").WithDescription("Configure IDA install, python, and backend paths.");
                ida.AddCommand<IdaCheckCommand>("check").WithAlias("doctor").WithDescription("Verify the configured IDA 9.1 + idaxex 0.42b environment.");
                ida.AddCommand<IdaInstallLoaderCommand>("install-loader").WithDescription("Download or install the supported idaxex 0.42b loader set into IDA.");
                ida.AddCommand<IdaAnalyzeCommand>("analyze").WithDescription("Import a XEX into an IDA database headlessly.");
                ida.AddCommand<IdaDecompileCommand>("decompile").WithDescription("Decompile a XEX or IDA database to C.");
                ida.AddCommand<IdaVerifyCommand>("verify").WithDescription("Verify IDA decompiler output for obvious failures.");
            });
        });

        return await app.RunAsync(args);
    }

    private static Task HandleFirstRunPathPromptAsync(string[] args) {
        if (!ShouldShowPathPrompt(args))
            return Task.CompletedTask;

        if (!OperatingSystem.IsWindows())
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
            "XeCLI can install itself to a standard folder and register the [green]rgh[/] command for terminal use.\n[grey]You can install for the current user or all users from the installer.[/]\n\n[mediumpurple3]Created by Pew - Se7ensins[/]")
            .Header("[bold deepskyblue1]First-Run Setup[/]")
            .BorderColor(Color.Grey);
        AnsiConsole.Write(panel);

        string choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Launch the XeCLI installer now?")
                .AddChoices("Yes", "Not now", "Never ask again"));

        if (choice == "Yes") {
            try {
                ProcessStartInfo psi = new ProcessStartInfo(currentExe) {
                    UseShellExecute = true,
                    WorkingDirectory = exeDir
                };
                psi.ArgumentList.Add("install");
                psi.ArgumentList.Add("--source");
                psi.ArgumentList.Add(exeDir);

                using Process? process = Process.Start(psi);
                int exitCode = 1;
                if (process != null) {
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
                if (exitCode == 0) {
                    config.PathPromptHandled = true;
                    config.Save();
                    AnsiConsole.MarkupLine("[green]Installer completed.[/] Open a new terminal and run `rgh --help`.");
                }
                else {
                    AnsiConsole.MarkupLine($"[red]Installer exited with code {exitCode}.[/]");
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) {
                AnsiConsole.MarkupLine("[yellow]Installer was cancelled at the UAC prompt.[/]");
            }

            return Task.CompletedTask;
        }

        if (choice == "Not now") {
            config.PathPromptHandled = true;
            config.Save();
            AnsiConsole.MarkupLine("[grey]Skipped. You can install later with `rgh install`.[/]");
            return Task.CompletedTask;
        }

        config.PathPromptHandled = true;
        config.Save();
        AnsiConsole.MarkupLine("[grey]First-run installer prompt disabled. You can still install later with `rgh install`.[/]");
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

        if (string.Equals(args[0], "spoof", StringComparison.OrdinalIgnoreCase))
            return NormalizeSpoofArgs(args);

        if (string.Equals(args[0], "title", StringComparison.OrdinalIgnoreCase) &&
            args.Skip(1).All(a => a.StartsWith("-", StringComparison.Ordinal))) {
            return new[] { "title", "active" }.Concat(args.Skip(1)).ToArray();
        }

        return args;
    }

    private static string[] NormalizeSpoofArgs(string[] args) {
        if (args.Length == 1)
            return args;

        string topic = args[1];
        if (Matches(topic, "gamertag", "gt", "name")) {
            return NormalizeSpoofIdentityArgs(args, "gt");
        }

        if (Matches(topic, "xuid")) {
            return NormalizeSpoofIdentityArgs(args, "xuid");
        }

        if (Matches(topic, "remote", "remotes")) {
            return NormalizeSpoofRemoteArgs(args);
        }

        return args;
    }

    private static string[] NormalizeSpoofIdentityArgs(string[] args, string canonicalTopic) {
        if (args.Length == 2)
            return new[] { "spoof", canonicalTopic, "show" };

        string action = args[2];
        if (args.Length == 3 && IsDirectHelpOption(action))
            return args;

        if (action.StartsWith("-", StringComparison.Ordinal)) {
            bool isWrite = args.Skip(2).Any(arg =>
                arg.Equals("--value", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--current-user", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--notify", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--notify-icon", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--notify-logo", StringComparison.OrdinalIgnoreCase));
            return isWrite
                ? new[] { "spoof", canonicalTopic, "set" }.Concat(args.Skip(2)).ToArray()
                : new[] { "spoof", canonicalTopic, "show" }.Concat(args.Skip(2)).ToArray();
        }

        if (Matches(action, "show", "state", "set", "apply"))
            return new[] { "spoof", canonicalTopic }.Concat(args.Skip(2)).ToArray();

        return new[] { "spoof", canonicalTopic, "set", "--value" }.Concat(args.Skip(2)).ToArray();
    }

    private static string[] NormalizeSpoofRemoteArgs(string[] args) {
        if (args.Length == 2)
            return new[] { "spoof", "remote", "list" };

        string action = args[2];
        if (args.Length == 3 && IsDirectHelpOption(action))
            return args;

        if (action.StartsWith("-", StringComparison.Ordinal)) {
            bool isWrite = args.Skip(2).Any(arg =>
                arg.Equals("--slot", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--all", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--text", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--notify", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--notify-icon", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--notify-logo", StringComparison.OrdinalIgnoreCase));
            return isWrite
                ? new[] { "spoof", "remote", "apply" }.Concat(args.Skip(2)).ToArray()
                : new[] { "spoof", "remote", "list" }.Concat(args.Skip(2)).ToArray();
        }

        if (Matches(action, "list", "show", "set", "apply"))
            return new[] { "spoof", "remote" }.Concat(args.Skip(2)).ToArray();

        return new[] { "spoof", "remote", "apply", "--all", "--text" }.Concat(args.Skip(2)).ToArray();
    }

    private static bool Matches(string value, params string[] candidates) {
        return candidates.Any(candidate => value.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHelpToken(string arg) {
        return arg.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectHelpOption(string arg) {
        return IsHelpToken(arg) ||
               arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionRequest(string[] args) {
        if (args.Length != 1)
            return false;

        string arg = args[0];
        return arg.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("version", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetApplicationVersion() {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static bool ShouldShowPathPrompt(string[] args) {
        if (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected)
            return false;

        if (IsVersionRequest(args))
            return false;

        if (args.Any(IsHelpToken) || args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (args.Length > 0 &&
            (args[0].Equals("install", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("homebrew", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("hb", StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }
}
