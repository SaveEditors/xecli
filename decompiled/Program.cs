using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using XeCli.Localization;
using Xbox360.Remote.Cli;
using Xbox360.Remote.Cli.Commands;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

internal static class Program
{
	[STAThread]
	public static async Task<int> Main(string[] args)
	{
		args = LocalizedText.ExtractLanguageArgument(args, out string? requestedLanguage);
		args = NormalizeArgs(args);
		LocalizedText.Initialize(ResolveLanguageCode(args, requestedLanguage));
		Console.InputEncoding = Encoding.UTF8;
		Console.OutputEncoding = Encoding.UTF8;
		LocalizedConsole.Initialize();
		CultureInfo.CurrentUICulture = LocalizedText.Culture;
		CultureInfo.DefaultThreadCurrentUICulture = LocalizedText.Culture;
		if (OperatingSystem.IsWindows())
		{
			InstallHelpers.RemoveBrokenUserShim();
		}
		if (IsVersionRequest(args))
		{
			AnsiConsole.MarkupLine("[deepskyblue1]XeCLI[/] [white]" + Markup.Escape(GetApplicationVersion()) + "[/]");
			return 0;
		}
		CommandApp commandApp = new CommandApp();
		commandApp.Configure(delegate(IConfigurator config)
		{
			config.SetApplicationName("rgh");
			config.Settings.Culture = LocalizedText.Culture;
			config.Settings.Console = AnsiConsole.Console;
			config.Settings.ShowOptionDefaultValues = true;
			config.Settings.MaximumIndirectExamples = 2;
			ICommandAppSettings settings = config.Settings;
			HelpProviderStyle helpProviderStyle = new HelpProviderStyle();
			DescriptionStyle descriptionStyle = new DescriptionStyle();
			Color? foreground = Color.Gold1;
			Decoration? decoration = Decoration.Bold;
			descriptionStyle.Header = new Style(foreground, null, decoration);
			helpProviderStyle.Description = descriptionStyle;
			UsageStyle usageStyle = new UsageStyle();
			Color? foreground2 = Color.Gold1;
			decoration = Decoration.Bold;
			usageStyle.Header = new Style(foreground2, null, decoration);
			Color? foreground3 = Color.SpringGreen3_1;
			decoration = Decoration.Bold;
			usageStyle.CurrentCommand = new Style(foreground3, null, decoration);
			Color? foreground4 = Color.White;
			decoration = Decoration.Bold;
			usageStyle.Command = new Style(foreground4, null, decoration);
			Color? foreground5 = Color.DeepSkyBlue1;
			decoration = Decoration.Bold;
			usageStyle.Options = new Style(foreground5, null, decoration);
			Color? foreground6 = Color.Aqua;
			decoration = Decoration.Bold;
			usageStyle.RequiredArgument = new Style(foreground6, null, decoration);
			usageStyle.OptionalArgument = new Style(Color.White);
			helpProviderStyle.Usage = usageStyle;
			ExampleStyle exampleStyle = new ExampleStyle();
			Color? foreground7 = Color.Gold1;
			decoration = Decoration.Bold;
			exampleStyle.Header = new Style(foreground7, null, decoration);
			Color? foreground8 = Color.DeepSkyBlue1;
			decoration = Decoration.Bold;
			exampleStyle.Arguments = new Style(foreground8, null, decoration);
			helpProviderStyle.Examples = exampleStyle;
			ArgumentStyle argumentStyle = new ArgumentStyle();
			Color? foreground9 = Color.Gold1;
			decoration = Decoration.Bold;
			argumentStyle.Header = new Style(foreground9, null, decoration);
			Color? foreground10 = Color.Aqua;
			decoration = Decoration.Bold;
			argumentStyle.RequiredArgument = new Style(foreground10, null, decoration);
			argumentStyle.OptionalArgument = new Style(Color.White);
			helpProviderStyle.Arguments = argumentStyle;
			OptionStyle optionStyle = new OptionStyle();
			Color? foreground11 = Color.Gold1;
			decoration = Decoration.Bold;
			optionStyle.Header = new Style(foreground11, null, decoration);
			Color? foreground12 = Color.White;
			decoration = Decoration.Bold;
			optionStyle.DefaultValueHeader = new Style(foreground12, null, decoration);
			Color? foreground13 = Color.MediumPurple3;
			decoration = Decoration.Bold;
			optionStyle.DefaultValue = new Style(foreground13, null, decoration);
			Color? foreground14 = Color.DeepSkyBlue1;
			decoration = Decoration.Bold;
			optionStyle.RequiredOption = new Style(foreground14, null, decoration);
			Color? foreground15 = Color.White;
			decoration = Decoration.Bold;
			optionStyle.OptionalOption = new Style(foreground15, null, decoration);
			helpProviderStyle.Options = optionStyle;
			CommandStyle commandStyle = new CommandStyle();
			Color? foreground16 = Color.Gold1;
			decoration = Decoration.Bold;
			commandStyle.Header = new Style(foreground16, null, decoration);
			Color? foreground17 = Color.SpringGreen3_1;
			decoration = Decoration.Bold;
			commandStyle.ChildCommand = new Style(foreground17, null, decoration);
			Color? foreground18 = Color.Aqua;
			decoration = Decoration.Bold;
			commandStyle.RequiredArgument = new Style(foreground18, null, decoration);
			helpProviderStyle.Commands = commandStyle;
			settings.HelpProviderStyles = helpProviderStyle;
			config.SetExceptionHandler(delegate(Exception ex, ITypeResolver? _)
			{
				AnsiConsole.MarkupLine("[red]Error:[/] " + Markup.Escape(ex.Message));
			});
			config.AddExample("status");
			config.AddExample("title");
			config.AddExample("modules", "list");
			config.AddExample("mem", "hexdump", "--addr", "0x30000000", "--size", "0x40");
			config.AddExample("notify", "XeCLI connected", "14");
			config.AddExample("smc", "version");
			config.AddExample("fan", "set", "--speed", "55", "--channel", "both");
			config.AddExample("led", "set", "--preset", "quadrant1");
			config.AddExample("tray", "open");
			config.AddExample("nand", "dump");
			config.AddExample("nand", "dump", "--single");
			config.AddExample("nand", "dump", "--output", "nand_backup.bin");
			config.AddExample("xell", "boot");
			config.AddExample("xell", "info");
			config.AddExample("xell", "kv", "export");
			config.AddExample("popup", "show", "--title", "XeCLI", "--body", "Connected to console", "--preset", "none");
			config.AddExample("avatar", "games", "--search", "Black Ops");
			config.AddExample("avatar", "install", "--contentid", "000000080DF3B242CAE65A52415608C3", "--current-user");
			config.AddExample("avatar", "browse", "--remote");
			config.AddExample("con", "info", ".\\package.con");
			config.AddExample("con", "rehash", ".\\profile.con");
			config.AddExample("profile", "info", ".\\E000000000000000.con");
			config.AddExample("profile", "titles", "list", ".\\E000000000000000.con");
			config.AddExample("xdbf", "list", ".\\415608C3.gpd", "--show-sync");
			config.AddExample("xdbf", "get", ".\\415608C3.gpd", "strings", "0x0000000000008000", "--out", ".\\title-name.bin");
			config.AddExample("xtaf", "devices");
			config.AddExample("xtaf", "partitions", "--image", ".\\hdd.img");
			config.AddExample("xtaf", "scan", "--image", ".\\hdd.img");
			config.AddExample("xtaf", "info", "--image", ".\\hdd.img", "--partition", "Content");
			config.AddExample("xtaf", "list", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/");
			config.AddExample("xtaf", "metadata", "backup", "--image", ".\\hdd.img", "--out", ".\\xtaf-meta");
			config.AddExample("homebrew", "install", "aurora", "--usb", "E:");
			config.AddExample("homebrew", "install", "all", "--usb", "E:", "--auto-confirm");
			config.AddExample("ogxbox", "install", "hacked", "--include-fixer", "--usb", "E:");
			config.AddExample("language", "--set", "es");
			config.AddExample("ghidra", "decompile", "--running", "--out", ".\\decomp");
			config.AddExample("ida", "check");
			config.AddExample("ida", "decompile", "--running", "--out", ".\\ida-decomp");
			config.AddCommand<StatusCommand>("status").WithDescription("Show a compact console status snapshot.");
			config.AddCommand<ProfilesCommand>("profiles").WithAlias("users").WithDescription("List profiles and signed-in users.");
			config.AddCommand<TitleLookupCommand>("title").WithAlias("titles").WithDescription("Resolve the active title or look up a Title ID.");
			config.AddCommand<TargetCommand>("target").WithDescription("Show or set the default target.");
			config.AddCommand<PingCommand>("ping").WithDescription("Ping the current console.");
			config.AddCommand<RebootCommand>("reboot").WithAlias("restart").WithDescription("Reboot the console (cold by default).");
			config.AddCommand<ShutdownCommand>("shutdown").WithAlias("poweroff").WithDescription("Power off the console.");
			config.AddCommand<LaunchCommand>("launch").WithAlias("run").WithDescription("Launch a XEX with optional arguments.");
			config.AddCommand<LanguageCommand>("language").WithAlias("lang").WithDescription("Show or change the saved UI language.");
			config.AddCommand<StartCommand>("start").WithAlias("s").WithDescription("Discover consoles and set the default target.");
			config.AddCommand<ConnectCommand>("connect").WithAlias("c").WithDescription("Set or select the default target.");
			config.AddCommand<ScanCommand>("scan").WithAlias("discover").WithDescription("Scan the network for consoles.");
			config.AddCommand<XbdmScreenshotCommand>("screenshot").WithAlias("shot").WithDescription("Capture a live screenshot.");
			ConfiguratorExtensions.AddBranch(config, "homebrew", delegate(IConfigurator<CommandSettings> homebrew)
			{
				homebrew.SetDescription("Download public homebrew packages to USB, a folder, or a detected console drive.");
				homebrew.AddExample("homebrew", "list");
				homebrew.AddExample("homebrew", "install", "aurora", "--usb", "E:");
				homebrew.AddExample("homebrew", "install", "all", "--usb", "E:", "--auto-confirm");
				homebrew.AddExample("homebrew", "install", "aurora", "--device", "Hdd1", "--ini-mode", "merge");
				homebrew.AddCommand<HomebrewListCommand>("list").WithAlias("ls").WithDescription("List the built-in package catalog.");
				homebrew.AddCommand<HomebrewInstallCommand>("install").WithAlias("stage").WithDescription("Download one or more public homebrew packages to USB, a folder, or the console.");
			}).WithAlias("hb");
			ConfiguratorExtensions.AddBranch(config, "ogxbox", delegate(IConfigurator<CommandSettings> ogxbox)
			{
				ogxbox.SetDescription("Original Xbox compatibility pack installer for HddX:\\Compatibility.");
				ogxbox.AddExample("ogxbox", "list");
				ogxbox.AddExample("ogxbox", "install", "hacked", "--usb", "E:");
				ogxbox.AddExample("ogxbox", "install", "hud", "--include-fixer");
				ogxbox.AddCommand<OriginalXboxCompatibilityListCommand>("list").WithAlias("ls").WithDescription("List the built-in XeFu compatibility sets and partition fixer.");
				ogxbox.AddCommand<OriginalXboxCompatibilityInstallCommand>("install").WithAlias("stage").WithDescription("Stage or install an Original Xbox compatibility set.");
			}).WithAlias("xefu");
			ConfiguratorExtensions.AddBranch(config, "xbdm", delegate(IConfigurator<CommandSettings> xbdm)
			{
				xbdm.SetDescription("XBDM commands.");
				xbdm.AddCommand<XbdmInfoCommand>("info").WithDescription("Show XBDM console info.");
				xbdm.AddCommand<XbdmRawCommand>("raw").WithDescription("Send a raw XBDM command.");
				xbdm.AddCommand<XbdmScreenshotCommand>("screenshot").WithDescription("Capture a screenshot via XBDM.");
				ConfiguratorExtensions.AddBranch(xbdm, "modules", delegate(IConfigurator<CommandSettings> modules)
				{
					modules.SetDescription("Manage loaded modules through XBDM and kernel RPC.");
					modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
					modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
					modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
					modules.AddCommand<XbdmModuleLoadCommand>("load").WithDescription("Load a module via kernel RPC.");
					modules.AddCommand<XbdmModuleUnloadCommand>("unload").WithAlias("remove").WithDescription("Unload a module via kernel RPC.");
					modules.AddCommand<XbdmModulesPendingCommand>("pending").WithAlias("verify").WithDescription("Verify a pending module operation after reboot.");
				});
				ConfiguratorExtensions.AddBranch(xbdm, "mem", delegate(IConfigurator<CommandSettings> mem)
				{
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
				ConfiguratorExtensions.AddBranch(xbdm, "xex", delegate(IConfigurator<CommandSettings> xex)
				{
					xex.SetDescription("Dump, launch, and analyze XEX images.");
					xex.AddCommand<XbdmXexDumpCommand>("dump").WithDescription("Dump the active XEX image.");
					xex.AddCommand<LaunchCommand>("launch").WithAlias("run").WithDescription("Launch a XEX with optional arguments.");
					xex.AddCommand<XexStringsCommand>("strings").WithDescription("Extract strings from a XEX (local/FTP/running).");
					xex.AddCommand<GhidraDecompileCommand>("decompile").WithAlias("decode").WithDescription("Decompile a XEX to C via Ghidra.");
					xex.AddCommand<IdaDecompileCommand>("ida-decompile").WithAlias("decompile-ida").WithDescription("Decompile a XEX to C via IDA.");
				});
				ConfiguratorExtensions.AddBranch(xbdm, "fs", delegate(IConfigurator<CommandSettings> fs)
				{
					fs.SetDescription("Browse and transfer files over XBDM.");
					fs.AddCommand<XbdmFsListCommand>("list").WithAlias("ls").WithDescription("List files in a directory.");
					fs.AddCommand<XbdmFsGetCommand>("get").WithDescription("Download a file from the console.");
					fs.AddCommand<XbdmFsPutCommand>("put").WithDescription("Upload a file to the console.");
					fs.AddCommand<XbdmFsCatCommand>("cat").WithDescription("Print a file from the console.");
					fs.AddCommand<XbdmFsDeleteCommand>("rm").WithAlias("del").WithDescription("Delete a file on the console.");
					fs.AddCommand<XbdmFsMkdirCommand>("mkdir").WithDescription("Create a directory on the console.");
					fs.AddCommand<XbdmFsMoveCommand>("mv").WithAlias("move").WithDescription("Move or rename a file on the console.");
				});
				ConfiguratorExtensions.AddBranch(xbdm, "threads", delegate(IConfigurator<CommandSettings> threads)
				{
					threads.SetDescription("Inspect and control threads.");
					threads.AddCommand<XbdmThreadsListCommand>("list").WithAlias("ls").WithDescription("List threads.");
					threads.AddCommand<XbdmThreadContextCommand>("context").WithDescription("Read thread registers.");
					threads.AddCommand<XbdmThreadSuspendCommand>("suspend").WithDescription("Suspend a thread.");
					threads.AddCommand<XbdmThreadResumeCommand>("resume").WithDescription("Resume a thread.");
				});
				ConfiguratorExtensions.AddBranch(xbdm, "debug", delegate(IConfigurator<CommandSettings> debug)
				{
					debug.SetDescription("Break, resume, and watch live debug state.");
					debug.AddCommand<XbdmDebugStopCommand>("stop").WithDescription("Break execution.");
					debug.AddCommand<XbdmDebugGoCommand>("go").WithDescription("Resume execution.");
					debug.AddCommand<XbdmDebugWatchCommand>("watch").WithAlias("events").WithDescription("Stream live XBDM debug notifications.");
					ConfiguratorExtensions.AddBranch(debug, "break", delegate(IConfigurator<CommandSettings> brk)
					{
						brk.SetDescription("Code breakpoints.");
						brk.AddCommand<XbdmBreakpointAddCommand>("add").WithDescription("Add a code breakpoint.");
						brk.AddCommand<XbdmBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a code breakpoint.");
						brk.AddCommand<XbdmBreakpointClearAllCommand>("clearall").WithAlias("clear").WithDescription("Clear all code and data breakpoints.");
					});
					ConfiguratorExtensions.AddBranch(debug, "databreak", delegate(IConfigurator<CommandSettings> brk)
					{
						brk.SetDescription("Data breakpoints.");
						brk.AddCommand<XbdmDataBreakpointAddCommand>("add").WithDescription("Add a data breakpoint.");
						brk.AddCommand<XbdmDataBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a data breakpoint.");
					});
				});
			});
			ConfiguratorExtensions.AddBranch(config, "modules", delegate(IConfigurator<CommandSettings> modules)
			{
				modules.SetDescription("Shortcut for `rgh xbdm modules`.");
				modules.AddExample("modules", "list");
				modules.AddExample("modules", "load", "--path", "Hdd:\\HvP2.xex", "--system", "--reboot-expected");
				modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
				modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
				modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
				modules.AddCommand<XbdmModuleLoadCommand>("load").WithDescription("Load a module via kernel RPC.");
				modules.AddCommand<XbdmModuleUnloadCommand>("unload").WithAlias("remove").WithDescription("Unload a module via kernel RPC.");
				modules.AddCommand<XbdmModulesPendingCommand>("pending").WithAlias("verify").WithDescription("Verify a pending module operation after reboot.");
			});
			ConfiguratorExtensions.AddBranch(config, "module", delegate(IConfigurator<CommandSettings> modules)
			{
				modules.SetDescription("Alias of `rgh modules` and `rgh xbdm modules`.");
				modules.AddExample("module", "info", "--name", "Aurora.xex");
				modules.AddCommand<XbdmModulesListCommand>("list").WithAlias("ls").WithDescription("List loaded modules.");
				modules.AddCommand<XbdmModulesInfoCommand>("info").WithDescription("Show module details.");
				modules.AddCommand<XbdmModulesDumpCommand>("dump").WithDescription("Dump a module from memory.");
				modules.AddCommand<XbdmModuleLoadCommand>("load").WithDescription("Load a module via kernel RPC.");
				modules.AddCommand<XbdmModuleUnloadCommand>("unload").WithAlias("remove").WithDescription("Unload a module via kernel RPC.");
				modules.AddCommand<XbdmModulesPendingCommand>("pending").WithAlias("verify").WithDescription("Verify a pending module operation after reboot.");
			});
			ConfiguratorExtensions.AddBranch(config, "mem", delegate(IConfigurator<CommandSettings> mem)
			{
				mem.SetDescription("Shortcut for `rgh xbdm mem`.");
				mem.AddExample("mem", "hexdump", "--addr", "0x30000000", "--size", "0x40");
				mem.AddExample("mem", "search", "--addr", "0x30000000", "--size", "0x1000", "--pattern", "DEADBEEF");
				mem.AddCommand<XbdmMemDumpCommand>("dump").WithDescription("Dump a memory range to a file.");
				mem.AddCommand<XbdmMemHexDumpCommand>("hexdump").WithDescription("Hex dump a memory range.");
				mem.AddCommand<XbdmMemRegionsCommand>("regions").WithAlias("map").WithDescription("List memory regions.");
				mem.AddCommand<XbdmMemPeekCommand>("peek").WithAlias("read").WithDescription("Read a value from memory.");
				mem.AddCommand<XbdmMemPokeCommand>("poke").WithAlias("write").WithDescription("Write a value to memory.");
				mem.AddCommand<XbdmMemWatchCommand>("watch").WithDescription("Stream memory changes.");
				mem.AddCommand<XbdmMemStringsCommand>("strings").WithDescription("Extract strings from memory.");
				mem.AddCommand<XbdmMemFindCommand>("find").WithAlias("search").WithDescription("Search memory for a pattern.");
			});
			ConfiguratorExtensions.AddBranch(config, "xex", delegate(IConfigurator<CommandSettings> xex)
			{
				xex.SetDescription("Shortcut for `rgh xbdm xex`.");
				xex.AddExample("xex", "dump", "--out", ".\\title.xex");
				xex.AddExample("xex", "strings", "--running", "--unicode", "--min", "6");
				xex.AddCommand<XbdmXexDumpCommand>("dump").WithDescription("Dump the active XEX image.");
				xex.AddCommand<LaunchCommand>("launch").WithAlias("run").WithDescription("Launch a XEX with optional arguments.");
				xex.AddCommand<XexStringsCommand>("strings").WithDescription("Extract strings from a XEX (local/FTP/running).");
				xex.AddCommand<GhidraDecompileCommand>("decompile").WithAlias("decode").WithDescription("Decompile a XEX to C via Ghidra.");
				xex.AddCommand<IdaDecompileCommand>("ida-decompile").WithAlias("decompile-ida").WithDescription("Decompile a XEX to C via IDA.");
			});
			ConfiguratorExtensions.AddBranch(config, "fs", delegate(IConfigurator<CommandSettings> fs)
			{
				fs.SetDescription("Shortcut for `rgh xbdm fs`.");
				fs.AddExample("fs", "list", "--path", "Hdd:\\");
				fs.AddCommand<XbdmFsListCommand>("list").WithAlias("ls").WithDescription("List files in a directory.");
				fs.AddCommand<XbdmFsGetCommand>("get").WithDescription("Download a file from the console.");
				fs.AddCommand<XbdmFsPutCommand>("put").WithDescription("Upload a file to the console.");
				fs.AddCommand<XbdmFsCatCommand>("cat").WithDescription("Print a file from the console.");
				fs.AddCommand<XbdmFsDeleteCommand>("rm").WithAlias("del").WithDescription("Delete a file on the console.");
				fs.AddCommand<XbdmFsMkdirCommand>("mkdir").WithDescription("Create a directory on the console.");
				fs.AddCommand<XbdmFsMoveCommand>("mv").WithAlias("move").WithDescription("Move or rename a file on the console.");
			});
			ConfiguratorExtensions.AddBranch(config, "threads", delegate(IConfigurator<CommandSettings> threads)
			{
				threads.SetDescription("Shortcut for `rgh xbdm threads`.");
				threads.AddExample("threads", "list");
				threads.AddExample("threads", "context", "--id", "0xFB000008");
				threads.AddCommand<XbdmThreadsListCommand>("list").WithAlias("ls").WithDescription("List threads.");
				threads.AddCommand<XbdmThreadContextCommand>("context").WithDescription("Read thread registers.");
				threads.AddCommand<XbdmThreadSuspendCommand>("suspend").WithDescription("Suspend a thread.");
				threads.AddCommand<XbdmThreadResumeCommand>("resume").WithDescription("Resume a thread.");
			});
			ConfiguratorExtensions.AddBranch(config, "debug", delegate(IConfigurator<CommandSettings> debug)
			{
				debug.SetDescription("Shortcut for `rgh xbdm debug`.");
				debug.AddExample("debug", "stop");
				debug.AddExample("debug", "watch");
				debug.AddCommand<XbdmDebugStopCommand>("stop").WithDescription("Break execution.");
				debug.AddCommand<XbdmDebugGoCommand>("go").WithDescription("Resume execution.");
				debug.AddCommand<XbdmDebugWatchCommand>("watch").WithAlias("events").WithDescription("Stream live XBDM debug notifications.");
				ConfiguratorExtensions.AddBranch(debug, "break", delegate(IConfigurator<CommandSettings> brk)
				{
					brk.SetDescription("Code breakpoints.");
					brk.AddCommand<XbdmBreakpointAddCommand>("add").WithDescription("Add a code breakpoint.");
					brk.AddCommand<XbdmBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a code breakpoint.");
					brk.AddCommand<XbdmBreakpointClearAllCommand>("clearall").WithAlias("clear").WithDescription("Clear all code and data breakpoints.");
				});
				ConfiguratorExtensions.AddBranch(debug, "databreak", delegate(IConfigurator<CommandSettings> brk)
				{
					brk.SetDescription("Data breakpoints.");
					brk.AddCommand<XbdmDataBreakpointAddCommand>("add").WithDescription("Add a data breakpoint.");
					brk.AddCommand<XbdmDataBreakpointRemoveCommand>("remove").WithAlias("del").WithDescription("Remove a data breakpoint.");
				});
			});
			ConfiguratorExtensions.AddBranch(config, "jrpc2", delegate(IConfigurator<CommandSettings> rpc)
			{
				rpc.SetDescription("JRPC2 (XDRPC-style) commands.");
				rpc.AddExample("jrpc2", "temps");
				rpc.AddExample("jrpc2", "notify", "XeCLI connected", "14");
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
			ConfiguratorExtensions.AddBranch(config, "notify-icons", delegate(IConfigurator<CommandSettings> icons)
			{
				icons.SetDescription("Browse XNotify icons and manage preset aliases.");
				icons.AddExample("notify-icons", "list");
				icons.AddExample("notify-icons", "show", "14");
				icons.AddExample("notify-icons", "add", "--name", "success", "--logo", "14");
				icons.AddCommand<NotifyIconsListCommand>("list").WithDescription("List built-in XNotify icons and preset aliases.");
				icons.AddCommand<NotifyIconsShowCommand>("show").WithAlias("resolve").WithDescription("Resolve an icon id, built-in name, or preset alias.");
				icons.AddCommand<NotifyIconsAddCommand>("add").WithDescription("Add an icon preset.");
				icons.AddCommand<NotifyIconsRemoveCommand>("remove").WithAlias("del").WithDescription("Remove an icon preset.");
			});
			ConfiguratorExtensions.AddBranch(config, "smc", delegate(IConfigurator<CommandSettings> smc)
			{
				smc.SetDescription("System Management Controller helpers.");
				smc.AddExample("smc", "version");
				smc.AddCommand<SmcVersionCommand>("version").WithAlias("ver").WithDescription("Probe the console SMC version.");
			});
			ConfiguratorExtensions.AddBranch(config, "fan", delegate(IConfigurator<CommandSettings> fan)
			{
				fan.SetDescription("Fan speed helpers.");
				fan.AddExample("fan", "set", "--speed", "55", "--channel", "both");
				fan.AddExample("fan", "show");
				fan.AddCommand<FanSetCommand>("set").WithAlias("speed").WithDescription("Send a manual fan speed command.");
				fan.AddCommand<FanShowCommand>("show").WithAlias("state").WithDescription("Show the last XeCLI-applied manual fan setting.");
			});
			ConfiguratorExtensions.AddBranch(config, "led", delegate(IConfigurator<CommandSettings> led)
			{
				led.SetDescription("Ring-of-light LED helpers.");
				led.AddExample("led", "set", "--preset", "quadrant1");
				led.AddExample("led", "set", "--tl", "green", "--tr", "off", "--bl", "off", "--br", "off");
				led.AddCommand<LedSetCommand>("set").WithAlias("apply").WithDescription("Set the ring-of-light LEDs.");
				led.AddCommand<LedStateCommand>("state").WithAlias("show").WithDescription("Show the last XeCLI-applied ring-light state.");
			});
			ConfiguratorExtensions.AddBranch(config, "signin", delegate(IConfigurator<CommandSettings> signin)
			{
				signin.SetDescription("Signed-in user helpers.");
				signin.AddExample("signin", "state");
				signin.AddCommand<SignInStateCommand>("state").WithAlias("status").WithDescription("Read the active sign-in state, gamertag, and XUID.");
			});
			ConfiguratorExtensions.AddBranch(config, "tray", delegate(IConfigurator<CommandSettings> tray)
			{
				tray.SetDescription("Disc tray helpers.");
				tray.AddExample("tray", "open");
				tray.AddExample("tray", "close");
				tray.AddCommand<TrayOpenCommand>("open").WithDescription("Open the disc tray.");
				tray.AddCommand<TrayCloseCommand>("close").WithDescription("Close the disc tray.");
			});
			ConfiguratorExtensions.AddBranch(config, "xell", delegate(IConfigurator<CommandSettings> xell)
			{
				xell.SetDescription("XeLL Reloaded helpers with guided auto-launch when needed.");
				xell.AddExample("xell", "boot");
				xell.AddExample("xell", "boot", "--quickboot");
				xell.AddExample("xell", "boot", "--quickboot", "--quickboot-target", "flash");
				xell.AddExample("xell", "info");
				xell.AddExample("xell", "kv", "export");
				xell.AddExample("xell", "kv", "export", "--raw");
				xell.AddExample("xell", "kv", "export", "--single");
				xell.AddCommand<XellBootCommand>("boot").WithDescription("Launch XeLL Reloaded, or confirm that XeLL is already running.");
				xell.AddCommand<XellInfoCommand>("info").WithDescription("Inspect the available XeLL HTTP services, endpoints, and detected CPU key.");
				ConfiguratorExtensions.AddBranch(xell, "kv", delegate(IConfigurator<CommandSettings> kv)
				{
					kv.SetDescription("XeLL keyvault export helpers with optional repeated verification.");
					kv.AddExample("xell", "kv", "export");
					kv.AddExample("xell", "kv", "export", "--output", "kv_backup.bin");
					kv.AddExample("xell", "kv", "export", "--raw");
					kv.AddExample("xell", "kv", "export", "--single");
					kv.AddCommand<XellKvExportCommand>("export").WithDescription("Export the keyvault, CPU key text, and a packaged verified backup set.");
				});
			});
			ConfiguratorExtensions.AddBranch(config, "nand", delegate(IConfigurator<CommandSettings> nand)
			{
				nand.SetDescription("XeLL-backed NAND dumping and verification.");
				nand.AddExample("nand", "dump");
				nand.AddExample("nand", "dump", "--single");
				nand.AddExample("nand", "dump", "--quickboot", "--yes");
				nand.AddExample("nand", "dump", "--output", "nand_backup.bin");
				nand.AddExample("nand", "dump", "--force-xell");
				nand.AddCommand<NandDumpCommand>("dump").WithDescription("Boot XeLL Reloaded, download the flash dump, verify repeated dumps, and package a safe backup.");
			});
			ConfiguratorExtensions.AddBranch(config, "popup", delegate(IConfigurator<CommandSettings> popup)
			{
				popup.SetDescription("Trainer-style console popup helpers.");
				popup.AddExample("popup", "show", "--title", "XeCLI", "--body", "Connected to console");
				popup.AddCommand<PopupMessageBoxCommand>("show").WithAlias("open").WithDescription("Show a native Xbox 360 popup.");
			});
			ConfiguratorExtensions.AddBranch(config, "spoof", delegate(IConfigurator<CommandSettings> spoof)
			{
				spoof.SetDescription("Private title-aware spoof helpers. BO2 supports local GT, local XUID, and remote spoofing.");
				spoof.AddExample("spoof", "reset", "--current-user", "--clear-remote");
				ConfiguratorExtensions.AddBranch(spoof, "gt", delegate(IConfigurator<CommandSettings> gt)
				{
					gt.SetDescription("Spoof the current in-game gamertag for supported titles. BO2 GT spoof is supported.");
					gt.AddExample("spoof", "gt", "show");
					gt.AddExample("spoof", "gt", "set", "--value", "NobodyEpic", "--notify");
					gt.AddExample("spoof", "gt", "set", "--current-user");
					gt.AddExample("spoof", "gt", "set", "--value", "ExampleTag");
					gt.AddCommand<GamertagSpoofShowCommand>("show").WithAlias("state").WithDescription("Show the current in-game gamertag.");
					gt.AddCommand<GamertagSpoofSetCommand>("set").WithAlias("apply").WithDescription("Apply a gamertag spoof to the running title.");
				});
				ConfiguratorExtensions.AddBranch(spoof, "xuid", delegate(IConfigurator<CommandSettings> xuid)
				{
					xuid.SetDescription("Spoof the current in-game XUID for supported titles. BO2 XUID spoof is supported.");
					xuid.AddExample("spoof", "xuid", "show");
					xuid.AddExample("spoof", "xuid", "set", "--current-user");
					xuid.AddExample("spoof", "xuid", "set", "--value", "5D83300C00000900");
					xuid.AddCommand<XuidSpoofShowCommand>("show").WithAlias("state").WithDescription("Show the current in-game XUID.");
					xuid.AddCommand<XuidSpoofSetCommand>("set").WithAlias("apply").WithDescription("Apply an XUID spoof to the running title.");
				});
				ConfiguratorExtensions.AddBranch(spoof, "remote", delegate(IConfigurator<CommandSettings> remote)
				{
					remote.SetDescription("Remote-player text spoofing for supported titles. BO2 remote slots are exposed as 2-12.");
					remote.AddExample("spoof", "remote", "list");
					remote.AddExample("spoof", "remote", "apply", "--slot", "2", "--text", "XeCLI");
					remote.AddExample("spoof", "remote", "apply", "--all", "--text", "NobodyEpic", "--notify");
					remote.AddExample("spoof", "remote", "apply", "--all", "--text", "XeCLI-{slot}");
					remote.AddCommand<RemoteSpoofListCommand>("list").WithAlias("show").WithDescription("List spoofable remote slots for the current title.");
					remote.AddCommand<RemoteSpoofApplyCommand>("apply").WithAlias("set").WithDescription("Overwrite one or more remote slot names.");
				});
				spoof.AddCommand<SpoofResetCommand>("reset").WithAlias("restore").WithDescription("Restore the in-game identity from the signed-in user or explicit values.");
			});
			ConfiguratorExtensions.AddBranch(config, "ftp", delegate(IConfigurator<CommandSettings> ftp)
			{
				ftp.SetDescription("FTP commands (alternate access).");
				ftp.AddExample("ftp", "list", "--path", "/Hdd1/");
				ftp.AddExample("ftp", "target", "--set", "<console-ip>", "--user", "<ftp-user>", "--pass", "<ftp-pass>");
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
			ConfiguratorExtensions.AddBranch(config, "save", delegate(IConfigurator<CommandSettings> save)
			{
				save.SetDescription("Profile and save-data helpers over FTP.");
				save.AddExample("save", "list", "--titleid", "FFFE07D1", "--device", "Hdd1");
				save.AddExample("save", "extract", "--titleid", "415608C3", "--out", ".\\saves");
				save.AddCommand<SaveListCommand>("list").WithAlias("ls").WithDescription("List save files for a title.");
				save.AddCommand<SaveExtractCommand>("extract").WithAlias("pull").WithDescription("Extract save files for a title.");
				save.AddCommand<SaveInjectCommand>("inject").WithAlias("push").WithAlias("put")
					.WithDescription("Upload save files for a title.");
			});
			ConfiguratorExtensions.AddBranch(config, "content", delegate(IConfigurator<CommandSettings> content)
			{
				content.SetDescription("Installed content management over FTP.");
				content.AddExample("content", "list", "--device", "Hdd1", "--show-types");
				content.AddCommand<ContentListCommand>("list").WithAlias("ls").WithDescription("List installed titles and content types.");
				content.AddCommand<ContentDeleteCommand>("delete").WithAlias("rm").WithDescription("Delete installed content for a title.");
			});
			ConfiguratorExtensions.AddBranch(config, "con", delegate(IConfigurator<CommandSettings> con)
			{
				con.SetDescription("Inspect and modify local CON/LIVE/PIRS packages.");
				con.AddExample("con", "info", ".\\package.con");
				con.AddExample("con", "verify", ".\\package.con");
				con.AddExample("con", "rehash", ".\\package.con");
				con.AddExample("con", "resign", ".\\package.con");
				con.AddExample("con", "fatx-path", ".\\package.con", "--fix-name");
				con.AddCommand<ConInfoCommand>("info").WithDescription("Show package metadata and derived FATX values.");
				con.AddCommand<ConVerifyCommand>("verify").WithDescription("Verify a CON signature when supported.");
				con.AddCommand<ConRehashCommand>("rehash").WithDescription("Save package headers and refresh STFS hashes.");
				con.AddCommand<ConResignCommand>("resign").WithDescription("Re-sign a CON package and verify the result.");
				con.AddCommand<ConMagicNameCommand>("magic-name").WithDescription("Print the package's FATX magic filename.");
				con.AddCommand<ConFatxPathCommand>("fatx-path").WithDescription("Print the destination FATX content path.");
			});
			ConfiguratorExtensions.AddBranch(config, "profile", delegate(IConfigurator<CommandSettings> profile)
			{
				profile.SetDescription("Inspect and edit local Xbox 360 profile packages.");
				profile.AddExample("profile", "info", ".\\E000000000000000.con");
				profile.AddExample("profile", "extract", ".\\E000000000000000.con", ".\\profile-files");
				profile.AddExample("profile", "account", "show", ".\\E000000000000000.con");
				profile.AddExample("profile", "account", "extract", ".\\E000000000000000.con", ".\\Account");
				profile.AddExample("profile", "account", "set-gamertag", ".\\E000000000000000.con", "ExampleTag");
				profile.AddExample("profile", "gpd", "list", ".\\E000000000000000.con");
				profile.AddExample("profile", "gpd", "extract", ".\\E000000000000000.con", ".\\FFFE07D1.gpd", "--dashboard");
				profile.AddExample("profile", "titles", "list", ".\\E000000000000000.con");
				profile.AddExample("profile", "achievements", "list", ".\\E000000000000000.con", "--titleid", "4D530805");
				profile.AddExample("profile", "achievements", "unlock", ".\\E000000000000000.con", "--titleid", "4D530805", "--achievementid", "0x00000001");
				profile.AddExample("profile", "settings", "get", ".\\E000000000000000.con", "0x10040006");
				profile.AddExample("profile", "settings", "set", ".\\E000000000000000.con", "0x10040006", "1337");
				profile.AddExample("profile", "avatar-colors", "get", ".\\E000000000000000.con");
				profile.AddExample("profile", "avatar-colors", "set", ".\\E000000000000000.con", "--hair", "0xFF22150D");
				profile.AddCommand<ProfileInfoCommand>("info").WithDescription("Show profile package contents and summary information.");
				profile.AddCommand<ProfileExtractCommand>("extract").WithDescription("Extract files from a profile package.");
				ConfiguratorExtensions.AddBranch(profile, "account", delegate(IConfigurator<CommandSettings> account)
				{
					account.SetDescription("Inspect and update account data inside a profile package.");
					account.AddCommand<ProfileAccountShowCommand>("show").WithDescription("Show decoded profile account information.");
					account.AddCommand<ProfileAccountExtractCommand>("extract").WithDescription("Extract the raw Account payload from a profile package.");
					account.AddCommand<ProfileAccountSetGamertagCommand>("set-gamertag").WithAlias("set").WithDescription("Update the profile account gamertag.");
				});
				ConfiguratorExtensions.AddBranch(profile, "gpd", delegate(IConfigurator<CommandSettings> gpd)
				{
					gpd.SetDescription("List and extract embedded dashboard/title GPD files.");
					gpd.AddCommand<ProfileGpdListCommand>("list").WithAlias("ls").WithDescription("List embedded dashboard and title GPD files.");
					gpd.AddCommand<ProfileGpdExtractCommand>("extract").WithDescription("Extract one dashboard or title GPD from the profile package.");
				});
				ConfiguratorExtensions.AddBranch(profile, "titles", delegate(IConfigurator<CommandSettings> titles)
				{
					titles.SetDescription("Inspect title records inside a profile package.");
					titles.AddCommand<ProfileTitlesListCommand>("list").WithAlias("ls").WithDescription("List title records from the dashboard GPD.");
				});
				ConfiguratorExtensions.AddBranch(profile, "achievements", delegate(IConfigurator<CommandSettings> achievements)
				{
					achievements.SetDescription("Inspect and edit achievement data inside a profile package.");
					achievements.AddCommand<ProfileAchievementsListCommand>("list").WithAlias("ls").WithDescription("List achievements for one title GPD.");
					achievements.AddCommand<ProfileAchievementsUnlockCommand>("unlock").WithDescription("Unlock one profile achievement and update title totals.");
					achievements.AddCommand<ProfileAchievementsLockCommand>("lock").WithDescription("Lock one profile achievement and update title totals.");
				});
				ConfiguratorExtensions.AddBranch(profile, "settings", delegate(IConfigurator<CommandSettings> profileSettings)
				{
					profileSettings.SetDescription("Inspect and edit profile settings stored in the dashboard GPD.");
					profileSettings.AddCommand<ProfileSettingsListCommand>("list").WithAlias("ls").WithDescription("List profile settings.");
					profileSettings.AddCommand<ProfileSettingsGetCommand>("get").WithDescription("Read a single profile setting.");
					profileSettings.AddCommand<ProfileSettingsSetCommand>("set").WithDescription("Create or update a single profile setting.");
				});
				ConfiguratorExtensions.AddBranch(profile, "avatar-colors", delegate(IConfigurator<CommandSettings> avatarColors)
				{
					avatarColors.SetDescription("Inspect and edit avatar color values stored in the dashboard profile setting blob.");
					avatarColors.AddCommand<ProfileAvatarColorsGetCommand>("get").WithDescription("Show avatar ARGB color values.");
					avatarColors.AddCommand<ProfileAvatarColorsSetCommand>("set").WithDescription("Update one or more avatar ARGB color values.");
				});
			});
			ConfiguratorExtensions.AddBranch(config, "xdbf", delegate(IConfigurator<CommandSettings> xdbf)
			{
				xdbf.SetDescription("Inspect and extract records from local GPD/XDBF files.");
				xdbf.AddExample("xdbf", "list", ".\\415608C3.gpd", "--show-sync");
				xdbf.AddExample("xdbf", "get", ".\\415608C3.gpd", "strings", "0x0000000000008000", "--out", ".\\title-name.bin");
				xdbf.AddExample("xdbf", "extract", ".\\415608C3.gpd", ".\\records");
				xdbf.AddExample("xdbf", "sync-status", ".\\415608C3.gpd");
				xdbf.AddCommand<XdbfListCommand>("list").WithAlias("ls").WithDescription("List records in a GPD/XDBF file.");
				xdbf.AddCommand<XdbfGetCommand>("get").WithDescription("Read a single record from a GPD/XDBF file.");
				xdbf.AddCommand<XdbfExtractCommand>("extract").WithDescription("Extract records to a directory.");
				xdbf.AddCommand<XdbfSyncStatusCommand>("sync-status").WithDescription("Show records that are pending sync.");
			});
			ConfiguratorExtensions.AddBranch(config, "xtaf", delegate(IConfigurator<CommandSettings> xtaf)
			{
				xtaf.SetDescription("XTAF is XeCLI's FATX image and storage manager.");
				xtaf.AddExample("xtaf", "devices");
				xtaf.AddExample("xtaf", "disks");
				xtaf.AddExample("xtaf", "partitions", "--image", ".\\hdd.img");
				xtaf.AddExample("xtaf", "partitions", "--disk", "2");
				xtaf.AddExample("xtaf", "scan", "--image", ".\\hdd.img");
				xtaf.AddExample("xtaf", "info", "--image", ".\\hdd.img", "--partition", "Content");
				xtaf.AddExample("xtaf", "info", "--image", ".\\hdd.img", "--offset", "0xB6600000");
				xtaf.AddExample("xtaf", "list", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/");
				xtaf.AddExample("xtaf", "list", "--image", ".\\hdd.img", "--offset", "0xB6600000", "--path", "/");
				xtaf.AddExample("xtaf", "mkdir", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI");
				xtaf.AddExample("xtaf", "put", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--in", ".\\readme.txt", "--overwrite");
				xtaf.AddExample("xtaf", "mv", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI/readme.txt", "--to", "/XeCLI/readme-old.txt");
				xtaf.AddExample("xtaf", "rm", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/XeCLI", "--recursive");
				xtaf.AddExample("xtaf", "format", "--disk", "2", "--auto-confirm");
				xtaf.AddExample("xtaf", "check", "--image", ".\\hdd.img", "--partition", "Content");
				xtaf.AddExample("xtaf", "repair", "--image", ".\\hdd.img", "--partition", "Content", "--auto-confirm");
				xtaf.AddExample("xtaf", "metadata", "backup", "--image", ".\\hdd.img", "--out", ".\\xtaf-meta");
				xtaf.AddExample("xtaf", "extract", "--image", ".\\hdd.img", "--partition", "Content", "--path", "/Content", "--out", ".\\Content");
				xtaf.AddExample("xtaf", "dump", "--image", ".\\hdd.img", "--offset", "0xB6600000", "--length", "0x10000000", "--out", ".\\partition-dump");
				xtaf.AddExample("xtaf", "dump", "--image", ".\\hdd.img", "--out", ".\\xtaf-dump");
				xtaf.AddCommand<FatmanDevicesCommand>("devices").WithDescription("List host storage devices visible to XTAF.");
				xtaf.AddCommand<FatmanDisksCommand>("disks").WithDescription("List Windows physical disks that XTAF can open directly.");
				xtaf.AddCommand<FatmanPartitionsCommand>("partitions").WithAlias("parts").WithDescription("Detect partitions inside a raw Xbox 360 HDD image or physical disk.");
				xtaf.AddCommand<FatmanScanCommand>("scan").WithAlias("probe").WithDescription("Scan an image or physical disk for plausible FATX/XTAF volume headers.");
				xtaf.AddCommand<FatmanInfoCommand>("info").WithDescription("Inspect one detected FATX partition.");
				xtaf.AddCommand<FatmanListCommand>("list").WithAlias("ls").WithDescription("List entries inside a FATX partition.");
				xtaf.AddCommand<FatmanFindCommand>("find").WithDescription("Search a FATX partition for matching paths.");
				xtaf.AddCommand<FatmanGetCommand>("get").WithDescription("Extract a single file from a FATX partition.");
				xtaf.AddCommand<FatmanCatCommand>("cat").WithDescription("Print a text file from a FATX partition.");
				xtaf.AddCommand<FatmanMkdirCommand>("mkdir").WithDescription("Create a directory inside a FATX image.");
				xtaf.AddCommand<FatmanPutCommand>("put").WithAlias("inject").WithDescription("Write a host file into a FATX image.");
				xtaf.AddCommand<FatmanMoveCommand>("mv").WithAlias("rename").WithDescription("Move or rename a FATX entry.");
				xtaf.AddCommand<FatmanDeleteCommand>("rm").WithAlias("del").WithDescription("Remove a FATX entry from the image.");
				xtaf.AddCommand<FatmanFormatCommand>("format").WithAlias("initialize").WithDescription("Format one or more FATX partitions inside an image or physical disk.");
				xtaf.AddCommand<FatmanCheckCommand>("check").WithAlias("verify").WithDescription("Scan a FATX partition for orphaned and invalid chain-map state.");
				xtaf.AddCommand<FatmanRepairCommand>("repair").WithDescription("Repair safe FATX chain-map issues such as orphaned allocations.");
				xtaf.AddCommand<FatmanExtractCommand>("extract").WithDescription("Extract a directory tree from a FATX partition.");
				xtaf.AddCommand<FatmanDumpCommand>("dump").WithDescription("Dump one or more raw FATX partitions to host files.");
				ConfiguratorExtensions.AddBranch(xtaf, "metadata", delegate(IConfigurator<CommandSettings> metadata)
				{
					metadata.SetDescription("Back up or restore low-level FATX and partition metadata regions.");
					metadata.AddCommand<FatmanMetadataBackupCommand>("backup").WithDescription("Back up disk-prefix and partition-header metadata regions.");
					metadata.AddCommand<FatmanMetadataRestoreCommand>("restore").WithDescription("Restore metadata regions from a prior XTAF metadata backup.");
				});
			}).WithAlias("fatman").WithAlias("fatx");
			ConfiguratorExtensions.AddBranch(config, "avatar", delegate(IConfigurator<CommandSettings> avatar)
			{
				avatar.SetDescription("Avatar item library and install helpers.");
				avatar.AddExample("avatar", "library", "show");
				avatar.AddExample("avatar", "games", "--search", "Black Ops");
				avatar.AddExample("avatar", "items", "--titleid", "415608C3", "--limit", "10");
				avatar.AddExample("avatar", "choose", "--search", "Black Ops");
				avatar.AddExample("avatar", "browse", "--remote");
				avatar.AddExample("avatar", "install", "--contentid", "000000080DF3B242CAE65A52415608C3", "--current-user");
				avatar.AddExample("avatar", "apply", "--titleid", "415608C3", "--all", "--current-user");
				ConfiguratorExtensions.AddBranch(avatar, "library", delegate(IConfigurator<CommandSettings> library)
				{
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
			ConfiguratorExtensions.AddBranch(config, "plugin", delegate(IConfigurator<CommandSettings> plugin)
			{
				plugin.SetDescription("DashLaunch plugin management.");
				plugin.AddExample("plugin", "list");
				plugin.AddExample("plugin", "enable", "--slot", "5", "--path", "Hdd:\\XDRPC.xex", "--backup");
				plugin.AddCommand<PluginListCommand>("list").WithAlias("ls").WithDescription("List configured DashLaunch plugins.");
				plugin.AddCommand<PluginEnableCommand>("enable").WithDescription("Set a DashLaunch plugin slot.");
				plugin.AddCommand<PluginDisableCommand>("disable").WithDescription("Clear a DashLaunch plugin slot.");
			});
			ConfiguratorExtensions.AddBranch(config, "god", delegate(IConfigurator<CommandSettings> god)
			{
				god.SetDescription("ISO to Games on Demand conversion.");
				god.AddExample("god", "info", ".\\game.iso");
				god.AddExample("god", "watch", ".\\incoming", "--dest", ".\\god", "--once");
				god.AddCommand<GodInfoCommand>("info").WithDescription("Inspect an ISO and show title metadata.");
				god.AddCommand<GodBuildCommand>("build").WithAlias("make").WithAlias("convert")
					.WithDescription("Convert an ISO into GOD parts.");
				god.AddCommand<GodWatchCommand>("watch").WithAlias("watchdog").WithDescription("Watch a folder and auto-convert ISOs.");
			});
			ConfiguratorExtensions.AddBranch(config, "ghidra", delegate(IConfigurator<CommandSettings> ghidra)
			{
				ghidra.SetDescription("Ghidra headless helpers.");
				ghidra.AddExample("ghidra", "config", "--path", "C:\\Tools\\ghidra", "--java", "C:\\Java");
				ghidra.AddExample("ghidra", "decompile", "--running", "--out", ".\\decomp");
				ghidra.AddCommand<GhidraConfigCommand>("config").WithDescription("Configure Ghidra paths.");
				ghidra.AddCommand<GhidraAnalyzeCommand>("analyze").WithDescription("Run headless analysis.");
				ghidra.AddCommand<GhidraDecompileCommand>("decompile").WithDescription("Decompile a module or XEX.");
				ghidra.AddCommand<GhidraVerifyCommand>("verify").WithDescription("Verify decompiler output for bad-instruction placeholders.");
			});
			ConfiguratorExtensions.AddBranch(config, "ida", delegate(IConfigurator<CommandSettings> ida)
			{
				ida.SetDescription("IDA headless helpers.");
				ida.AddExample("ida", "check");
				ida.AddExample("ida", "config", "--path", "C:\\Tools\\IDA", "--python", "python");
				ida.AddExample("ida", "analyze", "--running", "--out-db", ".\\title.i64");
				ida.AddExample("ida", "decompile", "--running", "--out", ".\\ida-decomp");
				ida.AddCommand<IdaConfigCommand>("config").WithDescription("Configure IDA paths and backend defaults.");
				ida.AddCommand<IdaCheckCommand>("check").WithAlias("doctor").WithDescription("Inspect IDA, idaxex, and idalib readiness.");
				ida.AddCommand<IdaAnalyzeCommand>("analyze").WithDescription("Run headless analysis and save an IDA database.");
				ida.AddCommand<IdaDecompileCommand>("decompile").WithDescription("Decompile a module or XEX via IDA.");
				ida.AddCommand<IdaVerifyCommand>("verify").WithDescription("Verify IDA decompiler output for common failure placeholders.");
			});
		});
		return await commandApp.RunAsync(args);
	}

	private static string ResolveLanguageCode(string[] args, string? requestedLanguage)
	{
		if (!string.IsNullOrWhiteSpace(requestedLanguage))
		{
			return LocalizedText.NormalizeLanguageCode(requestedLanguage);
		}
		string environmentVariable = Environment.GetEnvironmentVariable("XECLI_LANG");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			return LocalizedText.NormalizeLanguageCode(environmentVariable);
		}
		CliConfig cliConfig = CliConfig.Load();
		if (!string.IsNullOrWhiteSpace(cliConfig.UiLanguage))
		{
			return LocalizedText.NormalizeLanguageCode(cliConfig.UiLanguage);
		}
		return LocalizedText.GetDefaultLanguageCode();
	}

	private static string[] NormalizeArgs(string[] args)
	{
		if (args.Length == 0)
		{
			return args;
		}
		if (string.Equals(args[0], "spoof", StringComparison.OrdinalIgnoreCase))
		{
			return NormalizeSpoofArgs(args);
		}
		if (string.Equals(args[0], "title", StringComparison.OrdinalIgnoreCase) && args.Skip(1).All((string a) => a.StartsWith("-", StringComparison.Ordinal)))
		{
			return new string[2] { "title", "active" }.Concat(args.Skip(1)).ToArray();
		}
		if (IsHelpToken(args[0]))
		{
			if (args.Length == 1)
			{
				return new string[1] { "--help" };
			}
			return args.Skip(1).Concat(new string[1] { "--help" }).ToArray();
		}
		int num = args.Length - 1;
		if (IsHelpToken(args[num]))
		{
			return args.Take(num).Concat(new string[1] { "--help" }).ToArray();
		}
		return args;
	}

	private static string[] NormalizeSpoofArgs(string[] args)
	{
		if (args.Length == 1)
		{
			return args;
		}
		string value = args[1];
		if (Matches(value, "gamertag", "gt", "name"))
		{
			return NormalizeSpoofIdentityArgs(args, "gt");
		}
		if (Matches(value, "xuid"))
		{
			return NormalizeSpoofIdentityArgs(args, "xuid");
		}
		if (Matches(value, "remote", "remotes"))
		{
			return NormalizeSpoofRemoteArgs(args);
		}
		return args;
	}

	private static string[] NormalizeSpoofIdentityArgs(string[] args, string canonicalTopic)
	{
		if (args.Length == 2)
		{
			return new string[3] { "spoof", canonicalTopic, "show" };
		}
		string text = args[2];
		if (text.StartsWith("-", StringComparison.Ordinal))
		{
			if (!args.Skip(2).Any((string arg) => arg.Equals("--value", StringComparison.OrdinalIgnoreCase) || arg.Equals("--current-user", StringComparison.OrdinalIgnoreCase) || arg.Equals("--notify", StringComparison.OrdinalIgnoreCase) || arg.Equals("--notify-icon", StringComparison.OrdinalIgnoreCase) || arg.Equals("--notify-logo", StringComparison.OrdinalIgnoreCase)))
			{
				return new string[3] { "spoof", canonicalTopic, "show" }.Concat(args.Skip(2)).ToArray();
			}
			return new string[3] { "spoof", canonicalTopic, "set" }.Concat(args.Skip(2)).ToArray();
		}
		if (Matches(text, "show", "state", "set", "apply"))
		{
			return new string[2] { "spoof", canonicalTopic }.Concat(args.Skip(2)).ToArray();
		}
		return new string[4] { "spoof", canonicalTopic, "set", "--value" }.Concat(args.Skip(2)).ToArray();
	}

	private static string[] NormalizeSpoofRemoteArgs(string[] args)
	{
		if (args.Length == 2)
		{
			return new string[3] { "spoof", "remote", "list" };
		}
		string text = args[2];
		if (text.StartsWith("-", StringComparison.Ordinal))
		{
			if (!args.Skip(2).Any((string arg) => arg.Equals("--slot", StringComparison.OrdinalIgnoreCase) || arg.Equals("--all", StringComparison.OrdinalIgnoreCase) || arg.Equals("--text", StringComparison.OrdinalIgnoreCase) || arg.Equals("--notify", StringComparison.OrdinalIgnoreCase) || arg.Equals("--notify-icon", StringComparison.OrdinalIgnoreCase) || arg.Equals("--notify-logo", StringComparison.OrdinalIgnoreCase)))
			{
				return new string[3] { "spoof", "remote", "list" }.Concat(args.Skip(2)).ToArray();
			}
			return new string[3] { "spoof", "remote", "apply" }.Concat(args.Skip(2)).ToArray();
		}
		if (Matches(text, "list", "show", "set", "apply"))
		{
			return new string[2] { "spoof", "remote" }.Concat(args.Skip(2)).ToArray();
		}
		return new string[5] { "spoof", "remote", "apply", "--all", "--text" }.Concat(args.Skip(2)).ToArray();
	}

	private static bool Matches(string value, params string[] candidates)
	{
		return candidates.Any((string candidate) => value.Equals(candidate, StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsHelpToken(string arg)
	{
		if (!arg.Equals("help", StringComparison.OrdinalIgnoreCase))
		{
			return arg.Equals("?", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsVersionRequest(string[] args)
	{
		if (args.Length != 1)
		{
			return false;
		}
		string text = args[0];
		if (!text.Equals("--version", StringComparison.OrdinalIgnoreCase) && !text.Equals("-v", StringComparison.OrdinalIgnoreCase))
		{
			return text.Equals("version", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string GetApplicationVersion()
	{
		Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
		return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
	}

}
