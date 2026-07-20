using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace Xbox360.Remote.Cli.Commands;

internal enum XellConsoleMode
{
	Unknown,
	Dashboard,
	Xell
}

internal sealed record XellDetectionResult(XellConsoleMode Mode, string PreferredIp, XellHttpEndpoint? Endpoint);

internal sealed record XellHttpEndpoint(string Ip, string BaseUrl, string RawFlashPath, string? CpuKeyPath, string? KeyVaultPath, string? RawKeyVaultPath, string? RebootPath, string? StartupLogPath, string? HomePageHtml);

internal sealed record XellCustomStatus(string State, string? Title, long? HeartbeatTicks, string Path, string RawText);

internal sealed record XellHttpPortDiagnostic(bool PortOpen, bool HttpResponseReceived, string Summary);

internal sealed record XellConsoleFinalizationResult(bool Accepted, bool RebootRequested, bool RebootObserved, string State);

internal sealed record XellLaunchPreflight(string LauncherPath, string SiblingXellBinPath, bool SiblingXellBinPresent, bool CheckedCommonFallbackPaths, IReadOnlyList<string> AlternateXellBinPathsPresent, IReadOnlyList<string> CheckedPaths);

internal sealed record XellBootRequest(bool ForceDirectBoot, string? LauncherPath, XellLaunchPreflight? Preflight, bool UsedQuickBoot = false, string? QuickBootPackagePath = null, string? QuickBootTargetPath = null, string? QuickBootLauncherPath = null, IReadOnlyList<string>? UsbStorage = null);

internal enum XellPayloadJobKind
{
	Nand,
	KeyVault
}

internal static class XellHelpers
{
	public const int DefaultBootTimeoutSeconds = 210;

	public static readonly TimeSpan DefaultBootTimeout = TimeSpan.FromSeconds(DefaultBootTimeoutSeconds);

	private const string DefaultStagedLauncherPath = "Hdd1:\\XeCLI\\XellLaunch\\default.xex";

