using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace Xbox360.Remote.Cli.Commands;

internal sealed record XellSessionResult(XellHttpEndpoint Endpoint, XellConsoleMode InitialMode, XellBootRequest? BootRequest);

internal static class XellWorkflowHelpers
{
	private sealed record DashboardSnapshot(bool Reachable, string? RunningXex);

	private static readonly TimeSpan InitialXellProbeDelay = TimeSpan.FromSeconds(6.0);

	private static readonly TimeSpan VerificationReturnTimeout = TimeSpan.FromSeconds(45.0);

	private static readonly TimeSpan VerificationPollInterval = TimeSpan.FromSeconds(2.0);

	private static string BuildBootTimeoutPrefix()
	{
		return "Timed out waiting " + XellHelpers.DefaultBootTimeoutSeconds + " seconds for the XeLL web server. ";
	}

	public static async Task<XellSessionResult> EnsureXellSessionAsync((string Ip, int Port, int TimeoutMs) target, XellCommandSettings settings, string actionDescription, CancellationToken cancellationToken)
	{
		AnsiConsole.MarkupLine("[grey]Mode detection:[/] probing dashboard and XeLL services on [cyan]" + Markup.Escape(target.Ip) + "[/]");
		XellDetectionResult xellDetectionResult = await XellHelpers.DetectConsoleModeAsync(target, cancellationToken);
		AnsiConsole.MarkupLine("[grey]Detected mode:[/] " + FormatMode(xellDetectionResult.Mode));
		if (xellDetectionResult.Mode == XellConsoleMode.Xell && xellDetectionResult.Endpoint != null)
		{
			AnsiConsole.MarkupLine("[grey]XeLL detected at[/] [springgreen3_1]" + Markup.Escape(xellDetectionResult.Endpoint.Ip) + "[/]");
			return new XellSessionResult(xellDetectionResult.Endpoint, xellDetectionResult.Mode, null);
		}
		if (xellDetectionResult.Mode != XellConsoleMode.Dashboard)
		{
			throw new InvalidOperationException("This command requires XeLL, but the console was not reachable on XBDM or the XeLL web interface. Boot XeLL manually with the eject button and rerun the command.");
		}
		ConfirmFirstXellLaunch(target.Ip, actionDescription, settings);
		return await BootDashboardIntoXellAsync(target, settings, xellDetectionResult.Mode, cancellationToken);
	}

