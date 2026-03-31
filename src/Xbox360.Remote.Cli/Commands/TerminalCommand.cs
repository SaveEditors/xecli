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

		[CommandOption("--no-telemetry")]
		[LocalizedDescription("Disable live telemetry polling in the terminal shell.")]
		public bool NoTelemetry { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!OperatingSystem.IsWindows())
		{
			AnsiConsole.MarkupLine("[red]The XeCLI terminal UI is only available on Windows.[/]");
			return 1;
		}
		CliConfig cliConfig = CliConfig.Load();
		string ip = !string.IsNullOrWhiteSpace(settings.Ip) ? settings.Ip.Trim() : (string.IsNullOrWhiteSpace(cliConfig.DefaultIp) ? "192.168.1.1" : cliConfig.DefaultIp.Trim());
		int port = settings.Port ?? cliConfig.DefaultPort ?? 730;
		int timeoutMs = settings.TimeoutMs ?? 5000;
		string fileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "rgh.exe";
		const int SafeOpacityPercent = 100;
		if (settings.OpacityPercent is { } requested && requested < SafeOpacityPercent)
		{
			AnsiConsole.MarkupLine("[yellow]Translucent opacity is temporarily disabled; the terminal shell will stay fully opaque to avoid rendering glitches.[/]");
		}
		XeCliTerminalForm.TerminalOptions options = new XeCliTerminalForm.TerminalOptions
		{
			ExePath = fileName,
			Ip = ip,
			Port = port,
			TimeoutMs = timeoutMs,
			OpacityPercent = SafeOpacityPercent,
			BackgroundPath = settings.BackgroundPath,
			TelemetryEnabled = !settings.NoTelemetry
		};
		await ShowTerminalAsync(options);
		return 0;
	}

	private static Task ShowTerminalAsync(XeCliTerminalForm.TerminalOptions options)
	{
		TaskCompletionSource<object?> taskCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		Thread thread = new Thread(new ThreadStart(delegate
		{
			Exception? uiThreadException = null;
			ThreadExceptionEventHandler handler = delegate(object? _, ThreadExceptionEventArgs e)
			{
				uiThreadException ??= e.Exception;
			};
			try
			{
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
}