	private static readonly Regex LinkRegex = new Regex("<a[^>]+href\\s*=\\s*[\"'](?<href>[^\"']+)[\"'][^>]*>(?<label>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

	private static readonly Regex TagRegex = new Regex("<.*?>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

	private static readonly Regex CpuKeyRegex = new Regex("cpu\\s*key[^0-9A-Fa-f]*(?<key>[0-9A-Fa-f]{32})", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

	private static readonly Regex BareHexRegex = new Regex("\\b(?<key>[0-9A-Fa-f]{32})\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly string[] DefaultRawFlashPaths = new string[2] { "/rawflash", "/FLASH" };

	private static readonly string[] DefaultCpuKeyPaths = new string[2] { "/cpukey.txt", "/FUSE" };

	private static readonly string[] DefaultKeyVaultPaths = new string[1] { "/KV" };

	private static readonly string[] DefaultRawKeyVaultPaths = new string[2] { "/KVRAW", "/KVRAW2" };

	private static readonly string[] DefaultRebootPaths = new string[2] { "/reboot", "/REBOOT" };

	private static readonly string[] DefaultStartupLogPaths = new string[2] { "/log", "/LOG" };

	private const string FinalRebootToken = "RBT";

	private const string PayloadJobNand = "nand";

	private const string PayloadJobKeyVault = "kv";

	private const long MinimumRawFlashBytes = 1024L * 1024L;

	private const long MinimumKeyVaultBytes = 16L * 1024L;

	private static readonly string[] DefaultCustomStatusPaths = new string[2] { "/XECLI_STATUS", "/xecli_status" };

	private static readonly string[] DefaultCompletionRebootPaths = new string[4] { "/XECLI_DONE", "/xecli_done", "/XECLI_REBOOT", "/xecli_reboot" };

	private static readonly string[] DefaultCompletionRebootUnverifiedPaths = new string[2] { "/XECLI_DONE_UNVERIFIED", "/xecli_done_unverified" };

	private static readonly string[] XellLaunchCandidates = new string[21]
	{
		"Hdd1:\\XeCLI\\XellLaunch\\default.xex",
		"Hdd1:\\XellLaunch\\default.xex",
		"Hdd:\\XellLaunch\\default.xex",
		"Hdd1:\\Apps\\XellLaunch\\default.xex",
		"Hdd1:\\Applications\\XellLaunch\\default.xex",
		"Hdd1:\\Content\\Homebrew\\XellLaunch\\default.xex",
		"Usb0:\\XellLaunch\\default.xex",
		"Usb1:\\XellLaunch\\default.xex",
		"Usb2:\\XellLaunch\\default.xex",
		"Usb0:\\Apps\\XellLaunch\\default.xex",
		"Usb1:\\Apps\\XellLaunch\\default.xex",
		"Usb2:\\Apps\\XellLaunch\\default.xex",
		"Usb0:\\Applications\\XellLaunch\\default.xex",
		"Usb1:\\Applications\\XellLaunch\\default.xex",
		"Usb2:\\Applications\\XellLaunch\\default.xex",
		"Hdd1:\\xelllaunch\\default.xex",
		"Usb0:\\xelllaunch\\default.xex",
		"Usb1:\\xelllaunch\\default.xex",
		"Usb2:\\xelllaunch\\default.xex",
		"Hdd1:\\Apps\\xelllaunch\\default.xex",
		"Hdd1:\\Applications\\xelllaunch\\default.xex"
	};

	private static readonly string[] CommonXellBinCandidates = new string[5] { "Hdd1:\\xell.bin", "Hdd:\\xell.bin", "Usb0:\\xell.bin", "Usb1:\\xell.bin", "Usb2:\\xell.bin" };

	public static async Task<XellDetectionResult> DetectConsoleModeAsync((string Ip, int Port, int TimeoutMs) target, CancellationToken cancellationToken)
	{
		if (await CanConnectToDashboardAsync(target, cancellationToken))
		{
			return new XellDetectionResult(XellConsoleMode.Dashboard, target.Ip, null);
		}
		XellHttpEndpoint? xellHttpEndpoint = await TryProbeXellAsync(target.Ip, TimeSpan.FromSeconds(2.0), cancellationToken);
		xellHttpEndpoint ??= await ScanSubnetForXellAsync(target.Ip, cancellationToken);
		if (xellHttpEndpoint != null)
		{
			return new XellDetectionResult(XellConsoleMode.Xell, xellHttpEndpoint.Ip, xellHttpEndpoint);
		}
		return new XellDetectionResult(XellConsoleMode.Unknown, target.Ip, null);
	}

	public static async Task<XellHttpEndpoint?> WaitForXellAsync(string preferredIp, TimeSpan timeout, bool scanSubnet, CancellationToken cancellationToken)
	{
		DateTime deadlineUtc = DateTime.UtcNow + timeout;
		DateTime nextSweepUtc = DateTime.UtcNow;
		while (DateTime.UtcNow < deadlineUtc)
		{
			XellHttpEndpoint? xellHttpEndpoint = await TryProbeXellAsync(preferredIp, TimeSpan.FromSeconds(2.0), cancellationToken);
			if (xellHttpEndpoint != null)
			{
				return xellHttpEndpoint;
			}
			if (scanSubnet && DateTime.UtcNow >= nextSweepUtc)
			{
				xellHttpEndpoint = await ScanSubnetForXellAsync(preferredIp, cancellationToken);
				if (xellHttpEndpoint != null)
				{
					return xellHttpEndpoint;
				}
				nextSweepUtc = DateTime.UtcNow.AddSeconds(5.0);
			}
			await Task.Delay(1000, cancellationToken);
		}
		return null;
	}

	public static async Task<bool> TryWaitForXellToDisappearAsync(string ip, TimeSpan timeout, CancellationToken cancellationToken)
	{
		DateTime deadlineUtc = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadlineUtc)
		{
			if (await TryProbeXellAsync(ip, TimeSpan.FromMilliseconds(900.0), cancellationToken) == null)
			{
				return true;
			}
			await Task.Delay(500, cancellationToken);
		}
		return false;
	}

	public static async Task<XellHttpEndpoint?> TryProbeXellAsync(string ip, TimeSpan requestTimeout, CancellationToken cancellationToken)
	{
		if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
		{
			return null;
		}
		XellCustomStatus? xellCustomStatus = await TryReadCustomStatusDirectAsync(ip, requestTimeout, cancellationToken);
		if (xellCustomStatus != null)
		{
			return CreateEndpoint(ip, string.Empty);
		}
		string? text = await TryReadHttpTextDirectAsync(ip, "/", requestTimeout, cancellationToken);
		if (!string.IsNullOrWhiteSpace(text) && LooksLikeXell(text))
		{
			return CreateEndpoint(ip, text);
		}
		return null;
	}

	public static async Task<XellHttpPortDiagnostic> DiagnoseHttpPortAsync(string ip, TimeSpan timeout, CancellationToken cancellationToken)
	{
		if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
		{
			return new XellHttpPortDiagnostic(PortOpen: false, HttpResponseReceived: false, "Port 80 probe was skipped because the target IP was invalid.");
		}
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancellationTokenSource.CancelAfter(timeout);
		try
		{
			using TcpClient tcpClient = new TcpClient();
			await tcpClient.ConnectAsync(ip, 80, cancellationTokenSource.Token);
			using NetworkStream networkStream = tcpClient.GetStream();
			byte[] bytes = Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\nHost: " + ip + "\r\nUser-Agent: " + CliHttpUserAgent.Value + "\r\n\r\n");
			await networkStream.WriteAsync(bytes, cancellationTokenSource.Token);
			await networkStream.FlushAsync(cancellationTokenSource.Token);
			byte[] array = new byte[512];
			int num = await networkStream.ReadAsync(array, cancellationTokenSource.Token);
			if (num <= 0)
			{
				return new XellHttpPortDiagnostic(PortOpen: true, HttpResponseReceived: false, "Port 80 accepted a TCP connection but closed without returning any HTTP data.");
			}
			string @string = Encoding.ASCII.GetString(array, 0, num);
			if (@string.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
			{
				return new XellHttpPortDiagnostic(PortOpen: true, HttpResponseReceived: true, "Port 80 accepted a TCP connection and returned HTTP headers.");
			}
			return new XellHttpPortDiagnostic(PortOpen: true, HttpResponseReceived: false, "Port 80 accepted a TCP connection and returned non-HTTP data: " + @string.Replace("\r", "\\r").Replace("\n", "\\n").Trim());
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return new XellHttpPortDiagnostic(PortOpen: false, HttpResponseReceived: false, "Port 80 did not complete a TCP/HTTP probe before the timeout expired.");
		}
		catch (SocketException ex)
		{
			return new XellHttpPortDiagnostic(PortOpen: false, HttpResponseReceived: false, "Port 80 was not reachable: " + ex.SocketErrorCode);
		}
		catch (IOException ex)
		{
			return new XellHttpPortDiagnostic(PortOpen: true, HttpResponseReceived: false, "Port 80 accepted a TCP connection, but the HTTP response failed: " + ex.Message);
		}
		catch (Exception ex)
		{
			return new XellHttpPortDiagnostic(PortOpen: false, HttpResponseReceived: false, "Port 80 probe failed: " + ex.Message);
		}
	}

	public static async Task<string?> FindInstalledXellLaunchAsync(XbdmClient client, string targetIp, CancellationToken cancellationToken)
	{
		string[] xellLaunchCandidates = XellLaunchCandidates;
		foreach (string candidate in xellLaunchCandidates)
		{
			if (await RemoteFileExistsAsync(client, candidate, cancellationToken))
			{
				return candidate;
			}
		}
		foreach (string candidate2 in xellLaunchCandidates)
		{
			if (await RemoteFileExistsViaFtpAsync(targetIp, candidate2, cancellationToken))
			{
				return candidate2;
			}
		}
		return null;
	}

	public static async Task<XellBootRequest> RequestXellBootAsync(XbdmClient client, XellCommandSettings settings, string targetIp, CancellationToken cancellationToken)
	{
		string? text = null;
		IReadOnlyList<string> readOnlyList = await TryGetAttachedUsbStorageAsync(client, cancellationToken);
		EnforceUsbSafetyPolicy(settings, readOnlyList);
		if (!settings.ForceXell)
		{
			if (XellQuickBootHelpers.IsEnabled(settings))
			{
				XellQuickBootInstall xellQuickBootInstall = await XellQuickBootHelpers.StageQuickBootPackageAsync(client, settings, () => ResolveLauncherPathAsync(client, settings, targetIp, cancellationToken), (string launcherPath) => PreflightLauncherAsync(client, targetIp, launcherPath, cancellationToken), cancellationToken);
				if (!settings.Json && xellQuickBootInstall.Preflight != null)
				{
					WritePreflightSummary(xellQuickBootInstall.Preflight);
				}
				string text2 = "[grey]Boot strategy:[/] [springgreen3_1]QuickBoot XeLL launcher[/] [grey](" + Markup.Escape(xellQuickBootInstall.RemoteLauncherPath) + " -> " + Markup.Escape(xellQuickBootInstall.ConfigPath) + ")[/]";
				if (!string.IsNullOrWhiteSpace(xellQuickBootInstall.RemotePackagePath))
				{
					text2 = text2 + "\n[grey]Dashboard shortcut:[/] [cyan]" + Markup.Escape(xellQuickBootInstall.RemotePackagePath) + "[/]";
				}
				if (!settings.Json)
				{
					AnsiConsole.MarkupLine(text2);
				}
				await SendPreLaunchNotificationsAsync(client, cancellationToken);
				await client.SendCommandAsync(BuildMagicBootCommand(xellQuickBootInstall.RemoteLauncherPath), cancellationToken);
				return new XellBootRequest(ForceDirectBoot: false, xellQuickBootInstall.LauncherPath, xellQuickBootInstall.Preflight, UsedQuickBoot: true, xellQuickBootInstall.RemotePackagePath, xellQuickBootInstall.ConfigPath, xellQuickBootInstall.RemoteLauncherPath, readOnlyList);
			}
			text = await ResolveLauncherPathAsync(client, settings, targetIp, cancellationToken);
			if (!string.IsNullOrWhiteSpace(text))
			{
				XellLaunchPreflight xellLaunchPreflight = await PreflightLauncherAsync(client, targetIp, text, cancellationToken);
				if (ShouldAutoProvisionBundledXellBin(settings, text, xellLaunchPreflight))
				{
					if (await TryAutoProvisionBundledXellBinAsync(client, settings, targetIp, text, cancellationToken))
					{
						xellLaunchPreflight = await PreflightLauncherAsync(client, targetIp, text, cancellationToken);
					}
				}
				if (!settings.Json)
				{
					WritePreflightSummary(xellLaunchPreflight);
					AnsiConsole.MarkupLine("[grey]Boot strategy:[/] [springgreen3_1]XellLaunch[/] [grey](" + Markup.Escape(text) + ")[/]");
				}
				await SendPreLaunchNotificationsAsync(client, cancellationToken);
				await client.SendCommandAsync(BuildMagicBootCommand(text), cancellationToken);
				return new XellBootRequest(ForceDirectBoot: false, text, xellLaunchPreflight, UsedQuickBoot: false, null, null, null, readOnlyList);
			}
			if (!settings.Json)
			{
				OperationFeedback.WriteWarning("XellLaunch not found", "Falling back to a tray-assisted cold reboot into XeLL.");
			}
		}
		else
		{
			if (!settings.Json)
			{
				AnsiConsole.MarkupLine("[grey]Boot strategy:[/] [gold1]forced direct XeLL reboot[/]");
			}
		}
		await SendPreLaunchNotificationsAsync(client, cancellationToken);
		await HardwareHelpers.ExecuteXamShortcutAsync(client, XamShortcutOrdinal.OpenTray, cancellationToken);
		await Task.Delay(900, cancellationToken);
		try
		{
			await client.SendCommandAsync("magicboot cold", cancellationToken);
		}
		catch
		{
		}
		return new XellBootRequest(ForceDirectBoot: true, null, null, UsedQuickBoot: false, null, null, null, readOnlyList);
	}

	public static async Task<bool> TryRelaunchBootRequestAsync(XbdmClient client, XellBootRequest bootRequest, CancellationToken cancellationToken)
	{
		if (bootRequest.UsedQuickBoot && !string.IsNullOrWhiteSpace(bootRequest.QuickBootLauncherPath))
		{
			await client.SendCommandAsync(BuildMagicBootCommand(bootRequest.QuickBootLauncherPath), cancellationToken);
			return true;
		}
		if (!bootRequest.ForceDirectBoot && !string.IsNullOrWhiteSpace(bootRequest.LauncherPath))
		{
			await client.SendCommandAsync(BuildMagicBootCommand(bootRequest.LauncherPath), cancellationToken);
			return true;
		}
		if (!bootRequest.ForceDirectBoot)
		{
			return false;
		}
		await HardwareHelpers.ExecuteXamShortcutAsync(client, XamShortcutOrdinal.OpenTray, cancellationToken);
		await Task.Delay(900, cancellationToken);
		try
		{
			await client.SendCommandAsync("magicboot cold", cancellationToken);
		}
		catch
		{
		}
		return true;
	}

	private static async Task<string?> ResolveLauncherPathAsync(XbdmClient client, XellCommandSettings settings, string targetIp, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(settings.StageLauncherPath) || !string.IsNullOrWhiteSpace(settings.StageXellBinPath))
		{
			return await StageLauncherAsync(client, settings, targetIp, cancellationToken);
		}
		if (!string.IsNullOrWhiteSpace(settings.LauncherPath))
		{
			string text = settings.LauncherPath.Trim();
			if (await RemoteFileExistsAsync(client, text, cancellationToken) || await RemoteFileExistsViaFtpAsync(targetIp, text, cancellationToken))
			{
				return text;
			}
			if (!settings.Json)
			{
				OperationFeedback.WriteWarning("XeLL launcher not found", "Specified helper was not found on the console: " + text);
			}
			return null;
		}
		if (XellQuickBootHelpers.IsEnabled(settings) && ShouldAutoProvisionBundledLauncher(settings))
		{
			return await ProvisionBundledLauncherAsync(client, settings, targetIp, cancellationToken);
		}
		string? text2 = await FindInstalledXellLaunchAsync(client, targetIp, cancellationToken);
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		if (ShouldAutoProvisionBundledLauncher(settings))
		{
			return await ProvisionBundledLauncherAsync(client, settings, targetIp, cancellationToken);
		}
		return null;
	}

	private static async Task<string> StageLauncherAsync(XbdmClient client, XellCommandSettings settings, string targetIp, CancellationToken cancellationToken)
	{
		string text = ResolveLauncherPath(settings.LauncherPath);
		(string, int, string, string, int) tuple = ResolveFtpTarget(targetIp, settings.TimeoutMs ?? 5000);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeoutMs = tuple.Item5;
		if (!string.IsNullOrWhiteSpace(settings.StageLauncherPath))
		{
			await FtpHelpers.UploadFileVerifiedAsync(ip, port, user, pass, timeoutMs, settings.StageLauncherPath, text, ensureRemoteDirectory: true, null, cancellationToken);
			if (!settings.Json)
			{
				AnsiConsole.MarkupLine("[grey]Staged launcher:[/] [cyan]" + Markup.Escape(text) + "[/]");
			}
		}
		else if (!await RemoteFileExistsAnyAsync(client, targetIp, text, cancellationToken))
		{
			throw new InvalidOperationException("Cannot stage xell.bin because the launch helper was not found at " + text + ". Provide --stage-launcher or point --launcher at an existing helper.");
		}
		if (!string.IsNullOrWhiteSpace(settings.StageXellBinPath))
		{
			string text2 = BuildXellBinPath(text);
			await FtpHelpers.UploadFileVerifiedAsync(ip, port, user, pass, timeoutMs, settings.StageXellBinPath, text2, ensureRemoteDirectory: true, null, cancellationToken);
			if (!settings.Json)
			{
				AnsiConsole.MarkupLine("[grey]Staged xell.bin:[/] [cyan]" + Markup.Escape(text2) + "[/]");
			}
			XellAssetHelpers.XellLaunchAssetResolution xellLaunchAssetResolution = XellAssetHelpers.DescribeLocalXellBinary(settings.StageXellBinPath);
			if (!settings.Json)
			{
				AnsiConsole.MarkupLine("[grey]Payload source:[/] [white]" + Markup.Escape(xellLaunchAssetResolution.SourceDescription) + "[/]");
				if (!string.IsNullOrWhiteSpace(xellLaunchAssetResolution.Sha256))
				{
					AnsiConsole.MarkupLine("[grey]Payload SHA-256:[/] [white]" + Markup.Escape(xellLaunchAssetResolution.Sha256) + "[/]");
				}
				if (!string.IsNullOrWhiteSpace(xellLaunchAssetResolution.BehaviorSummary))
				{
					AnsiConsole.MarkupLine("[grey]Expected behavior:[/] " + Markup.Escape(xellLaunchAssetResolution.BehaviorSummary));
				}
			}
		}
		return text;
	}

	private static async Task<XellLaunchPreflight> PreflightLauncherAsync(XbdmClient client, string targetIp, string launcherPath, CancellationToken cancellationToken)
	{
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		string text = BuildXellBinPath(launcherPath);
		bool flag = await CheckAndRecordPathAsync(client, targetIp, text, list, list2, cancellationToken);
		bool flag2 = LooksLikeXellLaunchPath(launcherPath);
		if (flag2)
		{
			string[] commonXellBinCandidates = CommonXellBinCandidates;
			foreach (string text2 in commonXellBinCandidates)
			{
				if (!string.Equals(text2, text, StringComparison.OrdinalIgnoreCase))
				{
					await CheckAndRecordPathAsync(client, targetIp, text2, list, list2, cancellationToken);
				}
			}
		}
		return new XellLaunchPreflight(launcherPath, text, flag, flag2, list2.Where((string path) => !string.Equals(path, text, StringComparison.OrdinalIgnoreCase)).ToArray(), list.ToArray());
	}

	private static bool ShouldAutoProvisionBundledLauncher(XellCommandSettings settings)
	{
		if (settings.ForceXell)
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(settings.LauncherPath))
		{
			return false;
		}
		return string.IsNullOrWhiteSpace(settings.StageLauncherPath);
	}

	private static bool ShouldAutoProvisionBundledXellBin(XellCommandSettings settings, string launcherPath, XellLaunchPreflight preflight)
	{
		if (settings.ForceXell || !string.IsNullOrWhiteSpace(settings.StageXellBinPath))
		{
			return false;
		}
		if (preflight.SiblingXellBinPresent || !LooksLikeXellLaunchPath(launcherPath))
		{
			return false;
		}
		return CanStageSiblingXellBin(launcherPath);
	}

	private static async Task<string> ProvisionBundledLauncherAsync(XbdmClient client, XellCommandSettings settings, string targetIp, CancellationToken cancellationToken)
	{
		string text = ResolveLauncherPath(null);
		XellAssetHelpers.XellLaunchAssetResolution xellLaunchAssetResolution = await XellAssetHelpers.ResolveXellLaunchAssetAsync("default.xex", cancellationToken);
		XellAssetHelpers.XellLaunchAssetResolution xellLaunchAssetResolution2 = await XellAssetHelpers.ResolveXellLaunchAssetAsync("xell.bin", cancellationToken);
		await UploadBundledHelperAssetAsync(targetIp, settings.TimeoutMs ?? 5000, xellLaunchAssetResolution.Path, text, "Auto-installing managed XellLaunch", settings.Json, cancellationToken);
		await UploadBundledHelperAssetAsync(targetIp, settings.TimeoutMs ?? 5000, xellLaunchAssetResolution2.Path, BuildXellBinPath(text), "Auto-installing managed xell.bin", settings.Json, cancellationToken);
		string text2 = "[cyan]" + Markup.Escape(text) + "[/]\n[grey]Helper source:[/] [white]" + Markup.Escape(xellLaunchAssetResolution.SourceDescription) + "[/]\n[grey]Payload:[/] [springgreen3_1]" + Markup.Escape(BuildXellBinPath(text)) + "[/]\n[grey]Payload source:[/] [white]" + Markup.Escape(xellLaunchAssetResolution2.SourceDescription) + "[/]";
		if (!string.IsNullOrWhiteSpace(xellLaunchAssetResolution2.Sha256))
		{
			text2 = text2 + "\n[grey]Payload SHA-256:[/] [white]" + Markup.Escape(xellLaunchAssetResolution2.Sha256) + "[/]";
		}
		if (!settings.Json)
		{
			OperationFeedback.WriteSuccess("Managed XellLaunch installed", text2);
			if (!string.IsNullOrWhiteSpace(xellLaunchAssetResolution2.BehaviorSummary))
			{
				AnsiConsole.MarkupLine("[grey]Expected behavior:[/] " + Markup.Escape(xellLaunchAssetResolution2.BehaviorSummary));
			}
		}
		return text;
	}

	private static async Task<bool> TryAutoProvisionBundledXellBinAsync(XbdmClient client, XellCommandSettings settings, string targetIp, string launcherPath, CancellationToken cancellationToken)
	{
		if (!CanStageSiblingXellBin(launcherPath))
		{
			return false;
		}
		string text = BuildXellBinPath(launcherPath);
		XellAssetHelpers.XellLaunchAssetResolution xellLaunchAssetResolution = await XellAssetHelpers.ResolveXellLaunchAssetAsync("xell.bin", cancellationToken);
		await UploadBundledHelperAssetAsync(targetIp, settings.TimeoutMs ?? 5000, xellLaunchAssetResolution.Path, text, "Auto-installing managed xell.bin", settings.Json, cancellationToken);
		string text2 = "[cyan]" + Markup.Escape(text) + "[/]\n[grey]Payload source:[/] [white]" + Markup.Escape(xellLaunchAssetResolution.SourceDescription) + "[/]";
		if (!string.IsNullOrWhiteSpace(xellLaunchAssetResolution.Sha256))
		{
			text2 = text2 + "\n[grey]Payload SHA-256:[/] [white]" + Markup.Escape(xellLaunchAssetResolution.Sha256) + "[/]";
		}
		if (!settings.Json)
		{
			OperationFeedback.WriteSuccess("Managed xell.bin installed", text2);
			if (!string.IsNullOrWhiteSpace(xellLaunchAssetResolution.BehaviorSummary))
			{
				AnsiConsole.MarkupLine("[grey]Expected behavior:[/] " + Markup.Escape(xellLaunchAssetResolution.BehaviorSummary));
			}
		}
		return true;
	}

	private static async Task UploadBundledHelperAssetAsync(string targetIp, int timeoutMs, string localPath, string remotePath, string title, bool json, CancellationToken cancellationToken)
	{
		(string, int, string, string, int) tuple = ResolveFtpTarget(targetIp, timeoutMs);
		await FtpHelpers.UploadFileVerifiedAsync(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5, localPath, remotePath, ensureRemoteDirectory: true, null, cancellationToken);
		if (!json)
		{
			AnsiConsole.MarkupLine("[grey]" + Markup.Escape(title) + ":[/] [cyan]" + Markup.Escape(remotePath) + "[/]");
		}
	}

	private static async Task<bool> CheckAndRecordPathAsync(XbdmClient client, string targetIp, string remotePath, ICollection<string> checkedPaths, ICollection<string> presentPaths, CancellationToken cancellationToken)
	{
		checkedPaths.Add(remotePath);
		if (await RemoteFileExistsAnyAsync(client, targetIp, remotePath, cancellationToken))
		{
			presentPaths.Add(remotePath);
			return true;
		}
		return false;
	}

	private static void WritePreflightSummary(XellLaunchPreflight preflight)
	{
		if (preflight.SiblingXellBinPresent)
		{
			AnsiConsole.MarkupLine("[grey]Helper xell.bin:[/] [green]found beside helper[/] [cyan]" + Markup.Escape(preflight.SiblingXellBinPath) + "[/]");
			return;
		}
		if (preflight.AlternateXellBinPathsPresent.Count > 0)
		{
			OperationFeedback.WriteWarning("No sibling xell.bin", "Helper-adjacent xell.bin was not found at " + preflight.SiblingXellBinPath + ". Alternate candidate(s) exist at: " + string.Join(", ", preflight.AlternateXellBinPathsPresent));
			return;
		}
		if (preflight.CheckedCommonFallbackPaths)
		{
			OperationFeedback.WriteWarning("No xell.bin detected", "No xell.bin was found beside the helper or in the common HDD/USB fallback locations. This launch will rely on flash XeLL fallback.");
			return;
		}
		OperationFeedback.WriteWarning("No sibling xell.bin", "No xell.bin was found beside the helper. This helper may depend on its own fallback behavior.");
	}

public static async Task DownloadRawFlashAsync(XellHttpEndpoint endpoint, string destinationPath, string title, CancellationToken cancellationToken)
{
	await DownloadRawFlashAsync(endpoint, destinationPath, title, cancellationToken, null);
}

public static async Task DownloadRawFlashAsync(XellHttpEndpoint endpoint, string destinationPath, string title, CancellationToken cancellationToken, Func<int, Task>? progressSyncCallback)
{
	await DownloadRawFlashAsync(endpoint, destinationPath, title, cancellationToken, progressSyncCallback, quiet: false);
}

public static async Task DownloadRawFlashAsync(XellHttpEndpoint endpoint, string destinationPath, string title, CancellationToken cancellationToken, Func<int, Task>? progressSyncCallback, bool quiet)
{
	await DownloadFromCandidatePathsAsync(endpoint, CombinePaths(endpoint.RawFlashPath, DefaultRawFlashPaths), destinationPath, title, "XeLL did not expose a readable flash dump endpoint.", MinimumRawFlashBytes, cancellationToken, progressSyncCallback, quiet);
}

	public static Task DownloadKeyVaultAsync(XellHttpEndpoint endpoint, string destinationPath, bool raw, string title, CancellationToken cancellationToken)
	{
		return DownloadKeyVaultAsync(endpoint, destinationPath, raw, title, cancellationToken, null);
	}

	public static async Task DownloadKeyVaultAsync(XellHttpEndpoint endpoint, string destinationPath, bool raw, string title, CancellationToken cancellationToken, Func<int, Task>? progressSyncCallback)
	{
		await DownloadKeyVaultAsync(endpoint, destinationPath, raw, title, cancellationToken, progressSyncCallback, quiet: false);
	}

	public static async Task DownloadKeyVaultAsync(XellHttpEndpoint endpoint, string destinationPath, bool raw, string title, CancellationToken cancellationToken, Func<int, Task>? progressSyncCallback, bool quiet)
	{
	if (raw)
	{
		await DownloadFromCandidatePathsAsync(endpoint, CombinePaths(endpoint.RawKeyVaultPath, DefaultRawKeyVaultPaths), destinationPath, title, "XeLL did not expose a readable raw keyvault endpoint.", MinimumKeyVaultBytes, cancellationToken, progressSyncCallback, quiet);
		return;
	}
	await DownloadFromCandidatePathsAsync(endpoint, CombinePaths(endpoint.KeyVaultPath, DefaultKeyVaultPaths), destinationPath, title, "XeLL did not expose a readable keyvault endpoint.", MinimumKeyVaultBytes, cancellationToken, progressSyncCallback, quiet);
}

public static async Task DownloadBinaryAsync(XellHttpEndpoint endpoint, string relativePath, string destinationPath, string title, CancellationToken cancellationToken)
{
	await DownloadBinaryAsync(endpoint, relativePath, destinationPath, title, cancellationToken, null);
}

public static async Task DownloadBinaryAsync(XellHttpEndpoint endpoint, string relativePath, string destinationPath, string title, CancellationToken cancellationToken, Func<int, Task>? progressSyncCallback)
{
	await DownloadBinaryAsync(endpoint, relativePath, destinationPath, title, cancellationToken, progressSyncCallback, quiet: false);
}

public static async Task DownloadBinaryAsync(XellHttpEndpoint endpoint, string relativePath, string destinationPath, string title, CancellationToken cancellationToken, Func<int, Task>? progressSyncCallback, bool quiet)
{
	Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Environment.CurrentDirectory);
	try
	{
		using HttpClient httpClient = CreateHttpClient(TimeSpan.FromMinutes(30.0));
		using HttpResponseMessage response = await httpClient.GetAsync(BuildUri(endpoint, relativePath), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		long? contentLength = response.Content.Headers.ContentLength;
		if (quiet)
		{
			await CopyResponseToFileAsync(response, destinationPath, contentLength, null, progressSyncCallback, cancellationToken);
			return;
		}
		await CliOutput.RunWithProgressAsync(title, contentLength, async delegate(IProgress<CliOutput.TransferProgressUpdate> progress)
		{
			await CopyResponseToFileAsync(response, destinationPath, contentLength, progress, progressSyncCallback, cancellationToken);
		});
	}
	catch
	{
		TryDeleteFile(destinationPath);
		throw;
	}
}

private static async Task CopyResponseToFileAsync(HttpResponseMessage response, string destinationPath, long? contentLength, IProgress<CliOutput.TransferProgressUpdate>? progress, Func<int, Task>? progressSyncCallback, CancellationToken cancellationToken)
{
	await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
	await using FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
	byte[] buffer = new byte[65536];
	long total = 0L;
	int lastReportedPercent = -1;
	long lastSyncTick = Environment.TickCount64;
	if (contentLength.HasValue && contentLength.Value > 0L)
	{
		await TryReportPayloadProgressAsync(progressSyncCallback, 0);
		lastReportedPercent = 0;
	}
	while (true)
	{
		int read = await responseStream.ReadAsync(buffer, cancellationToken);
		if (read <= 0)
		{
			break;
		}
		await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
		total += read;
		progress?.Report(new CliOutput.TransferProgressUpdate(total, "downloading"));
		if (contentLength.HasValue && contentLength.Value > 0L)
		{
			int num = (int)Math.Min(100L, (total * 100L + contentLength.Value - 1L) / contentLength.Value);
			long tickCount = Environment.TickCount64;
			if (num != lastReportedPercent && (num >= 100 || lastReportedPercent < 0 || tickCount - lastSyncTick >= 250L))
			{
				await TryReportPayloadProgressAsync(progressSyncCallback, num);
				lastReportedPercent = num;
				lastSyncTick = tickCount;
			}
		}
	}
	if (total <= 0L)
	{
		throw new InvalidDataException("XeLL returned a successful binary response with an empty body.");
	}
	if (contentLength.HasValue && total != contentLength.Value)
	{
		throw new EndOfStreamException($"XeLL binary response ended after {total} of {contentLength.Value} declared bytes.");
	}
	if (contentLength.HasValue && contentLength.Value > 0L && lastReportedPercent < 100)
	{
		await TryReportPayloadProgressAsync(progressSyncCallback, 100);
	}
	await fileStream.FlushAsync(cancellationToken);
}

	public static async Task<string?> TryReadCpuKeyAsync(XellHttpEndpoint endpoint, CancellationToken cancellationToken)
	{
		string?[] array = new string?[1 + DefaultCpuKeyPaths.Length];
		array[0] = endpoint.CpuKeyPath;
		Array.Copy(DefaultCpuKeyPaths, 0, array, 1, DefaultCpuKeyPaths.Length);
		foreach (string item in DistinctNonEmptyPaths(array))
		{
			string? text = await TryReadTextAsync(endpoint, item, cancellationToken);
			string? text2 = NormalizeCpuKey(text);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				return text2;
			}
		}
		return NormalizeCpuKey(endpoint.HomePageHtml);
	}

	public static async Task<string?> TryReadFuseTextAsync(XellHttpEndpoint endpoint, CancellationToken cancellationToken)
	{
		string?[] array = new string?[1 + DefaultCpuKeyPaths.Length];
		array[0] = endpoint.CpuKeyPath;
		Array.Copy(DefaultCpuKeyPaths, 0, array, 1, DefaultCpuKeyPaths.Length);
		foreach (string item in DistinctNonEmptyPaths(array))
		{
			string? text = await TryReadTextAsync(endpoint, item, cancellationToken);
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text.Trim();
			}
		}
		return null;
	}

