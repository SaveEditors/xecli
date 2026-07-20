using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class TerminalCommand : AsyncCommand<TerminalCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--opacity <PERCENT>")]
		[LocalizedDescription("Window opacity percentage (55-100).")]
		public int? OpacityPercent { get; init; }

		[CommandOption("--background <PATH>")]
		[LocalizedDescription("Optional background image path for the terminal shell.")]
		public string? BackgroundPath { get; init; }

		[CommandOption("--theme <NAME>")]
		[LocalizedDescription("Terminal theme: matrix, cyan, amber, mono, carbon, ruby, xenon, violet, aurora, steel, sunset, enterprise, operations, debugger, command, signal, graphite, alpine, or console.")]
		public string? Theme { get; init; }

		[CommandOption("--no-telemetry")]
		[LocalizedDescription("Disable live telemetry polling in the terminal shell.")]
		public bool NoTelemetry { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (settings.Json)
		{
			CliOutput.EmitJsonError(new CliErrorEnvelope(
				"Terminal JSON mode is unavailable",
				"The terminal command opens an interactive GUI and cannot emit a bounded JSON result.",
				"TERMINAL_JSON_UNSUPPORTED",
				new[] { "Run `rgh terminal` without --json, or invoke a non-interactive command with --json." }));
			return 1;
		}

		if (!OperatingSystem.IsWindows())
		{
			AnsiConsole.MarkupLine("[red]The XeCLI terminal UI is only available on Windows.[/]");
			return 1;
		}
		XeCliTerminalForm.TerminalOptions options = BuildTerminalOptions(settings);
		await ShowTerminalAsync(options);
		return 0;
	}

	internal static XeCliTerminalForm.TerminalOptions BuildTerminalOptions(Settings settings)
	{
		CliConfig.TryLoad(out CliConfig cliConfig);
		TargetProfileStoreData targetStore = TargetProfileStore.Load();
		if (!TargetProfileStore.TryResolveProfile(targetStore, cliConfig, settings.Profile, out TargetProfileRecord? currentProfile, out string profileError))
		{
			throw new InvalidOperationException(profileError);
		}
		bool hasExplicitIp = !string.IsNullOrWhiteSpace(settings.Ip);
		string? explicitIp = settings.Ip?.Trim();
		string ip = hasExplicitIp ? explicitIp! : (currentProfile?.Ip ?? (string.IsNullOrWhiteSpace(cliConfig.DefaultIp) ? "192.168.1.1" : cliConfig.DefaultIp.Trim()));
		int port = settings.Port ?? (hasExplicitIp ? 730 : currentProfile?.Port ?? cliConfig.DefaultPort ?? 730);
		int timeoutMs = settings.TimeoutMs ?? CliPreferences.GetConnectionTimeoutMs(cliConfig);
		string fileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "rgh.exe";
		const int SafeOpacityPercent = 100;
		if (settings.OpacityPercent is { } requested && requested < SafeOpacityPercent)
		{
			AnsiConsole.MarkupLine("[yellow]Translucent opacity is temporarily disabled; the terminal shell will stay fully opaque to avoid rendering glitches.[/]");
		}
		return new XeCliTerminalForm.TerminalOptions
		{
			ExePath = fileName,
			Ip = ip,
			Port = port,
			TimeoutMs = timeoutMs,
			OpacityPercent = SafeOpacityPercent,
			BackgroundPath = settings.BackgroundPath,
			TelemetryEnabled = !settings.NoTelemetry && cliConfig.TerminalTelemetryEnabled != false,
			DiscordRichPresenceEnabled = cliConfig.DiscordRichPresenceEnabled != false,
			AutoConnect = cliConfig.TerminalAutoConnect == true,
			ThemeName = XeCliTerminalForm.NormalizeTerminalThemeName(settings.Theme ?? cliConfig.TerminalTheme)
		};
	}

	private static Task ShowTerminalAsync(XeCliTerminalForm.TerminalOptions options)
	{
		TaskCompletionSource<object?> taskCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		Thread thread = new Thread(new ThreadStart(delegate
		{
			Exception? uiThreadException = null;
			ThreadExceptionEventHandler handler = delegate(object _, ThreadExceptionEventArgs e)
			{
				uiThreadException ??= e.Exception;
			};
			try
			{
				ConfigureWinFormsApplication();
				Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
				Application.ThreadException += handler;
				using XeCliTerminalForm xeCliTerminalForm = new XeCliTerminalForm(options);
				xeCliTerminalForm.ShowDialog();
				if (uiThreadException != null)
				{
					taskCompletionSource.TrySetException(uiThreadException);
				}
				else
				{
					taskCompletionSource.TrySetResult(null);
				}
			}
			catch (Exception exception)
			{
				taskCompletionSource.TrySetException(uiThreadException ?? exception);
			}
			finally
			{
				Application.ThreadException -= handler;
			}
		}));
		thread.IsBackground = true;
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		return taskCompletionSource.Task;
	}

	private static void ConfigureWinFormsApplication()
	{
		try
		{
			Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		}
		catch (InvalidOperationException)
		{
		}

		try
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
		}
		catch (InvalidOperationException)
		{
		}
	}
}