	public static async Task<XellSessionResult> BootDashboardIntoXellAsync((string Ip, int Port, int TimeoutMs) target, XellCommandSettings settings, XellConsoleMode initialMode, CancellationToken cancellationToken)
	{
		XellBootRequest? xellBootRequest = null;
		await using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
		{
			Host = target.Ip,
			Port = target.Port,
			TimeoutMs = Math.Max(target.TimeoutMs, 5000)
		}, cancellationToken))
		{
			xellBootRequest = await XellHelpers.RequestXellBootAsync(client, settings, target.Ip, cancellationToken);
		}
		XellHttpEndpoint endpoint = await AwaitBootedXellAsync(target, settings, xellBootRequest, cancellationToken);
		return new XellSessionResult(endpoint, initialMode, xellBootRequest);
	}

	public static async Task<XellHttpEndpoint> RecoverVerificationSessionAsync((string Ip, int Port, int TimeoutMs) target, XellCommandSettings settings, string previousXellIp, XellBootRequest? bootRequest, CancellationToken cancellationToken)
	{
		DateTime deadlineUtc = DateTime.UtcNow + VerificationReturnTimeout;
		while (DateTime.UtcNow < deadlineUtc)
		{
			XellDetectionResult xellDetectionResult = await XellHelpers.DetectConsoleModeAsync(target, cancellationToken);
			if (xellDetectionResult.Mode == XellConsoleMode.Xell && xellDetectionResult.Endpoint != null)
			{
				return xellDetectionResult.Endpoint;
			}
			if (xellDetectionResult.Mode == XellConsoleMode.Dashboard)
			{
				OperationFeedback.WriteWarning("XeLL reboot returned to dashboard", "The helper-loaded payload rebooted cleanly to the dashboard. Re-launching XeLL automatically for verification.");
				return await RelaunchDashboardIntoXellAsync(target, settings, bootRequest, cancellationToken);
			}
			await Task.Delay(VerificationPollInterval, cancellationToken);
		}
		XellHttpEndpoint xellHttpEndpoint = await XellHelpers.WaitForXellAsync(previousXellIp, TimeSpan.FromSeconds(15.0), scanSubnet: true, cancellationToken);
		if (xellHttpEndpoint != null)
		{
			return xellHttpEndpoint;
		}
		throw new TimeoutException("Timed out waiting for the console to return to XeLL or the dashboard after rebooting for verification.");
	}

	private static async Task<XellHttpEndpoint> RelaunchDashboardIntoXellAsync((string Ip, int Port, int TimeoutMs) target, XellCommandSettings settings, XellBootRequest? bootRequest, CancellationToken cancellationToken)
	{
		XellBootRequest? xellBootRequest = null;
		await using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
		{
			Host = target.Ip,
			Port = target.Port,
			TimeoutMs = Math.Max(target.TimeoutMs, 5000)
		}, cancellationToken))
		{
			if (bootRequest != null && await XellHelpers.TryRelaunchBootRequestAsync(client, bootRequest, cancellationToken))
			{
				if (bootRequest.UsedQuickBoot && !string.IsNullOrWhiteSpace(bootRequest.QuickBootLauncherPath))
				{
					AnsiConsole.MarkupLine("[grey]Verification relaunch:[/] [springgreen3_1]reusing staged QuickBoot XeLL launcher[/] [grey](" + Markup.Escape(bootRequest.QuickBootLauncherPath) + ")[/]");
				}
				else if (!bootRequest.ForceDirectBoot && !string.IsNullOrWhiteSpace(bootRequest.LauncherPath))
				{
					AnsiConsole.MarkupLine("[grey]Verification relaunch:[/] [springgreen3_1]reusing staged XellLaunch helper[/] [grey](" + Markup.Escape(bootRequest.LauncherPath) + ")[/]");
				}
				else if (bootRequest.ForceDirectBoot)
				{
					AnsiConsole.MarkupLine("[grey]Verification relaunch:[/] [gold1]reusing direct XeLL reboot path[/]");
				}
				xellBootRequest = bootRequest;
			}
			else
			{
				xellBootRequest = await XellHelpers.RequestXellBootAsync(client, settings, target.Ip, cancellationToken);
			}
		}
		return await AwaitBootedXellAsync(target, settings, xellBootRequest, cancellationToken);
	}

	private static async Task<XellHttpEndpoint> AwaitBootedXellAsync((string Ip, int Port, int TimeoutMs) target, XellCommandSettings settings, XellBootRequest? xellBootRequest, CancellationToken cancellationToken)
	{
		AnsiConsole.MarkupLine("[grey]Waiting for XeLL HTTP:[/] polling port 80 for up to [white]" + XellHelpers.DefaultBootTimeoutSeconds + "s[/]");
		AnsiConsole.MarkupLine("[grey]XeLL note:[/] when the custom payload is on-screen, use the payload's displayed IP to confirm where XeCLI should probe. The helper-loaded XeCLI path should reach that screen much faster than stock XeLL.");
		AnsiConsole.MarkupLine("[grey]Settle delay:[/] waiting [white]" + InitialXellProbeDelay.TotalSeconds.ToString("0") + "s[/] before the first probe so the payload network stack can finish coming up.");
		await Task.Delay(InitialXellProbeDelay, cancellationToken);
		XellHttpEndpoint xellHttpEndpoint = await XellHelpers.WaitForXellAsync(target.Ip, XellHelpers.DefaultBootTimeout, scanSubnet: true, cancellationToken);
		if (xellHttpEndpoint == null)
		{
			string text = await DiagnoseBootFailureAsync(target, settings, xellBootRequest, cancellationToken);
			throw new TimeoutException(text);
		}
		AnsiConsole.MarkupLine("[grey]XeLL detected at[/] [springgreen3_1]" + Markup.Escape(xellHttpEndpoint.Ip) + "[/]");
		XellCustomStatus? xellCustomStatus = await XellHelpers.TryReadCustomStatusAsync(xellHttpEndpoint, cancellationToken);
		if (xellCustomStatus != null)
		{
			AnsiConsole.MarkupLine("[grey]XeCLI payload:[/] [white]" + Markup.Escape(xellCustomStatus.Title ?? xellCustomStatus.State) + "[/]");
			AnsiConsole.MarkupLine("[grey]State:[/] [springgreen3_1]" + Markup.Escape(xellCustomStatus.State) + "[/] [grey]|[/] [grey]Heartbeat:[/] [white]" + Markup.Escape(xellCustomStatus.HeartbeatTicks?.ToString() ?? "n/a") + "[/]");
		}
		return xellHttpEndpoint;
	}

	public static void ConfirmFirstXellLaunch(string ip, string actionDescription, XellCommandSettings settings)
	{
		if (settings.Yes)
		{
			return;
		}
		if (Console.IsInputRedirected)
		{
			throw new InvalidOperationException("Interactive confirmation is required before launching XeLL automatically. Rerun with --yes, run the command interactively, or boot XeLL manually and rerun it.");
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[white]Field[/]"));
		table.AddColumn(new TableColumn("[white]Value[/]"));
		table.AddRow("[white]Console[/]", "[springgreen3_1]" + Markup.Escape(ip) + "[/]");
		table.AddRow("[white]Action[/]", "[grey]" + Markup.Escape(actionDescription) + "[/]");
		table.AddRow("[white]Writes[/]", "[green]No NAND writes[/] [grey]XeCLI will not modify the console NAND.[/]");
		string text = XellQuickBootHelpers.DescribeConsoleFileChanges(settings);
		if (!string.IsNullOrWhiteSpace(text))
		{
			table.AddRow("[white]Console Files[/]", text);
		}
		text = settings.ForceXell ? "[gold1]Force direct XeLL reboot[/]" : "[cyan]Auto (XellLaunch when present, direct XeLL reboot otherwise)[/]";
		if (XellQuickBootHelpers.IsEnabled(settings))
		{
			text = XellQuickBootHelpers.DescribeBootPath(settings);
		}
		if (!string.IsNullOrWhiteSpace(settings.LauncherPath))
		{
			text = "[cyan]Use specified launcher[/] [grey](" + Markup.Escape(settings.LauncherPath) + ")[/]";
		}
		if (!string.IsNullOrWhiteSpace(settings.StageLauncherPath) && !XellQuickBootHelpers.IsEnabled(settings))
		{
			text = "[cyan]Stage launcher and boot XeLL[/]";
		}
		table.AddRow("[white]Boot Path[/]", text);
		if (!string.IsNullOrWhiteSpace(settings.StageXellBinPath))
		{
			table.AddRow("[white]XeLL Bin[/]", "[cyan]Stage local xell.bin beside the helper before booting[/]");
		}
		AnsiConsole.Write(table);
		if (!AnsiConsole.Confirm("Launch XeLL now?"))
		{
			throw new OperationCanceledException("XeLL launch cancelled by user.");
		}
	}

	public static string FormatMode(XellConsoleMode mode)
	{
		return mode switch
		{
			XellConsoleMode.Dashboard => "[cyan]dashboard[/]",
			XellConsoleMode.Xell => "[springgreen3_1]XeLL[/]",
			_ => "[gold1]unknown[/]"
		};
	}

	public static void WriteManualLaunchGuidance(string commandExample)
	{
		AnsiConsole.MarkupLine("[grey]Fallback:[/] if XeLL cannot be launched automatically, boot XeLL manually with the eject button and rerun [cyan]" + Markup.Escape(commandExample) + "[/].");
		AnsiConsole.MarkupLine("[grey]Tip:[/] stage a helper with [cyan]--stage-launcher[/] and optionally a matching [cyan]--stage-xell-bin[/] when flash fallback is unreliable.");
		AnsiConsole.MarkupLine("[grey]Alt:[/] try [cyan]--quickboot[/] to auto-install a QuickBoot XeLL launcher and dashboard shortcut when direct helper booting is unreliable.");
		AnsiConsole.MarkupLine("[grey]USB:[/] XeCLI blocks XeLL launch when removable USB is attached because some consoles hang at [white]Fat mount uda0[/]. Unplug USB and retry, or use [cyan]--allow-usb[/] only when XeLL must read from USB on purpose.");
	}

	private static async Task<string> DiagnoseBootFailureAsync((string Ip, int Port, int TimeoutMs) target, XellCommandSettings settings, XellBootRequest? bootRequest, CancellationToken cancellationToken)
	{
		DashboardSnapshot dashboardSnapshot = await TryGetDashboardSnapshotAsync(target, cancellationToken);
		XellHttpPortDiagnostic xellHttpPortDiagnostic = await XellHelpers.DiagnoseHttpPortAsync(target.Ip, TimeSpan.FromSeconds(3.0), cancellationToken);
		string text;
		if (dashboardSnapshot.Reachable)
		{
			if (LooksLikeXellLaunchHelper(dashboardSnapshot.RunningXex, settings, bootRequest))
			{
				text = BuildHelperFailureMessage(dashboardSnapshot.RunningXex!, bootRequest?.Preflight);
				text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
				return AppendUsbHangHint(text, bootRequest);
			}
			if (!string.IsNullOrWhiteSpace(dashboardSnapshot.RunningXex))
			{
				if (bootRequest?.UsedQuickBoot == true)
				{
					text = BuildBootTimeoutPrefix() + "The QuickBoot XeLL launcher ran, but the console stayed on a normal XEX instead of transitioning to XeLL. QuickBoot target: " + (bootRequest.QuickBootTargetPath ?? "unknown") + ". Running XEX: " + dashboardSnapshot.RunningXex;
					text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
					return AppendUsbHangHint(text, bootRequest);
				}
				if (settings.ForceXell)
				{
					text = BuildBootTimeoutPrefix() + "The tray-assisted direct reboot path returned to a normal XEX instead of XeLL: " + dashboardSnapshot.RunningXex;
					text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
					return AppendUsbHangHint(text, bootRequest);
				}
				text = BuildBootTimeoutPrefix() + "The console stayed on a normal XEX instead of transitioning to XeLL: " + dashboardSnapshot.RunningXex;
				text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
				return AppendUsbHangHint(text, bootRequest);
			}
			if (bootRequest?.UsedQuickBoot == true)
			{
				text = BuildBootTimeoutPrefix() + "XBDM stayed reachable after the QuickBoot XeLL launcher ran, but XeCLI could not confirm a helper transition or a XeLL HTTP endpoint. QuickBoot target: " + (bootRequest.QuickBootTargetPath ?? "unknown") + ".";
				text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
				return AppendUsbHangHint(text, bootRequest);
			}
			text = BuildBootTimeoutPrefix() + "XBDM stayed reachable, but XeCLI could not confirm a helper transition or a XeLL HTTP endpoint.";
			text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
			return AppendUsbHangHint(text, bootRequest);
		}
		if (settings.ForceXell)
		{
			text = BuildBootTimeoutPrefix() + "The console dropped off both XBDM and HTTP after the direct reboot request, which looks like a hang, crash, or long reboot rather than a clean XeLL boot.";
			text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
			return AppendUsbHangHint(text, bootRequest);
		}
		if (bootRequest?.UsedQuickBoot == true)
		{
			text = BuildBootTimeoutPrefix() + "The console dropped off both XBDM and HTTP after the QuickBoot XeLL launcher ran, which looks like a hang, crash, or long reboot. QuickBoot target: " + (bootRequest.QuickBootTargetPath ?? "unknown") + ".";
			text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
			return AppendUsbHangHint(text, bootRequest);
		}
		if (bootRequest?.Preflight != null && bootRequest.Preflight.CheckedCommonFallbackPaths && !bootRequest.Preflight.SiblingXellBinPresent && bootRequest.Preflight.AlternateXellBinPathsPresent.Count == 0)
		{
			text = BuildBootTimeoutPrefix() + "The console dropped off both XBDM and HTTP after the XeLL launch request, which looks like a hang, crash, or long reboot. XeCLI had already confirmed there was no xell.bin beside the helper or in the common HDD/USB fallback locations, so flash XeLL fallback is the most likely crash path.";
			text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
			return AppendUsbHangHint(text, bootRequest);
		}
		text = BuildBootTimeoutPrefix() + "The console dropped off both XBDM and HTTP after the XeLL launch request, which looks like a hang, crash, or long reboot. If you are using XellLaunch, stage a matching xell.bin beside the helper or verify the flash XeLL fallback on this console.";
		text = AppendHttpPortHint(text, xellHttpPortDiagnostic);
		return AppendUsbHangHint(text, bootRequest);
	}

	private static string BuildHelperFailureMessage(string runningXex, XellLaunchPreflight? preflight)
	{
		string text = BuildBootTimeoutPrefix() + "The console switched into the XeLL launch helper (" + runningXex + ") but never exposed XeLL HTTP.";
		if (preflight == null)
		{
			return text + " This usually means the helper could not load xell.bin from disk or the flash XeLL fallback failed.";
		}
		if (preflight.SiblingXellBinPresent)
		{
			return text + " XeCLI confirmed xell.bin beside the helper at " + preflight.SiblingXellBinPath + ", so the staged binary is likely incompatible with this console or the helper stalled before handing off to XeLL.";
		}
		if (preflight.AlternateXellBinPathsPresent.Count > 0)
		{
			return text + " XeCLI did not find xell.bin beside the helper, but it did find fallback candidate(s) at " + string.Join(", ", preflight.AlternateXellBinPathsPresent) + ". The helper either could not use those locations or the discovered XeLL binary is incompatible with this console.";
		}
		if (preflight.CheckedCommonFallbackPaths)
		{
			return text + " XeCLI did not find xell.bin beside the helper or in the common HDD/USB fallback locations, so the helper almost certainly fell back to flash XeLL and that fallback failed.";
		}
		return text + " XeCLI did not find xell.bin beside the helper. This helper either requires an adjacent XeLL binary or failed before it could hand off to XeLL.";
	}

	private static string AppendUsbHangHint(string message, XellBootRequest? bootRequest)
	{
		IReadOnlyList<string>? usbStorage = bootRequest?.UsbStorage;
		if (usbStorage == null || usbStorage.Count == 0)
		{
			return message;
		}
		return message + " External USB was connected before launch (" + string.Join(", ", usbStorage) + "). XeLL starts HTTP before it mounts storage, so a later freeze at 'Fat mount uda0' points at removable USB. USB is optional unless XeLL needs to read a file from it. XeCLI now blocks this by default unless --allow-usb is set.";
	}

	private static string AppendHttpPortHint(string message, XellHttpPortDiagnostic diagnostic)
	{
		if (diagnostic == null || string.IsNullOrWhiteSpace(diagnostic.Summary))
		{
			return message;
		}
		if (diagnostic.PortOpen && !diagnostic.HttpResponseReceived)
		{
			return message + " Port 80 is still accepting TCP connections, so the payload/network stack is at least partially alive, but the XeLL HTTP handler is not serving valid pages yet. " + diagnostic.Summary;
		}
		if (diagnostic.HttpResponseReceived)
		{
			return message + " Port 80 responded during the post-timeout probe. " + diagnostic.Summary;
		}
		return message + " " + diagnostic.Summary;
	}

	private static bool LooksLikeXellLaunchHelper(string? runningXex, XellCommandSettings settings, XellBootRequest? bootRequest)
	{
		if (string.IsNullOrWhiteSpace(runningXex))
		{
			return false;
		}
		string text = runningXex.Trim();
		if (text.IndexOf("xelllaunch", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		if (!string.IsNullOrWhiteSpace(bootRequest?.LauncherPath) && text.IndexOf(System.IO.Path.GetFileName(bootRequest.LauncherPath), StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		if (!string.IsNullOrWhiteSpace(settings.LauncherPath) && text.IndexOf(System.IO.Path.GetFileName(settings.LauncherPath), StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		return !string.IsNullOrWhiteSpace(settings.StageLauncherPath) && text.EndsWith("default.xex", StringComparison.OrdinalIgnoreCase);
	}

	private static async Task<DashboardSnapshot> TryGetDashboardSnapshotAsync((string Ip, int Port, int TimeoutMs) target, CancellationToken cancellationToken)
	{
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancellationTokenSource.CancelAfter(Math.Clamp(target.TimeoutMs, 1500, 4000));
		try
		{
			await using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
			{
				Host = target.Ip,
				Port = target.Port,
				TimeoutMs = Math.Clamp(target.TimeoutMs, 1500, 4000)
			}, cancellationTokenSource.Token);
			string? text = null;
			try
			{
				text = await client.GetRunningXexPathAsync(null, cancellationTokenSource.Token);
			}
			catch
			{
			}
			return new DashboardSnapshot(Reachable: true, text);
		}
		catch
		{
			return new DashboardSnapshot(Reachable: false, null);
		}
	}
}