	public static async Task<string?> TryReadStartupLogAsync(XellHttpEndpoint endpoint, CancellationToken cancellationToken)
	{
		foreach (string item in DistinctNonEmptyPaths(CombinePaths(endpoint.StartupLogPath, DefaultStartupLogPaths)))
		{
			string? text = await TryReadTextAsync(endpoint, item, cancellationToken);
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text.Trim();
			}
		}
		return null;
	}

	public static async Task<XellCustomStatus?> TryReadCustomStatusAsync(XellHttpEndpoint endpoint, CancellationToken cancellationToken)
	{
		foreach (string item in DistinctNonEmptyPaths(DefaultCustomStatusPaths))
		{
			string? text = await TryReadHttpTextDirectAsync(endpoint.Ip, item, TimeSpan.FromSeconds(3.0), cancellationToken);
			XellCustomStatus? xellCustomStatus = ParseCustomStatus(text, item);
			if (xellCustomStatus != null)
			{
				return xellCustomStatus;
			}
		}
		return null;
	}

private static string GetPayloadJobName(XellPayloadJobKind job)
{
	return (job == XellPayloadJobKind.KeyVault) ? PayloadJobKeyVault : PayloadJobNand;
}

private static Task<bool> TrySyncJobStageAsync(XellHttpEndpoint endpoint, XellPayloadJobKind job, string stage, bool? verified, int? verificationPass, int? verificationTotal, string? path, string? reason, string? finalToken, int? percent, CancellationToken cancellationToken)
{
	return TryRequestPayloadSyncAsync(endpoint, BuildPayloadSyncPath(GetPayloadJobName(job), stage, verified, verificationPass, verificationTotal, path, reason, finalToken, percent), cancellationToken);
}

