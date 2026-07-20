using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using XeCli.Localization;
using Xbox360.Remote.Cli;
using Xbox360.Remote.Cli.Commands;
using Xbox360.Remote.Cli.Homebrew;
using Xbox360.Remote.Cli.Logging;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

internal static class Program {
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] lpdwProcessList, uint dwProcessCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;

    [STAThread]
    public static async Task<int> Main(string[] args) {
        args = LocalizedText.ExtractLanguageArgument(args, out string? requestedLanguage);
        bool launchTerminalByDefault = ShouldLaunchTerminalByDefault(args);
        args = NormalizeArgs(args);
        bool helpRequested = IsHelpRequest(args);
        if (helpRequested && Console.IsOutputRedirected)
            AnsiConsole.Profile.Width = 200;
        if (launchTerminalByDefault)
            args = new[] { "terminal" };
        bool commandMissing = args.Length == 0;
        bool terminalGuiRequested = IsTerminalGuiRequest(args);
        if (terminalGuiRequested)
            HideStandaloneConsoleWindow();
        bool completionRequested = IsCompletionRequest(args);
        LocalizedText.Initialize(ResolveLanguageCode(args, requestedLanguage, terminalGuiRequested || completionRequested));
        if (!terminalGuiRequested) {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            LocalizedConsole.Initialize();
        }
        int errorOutputEmitted = 0;
        CultureInfo.CurrentUICulture = LocalizedText.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = LocalizedText.Culture;
        if (IsVersionRequest(args)) {
            AnsiConsole.MarkupLine($"[deepskyblue1]XeCLI[/] [white]{Markup.Escape(GetApplicationVersion())}[/]");
            return CompleteMain(0);
        }
        if (!completionRequested)
            await HandleFirstRunPathPromptAsync(args);
        CommandApp app = new CommandApp();
        app.Configure(config => {
            config.SetApplicationName("rgh");
            config.UseStrictParsing();
            config.Settings.Culture = LocalizedText.Culture;
            config.Settings.Console = AnsiConsole.Console;
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
                try {
                    Exception root = CliErrorReporter.Unwrap(ex);
                    CommandLogInterceptor.RecordFailure(root);
                    string? targetDisplay = CliErrorReporter.TryGetTargetDisplay(args);
                    if (ShouldEmitJsonError(args)) {
                        CliOutput.EmitJsonError(CliErrorReporter.BuildError("Command", root, targetDisplay));
                        Interlocked.Exchange(ref errorOutputEmitted, 1);
                        return;
                    }

                    CliErrorReporter.WriteTextError("Command", root, targetDisplay);
                    Interlocked.Exchange(ref errorOutputEmitted, 1);
                }
                catch {
                    CliErrorReporter.WriteFrameworkExitCodeThree(args);
                }
            });
            if (!helpRequested)
                config.SetInterceptor(new CommandLogInterceptor());
            config.AddExample(new[] { "status" });
            config.AddExample(new[] { "title" });
            config.AddExample(new[] { "report", "--include-modules", "--include-threads", "--since", "2026-05-01T00:00:00Z", "--limit", "5", "--status", "running", "--out", ".\\console-report.md" });
            config.AddExample(new[] { "report", "--report-profile", "operator-review", "--out", ".\\console-report.json" });
            config.AddExample(new[] { "report", "--left", ".\\report-left.json", "--right", ".\\report-right.json", "--format", "json" });
            config.AddExample(new[] { "changelog" });
            config.AddExample(new[] { "changelog", "--version", "v2.0.0" });
            config.AddExample(new[] { "release-notes", "--latest" });
            config.AddExample(new[] { "release", "check", "--publish-dir", ".\\out\\win-x64" });
            config.AddExample(new[] { "log", "list" });
            config.AddExample(new[] { "log", "export", "--format", "csv", "--out", ".\\command-log.csv" });
            config.AddExample(new[] { "script", "run", ".\\batch.txt", "--dry-run" });
            config.AddExample(new[] { "script", "run", ".\\batch.txt", "--transcript-out", ".\\batch-transcript.json" });
            config.AddExample(new[] { "script", "replay", "--dry-run", ".\\batch-transcript.json" });
            config.AddExample(new[] { "health" });
            config.AddExample(new[] { "release-notes" });
            config.AddExample(new[] { "release-notes", "--version", "v2.0.0" });
            config.AddExample(new[] { "diagnostics", "bundle", "--out", ".\\xecli-diagnostics.zip" });
            config.AddExample(new[] { "support", "bundle", "--out", ".\\xecli-support.zip" });
            config.AddExample(new[] { "modules", "list" });
            config.AddExample(new[] { "modules", "address", "--name", "default.xex", "--rva", "0x1234" });
            config.AddExample(new[] { "debug", "break", "add", "--module", "default.xex", "--rva", "0x1234" });
            config.AddExample(new[] { "debug", "databreak", "add", "--module", "default.xex", "--ghidra", "0x82001234", "--ghidra-base", "0x82000000" });
            config.AddExample(new[] { "mem", "hexdump", "--addr", "0x30000000", "--size", "0x40" });
            config.AddExample(new[] { "mem", "session", "create", "--name", "lab", "--capture", "map:baseline=.\\left.json", "--capture", "dump:before=.\\left.bin" });
            config.AddExample(new[] { "notify", "\"XeCLI connected\"", "14" });
            config.AddExample(new[] { "smc", "version" });
            config.AddExample(new[] { "fan", "set", "--speed", "55", "--channel", "both" });
            config.AddExample(new[] { "led", "set", "--preset", "quadrant1" });
            config.AddExample(new[] { "tray", "open" });
            config.AddExample(new[] { "xell", "info" });
            config.AddExample(new[] { "xell", "nand", "dump", "--output", ".\\nand_backup.bin" });
            config.AddExample(new[] { "xell", "kv", "export", "--raw" });
            config.AddExample(new[] { "popup", "show", "--title", "XeCLI", "--body", "\"Connected to console\"", "--preset", "none" });
            config.AddExample(new[] { "avatar", "games", "--search", "\"Black Ops\"" });
            config.AddExample(new[] { "avatar", "install", "--contentid", "000000080DF3B242CAE65A52415608C3", "--current-user" });
            config.AddExample(new[] { "avatar", "browse", "--remote" });
            config.AddExample(new[] { "homebrew", "install", "aurora", "--usb", "E:\\XeCLI-Stage" });
            config.AddExample(new[] { "homebrew", "install", "all", "--usb", "E:\\XeCLI-Stage" });
            config.AddExample(new[] { "ogxbox", "install", "hacked", "--include-fixer", "--usb", "E:\\XeCLI-Stage" });
            config.AddExample(new[] { "xtaf", "disks" });
            config.AddExample(new[] { "xtaf", "partitions", "--image", ".\\hdd.img" });
            config.AddExample(new[] { "xtaf", "partitions", "--disk", "2" });
            config.AddExample(new[] { "xtaf", "scan", "--image", ".\\hdd.img" });
            config.AddExample(new[] { "xtaf", "info", "--image", ".\\hdd.img", "--offset", "0xB6600000" });
            config.AddExample(new[] { "xtaf", "list", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/" });
            config.AddExample(new[] { "xtaf", "list", "--image", ".\\hdd.img", "--offset", "0xB6600000", "--path", "/" });
            config.AddExample(new[] { "xtaf", "mkdir", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI" });
            config.AddExample(new[] { "xtaf", "put", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--in", ".\\readme.txt", "--overwrite" });
            config.AddExample(new[] { "xtaf", "mv", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--to", "/XeCLI/readme-old.txt" });
            config.AddExample(new[] { "xtaf", "rm", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI", "--recursive" });
            config.AddExample(new[] { "xtaf", "format", "--image", ".\\hdd.img", "--partition", "Content", "--dry-run" });
            config.AddExample(new[] { "xtaf", "check", "--image", ".\\hdd.img", "--partition", "Content" });
            config.AddExample(new[] { "xtaf", "repair", "--image", ".\\hdd.img", "--partition", "Content" });
            config.AddExample(new[] { "xtaf", "metadata", "backup", "--image", ".\\hdd.img", "--out", ".\\xtaf-meta" });
            config.AddExample(new[] { "xtaf", "patch", "plan", "--from", ".\\xtaf-meta-a", "--to", ".\\xtaf-meta-b", "--out", ".\\xtaf-patch.json" });
            config.AddExample(new[] { "xtaf", "patch", "apply", "--patch", ".\\xtaf-patch.json", "--image", ".\\hdd.img", "--dry-run" });
            config.AddExample(new[] { "xtaf", "get", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/Content/file.bin", "--out", ".\\file.bin" });
            config.AddExample(new[] { "xtaf", "dump", "--image", ".\\hdd.img", "--out", ".\\xtaf-dump" });
            config.AddExample(new[] { "ghidra", "decompile", "--running", "--out", ".\\decomp" });
            config.AddExample(new[] { "ida", "decompile", "--running", "--out", ".\\ida-decomp", "--max", "25" });
            config.AddExample(new[] { "ida", "export-symbols", "--in", ".\\game.i64", "--out", ".\\symbols.json" });
            config.AddExample(new[] { "xex", "bundle", "--file", ".\\default.xex", "--out", ".\\xex-bundle.zip" });
            config.AddExample(new[] { "xtaf-cli", "status" });
            config.AddExample(new[] { "xtaf-cli", "install" });
            config.AddExample(new[] { "xtaf-cli", "run", "--arg", "--help" });
            config.AddExample(new[] { "mem-bookmarks", "import", "--file", ".\\symbols.json" });

            config.AddCommand<StatusCommand>("status").WithDescription("Show a compact console status snapshot.");
            config.AddCommand<ProfilesCommand>("profiles").WithAlias("users").WithDescription("List profiles and signed-in users.");
            config.AddCommand<TitleLookupCommand>("title").WithAlias("titles").WithDescription("Resolve the active title, look up a raw Title ID, or search an optional user Title Database. MEDIAID prefers a matching row when available.");
            config.AddCommand<ReportCommand>("report").WithDescription("Generate or compare console reports as Markdown, HTML, CSV, or JSON.");
            config.AddBranch("report-profile", reportProfile => {
                reportProfile.SetDescription("Store and select saved report presets.");
                reportProfile.AddExample(new[] { "report-profile", "add", "operator-review", "--format", "json", "--include-modules" });
                reportProfile.AddExample(new[] { "report-profile", "list" });
                reportProfile.AddExample(new[] { "report-profile", "show", "operator-review" });
                reportProfile.AddExample(new[] { "report-profile", "remove", "operator-review" });
                reportProfile.AddCommand<ReportProfileAddCommand>("add").WithDescription("Add or update a saved report profile.");
                reportProfile.AddCommand<ReportProfileListCommand>("list").WithAlias("ls").WithDescription("List saved report profiles.");
                reportProfile.AddCommand<ReportProfileShowCommand>("show").WithAlias("get").WithDescription("Show one saved report profile.");
                reportProfile.AddCommand<ReportProfileRemoveCommand>("remove").WithAlias("rm").WithDescription("Remove a saved report profile.");
            });
            config.AddCommand<HealthCommand>("health").WithDescription("Run offline local health checks without connecting to a console.");
            config.AddBranch("script", script => {
                script.SetDescription("Run command files or review structured transcripts offline.");
                script.AddExample(new[] { "script", "run", ".\\batch.txt", "--dry-run" });
                script.AddExample(new[] { "script", "run", ".\\batch.txt", "--transcript-out", ".\\batch-transcript.json" });
                script.AddExample(new[] { "script", "replay", ".\\batch-transcript.json", "--dry-run" });
                script.AddExample(new[] { "batch", "run", ".\\batch.txt", "--allow-live" });
                script.AddCommand<BatchRunCommand>("run").WithDescription("Run newline-delimited local CLI commands from a file.");
                script.AddCommand<BatchReplayCommand>("replay").WithDescription("Replay a structured batch transcript offline for review without executing commands.");
            }).WithAlias("batch");
            config.AddBranch("mem-bookmarks", bookmarks => {
                bookmarks.SetDescription("Manage offline memory address bookmarks.");
                bookmarks.AddCommand<MemoryBookmarkAddCommand>("add").WithDescription("Add a memory bookmark.");
                bookmarks.AddCommand<MemoryBookmarkRenameCommand>("rename").WithAlias("mv").WithDescription("Rename a memory bookmark.");
                bookmarks.AddCommand<MemoryBookmarkListCommand>("list").WithAlias("ls").WithDescription("List memory bookmarks.");
                bookmarks.AddCommand<MemoryBookmarkShowCommand>("show").WithAlias("jump").WithDescription("Show a stored memory bookmark.");
                bookmarks.AddCommand<MemoryBookmarkExportCommand>("export").WithDescription("Export memory bookmarks to a JSON file.");
                bookmarks.AddCommand<MemoryBookmarkImportCommand>("import").WithDescription("Import memory bookmarks from a symbol sidecar.");
                bookmarks.AddCommand<MemoryBookmarkRemoveCommand>("remove").WithAlias("rm").WithAlias("del").WithDescription("Remove a memory bookmark.");
            }).WithAlias("bookmarks").WithAlias("memmarks");
            config.AddBranch("log", log => {
                log.SetDescription("Inspect the local append-only command log cache.");
                log.AddExample(new[] { "log", "list" });
                log.AddExample(new[] { "log", "list", "--limit", "1", "--json" });
                log.AddExample(new[] { "log", "export", "--format", "csv", "--out", ".\\command-log.csv" });
                log.AddCommand<LogListCommand>("list").WithDescription("List captured command log entries.");
                log.AddCommand<LogExportCommand>("export").WithDescription("Export captured command log entries.");
            });
            config.AddBranch("diagnostics", diagnostics => {
                diagnostics.SetDescription("Collect local support diagnostics without connecting to a console.");
                diagnostics.AddExample(new[] { "diagnostics", "bundle" });
                diagnostics.AddExample(new[] { "diagnostics", "bundle", "--folder", "--out", ".\\xecli-diagnostics" });
                diagnostics.AddCommand<DiagnosticsBundleCommand>("bundle").WithDescription("Create a redacted local diagnostics bundle.");
            }).WithAlias("diag");
            config.AddBranch("support", support => {
                support.SetDescription("Collect a redacted support bundle without connecting to a console.");
                support.AddExample(new[] { "support", "bundle" });
                support.AddExample(new[] { "support", "bundle", "--folder", "--out", ".\\xecli-support" });
                support.AddCommand<SupportBundleCommand>("bundle").WithDescription("Create a curated redacted support bundle.");
            });
            config.AddCommand<ReleaseNotesCommand>("release-notes").WithAlias("changelog").WithDescription("Show the built-in release catalog without network access.");
            config.AddCommand<CompletionCommand>("completion").WithDescription("Emit shell completion scripts to stdout.");
            config.AddBranch("release", release => {
                release.SetDescription("Validate local release artifacts before publish.");
                release.AddExample(new[] { "release", "check", "--publish-dir", ".\\out\\win-x64" });
                release.AddExample(new[] { "release", "check", "--publish-dir", ".\\out\\win-x64", "--zip", ".\\out\\release\\XeCLI-2.0.0-win-x64.zip" });
                release.AddCommand<ReleaseCheckCommand>("check").WithDescription("Validate a publish directory, release zip, and checksum metadata locally.");
            });
            config.AddBranch("trainer", trainer => {
                trainer.SetDescription("Apply trainer JSON memory writes.");
                trainer.AddExample(new[] { "trainer", "apply", "--file", ".\\trainer.json", "--dry-run" });
                trainer.AddExample(new[] { "trainer", "apply", "--file", ".\\trainer.json" });
                trainer.AddCommand<TrainerApplyCommand>("apply").WithAlias("run").WithDescription("Apply a trainer JSON file to live memory.");
            });
            config.AddCommand<TargetCommand>("target").WithDescription("Show or set the default console target.");
            config.AddCommand<LanguageCommand>("language").WithDescription("Show or change the saved UI language.");
            config.AddCommand<PingCommand>("ping").WithDescription("Probe XBDM reachability on the current console.");
            config.AddCommand<RebootCommand>("reboot").WithAlias("restart").WithDescription("Reboot the console (cold by default).");
            config.AddCommand<ShutdownCommand>("shutdown").WithAlias("poweroff").WithDescription("Power off the console.");
            config.AddCommand<LaunchCommand>("launch").WithAlias("run").WithDescription("Launch a XEX with optional arguments.");
            config.AddCommand<InstallCommand>("install").WithDescription("Install a verified portable release or manage command registration.");
            config.AddCommand<StartCommand>("start").WithAlias("s").WithDescription("Discover consoles and set the default target.");
            config.AddCommand<ConnectCommand>("connect").WithAlias("c").WithDescription("Set or select the default target.");
            config.AddCommand<ScanCommand>("scan").WithAlias("discover").WithDescription("Scan the network for consoles.");
            config.AddCommand<XbdmScreenshotCommand>("screenshot").WithAlias("shot").WithDescription("Capture a live screenshot.");
            config.AddBranch("network", network => {
                network.SetDescription("Inspect target, profile, FTP configuration, and recent console history without connecting.");
                network.AddExample(new[] { "network", "doctor" });
                network.AddExample(new[] { "network", "recent" });
                network.AddCommand<NetworkDoctorCommand>("doctor").WithDescription("Summarize target, profile, and FTP settings without probing the network.");
                network.AddCommand<NetworkRecentCommand>("recent").WithDescription("List recently seen console endpoints from local history.");
            });
            config.AddCommand<TerminalCommand>("terminal").WithAlias("shell").WithDescription("Open the XeTerminal desktop interface.");
            config.AddBranch("homebrew", homebrew => {
                homebrew.SetDescription("Download public homebrew packages to USB, a folder, or a detected console drive.");
                homebrew.AddExample(new[] { "homebrew", "list" });
                homebrew.AddExample(new[] { "homebrew", "list", "--csv" });
                homebrew.AddExample(new[] { "homebrew", "install", "aurora", "--usb", "E:\\XeCLI-Stage" });
                homebrew.AddExample(new[] { "homebrew", "install", "all", "--usb", "E:\\XeCLI-Stage" });
                homebrew.AddExample(new[] { "homebrew", "install", "aurora", "--device", "Hdd1", "--ini-mode", "merge" });
                homebrew.AddCommand<HomebrewListCommand>("list").WithAlias("ls").WithDescription("List the built-in package catalog.");
                homebrew.AddCommand<HomebrewInstallCommand>("install").WithAlias("stage").WithDescription("Download one or more public homebrew packages to USB, a folder, or the console.");
            }).WithAlias("hb");

            config.AddBranch("ogxbox", ogxbox => {
                ogxbox.SetDescription("Original Xbox compatibility pack installer for HddX:\\Compatibility.");
                ogxbox.AddExample(new[] { "ogxbox", "list" });
                ogxbox.AddExample(new[] { "ogxbox", "install", "hacked", "--usb", "E:\\XeCLI-Stage" });
                ogxbox.AddExample(new[] { "ogxbox", "install", "hud", "--include-fixer" });
                ogxbox.AddCommand<OriginalXboxCompatibilityListCommand>("list").WithAlias("ls").WithDescription("List the built-in XeFu compatibility sets and partition fixer.");
                ogxbox.AddCommand<OriginalXboxCompatibilityInstallCommand>("install").WithAlias("stage").WithDescription("Stage or install an Original Xbox compatibility set.");
            }).WithAlias("xefu");

            config.AddBranch("xtaf", fatman => {
                fatman.SetDescription("XTAF is XeCLI's FATX image and physical-disk manager (aliases: fatman, fatx).");
                fatman.AddExample(new[] { "xtaf", "devices" });
                fatman.AddExample(new[] { "xtaf", "disks" });
                fatman.AddExample(new[] { "xtaf", "partitions", "--image", ".\\hdd.img" });
                fatman.AddExample(new[] { "xtaf", "partitions", "--disk", "2" });
                fatman.AddExample(new[] { "xtaf", "scan", "--image", ".\\hdd.img" });
                fatman.AddExample(new[] { "xtaf", "info", "--image", ".\\hdd.img", "--partition", "Content" });
                fatman.AddExample(new[] { "xtaf", "info", "--image", ".\\hdd.img", "--offset", "0xB6600000" });
                fatman.AddExample(new[] { "xtaf", "list", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/" });
                fatman.AddExample(new[] { "xtaf", "list", "--image", ".\\hdd.img", "--offset", "0xB6600000", "--path", "/" });
                fatman.AddExample(new[] { "xtaf", "mkdir", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI" });
                fatman.AddExample(new[] { "xtaf", "put", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--in", ".\\readme.txt", "--overwrite" });
                fatman.AddExample(new[] { "xtaf", "mv", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--to", "/XeCLI/readme-old.txt" });
                fatman.AddExample(new[] { "xtaf", "rm", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI", "--recursive" });
                fatman.AddExample(new[] { "xtaf", "format", "--disk", "2", "--dry-run" });
                fatman.AddExample(new[] { "xtaf", "check", "--image", ".\\hdd.img", "--partition", "Content" });
                fatman.AddExample(new[] { "xtaf", "repair", "--image", ".\\hdd.img", "--partition", "Content" });
                fatman.AddExample(new[] { "xtaf", "metadata", "backup", "--image", ".\\hdd.img", "--out", ".\\xtaf-meta" });
                fatman.AddExample(new[] { "xtaf", "metadata", "restore", "--manifest", ".\\xtaf-meta\\fatman-metadata.json", "--image", ".\\hdd.img", "--dry-run" });
                fatman.AddExample(new[] { "xtaf", "patch", "plan", "--from", ".\\xtaf-meta-a", "--to", ".\\xtaf-meta-b", "--out", ".\\xtaf-patch.json" });
                fatman.AddExample(new[] { "xtaf", "patch", "apply", "--patch", ".\\xtaf-patch.json", "--image", ".\\hdd.img", "--dry-run" });
                fatman.AddExample(new[] { "xtaf", "extract", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/Content", "--out", ".\\Content" });
                fatman.AddExample(new[] { "xtaf", "dump", "--image", ".\\hdd.img", "--offset", "0xB6600000", "--length", "0x10000000", "--out", ".\\partition-dump" });
                fatman.AddExample(new[] { "xtaf", "dump", "--image", ".\\hdd.img", "--out", ".\\xtaf-dump" });
                fatman.AddCommand<FatmanDevicesCommand>("devices").WithDescription("List host storage devices visible to XTAF.");
                fatman.AddCommand<FatmanDisksCommand>("disks").WithDescription("List Windows physical disks that XTAF can open directly.");
                fatman.AddCommand<FatmanPartitionsCommand>("partitions").WithAlias("parts").WithDescription("Detect partitions inside a raw Xbox 360 HDD image or physical disk.");
                fatman.AddCommand<FatmanScanCommand>("scan").WithAlias("probe").WithDescription("Scan an image or physical disk for plausible FATX/XTAF volume headers.");
                fatman.AddCommand<FatmanInfoCommand>("info").WithDescription("Inspect one detected FATX partition.");
                fatman.AddCommand<FatmanListCommand>("list").WithAlias("ls").WithDescription("List entries inside a FATX partition.");
                fatman.AddCommand<FatmanFindCommand>("find").WithDescription("Search a FATX partition for matching paths.");
                fatman.AddCommand<FatmanGetCommand>("get").WithDescription("Extract a single file from a FATX partition.");
                fatman.AddCommand<FatmanCatCommand>("cat").WithDescription("Print a text file from a FATX partition.");
                fatman.AddCommand<FatmanMkdirCommand>("mkdir").WithDescription("Create a directory in an image or writable physical disk.");
                fatman.AddCommand<FatmanPutCommand>("put").WithAlias("inject").WithDescription("Write a host file to an image or writable physical disk.");
                fatman.AddCommand<FatmanMoveCommand>("mv").WithAlias("rename").WithDescription("Move or rename a FATX entry.");
                fatman.AddCommand<FatmanDeleteCommand>("rm").WithAlias("del").WithDescription("Remove a FATX entry from an image or writable physical disk.");
                fatman.AddCommand<FatmanFormatCommand>("format").WithAlias("initialize").WithDescription("Format one or more FATX partitions inside an image or physical disk.");
                fatman.AddCommand<FatmanCheckCommand>("check").WithAlias("verify").WithDescription("Scan a FATX partition for orphaned and invalid chain-map state.");
                fatman.AddCommand<FatmanRepairCommand>("repair").WithDescription("Repair safe FATX chain-map issues such as orphaned allocations.");
                fatman.AddCommand<FatmanExtractCommand>("extract").WithDescription("Extract a directory tree from a FATX partition.");
                fatman.AddCommand<FatmanDumpCommand>("dump").WithDescription("Dump one or more raw FATX partitions to host files.");
                fatman.AddBranch("patch", patch => {
                    patch.SetDescription("Plan and apply FATX metadata patches between metadata backup directories.");
                    patch.AddCommand<FatmanMetadataPatchPlanCommand>("plan").WithDescription("Compare two metadata backup directories and write a patch manifest.");
                    patch.AddCommand<FatmanMetadataPatchApplyCommand>("apply").WithDescription("Apply a metadata patch manifest to an image or physical disk.");
                });
                fatman.AddBranch("metadata", metadata => {
                    metadata.SetDescription("Back up or restore low-level FATX and partition metadata regions.");
                    metadata.AddCommand<FatmanMetadataBackupCommand>("backup").WithDescription("Back up disk-prefix and partition-header metadata regions.");
                    metadata.AddCommand<FatmanMetadataRestoreCommand>("restore").WithDescription("Restore metadata regions from a prior XTAF metadata backup.");
                }).WithAlias("meta");
            }).WithAlias("fatman").WithAlias("fatx");

            config.AddBranch("xtaf-cli", xtafCli => {
                xtafCli.SetDescription("Optional SaveEditors Xtaf-CLI wrapper for mount, recovery, trim, and volume workflows.");
                xtafCli.AddExample(new[] { "xtaf-cli", "status" });
                xtafCli.AddExample(new[] { "xtaf-cli", "config", "--path", ".\\tools\\Xtaf-CLI" });
                xtafCli.AddExample(new[] { "xtaf-cli", "install" });
                xtafCli.AddExample(new[] { "xtaf-cli", "run", "--arg", "--help" });
                xtafCli.AddCommand<XtafCliConfigCommand>("config").WithDescription("Store or display the configured Xtaf-CLI path.");
                xtafCli.AddCommand<XtafCliStatusCommand>("status").WithDescription("Show whether Xtaf-CLI is configured and ready.");
                xtafCli.AddCommand<XtafCliInstallCommand>("install").WithDescription($"Download and install pinned SaveEditors Xtaf-CLI {XtafCliSupportConstants.Version}.");
                xtafCli.AddCommand<XtafCliRunCommand>("run").WithDescription("Run the configured Xtaf-CLI executable with raw pass-through arguments.");
            });

            config.AddBranch("xbdm", xbdm => {
                xbdm.SetDescription("XBDM commands.");
                xbdm.AddCommand<XbdmInfoCommand>("info").WithDescription("Show XBDM console info.");
                xbdm.AddCommand<XbdmRawCommand>("raw").WithDescription("Send a raw XBDM command.");
                xbdm.AddCommand<XbdmScreenshotCommand>("screenshot").WithDescription("Capture a screenshot via XBDM.");

                xbdm.AddBranch("modules", modules => {
                    modules.SetDescription("Manage loaded modules through XBDM and kernel RPC.");
                    modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
                    modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
                    modules.AddCommand<XbdmModuleAddressCommand>("address").WithAlias("addr").WithAlias("resolve").WithDescription("Translate module RVAs or Ghidra addresses to live runtime addresses.");
                modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
                modules.AddCommand<XbdmModuleLoadCommand>("load").WithDescription("Load a module via kernel RPC.");
                modules.AddCommand<XbdmModuleUnloadCommand>("unload").WithAlias("remove").WithDescription("Unload a module via kernel RPC.");
                    modules.AddCommand<XbdmModulesPendingCommand>("pending").WithAlias("verify").WithDescription("Verify a pending module operation after reboot.");
                }).WithAlias("module");

                xbdm.AddBranch("mem", mem => {
                    mem.SetDescription("Inspect, search, and modify live memory.");
                    mem.AddExample(new[] { "xbdm", "mem", "diff", "--left", ".\\left.json", "--right", ".\\right.json" });
                    mem.AddExample(new[] { "xbdm", "mem", "dump-diff", "--left", ".\\left.bin", "--right", ".\\right.bin" });
                    mem.AddExample(new[] { "xbdm", "mem", "session", "list" });
                    mem.AddCommand<XbdmMemDumpCommand>("dump").WithDescription("Dump a memory range to a file.");
                    mem.AddCommand<XbdmMemHexDumpCommand>("hexdump").WithDescription("Hex dump a memory range.");
                    mem.AddCommand<XbdmMemRegionsCommand>("regions").WithDescription("List live memory regions.");
                    mem.AddCommand<XbdmMemMapCommand>("map").WithDescription("Render the live memory map.");
                    mem.AddCommand<MemoryMapDiffCommand>("diff").WithAlias("compare").WithDescription("Compare two saved memory map JSON captures.");
                    mem.AddCommand<MemoryDumpDiffCommand>("dump-diff").WithAlias("dump-compare").WithDescription("Compare two saved memory dump files.");
                    mem.AddBranch("session", session => {
                        session.SetDescription("Manage offline memory capture sessions.");
                        session.AddExample(new[] { "xbdm", "mem", "session", "create", "--name", "lab", "--capture", "map:baseline=.\\left.json" });
                        session.AddExample(new[] { "xbdm", "mem", "session", "show", "--name", "lab" });
                        session.AddCommand<MemorySessionCreateCommand>("create").WithDescription("Create a named memory session manifest.");
                        session.AddCommand<MemorySessionListCommand>("list").WithAlias("ls").WithDescription("List saved memory sessions.");
                        session.AddCommand<MemorySessionShowCommand>("show").WithAlias("jump").WithDescription("Show a saved memory session manifest.");
                        session.AddCommand<MemorySessionRemoveCommand>("remove").WithAlias("rm").WithAlias("del").WithDescription("Remove a saved memory session manifest.");
                    });
                    mem.AddCommand<XbdmMemPeekCommand>("peek").WithAlias("read").WithDescription("Read a value from memory.");
                    mem.AddCommand<XbdmMemPokeCommand>("poke").WithAlias("write").WithDescription("Write a value to memory.");
                    mem.AddCommand<XbdmMemWatchCommand>("watch").WithDescription("Stream memory changes.");
                    mem.AddCommand<XbdmMemStringsCommand>("strings").WithDescription("Extract strings from memory.");
                    mem.AddCommand<XbdmMemFindCommand>("find").WithAlias("search").WithDescription("Search memory for bytes, ASCII, or a typed value.");
                });

                xbdm.AddBranch("xex", xex => {
                    xex.SetDescription("Dump, launch, and analyze XEX images.");
                    xex.AddExample(new[] { "xex", "info", "--file", ".\\default.xex" });
                    xex.AddExample(new[] { "xex", "map", "--file", ".\\default.xex", "--ghidra-base", "0x82000000", "--ghidra", "0x82001234" });
                    xex.AddCommand<XbdmXexDumpCommand>("dump").WithDescription("Dump the active XEX image.");
                    xex.AddCommand<LaunchCommand>("launch").WithAlias("run").WithDescription("Launch a XEX with optional arguments.");
                    xex.AddCommand<XexStringsCommand>("strings").WithDescription("Extract strings from a local binary/XEX, FTP path, or running title.");
                    xex.AddCommand<XexInfoCommand>("info").WithAlias("header").WithDescription("Inspect a local XEX header.");
                    xex.AddCommand<XexMapCommand>("map").WithDescription("Map a Ghidra address to a local XEX address.");
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
                    debug.AddExample(new[] { "xbdm", "debug", "address-check", "--module", "default.xex", "--rva", "0x1234" });
                    debug.AddExample(new[] { "xbdm", "debug", "break", "add", "--module", "default.xex", "--rva", "0x1234" });
                    debug.AddExample(new[] { "xbdm", "debug", "databreak", "add", "--module", "default.xex", "--ghidra", "0x82001234", "--ghidra-base", "0x82000000" });
                    debug.AddCommand<XbdmDebugAddressCheckCommand>("address-check").WithAlias("addr").WithAlias("resolve").WithDescription("Resolve and verify a live debug address.");
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
                modules.AddExample(new[] { "modules", "address", "--name", "default.xex", "--ghidra", "0x82001234", "--ghidra-base", "0x82000000" });
                modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
                modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
                modules.AddCommand<XbdmModuleAddressCommand>("address").WithAlias("addr").WithAlias("resolve").WithDescription("Translate module RVAs or Ghidra addresses to live runtime addresses.");
                modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
                modules.AddCommand<XbdmModuleLoadCommand>("load").WithDescription("Load a module via kernel RPC.");
                modules.AddCommand<XbdmModuleUnloadCommand>("unload").WithAlias("remove").WithDescription("Unload a module via kernel RPC.");
                modules.AddCommand<XbdmModulesPendingCommand>("pending").WithAlias("verify").WithDescription("Verify a pending module operation after reboot.");
            });

            config.AddBranch("module", modules => {
                modules.SetDescription("Alias of `rgh modules` and `rgh xbdm modules`.");
                modules.AddExample(new[] { "module", "info", "--name", "Aurora.xex" });
                modules.AddExample(new[] { "module", "address", "--name", "Aurora.xex", "--rva", "0x1234" });
                modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
                modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
                modules.AddCommand<XbdmModuleAddressCommand>("address").WithAlias("addr").WithAlias("resolve").WithDescription("Translate module RVAs or Ghidra addresses to live runtime addresses.");
                modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
                modules.AddCommand<XbdmModuleLoadCommand>("load").WithDescription("Load a module via kernel RPC.");
                modules.AddCommand<XbdmModuleUnloadCommand>("unload").WithAlias("remove").WithDescription("Unload a module via kernel RPC.");
                modules.AddCommand<XbdmModulesPendingCommand>("pending").WithAlias("verify").WithDescription("Verify a pending module operation after reboot.");
            });

            config.AddBranch("mem", mem => {
                mem.SetDescription("Shortcut for `rgh xbdm mem`.");
                mem.AddExample(new[] { "mem", "hexdump", "--addr", "0x30000000", "--size", "0x40" });
                mem.AddExample(new[] { "mem", "search", "--addr", "0x30000000", "--size", "0x1000", "--pattern", "DEADBEEF" });
                mem.AddExample(new[] { "mem", "diff", "--left", ".\\left.json", "--right", ".\\right.json" });
                mem.AddExample(new[] { "mem", "dump-diff", "--left", ".\\left.bin", "--right", ".\\right.bin" });
                mem.AddExample(new[] { "mem", "session", "show", "--name", "lab" });
                mem.AddCommand<XbdmMemDumpCommand>("dump").WithDescription("Dump a memory range to a file.");
                mem.AddCommand<XbdmMemHexDumpCommand>("hexdump").WithDescription("Hex dump a memory range.");
                mem.AddCommand<XbdmMemRegionsCommand>("regions").WithDescription("List live memory regions.");
                mem.AddCommand<XbdmMemMapCommand>("map").WithDescription("Render the live memory map.");
                mem.AddCommand<MemoryMapDiffCommand>("diff").WithAlias("compare").WithDescription("Compare two saved memory map JSON captures.");
                mem.AddCommand<MemoryDumpDiffCommand>("dump-diff").WithAlias("dump-compare").WithDescription("Compare two saved memory dump files.");
                mem.AddBranch("session", session => {
                    session.SetDescription("Manage offline memory capture sessions.");
                    session.AddExample(new[] { "mem", "session", "create", "--name", "lab", "--capture", "map:baseline=.\\left.json", "--capture", "dump:before=.\\left.bin" });
                    session.AddExample(new[] { "mem", "session", "show", "--name", "lab" });
                    session.AddCommand<MemorySessionCreateCommand>("create").WithDescription("Create a named memory session manifest.");
                    session.AddCommand<MemorySessionListCommand>("list").WithAlias("ls").WithDescription("List saved memory sessions.");
                    session.AddCommand<MemorySessionShowCommand>("show").WithAlias("jump").WithDescription("Show a saved memory session manifest.");
                    session.AddCommand<MemorySessionRemoveCommand>("remove").WithAlias("rm").WithAlias("del").WithDescription("Remove a saved memory session manifest.");
                });
                mem.AddCommand<XbdmMemPeekCommand>("peek").WithAlias("read").WithDescription("Read a value from memory.");
                mem.AddCommand<XbdmMemPokeCommand>("poke").WithAlias("write").WithDescription("Write a value to memory.");
                mem.AddCommand<XbdmMemWatchCommand>("watch").WithDescription("Stream memory changes.");
                mem.AddCommand<XbdmMemStringsCommand>("strings").WithDescription("Extract strings from memory.");
                mem.AddCommand<XbdmMemFindCommand>("find").WithAlias("search").WithDescription("Search memory for bytes, ASCII, or a typed value.");
            });

            config.AddBranch("xex", xex => {
                xex.SetDescription("Shortcut for `rgh xbdm xex`.");
                xex.AddExample(new[] { "xex", "dump", "--out", ".\\title.xex" });
                xex.AddExample(new[] { "xex", "strings", "--file", ".\\title.xex", "--unicode", "--min-length", "6", "--limit", "50" });
                xex.AddExample(new[] { "xex", "info", "--file", ".\\default.xex" });
                xex.AddExample(new[] { "xex", "map", "--file", ".\\default.xex", "--ghidra-base", "0x82000000", "--ghidra", "0x82001234" });
                xex.AddExample(new[] { "xex", "bundle", "--file", ".\\default.xex", "--out", ".\\xex-bundle.zip" });
                xex.AddExample(new[] { "xex", "ida-decompile", "--running", "--out", ".\\ida-decomp", "--max", "10" });
                xex.AddCommand<XbdmXexDumpCommand>("dump").WithDescription("Dump the active XEX image.");
                xex.AddCommand<LaunchCommand>("launch").WithAlias("run").WithDescription("Launch a XEX with optional arguments.");
                xex.AddCommand<XexStringsCommand>("strings").WithDescription("Extract strings from a local binary/XEX, FTP path, or running title.");
                xex.AddCommand<XexInfoCommand>("info").WithAlias("header").WithDescription("Inspect a local XEX header.");
                xex.AddCommand<XexMapCommand>("map").WithDescription("Map a Ghidra address to a local XEX address.");
                xex.AddCommand<XexAnalysisBundleCommand>("bundle").WithDescription("Export a local XEX analysis bundle for offline round-trip use.");
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
                debug.AddExample(new[] { "debug", "watch", "--duration", "5" });
                debug.AddExample(new[] { "debug", "address-check", "--module", "default.xex", "--rva", "0x1234" });
                debug.AddExample(new[] { "debug", "break", "add", "--module", "default.xex", "--rva", "0x1234" });
                debug.AddExample(new[] { "debug", "databreak", "add", "--module", "default.xex", "--ghidra", "0x82001234", "--ghidra-base", "0x82000000" });
                debug.AddCommand<XbdmDebugAddressCheckCommand>("address-check").WithAlias("addr").WithAlias("resolve").WithDescription("Resolve and verify a live debug address.");
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
                rpc.SetDescription("JRPC2 commands.");
                rpc.AddExample(new[] { "jrpc2", "temps" });
                rpc.AddExample(new[] { "jrpc2", "notify", "\"XeCLI connected\"", "14" });
                rpc.AddCommand<Jrpc2CpuKeyCommand>("cpu-key").WithDescription("Read the CPU key.");
                rpc.AddCommand<Jrpc2TempsCommand>("temps").WithDescription("Read temperature sensors.");
                rpc.AddCommand<Jrpc2TitleIdCommand>("title-id").WithDescription("Read the current Title ID.");
                rpc.AddCommand<Jrpc2DashboardCommand>("dashboard").WithDescription("Read the dashboard version.");
                rpc.AddCommand<Jrpc2MotherboardCommand>("motherboard").WithDescription("Read motherboard type.");
                rpc.AddCommand<Jrpc2ResolveCommand>("resolve").WithDescription("Resolve a function by module/ordinal.");
                rpc.AddCommand<Jrpc2NotifyCommand>("notify").WithDescription("Send a notification via JRPC2.");
                rpc.AddCommand<Jrpc2CallCommand>("call").WithDescription("Call a function with RPC.");
            }).WithAlias("jrpc");

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
                smc.AddExample(new[] { "smc", "temps" });
                smc.AddCommand<SmcVersionCommand>("version").WithAlias("ver").WithDescription("Probe the console SMC version.");
                smc.AddCommand<SmcTemperaturesCommand>("temps").WithAlias("temperature").WithDescription("Read SMC temperature sensors with fixed-point precision.");
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

            config.AddBranch("xell", xell => {
                xell.SetDescription("XeLL Reloaded helpers with guided auto-launch when needed.");
                xell.AddExample(new[] { "xell", "boot" });
                xell.AddExample(new[] { "xell", "boot", "--quickboot" });
                xell.AddExample(new[] { "xell", "boot", "--quickboot", "--quickboot-target", "flash" });
                xell.AddExample(new[] { "xell", "info" });
                xell.AddExample(new[] { "xell", "kv", "export" });
                xell.AddExample(new[] { "xell", "kv", "export", "--raw" });
                xell.AddExample(new[] { "xell", "kv", "export", "--single" });
                xell.AddExample(new[] { "xell", "nand", "dump" });
                xell.AddExample(new[] { "xell", "nand", "dump", "--single" });
                xell.AddExample(new[] { "xell", "nand", "dump", "--output", ".\\nand_backup.bin" });
                xell.AddCommand<XellBootCommand>("boot").WithDescription("Launch XeLL Reloaded, or confirm that XeLL is already running.");
                xell.AddCommand<XellInfoCommand>("info").WithDescription("Inspect the available XeLL HTTP services, endpoints, and detected CPU key.");
                xell.AddBranch("kv", kv => {
                    kv.SetDescription("XeLL keyvault export helpers with optional repeated verification.");
                    kv.AddExample(new[] { "xell", "kv", "export" });
                    kv.AddExample(new[] { "xell", "kv", "export", "--output", "kv_backup.bin" });
                    kv.AddExample(new[] { "xell", "kv", "export", "--raw" });
                    kv.AddExample(new[] { "xell", "kv", "export", "--single" });
                    kv.AddCommand<XellKvExportCommand>("export").WithAlias("dump").WithDescription("Export the keyvault, CPU key text, and a packaged verified backup set.");
                });
                xell.AddBranch("nand", nand => {
                    nand.SetDescription("XeLL-backed NAND dumping and verification.");
                    nand.AddExample(new[] { "xell", "nand", "dump" });
                    nand.AddExample(new[] { "xell", "nand", "dump", "--single" });
                    nand.AddExample(new[] { "xell", "nand", "dump", "--quickboot", "--yes" });
                    nand.AddExample(new[] { "xell", "nand", "dump", "--output", "nand_backup.bin" });
                    nand.AddExample(new[] { "xell", "nand", "dump", "--force-xell" });
                    nand.AddCommand<NandDumpCommand>("dump").WithDescription("Boot XeLL Reloaded, download the flash dump, verify repeated dumps, and package a safe backup.");
                });
            });

            config.AddBranch("popup", popup => {
                popup.SetDescription("Trainer-style console popup helpers.");
                popup.AddExample(new[] { "popup", "show", "--title", "XeCLI", "--body", "\"Connected to console\"" });
                popup.AddCommand<PopupMessageBoxCommand>("show").WithAlias("open").WithDescription("Show a native Xbox 360 popup.");
            });

            config.AddBranch("ftp", ftp => {
                ftp.SetDescription("FTP commands (alternate access). Requires an active console FTP service.");
                ftp.AddExample(new[] { "ftp", "list", "--path", "/Hdd1/" });
                ftp.AddExample(new[] { "ftp", "check" });
                ftp.AddExample(new[] { "ftp", "target", "--set", "<console-ip>", "--user", "<ftp-user>", "--pass", "<ftp-pass>" });
                ftp.AddExample(new[] { "ftp", "hash", "--path", "/Hdd1/XeCLI/stage2/scratch.bin", "--algorithm", "sha256" });
                ftp.AddExample(new[] { "ftp", "diff", "--path", "/Hdd1/XeCLI/stage2/scratch.bin", "--local", ".\\scratch.bin", "--algorithm", "sha256" });
                ftp.AddExample(new[] { "ftp", "get", "--path", "/Hdd1/launch.ini", "--out", ".\\launch.ini", "--verify-hash", "sha256" });
                ftp.AddExample(new[] { "ftp", "put", "--path", "/Hdd1/launch.ini", "--in", ".\\launch.ini", "--verify-hash", "sha256" });
                ftp.AddExample(new[] { "ftp", "sync", "--direction", "upload", "--path", "/Hdd1/Plugins", "--in", ".\\Plugins", "--recursive", "--dry-run" });
                ftp.AddExample(new[] { "ftp", "sync", "--direction", "download", "--path", "/Hdd1/Content", "--in", ".\\ContentBackup", "--recursive" });
                ftp.AddCommand<FtpCheckCommand>("check").WithAlias("doctor").WithDescription("Probe port 21 to confirm whether a compatible dashboard FTP service is running.");
                ftp.AddCommand<FtpTargetCommand>("target").WithDescription("Show or set FTP target.");
                ftp.AddCommand<FtpListCommand>("list").WithAlias("ls").WithDescription("List files via FTP.");
                ftp.AddCommand<FtpFindCommand>("find").WithDescription("Find files via FTP.");
                ftp.AddCommand<FtpGetCommand>("get").WithDescription("Download a file via FTP.");
                ftp.AddCommand<FtpPutCommand>("put").WithDescription("Upload a file via FTP.");
                ftp.AddCommand<FtpSyncCommand>("sync").WithDescription("Synchronize files via FTP.");
                ftp.AddCommand<FtpCatCommand>("cat").WithDescription("Print a file via FTP.");
                ftp.AddCommand<FtpHashCommand>("hash").WithDescription("Hash a remote file via FTP.");
                ftp.AddCommand<FtpDiffCommand>("diff").WithDescription("Compare a remote file with a local file via FTP.");
                ftp.AddCommand<FtpDeleteCommand>("rm").WithAlias("del").WithDescription("Delete a file via FTP.");
                ftp.AddCommand<FtpMkdirCommand>("mkdir").WithDescription("Create a directory via FTP.");
                ftp.AddCommand<FtpMoveCommand>("mv").WithAlias("move").WithDescription("Move or rename a file via FTP.");
            });

            config.AddBranch("target-saved", targetProfile => {
                targetProfile.SetDescription("Store and select saved target profiles.");
                targetProfile.AddExample(new[] { "target-profile", "list" });
                targetProfile.AddExample(new[] { "target", "profile", "list" });
                targetProfile.AddExample(new[] { "target-profile", "list", "--csv" });
                targetProfile.AddExample(new[] { "target-profile", "add", "home", "--ip", "<console-ip>", "--port", "730" });
                targetProfile.AddExample(new[] { "target-profile", "use", "home" });
                targetProfile.AddExample(new[] { "target-profile", "validate" });
                targetProfile.AddExample(new[] { "target-profile", "current" });
                targetProfile.AddCommand<TargetProfileAddCommand>("add").WithAlias("set").WithDescription("Add or update a saved target profile.");
                targetProfile.AddCommand<TargetProfileListCommand>("list").WithAlias("ls").WithDescription("List saved target profiles.");
                targetProfile.AddCommand<TargetProfileShowCommand>("show").WithAlias("get").WithDescription("Show one saved target profile.");
                targetProfile.AddCommand<TargetProfileValidateCommand>("validate").WithDescription("Validate saved target profiles and report any issues.");
                targetProfile.AddCommand<TargetProfileUseCommand>("use").WithAlias("select").WithDescription("Set the current target profile and update the saved default target.");
                targetProfile.AddCommand<TargetProfileCurrentCommand>("current").WithAlias("state").WithDescription("Show the effective target for the current profile.");
                targetProfile.AddCommand<TargetProfileRemoveCommand>("remove").WithAlias("rm").WithDescription("Remove a saved target profile.");
            }).WithAlias("target-profile").WithAlias("tp");

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
                content.AddExample(new[] { "content", "browse", ".\\profile.con" });
                content.AddExample(new[] { "content", "browse", ".\\FFFE07D1.gpd" });
                content.AddCommand<ContentListCommand>("list").WithAlias("ls").WithDescription("List installed titles and content types.");
                content.AddCommand<ContentDeleteCommand>("delete").WithAlias("rm").WithDescription("Delete installed content for a title.");
                content.AddCommand<ContentBrowseCommand>("browse").WithDescription("Browse a local profile package or dashboard GPD.");
            });

            config.AddBranch("con", con => {
                con.SetDescription("Inspect and repair local Xbox 360 content packages.");
                con.AddExample(new[] { "con", "info", ".\\E000000000000000.con" });
                con.AddExample(new[] { "con", "verify", ".\\E000000000000000.con" });
                con.AddExample(new[] { "con", "rehash", ".\\E000000000000000.con", "--keyvault", ".\\KV.bin" });
                con.AddExample(new[] { "con", "resign", ".\\E000000000000000.con", "--keyvault", ".\\KV.bin" });
                con.AddCommand<ConInfoCommand>("info").WithDescription("Show package metadata and derived FATX values.");
                con.AddCommand<ConVerifyCommand>("verify").WithDescription("Verify a CON signature when supported.");
                con.AddCommand<ConRehashCommand>("rehash").WithDescription("Save package headers and refresh STFS hashes.");
                con.AddCommand<ConResignCommand>("resign").WithDescription("Re-sign a CON package and verify the result.");
                con.AddCommand<ConMagicNameCommand>("magic-name").WithDescription("Print the package's FATX magic filename.");
                con.AddCommand<ConFatxPathCommand>("fatx-path").WithDescription("Print the destination FATX content path.");
            });

            config.AddBranch("profile", profile => {
                profile.SetDescription("Inspect and edit local Xbox 360 profile packages.");
                profile.AddExample(new[] { "profile", "info", ".\\E000000000000000.con" });
                profile.AddExample(new[] { "profile", "extract", ".\\E000000000000000.con", ".\\profile-files" });
                profile.AddExample(new[] { "profile", "account", "show", ".\\E000000000000000.con" });
                profile.AddExample(new[] { "profile", "account", "extract", ".\\E000000000000000.con", ".\\Account" });
                profile.AddExample(new[] { "profile", "account", "set-gamertag", ".\\E000000000000000.con", "ExampleTag", "--keyvault", ".\\KV.bin" });
                profile.AddExample(new[] { "profile", "gpd", "list", ".\\E000000000000000.con" });
                profile.AddExample(new[] { "profile", "gpd", "extract", ".\\E000000000000000.con", ".\\FFFE07D1.gpd", "--dashboard" });
                profile.AddExample(new[] { "profile", "titles", "list", ".\\E000000000000000.con" });
                profile.AddExample(new[] { "profile", "titles", "add", ".\\E000000000000000.con", "--titleid", "415607E7", "--keyvault", ".\\KV.bin" });
                profile.AddExample(new[] { "profile", "achievements", "list", ".\\E000000000000000.con", "--titleid", "4D530805" });
                profile.AddExample(new[] { "profile", "achievements", "unlock", ".\\E000000000000000.con", "--titleid", "4D530805", "--achievementid", "0x00000001", "--keyvault", ".\\KV.bin" });
                profile.AddExample(new[] { "profile", "settings", "get", ".\\E000000000000000.con", "0x10040006" });
                profile.AddExample(new[] { "profile", "settings", "set", ".\\E000000000000000.con", "0x10040006", "1337", "--keyvault", ".\\KV.bin" });
                profile.AddExample(new[] { "profile", "avatar-colors", "get", ".\\E000000000000000.con" });
                profile.AddExample(new[] { "profile", "avatar-colors", "set", ".\\E000000000000000.con", "--hair", "0xFF22150D", "--keyvault", ".\\KV.bin" });
                profile.AddCommand<ProfileInfoCommand>("info").WithDescription("Show profile package contents and summary information.");
                profile.AddCommand<ProfileExtractCommand>("extract").WithDescription("Extract files from a profile package.");

                profile.AddBranch("account", account => {
                    account.SetDescription("Inspect and update account data inside a profile package.");
                    account.AddCommand<ProfileAccountShowCommand>("show").WithDescription("Show decoded profile account information.");
                    account.AddCommand<ProfileAccountExtractCommand>("extract").WithDescription("Extract the raw Account payload from a profile package.");
                    account.AddCommand<ProfileAccountSetGamertagCommand>("set-gamertag").WithAlias("set").WithDescription("Update the profile account gamertag.");
                });

                profile.AddBranch("gpd", gpd => {
                    gpd.SetDescription("List and extract embedded dashboard and title GPD files.");
                    gpd.AddCommand<ProfileGpdListCommand>("list").WithAlias("ls").WithDescription("List embedded dashboard and title GPD files.");
                    gpd.AddCommand<ProfileGpdExtractCommand>("extract").WithDescription("Extract one dashboard or title GPD from the profile package.");
                });

                profile.AddBranch("titles", titles => {
                    titles.SetDescription("Inspect title records inside a profile package.");
                    titles.AddCommand<ProfileTitlesListCommand>("list").WithAlias("ls").WithDescription("List title records from the dashboard GPD.");
                    titles.AddCommand<ProfileTitlesAddCommand>("add").WithDescription("Insert or refresh one dashboard title record.");
                });

                profile.AddBranch("achievements", achievements => {
                    achievements.SetDescription("Inspect and edit achievement data inside a profile package.");
                    achievements.AddCommand<ProfileAchievementsListCommand>("list").WithAlias("ls").WithDescription("List achievements for one title GPD.");
                    achievements.AddCommand<ProfileAchievementsUnlockCommand>("unlock").WithDescription("Unlock one profile achievement and update title totals.");
                    achievements.AddCommand<ProfileAchievementsLockCommand>("lock").WithDescription("Lock one profile achievement and update title totals.");
                });

                profile.AddBranch("settings", profileSettings => {
                    profileSettings.SetDescription("Inspect and edit profile settings stored in the dashboard GPD.");
                    profileSettings.AddCommand<ProfileSettingsListCommand>("list").WithAlias("ls").WithDescription("List profile settings.");
                    profileSettings.AddCommand<ProfileSettingsGetCommand>("get").WithDescription("Read a single profile setting.");
                    profileSettings.AddCommand<ProfileSettingsSetCommand>("set").WithDescription("Create or update a single profile setting.");
                });

                profile.AddBranch("avatar-colors", avatarColors => {
                    avatarColors.SetDescription("Inspect and edit avatar color values stored in the dashboard profile setting blob.");
                    avatarColors.AddCommand<ProfileAvatarColorsGetCommand>("get").WithDescription("Show avatar ARGB color values.");
                    avatarColors.AddCommand<ProfileAvatarColorsSetCommand>("set").WithDescription("Update one or more avatar ARGB color values.");
                });
            });

            config.AddBranch("xdbf", xdbf => {
                xdbf.SetDescription("Inspect and extract records from local GPD/XDBF files.");
                xdbf.AddExample(new[] { "xdbf", "list", ".\\415608C3.gpd", "--show-sync" });
                xdbf.AddExample(new[] { "xdbf", "get", ".\\415608C3.gpd", "strings", "0x0000000000008000", "--out", ".\\title-name.bin" });
                xdbf.AddExample(new[] { "xdbf", "extract", ".\\415608C3.gpd", ".\\records" });
                xdbf.AddExample(new[] { "xdbf", "sync-status", ".\\415608C3.gpd" });
                xdbf.AddCommand<XdbfListCommand>("list").WithAlias("ls").WithDescription("List records in a GPD/XDBF file.");
                xdbf.AddCommand<XdbfGetCommand>("get").WithDescription("Read a single record from a GPD/XDBF file.");
                xdbf.AddCommand<XdbfExtractCommand>("extract").WithDescription("Extract records to a directory.");
                xdbf.AddCommand<XdbfSyncStatusCommand>("sync-status").WithDescription("Show records that are pending sync.");
            });

            config.AddBranch("avatar", avatar => {
                avatar.SetDescription("Avatar item library and install helpers.");
                avatar.AddExample(new[] { "avatar", "library", "show" });
                avatar.AddExample(new[] { "avatar", "cache", "status" });
                avatar.AddExample(new[] { "avatar", "cache", "refresh" });
                avatar.AddExample(new[] { "avatar", "games", "--search", "\"Black Ops\"" });
                avatar.AddExample(new[] { "avatar", "items", "--titleid", "415608C3", "--limit", "10" });
                avatar.AddExample(new[] { "avatar", "choose", "--search", "\"Black Ops\"" });
                avatar.AddExample(new[] { "avatar", "browse", "--remote" });
                avatar.AddExample(new[] { "avatar", "install", "--contentid", "000000080DF3B242CAE65A52415608C3", "--current-user" });
                avatar.AddExample(new[] { "avatar", "apply", "--titleid", "415608C3", "--all", "--current-user" });

                avatar.AddBranch("library", library => {
                    library.SetDescription("Show or set avatar collection paths.");
                    library.AddCommand<AvatarLibraryShowCommand>("show").WithDescription("Show the current avatar library and cache paths.");
                    library.AddCommand<AvatarLibrarySetCommand>("set").WithDescription("Set the default avatar library and cache paths.");
                });
                avatar.AddBranch("cache", cache => {
                    cache.SetDescription("Inspect and refresh avatar index caches.");
                    cache.AddCommand<AvatarCacheStatusCommand>("status").WithDescription("Show the current avatar cache state.");
                    cache.AddCommand<AvatarCacheRefreshCommand>("refresh").WithDescription("Refresh the local or remote avatar cache.");
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
                ghidra.AddExample(new[] { "ghidra", "export-symbols", "--in", ".\\game.xex", "--out", ".\\symbols.json" });
                ghidra.AddExample(new[] { "ghidra", "decompile", "--running", "--out", ".\\decomp" });
                ghidra.AddCommand<GhidraConfigCommand>("config").WithDescription("Configure Ghidra paths.");
                ghidra.AddCommand<GhidraInstallLoaderCommand>("install-loader").WithDescription("Download or install XEXLoaderWV into the configured Ghidra install.");
                ghidra.AddCommand<GhidraAnalyzeCommand>("analyze").WithDescription("Run headless analysis.");
                ghidra.AddCommand<GhidraExportSymbolsCommand>("export-symbols").WithDescription("Export function entry symbols to a sidecar JSON file.");
                ghidra.AddCommand<GhidraDecompileCommand>("decompile").WithDescription("Decompile a module or XEX.");
                ghidra.AddCommand<GhidraVerifyCommand>("verify").WithDescription("Verify decompiler output for bad-instruction placeholders.");
            });

            config.AddBranch("ida", ida => {
                ida.SetDescription("IDA Pro headless helpers (IDA 9.3 supported; legacy 9.1 archive compatibility available, external install required).");
                ida.AddExample(new[] { "ida", "config", "--path", "\"C:\\Program Files\\IDA Professional 9.3\"", "--python", "python" });
                ida.AddExample(new[] { "ida", "install-loader" });
                ida.AddExample(new[] { "ida", "decompile", "--running", "--out", ".\\ida-decomp", "--max", "25" });
                ida.AddCommand<IdaConfigCommand>("config").WithDescription("Configure IDA install, python, and backend paths.");
                ida.AddCommand<IdaCheckCommand>("check").WithAlias("doctor").WithDescription("Verify the configured IDA environment.");
                ida.AddCommand<IdaInstallLoaderCommand>("install-loader").WithDescription("Download or install the supported loader set into IDA.");
                ida.AddCommand<IdaAnalyzeCommand>("analyze").WithDescription("Import a XEX into an IDA database headlessly.");
                ida.AddCommand<IdaDecompileCommand>("decompile").WithDescription("Decompile a XEX or IDA database to C.");
                ida.AddCommand<IdaExportSymbolsCommand>("export-symbols").WithDescription("Export function entry symbols from an IDA database or XEX.");
                ida.AddCommand<IdaVerifyCommand>("verify").WithDescription("Verify IDA decompiler output for obvious failures.");
            });
        });

        try {
            int exitCode = await app.RunAsync(args);
            if (commandMissing && exitCode == 0)
                exitCode = 1;
            if (exitCode == 3 && Interlocked.CompareExchange(ref errorOutputEmitted, 1, 0) == 0)
                CliErrorReporter.WriteFrameworkExitCodeThree(args);

            return CompleteMain(exitCode);
        }
        catch (Exception ex) {
            try {
                string? targetDisplay = CliErrorReporter.TryGetTargetDisplay(args);
                if (ShouldEmitJsonError(args)) {
                    CliErrorReporter.WriteJsonError("Command", ex, targetDisplay);
                    Interlocked.Exchange(ref errorOutputEmitted, 1);
                }
                else {
                    CliErrorReporter.WriteTextError("Command", ex, targetDisplay);
                    Interlocked.Exchange(ref errorOutputEmitted, 1);
                }
            }
            catch {
                CliErrorReporter.WriteFrameworkExitCodeThree(args);
            }

            return CompleteMain(3);
        }
    }

    private static string ResolveLanguageCode(string[] args, string? requestedLanguage, bool suppressPrompt) {
        if (!string.IsNullOrWhiteSpace(requestedLanguage))
            return LocalizedText.NormalizeLanguageCode(requestedLanguage);

        string? environmentLanguage = Environment.GetEnvironmentVariable("XECLI_LANG");
        if (!string.IsNullOrWhiteSpace(environmentLanguage))
            return LocalizedText.NormalizeLanguageCode(environmentLanguage);

        CliConfig? config = null;
        bool configLoadFailed = false;
        try {
            config = CliConfig.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
            configLoadFailed = true;
        }

        if (!string.IsNullOrWhiteSpace(config?.UiLanguage))
            return LocalizedText.NormalizeLanguageCode(config.UiLanguage);

        if (!suppressPrompt && !configLoadFailed && ShouldPromptForLanguage(args)) {
            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Choose language / Elige idioma")
                    .AddChoices("English", "Español"));
            config ??= new CliConfig();
            config.UiLanguage = choice == "Español" ? "es" : "en";
            config.Save();
            return config.UiLanguage;
        }

        return LocalizedText.GetDefaultLanguageCode();
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
            "XeCLI can install itself to a standard folder and register the [green]rgh[/] command for terminal use.\n[grey]You can install for the current user or all users from the installer.[/]\n\n[mediumpurple3]@SaveEditors[/]")
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

        if (string.Equals(args[0], "title", StringComparison.OrdinalIgnoreCase) &&
            args.Skip(1).All(a => a.StartsWith("-", StringComparison.Ordinal))) {
            return new[] { "title", "active" }.Concat(args.Skip(1)).ToArray();
        }

        if (string.Equals(args[0], "target", StringComparison.OrdinalIgnoreCase) &&
            args.Length > 1 &&
            string.Equals(args[1], "profile", StringComparison.OrdinalIgnoreCase)) {
            return new[] { "target-saved" }.Concat(args.Skip(2)).ToArray();
        }

        if (ShouldRewriteTargetProfileOption(args))
            return args.Select(arg => string.Equals(arg, "--profile", StringComparison.OrdinalIgnoreCase) ? "--target-profile" : arg).ToArray();

        return args;
    }

    private static bool Matches(string value, params string[] candidates) {
        return candidates.Any(candidate => value.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHelpToken(string arg) {
        return arg.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldRewriteTargetProfileOption(string[] args) {
        if (args.Length == 0 || !args.Any(arg => arg.Equals("--profile", StringComparison.OrdinalIgnoreCase)))
            return false;

        string first = args[0];
        if (Matches(first, "save", "profile"))
            return false;

        return true;
    }

    private static bool IsDirectHelpOption(string arg) {
        return IsHelpToken(arg) ||
               arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHelpRequest(string[] args) {
        return args.Any(IsDirectHelpOption);
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

    private static bool ShouldPromptForLanguage(string[] args) {
        if (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected)
            return false;

        if (IsVersionRequest(args))
            return false;

        return !args.Any(arg =>
            arg.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-v", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTerminalGuiRequest(string[] args) {
        return args.Length > 0 &&
               (args[0].Equals("terminal", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("shell", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCompletionRequest(string[] args) {
        return args.Length > 0 && args[0].Equals("completion", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldEmitJsonError(string[] args) {
        return args.Any(arg => arg.Equals("--json", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetSavedTargetDisplay() {
        try {
            CliConfig config = CliConfig.Load();
            if (string.IsNullOrWhiteSpace(config.DefaultIp))
                return null;

            return $"{config.DefaultIp}:{config.DefaultPort ?? 730}";
        }
        catch {
            return null;
        }
    }

    private static bool ShouldShowPathPrompt(string[] args) {
        if (CliPaths.IsPortable)
            return false;

        if (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected)
            return false;

        // Keep first-run install guidance off real commands. A bare `rgh`
        // invocation is the only place where an install prompt is appropriate.
        if (args.Length != 0)
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

    private static bool ShouldLaunchTerminalByDefault(string[] args) {
        if (args.Length != 0)
            return false;

        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        string executableName = Path.GetFileNameWithoutExtension(processPath);
        return executableName.Equals("XeTerminal", StringComparison.OrdinalIgnoreCase);
    }

    private static int CompleteMain(int exitCode) {
        FlushStandardStreams();
        if (ShouldForceProcessExitOnCompletion())
            Environment.Exit(exitCode);

        return exitCode;
    }

    private static bool ShouldForceProcessExitOnCompletion() {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        string executableName = Path.GetFileNameWithoutExtension(processPath);
        return executableName.Equals("rgh", StringComparison.OrdinalIgnoreCase) ||
               executableName.Equals("XeTerminal", StringComparison.OrdinalIgnoreCase);
    }

    private static void FlushStandardStreams() {
        try {
            Console.Out.Flush();
            Console.Error.Flush();
        }
        catch {
        }
    }

    private static void HideStandaloneConsoleWindow() {
        try {
            IntPtr consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
                return;

            uint[] processIds = new uint[4];
            uint count = GetConsoleProcessList(processIds, (uint)processIds.Length);
            if (count <= 1)
                ShowWindow(consoleWindow, SW_HIDE);
        }
        catch {
        }
    }
}
