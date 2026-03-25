using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class NandDumpCommand : AsyncCommand<NandDumpCommand.Settings>
{
	private enum NandVerificationMode
	{
		Skipped,
		RebootCycle,
		SameSessionFallback
	}

	private readonly record struct NandVerificationResult(string DumpPath, NandVerificationMode Mode, int SuccessfulPass);

	public sealed class Settings : XellCommandSettings
	{
		[CommandOption("--single")]
		[Description("Take one dump only and skip byte-for-byte verification.")]
		public bool Single { get; init; }

		[CommandOption("--output <FILE>")]
		[Description("Output NAND filename. Defaults to nand_backup_yyyyMMdd_HHmmss.bin.")]
		public string? Output { get; init; }

		[CommandOption("--no-verify")]
		[Description("Skip the second-dump verification loop.")]
		public bool NoVerify { get; init; }

		public override ValidationResult Validate()
		{
			if (string.IsNullOrWhiteSpace(Output))
			{
				return ValidationResult.Success();
			}
			string text = Output.Trim();
			if (text.EndsWith(Path.DirectorySeparatorChar) || text.EndsWith(Path.AltDirectorySeparatorChar))
			{
				return ValidationResult.Error("--output must be a file path, not a directory.");
			}
			return ValidationResult.Success();
		}
	}

	private const int MaxVerificationDownloads = 4;

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		string? stagingRoot = null;
		XellHttpEndpoint? xellEndpoint = null;
		bool flag = !settings.Single && !settings.NoVerify;
		string text = ResolveOutputPath(settings.Output);
		int verificationPass = 0;
		try
		{
			(string, int, int) tuple = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
			string text2 = ResolveSiblingPath(text, "cpukey.txt");
			string text3 = ResolveSiblingPath(text, "startup-log.txt");
			string text4 = ResolveSiblingPath(text, "sha256.txt");
			Directory.CreateDirectory(Path.GetDirectoryName(text) ?? Environment.CurrentDirectory);
			stagingRoot = Path.Combine(CliPaths.CachePath, "nand-dumps", $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
			Directory.CreateDirectory(stagingRoot);
			AnsiConsole.MarkupLine("[grey]Output destination:[/] [cyan]" + Markup.Escape(text) + "[/]");
			NandVerificationMode nandVerificationMode = NandVerificationMode.Skipped;
			string actionDescription = flag ? "Launch XeLL Reloaded, take repeated NAND dumps, compare them, and package a verified backup at " + text + "." : "Launch XeLL Reloaded, take a single NAND dump, and package it at " + text + ".";
			XellSessionResult xellSessionResult = await XellWorkflowHelpers.EnsureXellSessionAsync(tuple, settings, actionDescription, CancellationToken.None);
			XellHttpEndpoint endpoint = xellSessionResult.Endpoint;
			xellEndpoint = endpoint;
			bool preferSameSessionVerification = flag && await XellHelpers.TryReadCustomStatusAsync(endpoint, CancellationToken.None) != null;
			await XellHelpers.TrySyncDumpStartedAsync(endpoint, CancellationToken.None);
			string text5 = Path.Combine(stagingRoot, "nand1.bin");
			await XellHelpers.DownloadRawFlashAsync(endpoint, text5, flag ? "NAND dump 1/2" : "NAND dump 1/1", CancellationToken.None, async delegate(int percent)
			{
				await XellHelpers.TrySyncDumpProgressAsync(endpoint, percent, CancellationToken.None);
			});
			string? text6 = await XellHelpers.TryReadCpuKeyAsync(endpoint, CancellationToken.None);
			string? text7 = await XellHelpers.TryReadStartupLogAsync(endpoint, CancellationToken.None);
			if (flag)
			{
				NandVerificationResult nandVerificationResult = await RunVerificationLoopAsync(tuple, settings, xellSessionResult.BootRequest, endpoint, text5, stagingRoot, preferSameSessionVerification, async delegate(int pass)
				{
					verificationPass = pass;
					await XellHelpers.TrySyncVerificationProgressAsync(endpoint, pass, MaxVerificationDownloads, CancellationToken.None);
				}, CancellationToken.None);
				nandVerificationMode = nandVerificationResult.Mode;
				verificationPass = nandVerificationResult.SuccessfulPass;
				if (File.Exists(nandVerificationResult.DumpPath))
				{
					File.Delete(nandVerificationResult.DumpPath);
				}
			}
			File.Copy(text5, text, overwrite: true);
			BackupPackageHelpers.VerifyCopiedOutput(text5, text);
			string? text9 = BackupPackageHelpers.SaveOptionalText(text2, text6);
			string? text10 = BackupPackageHelpers.SaveOptionalText(text3, text7);
			List<BackupArtifact> list = new List<BackupArtifact> { new BackupArtifact(text, Path.GetFileName(text)) };
			if (!string.IsNullOrWhiteSpace(text9))
			{
				list.Add(new BackupArtifact(text9, Path.GetFileName(text9)));
			}
			if (!string.IsNullOrWhiteSpace(text10))
			{
				list.Add(new BackupArtifact(text10, Path.GetFileName(text10)));
			}
			string text11 = BackupPackageHelpers.WriteSha256Manifest(text4, list);
			list.Add(new BackupArtifact(text11, Path.GetFileName(text11)));
			string text12 = BackupPackageHelpers.CreateZip(text, list);
			BackupPackageHelpers.VerifyZipContents(text12, list);
			WriteCompletion(settings, flag, nandVerificationMode, endpoint.Ip, text, text12, text9, text6, text11, verificationPass);
			await TryFinalizeConsoleAsync(endpoint, flag, verificationPass, text, CancellationToken.None);
			TryDeleteDirectory(stagingRoot);
			return 0;
		}
		catch (OperationCanceledException)
		{
			OperationFeedback.WriteWarning("NAND dump cancelled", "No NAND writes were performed.");
			if (!string.IsNullOrWhiteSpace(stagingRoot) && Directory.Exists(stagingRoot))
			{
				AnsiConsole.MarkupLine("[grey]Staging kept at[/] [cyan]" + Markup.Escape(stagingRoot) + "[/]");
			}
			return 1;
		}
		catch (Exception ex)
		{
			await TryNotifyFailureAsync(xellEndpoint, text, flag, verificationPass, ex, CancellationToken.None);
			OperationFeedback.WriteFailure("NAND dump", ex.Message);
			if (!string.IsNullOrWhiteSpace(stagingRoot) && Directory.Exists(stagingRoot))
			{
				AnsiConsole.MarkupLine("[grey]Staging kept at[/] [cyan]" + Markup.Escape(stagingRoot) + "[/]");
			}
			WriteFallbackGuidance();
			return 1;
		}
	}

	private static async Task<NandVerificationResult> RunVerificationLoopAsync((string Ip, int Port, int TimeoutMs) target, Settings settings, XellBootRequest? bootRequest, XellHttpEndpoint endpoint, string referenceDumpPath, string stagingRoot, bool preferSameSessionVerification, Func<int, Task> onPassStarted, CancellationToken cancellationToken)
	{
		string text = string.Empty;
		XellHttpEndpoint xellHttpEndpoint = endpoint;
		for (int i = 1; i <= MaxVerificationDownloads; i++)
		{
			await onPassStarted(i);
			AnsiConsole.MarkupLine("[grey]Verification Pass[/] [white]" + i + "/" + MaxVerificationDownloads + "[/] [grey]in progress[/]");
			if (preferSameSessionVerification)
			{
				if (i == 1)
				{
					OperationFeedback.WriteWarning("Same-session verification selected", "The active XeCLI payload is returning through the dashboard between boots on this console, so XeCLI is verifying repeated reads in the same XeLL session to avoid dashboard-induced NAND drift.");
				}
				text = Path.Combine(stagingRoot, $"nand2_attempt{i}.bin");
				XellHttpEndpoint endpointForSameSession = xellHttpEndpoint;
				await XellHelpers.DownloadRawFlashAsync(endpointForSameSession, text, "NAND verify " + i + "/" + MaxVerificationDownloads, cancellationToken, async delegate(int percent)
				{
					await XellHelpers.TrySyncVerificationPercentAsync(endpointForSameSession, percent, cancellationToken);
				});
				if (BackupPackageHelpers.FilesAreIdentical(referenceDumpPath, text))
				{
					return new NandVerificationResult(text, NandVerificationMode.SameSessionFallback, i);
				}
				if (i < MaxVerificationDownloads)
				{
					OperationFeedback.WriteWarning("NAND mismatch", "The reference dump and the same-session verification dump differed byte-for-byte. Retrying the second dump.");
				}
				continue;
			}
			bool flag = await XellHelpers.TryRequestRebootAsync(xellHttpEndpoint, cancellationToken);
			if (!flag)
			{
				OperationFeedback.WriteWarning("XeLL reboot unavailable", "This XeLL session did not accept a reboot request. Falling back to a same-session verification dump.");
				text = Path.Combine(stagingRoot, $"nand2_attempt{i}.bin");
				XellHttpEndpoint endpointForFallback = xellHttpEndpoint;
				await XellHelpers.DownloadRawFlashAsync(endpointForFallback, text, "NAND verify " + i + "/" + MaxVerificationDownloads, cancellationToken, async delegate(int percent)
				{
					await XellHelpers.TrySyncVerificationPercentAsync(endpointForFallback, percent, cancellationToken);
				});
				if (BackupPackageHelpers.FilesAreIdentical(referenceDumpPath, text))
				{
					return new NandVerificationResult(text, NandVerificationMode.SameSessionFallback, i);
				}
				if (i < MaxVerificationDownloads)
				{
					OperationFeedback.WriteWarning("NAND mismatch", "The reference dump and the fallback verification dump differed byte-for-byte. Retrying the second dump.");
				}
				continue;
			}
			if (!await XellHelpers.TryWaitForXellToDisappearAsync(xellHttpEndpoint.Ip, TimeSpan.FromSeconds(10.0), cancellationToken))
			{
				OperationFeedback.WriteWarning("XeLL reboot ignored", "The XeLL web server stayed up after the reboot request. Falling back to a same-session verification dump.");
				text = Path.Combine(stagingRoot, $"nand2_attempt{i}.bin");
				XellHttpEndpoint endpointForIgnoredReboot = xellHttpEndpoint;
				await XellHelpers.DownloadRawFlashAsync(endpointForIgnoredReboot, text, "NAND verify " + i + "/" + MaxVerificationDownloads, cancellationToken, async delegate(int percent)
				{
					await XellHelpers.TrySyncVerificationPercentAsync(endpointForIgnoredReboot, percent, cancellationToken);
				});
				if (BackupPackageHelpers.FilesAreIdentical(referenceDumpPath, text))
				{
					return new NandVerificationResult(text, NandVerificationMode.SameSessionFallback, i);
				}
				if (i < MaxVerificationDownloads)
				{
					OperationFeedback.WriteWarning("NAND mismatch", "The reference dump and the fallback verification dump differed byte-for-byte. Retrying the second dump.");
				}
				continue;
			}
			xellHttpEndpoint = await XellWorkflowHelpers.RecoverVerificationSessionAsync(target, settings, xellHttpEndpoint.Ip, bootRequest, cancellationToken);
			text = Path.Combine(stagingRoot, $"nand2_attempt{i}.bin");
			XellHttpEndpoint endpointForVerification = xellHttpEndpoint;
			await XellHelpers.DownloadRawFlashAsync(endpointForVerification, text, "NAND verify " + i + "/" + MaxVerificationDownloads, cancellationToken, async delegate(int percent)
			{
				await XellHelpers.TrySyncVerificationPercentAsync(endpointForVerification, percent, cancellationToken);
			});
			if (BackupPackageHelpers.FilesAreIdentical(referenceDumpPath, text))
			{
				return new NandVerificationResult(text, NandVerificationMode.RebootCycle, i);
			}
			if (i < MaxVerificationDownloads)
			{
				OperationFeedback.WriteWarning("NAND mismatch", "The reference dump and the verification dump differed byte-for-byte. Retrying the second dump.");
			}
		}
		throw new InvalidOperationException("NAND dumps did not match after repeated verification attempts. Do not flash these files.");
	}

	private static void WriteCompletion(Settings settings, bool verified, NandVerificationMode verificationMode, string xellIp, string nandPath, string zipPath, string? cpuKeyPath, string? cpuKeyValue, string manifestPath, int verificationPass)
	{
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				XellIp = xellIp,
				Output = nandPath,
				OutputDirectory = Path.GetDirectoryName(nandPath),
				Archive = zipPath,
				CpuKey = cpuKeyValue,
				CpuKeyPath = cpuKeyPath,
				Manifest = manifestPath,
				Verified = verified,
				VerificationMode = verificationMode.ToString(),
				VerificationPass = verificationPass,
				VerificationTotal = verified ? MaxVerificationDownloads : 0
			});
			return;
		}
		string text = "[grey]XeLL IP:[/] [springgreen3_1]" + Markup.Escape(xellIp) + "[/]\n[grey]Dump file:[/] [cyan]" + Markup.Escape(nandPath) + "[/]\n[grey]Dump directory:[/] [cyan]" + Markup.Escape(Path.GetDirectoryName(nandPath) ?? Environment.CurrentDirectory) + "[/]\n[grey]Archive:[/] [cyan]" + Markup.Escape(zipPath) + "[/]\n[grey]Manifest:[/] [cyan]" + Markup.Escape(manifestPath) + "[/]\n[grey]CPU key:[/] " + (string.IsNullOrWhiteSpace(cpuKeyValue) ? "[grey]not detected[/]" : "[gold1]" + Markup.Escape(cpuKeyValue) + "[/]") + "\n[grey]CPU key file:[/] " + (string.IsNullOrWhiteSpace(cpuKeyPath) ? "[grey]not exposed[/]" : "[cyan]" + Markup.Escape(cpuKeyPath) + "[/]") + "\n[grey]Verified:[/] " + (verified ? "[springgreen3_1]yes[/]" : "[gold1]no[/]") + "\n[grey]Verification mode:[/] [cyan]" + Markup.Escape(verificationMode.ToString()) + "[/]" + (verified ? "\n[grey]Verification pass:[/] [white]" + verificationPass + "/" + MaxVerificationDownloads + "[/]" : string.Empty);
		if (verified)
		{
			if (verificationMode == NandVerificationMode.SameSessionFallback)
			{
				OperationFeedback.WriteSuccess("NAND dump verified", text + "\n[springgreen3_1]Byte-for-byte verification passed in the same XeLL session.[/]\n[grey]XeCLI used a same-session second dump for this console/payload combination instead of a reboot-cycled pass. Archive and manifest verified.[/]");
				return;
			}
			OperationFeedback.WriteSuccess("NAND dump verified", text + "\n[springgreen3_1]NAND verified - safe to flash[/]\n[grey]Archive and manifest verified.[/]");
			return;
		}
		OperationFeedback.WriteSuccess("NAND dump complete", text + "\n[gold1]Byte-for-byte dump verification skipped.[/]\n[grey]Archive and manifest verified.[/]");
	}

	private static async Task TryFinalizeConsoleAsync(XellHttpEndpoint endpoint, bool verified, int verificationPass, string nandPath, CancellationToken cancellationToken)
	{
		bool flag = await XellHelpers.TryRequestCompletionRebootAsync(endpoint, verified, verificationPass, verified ? MaxVerificationDownloads : 0, nandPath, cancellationToken);
		if (!flag)
		{
			if (verified)
			{
				OperationFeedback.WriteWarning("Auto reboot unavailable", "XeCLI finished and verified the NAND dump, but this XeLL payload did not accept the automatic return-to-dashboard request.");
			}
			else
			{
				OperationFeedback.WriteWarning("Manual reboot required", "XeCLI finished the NAND dump without byte-for-byte verification. The console was intentionally left in XeLL so the unverified result stays visible.");
			}
			return;
		}
		if (!verified)
		{
			AnsiConsole.MarkupLine("[grey]Console finalization:[/] [cyan]unverified completion status sent; console left in XeLL for manual reboot[/]");
			return;
		}
		AnsiConsole.MarkupLine("[grey]Console finalization:[/] [cyan]automatic return to dashboard requested[/]");
		if (await XellHelpers.TryWaitForXellToDisappearAsync(endpoint.Ip, TimeSpan.FromSeconds(15.0), cancellationToken))
		{
			OperationFeedback.WriteSuccess("Console reboot requested", "[springgreen3_1]The XeLL payload accepted the automatic reboot request.[/]");
			return;
		}
		OperationFeedback.WriteWarning("Auto reboot pending", "XeCLI finished the NAND dump, but the console stayed in XeLL for 15 seconds after the auto-reboot request. The dump is valid; the return-to-dashboard path still needs hardware validation.");
	}

	private static async Task TryNotifyFailureAsync(XellHttpEndpoint? endpoint, string nandPath, bool verificationEnabled, int verificationPass, Exception exception, CancellationToken cancellationToken)
	{
		if (endpoint == null)
		{
			return;
		}

		if (!await XellHelpers.TryNotifyFailureAsync(endpoint, BuildPayloadFailureSummary(exception, verificationEnabled, verificationPass), verificationPass, verificationEnabled ? MaxVerificationDownloads : 0, nandPath, cancellationToken))
		{
			OperationFeedback.WriteWarning("Payload failure status unavailable", "XeCLI could not update the XeLL screen with the failure state. The console was not asked to reboot.");
			return;
		}

		AnsiConsole.MarkupLine("[grey]Console finalization:[/] [cyan]failure status sent to the XeCLI payload[/]");
		OperationFeedback.WriteWarning("Manual reboot required", "XeCLI left the console on the failure screen so the error stays visible until you review it.");
	}

	private static string BuildPayloadFailureSummary(Exception exception, bool verificationEnabled, int verificationPass)
	{
		string message = exception.Message ?? string.Empty;

		if (message.IndexOf("did not match", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			if (verificationPass > 0)
			{
				return "Verification failed on pass " + verificationPass + "/" + MaxVerificationDownloads + ".";
			}
			return "Verification failed after repeated mismatches.";
		}
		if (message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return verificationEnabled ? "XeCLI timed out during NAND verification." : "XeCLI timed out during the NAND dump.";
		}
		if (message.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("xell", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "XeLL stopped responding before XeCLI finished.";
		}
		return verificationEnabled ? "XeCLI could not finish NAND verification." : "XeCLI could not finish the NAND dump.";
	}

	private static void WriteFallbackGuidance()
	{
		XellWorkflowHelpers.WriteManualLaunchGuidance("rgh nand dump");
		AnsiConsole.MarkupLine("[grey]Fallback:[/] connect Ethernet and verify that XeLL Reloaded serves HTTP on port 80.");
	}

	private static string ResolveOutputPath(string? output)
	{
		if (!string.IsNullOrWhiteSpace(output))
		{
			return Path.GetFullPath(output);
		}
		return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "nand_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bin"));
	}

	private static string ResolveSiblingPath(string outputPath, string suffixName)
	{
		string directoryName = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
		return Path.Combine(directoryName, fileNameWithoutExtension + "." + suffixName);
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
		}
	}
}