public static Task<bool> TrySyncDumpStartedAsync(XellHttpEndpoint endpoint, CancellationToken cancellationToken)
{
	return TrySyncJobStageAsync(endpoint, XellPayloadJobKind.Nand, "dump", null, null, null, null, null, null, null, cancellationToken);
}

public static Task<bool> TrySyncKeyVaultStartedAsync(XellHttpEndpoint endpoint, CancellationToken cancellationToken)
{
	return TrySyncJobStageAsync(endpoint, XellPayloadJobKind.KeyVault, "dump", null, null, null, null, null, null, null, cancellationToken);
}

public static Task<bool> TrySyncVerificationProgressAsync(XellHttpEndpoint endpoint, int pass, int total, CancellationToken cancellationToken)
{
	return TrySyncVerificationProgressAsync(endpoint, XellPayloadJobKind.Nand, pass, total, cancellationToken);
}

public static Task<bool> TrySyncVerificationProgressAsync(XellHttpEndpoint endpoint, XellPayloadJobKind job, int pass, int total, CancellationToken cancellationToken)
{
	return TrySyncJobStageAsync(endpoint, job, "verify", null, pass, total, null, null, null, null, cancellationToken);
}

public static Task<bool> TrySyncDumpProgressAsync(XellHttpEndpoint endpoint, int percent, CancellationToken cancellationToken)
{
	return TrySyncDumpProgressAsync(endpoint, XellPayloadJobKind.Nand, percent, cancellationToken);
}

public static Task<bool> TrySyncDumpProgressAsync(XellHttpEndpoint endpoint, XellPayloadJobKind job, int percent, CancellationToken cancellationToken)
{
	return TrySyncJobStageAsync(endpoint, job, "dump", null, null, null, null, null, null, percent, cancellationToken);
}

public static Task<bool> TrySyncKeyVaultProgressAsync(XellHttpEndpoint endpoint, int percent, CancellationToken cancellationToken)
{
	return TrySyncDumpProgressAsync(endpoint, XellPayloadJobKind.KeyVault, percent, cancellationToken);
}

public static Task<bool> TrySyncVerificationPercentAsync(XellHttpEndpoint endpoint, int percent, CancellationToken cancellationToken)
{
	return TrySyncVerificationPercentAsync(endpoint, XellPayloadJobKind.Nand, percent, cancellationToken);
}

public static Task<bool> TrySyncVerificationPercentAsync(XellHttpEndpoint endpoint, XellPayloadJobKind job, int percent, CancellationToken cancellationToken)
{
	return TrySyncJobStageAsync(endpoint, job, "verify", null, null, null, null, null, null, percent, cancellationToken);
}

	public static async Task<bool> TryRequestRebootAsync(XellHttpEndpoint endpoint, CancellationToken cancellationToken)
	{
	foreach (string item in DistinctNonEmptyPaths(CombinePaths(endpoint.RebootPath, DefaultRebootPaths)))
	{
		try
		{
			using HttpClient httpClient = CreateHttpClient(TimeSpan.FromSeconds(3.0));
			using HttpResponseMessage response = await httpClient.GetAsync(BuildUri(endpoint, item), cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				return true;
			}
		}
		catch
			{
				await Task.Delay(500, cancellationToken);
				if (await TryProbeXellAsync(endpoint.Ip, TimeSpan.FromMilliseconds(800.0), cancellationToken) == null)
				{
					return true;
				}
			}
		}
		return false;
	}

public static Task<bool> TryRequestCompletionRebootAsync(XellHttpEndpoint endpoint, bool verified, int verificationPass, int verificationTotal, string? path, CancellationToken cancellationToken)
{
	return TryRequestCompletionRebootAsync(endpoint, XellPayloadJobKind.Nand, verified, verificationPass, verificationTotal, path, cancellationToken);
}

public static async Task<bool> TryRequestCompletionRebootAsync(XellHttpEndpoint endpoint, XellPayloadJobKind job, bool verified, int verificationPass, int verificationTotal, string? path, CancellationToken cancellationToken)
{
	if (await TrySyncJobStageAsync(endpoint, job, "complete", verified, verificationPass, verificationTotal, path, null, verified ? FinalRebootToken : null, null, cancellationToken))
	{
		return true;
	}
		if (!verified)
		{
			return false;
		}
		string[] paths = verified ? DefaultCompletionRebootPaths : DefaultCompletionRebootUnverifiedPaths;
	foreach (string item in DistinctNonEmptyPaths(paths))
	{
		try
		{
			using HttpClient httpClient = CreateHttpClient(TimeSpan.FromSeconds(3.0));
			using HttpResponseMessage response = await httpClient.GetAsync(BuildUri(endpoint, item), cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				return true;
			}
		}
			catch
			{
				await Task.Delay(500, cancellationToken);
				if (await TryProbeXellAsync(endpoint.Ip, TimeSpan.FromMilliseconds(800.0), cancellationToken) == null)
				{
					return true;
				}
			}
		}
		return await TryRequestRebootAsync(endpoint, cancellationToken);
}

public static async Task<XellConsoleFinalizationResult> FinalizeJobAsync(XellHttpEndpoint endpoint, XellPayloadJobKind job, bool verified, int verificationPass, int verificationTotal, string? path, CancellationToken cancellationToken)
{
	bool accepted = await TryRequestCompletionRebootAsync(endpoint, job, verified, verificationPass, verificationTotal, path, cancellationToken);
	if (!accepted)
	{
		return new XellConsoleFinalizationResult(Accepted: false, RebootRequested: false, RebootObserved: false, State: "completion-unavailable");
	}
	if (!verified)
	{
		return new XellConsoleFinalizationResult(Accepted: true, RebootRequested: false, RebootObserved: false, State: "left-in-xell");
	}
	bool rebootObserved = await TryWaitForXellToDisappearAsync(endpoint.Ip, TimeSpan.FromSeconds(15.0), cancellationToken);
	return new XellConsoleFinalizationResult(Accepted: true, RebootRequested: true, RebootObserved: rebootObserved, State: rebootObserved ? "xell-exited" : "reboot-pending");
}

public static Task<bool> TryNotifyFailureAsync(XellHttpEndpoint endpoint, string reason, int verificationPass, int verificationTotal, string? path, CancellationToken cancellationToken)
{
	return TryNotifyFailureAsync(endpoint, XellPayloadJobKind.Nand, reason, verificationPass, verificationTotal, path, cancellationToken);
}

public static Task<bool> TryNotifyFailureAsync(XellHttpEndpoint endpoint, XellPayloadJobKind job, string reason, int verificationPass, int verificationTotal, string? path, CancellationToken cancellationToken)
{
	return TrySyncJobStageAsync(endpoint, job, "failed", verified: false, verificationPass, verificationTotal, path, reason, null, null, cancellationToken);
}

	public static IReadOnlyList<string> BuildCandidateIps(string preferredIp, bool includeSubnetSweep)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> list = new List<string>();
		AddIp(list, hashSet, preferredIp);
		if (includeSubnetSweep && TryGetSubnetPrefix(preferredIp, out var prefix))
		{
			for (int i = 1; i <= 254; i++)
			{
				AddIp(list, hashSet, prefix + i.ToString(CultureInfo.InvariantCulture));
			}
		}
		AddIp(list, hashSet, "192.168.1.99");
		AddIp(list, hashSet, "192.168.88.99");
		return list;
	}

	private static async Task<bool> CanConnectToDashboardAsync((string Ip, int Port, int TimeoutMs) target, CancellationToken cancellationToken)
	{
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancellationTokenSource.CancelAfter(Math.Clamp(target.TimeoutMs, 1500, 3000));
		try
		{
			await using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
			{
				Host = target.Ip,
				Port = target.Port,
				TimeoutMs = Math.Clamp(target.TimeoutMs, 1500, 3000)
			}, cancellationTokenSource.Token);
			await client.GetConsoleInfoAsync(cancellationTokenSource.Token);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static async Task<XellHttpEndpoint?> ScanSubnetForXellAsync(string preferredIp, CancellationToken cancellationToken)
	{
		IReadOnlyList<string> readOnlyList = BuildCandidateIps(preferredIp, includeSubnetSweep: true);
		const int BatchSize = 16;
		for (int i = 0; i < readOnlyList.Count; i += BatchSize)
		{
			string[] array = readOnlyList.Skip(i).Take(BatchSize).ToArray();
			XellHttpEndpoint?[] array2 = await Task.WhenAll(array.Select((string ip) => TryProbeXellAsync(ip, TimeSpan.FromMilliseconds(800.0), cancellationToken)));
			XellHttpEndpoint? xellHttpEndpoint = array2.FirstOrDefault((XellHttpEndpoint? result) => result != null);
			if (xellHttpEndpoint != null)
			{
				return xellHttpEndpoint;
			}
		}
		return null;
	}

	private static bool LooksLikeXell(string html)
	{
		if (string.IsNullOrWhiteSpace(html))
		{
			return false;
		}
		if (ContainsAny(html, "/rawflash", "/flash", "/fuse", "/kv", "/log", "/reboot", "startup log", "cpu key", "dvd key", "xell"))
		{
			return true;
		}
		return !string.IsNullOrWhiteSpace(NormalizeCpuKey(html));
	}

	private static XellHttpEndpoint CreateEndpoint(string ip, string html)
	{
		string text = ResolveEndpointPath(html, DefaultRawFlashPaths, "raw flash", "rawflash", "flash");
		string text2 = ResolveEndpointPath(html, DefaultCpuKeyPaths, "cpu key", "cpukey", "fuse");
		string text3 = ResolveEndpointPath(html, DefaultKeyVaultPaths, "key vault", "keyvault", "/kv");
		string text4 = ResolveEndpointPath(html, DefaultRawKeyVaultPaths, "kvraw", "raw key vault", "raw keyvault");
		string text5 = ResolveEndpointPath(html, DefaultRebootPaths, "reboot");
		string text6 = ResolveEndpointPath(html, DefaultStartupLogPaths, "startup log", "log");
		return new XellHttpEndpoint(ip, "http://" + ip, text, text2, text3, text4, text5, text6, html);
	}

	private static XellCustomStatus? ParseCustomStatus(string? text, string path)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string[] array = text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			int num = text2.IndexOf('=');
			if (num <= 0)
			{
				continue;
			}
			dictionary[text2.Substring(0, num).Trim()] = text2.Substring(num + 1).Trim();
		}
		if (!dictionary.ContainsKey("state") && !dictionary.ContainsKey("payload"))
		{
			return null;
		}
		dictionary.TryGetValue("state", out var value);
		dictionary.TryGetValue("title", out var value2);
		long result;
		long? heartbeatTicks = (dictionary.TryGetValue("heartbeat_ticks", out var value3) && long.TryParse(value3, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)) ? new long?(result) : null;
		return new XellCustomStatus(string.IsNullOrWhiteSpace(value) ? "unknown" : value, value2, heartbeatTicks, path, text.Trim());
	}

	private static string ResolveEndpointPath(string html, IReadOnlyList<string> preferredPaths, params string[] tokens)
	{
		string? text = FindLinkPath(html, tokens);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		for (int i = 0; i < preferredPaths.Count; i++)
		{
			string text2 = preferredPaths[i];
			if (html.IndexOf(text2, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return text2;
			}
		}
		return preferredPaths[0];
	}

	private static string? FindLinkPath(string html, params string[] tokens)
	{
		foreach (Match item in LinkRegex.Matches(html))
		{
			string value = item.Groups["href"].Value;
			if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("/", StringComparison.Ordinal))
			{
				continue;
			}
			string text = NormalizeLabel(item.Groups["label"].Value);
			if (tokens.Any((string token) => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
			{
				return value;
			}
		}
		return null;
	}

	private static string NormalizeLabel(string rawLabel)
	{
		string input = TagRegex.Replace(rawLabel, " ");
		return WebUtility.HtmlDecode(input).Trim();
	}

	private static Uri BuildUri(XellHttpEndpoint endpoint, string relativePath)
	{
		return new Uri(new Uri(endpoint.BaseUrl.TrimEnd('/') + "/"), relativePath.TrimStart('/'));
	}

private static string BuildPayloadSyncPath(string job, string stage, bool? verified, int? verificationPass, int? verificationTotal, string? path, string? reason, string? finalToken, int? percent)
{
	List<string> list = new List<string> { "job=" + Uri.EscapeDataString(job), "stage=" + Uri.EscapeDataString(stage) };
		if (verified.HasValue)
		{
			list.Add("verified=" + (verified.Value ? "1" : "0"));
		}
		if (verificationPass.HasValue && verificationPass.Value > 0)
		{
			list.Add("pass=" + verificationPass.Value.ToString(CultureInfo.InvariantCulture));
		}
		if (verificationTotal.HasValue && verificationTotal.Value > 0)
		{
			list.Add("total=" + verificationTotal.Value.ToString(CultureInfo.InvariantCulture));
		}
		if (!string.IsNullOrWhiteSpace(path))
		{
			list.Add("path=" + Uri.EscapeDataString(path));
		}
		if (!string.IsNullOrWhiteSpace(reason))
		{
			list.Add("reason=" + Uri.EscapeDataString(reason));
		}
	if (!string.IsNullOrWhiteSpace(finalToken))
	{
		list.Add("final=" + Uri.EscapeDataString(finalToken));
	}
	if (percent.HasValue && percent.Value >= 0)
	{
		list.Add("percent=" + Math.Clamp(percent.Value, 0, 100).ToString(CultureInfo.InvariantCulture));
	}
	return "/XECLI_SYNC?" + string.Join("&", list);
}

	private static HttpClient CreateHttpClient(TimeSpan timeout)
	{
		SocketsHttpHandler socketsHttpHandler = new SocketsHttpHandler
		{
			ConnectTimeout = timeout,
			AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
		};
		HttpClient httpClient = new HttpClient(socketsHttpHandler);
		httpClient.Timeout = timeout;
		CliHttpUserAgent.Apply(httpClient);
		return httpClient;
	}

	private static async Task<bool> TryRequestPayloadSyncAsync(XellHttpEndpoint endpoint, string relativePath, CancellationToken cancellationToken)
	{
		try
		{
			using HttpClient httpClient = CreateHttpClient(TimeSpan.FromSeconds(3.0));
			using HttpResponseMessage response = await httpClient.GetAsync(BuildUri(endpoint, relativePath), cancellationToken);
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	private static async Task<IReadOnlyList<string>> TryGetAttachedUsbStorageAsync(XbdmClient client, CancellationToken cancellationToken)
	{
		try
		{
			IReadOnlyList<XbdmDriveEntry> drives = await client.GetDrivesAsync(includeSize: true, cancellationToken);
			return drives.Where((XbdmDriveEntry drive) => drive.Name.StartsWith("Usb", StringComparison.OrdinalIgnoreCase)).Select(FormatUsbDriveSummary).ToArray();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static string FormatUsbDriveSummary(XbdmDriveEntry drive)
	{
		string text = drive.Name.Trim();
		if (drive.TotalBytes.HasValue && drive.TotalBytes.Value > 0)
		{
			return text + " (" + FormatByteSize(drive.TotalBytes) + ")";
		}
		return text;
	}

	private static string FormatByteSize(ulong? bytes)
	{
		if (!bytes.HasValue)
		{
			return "unknown size";
		}
		double num = bytes.Value;
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		return $"{num:0.##} {array[num2]}";
	}

	private static void WriteUsbStorageGuidance(IReadOnlyList<string> usbStorage)
	{
		if (usbStorage == null || usbStorage.Count == 0)
		{
			return;
		}
		string text = string.Join(", ", usbStorage.Select(Markup.Escape));
		OperationFeedback.WriteWarning("External USB detected", "[grey]Attached:[/] [cyan]" + text + "[/]\n[grey]XeLL does not need removable USB unless it must read a file from it. XeLL starts HTTP before it scans storage, so a later freeze at [white]Fat mount uda0[/] points at removable USB. Unplug it and rerun if you are not intentionally using USB for XeLL.[/]");
	}

	private static void EnforceUsbSafetyPolicy(XellCommandSettings settings, IReadOnlyList<string> usbStorage)
	{
		if (usbStorage == null || usbStorage.Count == 0)
		{
			return;
		}
		if (!settings.Json)
		{
			WriteUsbStorageGuidance(usbStorage);
		}
		if (!settings.AllowUsb)
		{
			string text = string.Join(", ", usbStorage);
			throw new InvalidOperationException("XeCLI blocked the XeLL launch because removable USB storage is attached (" + text + "). Unplug removable USB and retry. Use --allow-usb only when XeLL must read a file from USB on purpose.");
		}
		if (!settings.Json)
		{
			OperationFeedback.WriteWarning("USB safety override enabled", "[grey]Continuing because [cyan]--allow-usb[/] was set. If this console stops at [white]Fat mount uda0[/], remove removable USB and retry without the override.[/]");
		}
	}

	private static Task SendPreLaunchNotificationsAsync(XbdmClient client, CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	private static async Task<string?> TryReadTextAsync(XellHttpEndpoint endpoint, string relativePath, CancellationToken cancellationToken)
	{
		try
		{
			using HttpClient httpClient = CreateHttpClient(TimeSpan.FromSeconds(5.0));
			using HttpResponseMessage response = await httpClient.GetAsync(BuildUri(endpoint, relativePath), cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			return await response.Content.ReadAsStringAsync(cancellationToken);
		}
		catch
		{
			return null;
		}
	}

	private static async Task<XellCustomStatus?> TryReadCustomStatusDirectAsync(string ip, TimeSpan requestTimeout, CancellationToken cancellationToken)
	{
		foreach (string item in DistinctNonEmptyPaths(DefaultCustomStatusPaths))
		{
			string? text = await TryReadHttpTextDirectAsync(ip, item, requestTimeout, cancellationToken);
			XellCustomStatus? xellCustomStatus = ParseCustomStatus(text, item);
			if (xellCustomStatus != null)
			{
				return xellCustomStatus;
			}
		}
		return null;
	}

	private static async Task<string?> TryReadHttpTextDirectAsync(string ip, string relativePath, TimeSpan requestTimeout, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			return null;
		}
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancellationTokenSource.CancelAfter(requestTimeout);
		try
		{
			using TcpClient tcpClient = new TcpClient();
			await tcpClient.ConnectAsync(ip, 80, cancellationTokenSource.Token);
			using NetworkStream networkStream = tcpClient.GetStream();
			byte[] bytes = Encoding.ASCII.GetBytes("GET " + relativePath + " HTTP/1.0\r\nHost: " + ip + "\r\nUser-Agent: " + CliHttpUserAgent.Value + "\r\nConnection: close\r\n\r\n");
			await networkStream.WriteAsync(bytes, cancellationTokenSource.Token);
			await networkStream.FlushAsync(cancellationTokenSource.Token);
			using MemoryStream memoryStream = new MemoryStream();
			byte[] array = new byte[4096];
			while (memoryStream.Length < 65536L)
			{
				int num = await networkStream.ReadAsync(array, cancellationTokenSource.Token);
				if (num <= 0)
				{
					break;
				}
				memoryStream.Write(array, 0, num);
			}
			return ExtractHttpBody(Encoding.ASCII.GetString(memoryStream.ToArray()));
		}
		catch
		{
			return null;
		}
	}

	private static string? ExtractHttpBody(string responseText)
	{
		if (string.IsNullOrWhiteSpace(responseText))
		{
			return null;
		}
		if (!responseText.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
		{
			return responseText;
		}
		int num = responseText.IndexOf("\r\n", StringComparison.Ordinal);
		string text = (num >= 0) ? responseText.Substring(0, num) : responseText;
		if (text.IndexOf(" 200 ", StringComparison.Ordinal) < 0)
		{
			return null;
		}
		int num2 = responseText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
		if (num2 < 0)
		{
			return null;
		}
		return responseText.Substring(num2 + 4);
	}

	private static string? NormalizeCpuKey(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		Match match = CpuKeyRegex.Match(text);
		if (match.Success)
		{
			return match.Groups["key"].Value.ToUpperInvariant();
		}
		match = BareHexRegex.Match(text.Trim());
		if (match.Success)
		{
			return match.Groups["key"].Value.ToUpperInvariant();
		}
		return null;
	}

	private static bool ContainsAny(string text, params string[] tokens)
	{
		for (int i = 0; i < tokens.Length; i++)
		{
			if (text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

private static async Task DownloadFromCandidatePathsAsync(XellHttpEndpoint endpoint, string?[] candidatePaths, string destinationPath, string title, string failureMessage, long minimumBytes, CancellationToken cancellationToken, Func<int, Task>? progressSyncCallback, bool quiet)
{
	Exception? ex = null;
	string[] array = DistinctNonEmptyPaths(candidatePaths).ToArray();
	for (int i = 0; i < array.Length; i++)
	{
		try
		{
			await DownloadBinaryAsync(endpoint, array[i], destinationPath, title, cancellationToken, progressSyncCallback, quiet);
			long downloadedBytes = new FileInfo(destinationPath).Length;
			if (downloadedBytes < minimumBytes)
			{
				throw new InvalidDataException($"XeLL returned only {downloadedBytes} bytes from {array[i]}; at least {minimumBytes} bytes are required.");
			}
			return;
		}
			catch (Exception ex2)
			{
				ex = ex2;
				TryDeleteFile(destinationPath);
			}
	}
	throw new InvalidOperationException(failureMessage + " Tried: " + string.Join(", ", array), ex);
}

private static async Task TryReportPayloadProgressAsync(Func<int, Task>? progressSyncCallback, int percent)
{
	if (progressSyncCallback == null)
	{
		return;
	}
	try
	{
		await progressSyncCallback(percent);
	}
	catch
	{
	}
}

	private static string?[] CombinePaths(string? preferredPath, IReadOnlyList<string> fallbackPaths)
	{
		string?[] array = new string?[fallbackPaths.Count + 1];
		array[0] = preferredPath;
		for (int i = 0; i < fallbackPaths.Count; i++)
		{
			array[i + 1] = fallbackPaths[i];
		}
		return array;
	}

	private static IEnumerable<string> DistinctNonEmptyPaths(params string?[] values)
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < values.Length; i++)
		{
			string? value = values[i];
			if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
			{
				yield return value;
			}
		}
	}

	private static async Task<bool> RemoteFileExistsAsync(XbdmClient client, string remotePath, CancellationToken cancellationToken)
	{
		string directoryName = Path.GetDirectoryName(remotePath) ?? string.Empty;
		string fileName = Path.GetFileName(remotePath);
		if (string.IsNullOrWhiteSpace(directoryName) || string.IsNullOrWhiteSpace(fileName))
		{
			return false;
		}
		try
		{
			string text = directoryName.EndsWith("\\", StringComparison.Ordinal) ? directoryName : (directoryName + "\\");
			return (await client.GetDirectoryAsync(text, cancellationToken)).Any((XbdmFileEntry entry) => !entry.IsDirectory && string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase));
		}
		catch
		{
			return false;
		}
	}

	private static async Task<bool> RemoteFileExistsAnyAsync(XbdmClient client, string targetIp, string remotePath, CancellationToken cancellationToken)
	{
		if (await RemoteFileExistsAsync(client, remotePath, cancellationToken))
		{
			return true;
		}
		return await RemoteFileExistsViaFtpAsync(targetIp, remotePath, cancellationToken);
	}

	private static async Task<bool> RemoteFileExistsViaFtpAsync(string targetIp, string remotePath, CancellationToken cancellationToken)
	{
		(string, int, string, string, int) tuple = ResolveFtpTarget(targetIp, 5000);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeoutMs = tuple.Item5;
		try
		{
			await using var asyncFtpClient = FtpHelpers.CreateClient(ip, port, user, pass, timeoutMs);
			await asyncFtpClient.Connect(cancellationToken);
			return await asyncFtpClient.FileExists(FtpHelpers.NormalizePath(remotePath));
		}
		catch
		{
			return false;
		}
	}

	private static string BuildMagicBootCommand(string xexPath)
	{
		string text = DeriveDirectory(xexPath);
		return "magicboot title=\"" + EscapeQuoted(xexPath) + "\" directory=\"" + EscapeQuoted(text) + "\"";
	}

	private static string ResolveLauncherPath(string? launcherPath)
	{
		if (!string.IsNullOrWhiteSpace(launcherPath))
		{
			return launcherPath.Trim();
		}
		return DefaultStagedLauncherPath;
	}

	private static string BuildXellBinPath(string launcherPath)
	{
		return DeriveDirectory(launcherPath).TrimEnd('\\', '/') + "\\xell.bin";
	}

	private static bool CanStageSiblingXellBin(string launcherPath)
	{
		return !launcherPath.StartsWith("\\Device\\Flash\\", StringComparison.OrdinalIgnoreCase);
	}

	private static bool LooksLikeXellLaunchPath(string launcherPath)
	{
		return launcherPath.IndexOf("xelllaunch", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static string DeriveDirectory(string xexPath)
	{
		int num = Math.Max(xexPath.LastIndexOf('\\'), xexPath.LastIndexOf('/'));
		if (num <= 0)
		{
			return xexPath;
		}
		return xexPath.Substring(0, num);
	}

	private static string EscapeQuoted(string value)
	{
		return value.Replace("\"", "\\\"", StringComparison.Ordinal);
	}

	private static (string Ip, int Port, string User, string Pass, int TimeoutMs) ResolveFtpTarget(string targetIp, int timeoutMs)
	{
		CliConfig cliConfig = CliConfig.Load();
		return (targetIp, cliConfig.DefaultFtpPort ?? 21, cliConfig.DefaultFtpUser ?? "xboxftp", cliConfig.DefaultFtpPassword ?? "xboxftp", Math.Max(timeoutMs, 5000));
	}

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static bool TryGetSubnetPrefix(string ip, out string prefix)
	{
		prefix = string.Empty;
		string[] array = ip.Split('.', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length != 4)
		{
			return false;
		}
		prefix = array[0] + "." + array[1] + "." + array[2] + ".";
		return true;
	}

	private static void AddIp(List<string> items, HashSet<string> seen, string? ip)
	{
		if (!string.IsNullOrWhiteSpace(ip) && seen.Add(ip))
		{
			items.Add(ip);
		}
	}
}
