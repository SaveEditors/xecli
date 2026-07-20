using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XellKvExportCommand : AsyncCommand<XellKvExportCommand.Settings>
{
	private enum KvVerificationMode
	{
		Skipped,
		SameSession
	}

	private const int MaxVerificationDownloads = 4;
	private const string OutputExistsTitle = "XeLL keyvault export failed";
	private const string OutputExistsCode = "XELL_KV_EXPORT_OUTPUT_EXISTS";
	private const string FailureTitle = "XeLL keyvault export failed";
	private const string FailureCode = "XELL_KV_EXPORT_FAILED";

	public sealed class Settings : XellCommandSettings
	{
		[CommandOption("--overwrite")]
		[LocalizedDescription("Allow existing output files and archive to be replaced.")]
		public bool Overwrite { get; init; }

		[CommandOption("--output <FILE>")]
		[LocalizedDescription("Output keyvault filename. Defaults to kv_yyyyMMdd_HHmmss.bin.")]
		public string? Output { get; init; }

		[CommandOption("--raw")]
		[LocalizedDescription("Export the raw keyvault block instead of the decrypted KV.")]
		public bool Raw { get; init; }

		[CommandOption("--single")]
		[LocalizedDescription("Take one keyvault export only and skip byte-for-byte verification.")]
		public bool Single { get; init; }

		[CommandOption("--no-verify")]
		[LocalizedDescription("Skip the repeated keyvault verification loop.")]
		public bool NoVerify { get; init; }

		[CommandOption("--dry-run")]
		[LocalizedDescription("Preview the export plan without connecting to the console.")]
		public bool DryRun { get; init; }

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

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		bool verified = !settings.Single && !settings.NoVerify;
		string outputPath = ResolveOutputPath(settings.Output, settings.Raw);
		if (settings.DryRun)
		{
			XellWorkflowHelpers.WriteDryRunPreview("kv export", outputPath, verified ? "repeated byte-for-byte verification" : "single pass", XellQuickBootHelpers.DescribeBootMode(settings), settings.Json);
			return 0;
		}
		string? stagingRoot = null;
		XellHttpEndpoint? xellEndpoint = null;
		string combinedTextPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory, "KV+CPU KEY.txt");
		string cpuKeyPath = ResolveSiblingPath(outputPath, "CPUKEY.txt");
		string fusePath = ResolveSiblingPath(outputPath, "fuses.txt");
		string manifestPath = ResolveSiblingPath(outputPath, "sha256.txt");
		string archivePath = Path.ChangeExtension(outputPath, ".zip");
		int verificationPass = 0;
		try
		{
			if (!TryReserveOutputPaths(settings, outputPath, archivePath, combinedTextPath, cpuKeyPath, fusePath, manifestPath))
			{
				return 1;
			}
			(string, int, int) target = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
			Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
			stagingRoot = Path.Combine(CliPaths.CachePath, "kv-exports", $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
			Directory.CreateDirectory(stagingRoot);
			if (!settings.Json)
			{
				AnsiConsole.MarkupLine("[grey]Output destination:[/] [cyan]" + Markup.Escape(outputPath) + "[/]");
			}
			string actionDescription = verified ? "Launch XeLL Reloaded, export the keyvault repeatedly, compare the captures, and package a verified backup at " + outputPath + "." : "Launch XeLL Reloaded, export the keyvault once, and package it at " + outputPath + ".";
			XellSessionResult xellSessionResult = await XellWorkflowHelpers.EnsureXellSessionAsync(target, settings, actionDescription, CancellationToken.None);
			XellHttpEndpoint endpoint = xellSessionResult.Endpoint;
			xellEndpoint = endpoint;
			await XellHelpers.TrySyncKeyVaultStartedAsync(endpoint, CancellationToken.None);
			string stagedReferencePath = Path.Combine(stagingRoot, settings.Raw ? "kvraw1.bin" : "kv1.bin");
			await XellHelpers.DownloadKeyVaultAsync(endpoint, stagedReferencePath, settings.Raw, verified ? "Keyvault export 1/2" : "Keyvault export 1/1", CancellationToken.None, async delegate(int percent)
			{
				await XellHelpers.TrySyncKeyVaultProgressAsync(endpoint, percent, CancellationToken.None);
			}, settings.Json);
			string? cpuKeyValue = await XellHelpers.TryReadCpuKeyAsync(endpoint, CancellationToken.None);
			string? fuseText = await XellHelpers.TryReadFuseTextAsync(endpoint, CancellationToken.None);
			if (string.IsNullOrWhiteSpace(cpuKeyValue))
			{
				throw new InvalidOperationException("XeLL did not expose a CPU key. The keyvault export set would be incomplete.");
			}
			KvVerificationMode verificationMode = KvVerificationMode.Skipped;
			if (verified)
			{
				verificationPass = await RunVerificationLoopAsync(endpoint, stagedReferencePath, settings.Raw, stagingRoot, settings.Json, async delegate(int pass)
				{
					verificationPass = pass;
					await XellHelpers.TrySyncVerificationProgressAsync(endpoint, XellPayloadJobKind.KeyVault, pass, MaxVerificationDownloads, CancellationToken.None);
				}, CancellationToken.None);
				verificationMode = KvVerificationMode.SameSession;
			}
			File.Copy(stagedReferencePath, outputPath, overwrite: settings.Overwrite);
			BackupPackageHelpers.VerifyCopiedOutput(stagedReferencePath, outputPath);
			string cpuKeyTextPath = BackupPackageHelpers.WriteText(combinedTextPath, BuildKvCpuKeyText(cpuKeyValue, outputPath, endpoint.Ip, verified, verificationPass));
			string cpuKeyFilePath = BackupPackageHelpers.WriteText(cpuKeyPath, cpuKeyValue.Trim() + Environment.NewLine);
			string? savedFusePath = BackupPackageHelpers.SaveOptionalText(fusePath, fuseText);
			List<BackupArtifact> artifacts = new List<BackupArtifact>
			{
				new BackupArtifact(outputPath, settings.Raw ? "KV_RAW.bin" : "KV.bin"),
				new BackupArtifact(cpuKeyFilePath, "CPUKEY.txt"),
				new BackupArtifact(cpuKeyTextPath, "KV+CPU KEY.txt")
			};
			if (!string.IsNullOrWhiteSpace(savedFusePath))
			{
				artifacts.Add(new BackupArtifact(savedFusePath, "fuses.txt"));
			}
			string manifestFilePath = BackupPackageHelpers.WriteSha256Manifest(manifestPath, artifacts);
			artifacts.Add(new BackupArtifact(manifestFilePath, "manifest.sha256.txt"));
			string zipPath = BackupPackageHelpers.CreateZip(outputPath, artifacts);
			BackupPackageHelpers.VerifyZipContents(zipPath, artifacts);
			XellConsoleFinalizationResult finalization = await XellHelpers.FinalizeJobAsync(endpoint, XellPayloadJobKind.KeyVault, verified, verificationPass, verified ? MaxVerificationDownloads : 0, outputPath, CancellationToken.None);
			WriteCompletion(settings, verified, verificationMode, endpoint.Ip, outputPath, zipPath, cpuKeyFilePath, cpuKeyValue, manifestFilePath, verificationPass, finalization);
			if (!settings.Json)
			{
				WriteFinalizationFeedback(finalization, verified);
			}
			TryDeleteDirectory(stagingRoot);
			return 0;
		}
		catch (Exception ex)
		{
			await TryNotifyFailureAsync(xellEndpoint, outputPath, verified, verificationPass, ex, CancellationToken.None, settings.Json);
			if (settings.Json)
			{
				CliOutput.EmitJsonError(BuildJsonFailureEnvelope(ex));
			}
			else
			{
				OperationFeedback.WriteFailure("XeLL keyvault export", ex.Message);
			}
			if (!settings.Json && !string.IsNullOrWhiteSpace(stagingRoot) && Directory.Exists(stagingRoot))
			{
				AnsiConsole.MarkupLine("[grey]Staging kept at[/] [cyan]" + Markup.Escape(stagingRoot) + "[/]");
			}
			if (!settings.Json)
			{
				XellWorkflowHelpers.WriteManualLaunchGuidance("rgh xell kv export");
			}
			return 1;
		}
	}

	private static async Task<int> RunVerificationLoopAsync(XellHttpEndpoint endpoint, string referencePath, bool raw, string stagingRoot, bool json, Func<int, Task> onPassStarted, CancellationToken cancellationToken)
	{
		for (int i = 1; i <= MaxVerificationDownloads; i++)
		{
			await onPassStarted(i);
			if (!json)
			{
				AnsiConsole.MarkupLine("[grey]Verification Pass[/] [white]" + i + "/" + MaxVerificationDownloads + "[/] [grey]in progress[/]");
			}
			string candidatePath = Path.Combine(stagingRoot, raw ? $"kvraw2_attempt{i}.bin" : $"kv2_attempt{i}.bin");
			await XellHelpers.DownloadKeyVaultAsync(endpoint, candidatePath, raw, "Keyvault verify " + i + "/" + MaxVerificationDownloads, cancellationToken, async delegate(int percent)
			{
				await XellHelpers.TrySyncVerificationPercentAsync(endpoint, XellPayloadJobKind.KeyVault, percent, cancellationToken);
			}, json);
			if (BackupPackageHelpers.FilesAreIdentical(referencePath, candidatePath))
			{
				File.Delete(candidatePath);
				return i;
			}
			if (File.Exists(candidatePath))
			{
				File.Delete(candidatePath);
			}
			if (!json && i < MaxVerificationDownloads)
			{
				OperationFeedback.WriteWarning("Keyvault mismatch", "The reference export and the verification export differed byte-for-byte. Retrying the verification read.");
			}
		}
		throw new InvalidOperationException("Keyvault exports did not match after repeated verification attempts. Do not trust these files.");
	}

	private static bool TryReserveOutputPaths(Settings settings, string outputPath, string archivePath, string combinedTextPath, string cpuKeyPath, string fusePath, string manifestPath)
	{
		string? existingPath = BackupPackageHelpers.FindExistingOutputPath(new[] { outputPath, archivePath, combinedTextPath, cpuKeyPath, fusePath, manifestPath }, settings.Overwrite);
		if (string.IsNullOrWhiteSpace(existingPath))
		{
			return true;
		}
		string text = BuildOutputCollisionMessage(existingPath);
		if (settings.Json)
		{
			CliOutput.EmitJsonError(new CliErrorEnvelope(OutputExistsTitle, text, OutputExistsCode, new[] { "Delete or move the existing output files, then rerun.", "Use --overwrite to replace the existing keyvault backup artifacts." }));
		}
		else
		{
			OperationFeedback.WriteFailure("XeLL keyvault export", text);
		}
		return false;
	}

	private static CliErrorEnvelope BuildJsonFailureEnvelope(Exception exception)
	{
		string message = BuildJsonFailureMessage(exception);
		return new CliErrorEnvelope(FailureTitle, message, FailureCode, new[] { "Check the console connection and retry.", "Use --single or --no-verify if verification is the failing step." });
	}

	private static string BuildJsonFailureMessage(Exception exception)
	{
		string message = exception.Message ?? string.Empty;
		if (message.IndexOf("did not match", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "The final keyvault output did not match the verified staging export.";
		}
		if (message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "XeCLI timed out while exporting the keyvault.";
		}
		if (message.IndexOf("cpu key", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "XeLL did not expose a CPU key for the export.";
		}
		return "XeCLI could not finish the keyvault export.";
	}

	private static string BuildOutputCollisionMessage(string path)
	{
		string leafName = Path.GetFileName(path);
		return string.IsNullOrWhiteSpace(leafName)
			? "An output artifact already exists."
			: "Output artifact '" + leafName + "' already exists.";
	}

	private static void WriteCompletion(Settings settings, bool verified, KvVerificationMode verificationMode, string xellIp, string kvPath, string zipPath, string cpuKeyPath, string cpuKeyValue, string manifestPath, int verificationPass, XellConsoleFinalizationResult finalization)
	{
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				XellIp = xellIp,
				KeyVault = kvPath,
				OutputDirectory = Path.GetDirectoryName(kvPath),
				Archive = zipPath,
				CpuKey = cpuKeyValue,
				CpuKeyPath = cpuKeyPath,
				Manifest = manifestPath,
				Raw = settings.Raw,
				Verified = verified,
				VerificationMode = verificationMode.ToString(),
				VerificationPass = verificationPass,
				VerificationTotal = verified ? MaxVerificationDownloads : 0,
				ConsoleFinalization = finalization.State,
				ConsoleFinalizationAccepted = finalization.Accepted,
				ConsoleRebootRequested = finalization.RebootRequested,
				ConsoleRebootObserved = finalization.RebootObserved
			});
			return;
		}
		string details = "[grey]XeLL IP:[/] [springgreen3_1]" + Markup.Escape(xellIp) + "[/]\n[grey]Keyvault file:[/] [cyan]" + Markup.Escape(kvPath) + "[/]\n[grey]Output directory:[/] [cyan]" + Markup.Escape(Path.GetDirectoryName(kvPath) ?? Environment.CurrentDirectory) + "[/]\n[grey]Archive:[/] [cyan]" + Markup.Escape(zipPath) + "[/]\n[grey]Manifest:[/] [cyan]" + Markup.Escape(manifestPath) + "[/]\n[grey]CPU key:[/] [gold1]" + Markup.Escape(cpuKeyValue) + "[/]\n[grey]CPU key file:[/] [cyan]" + Markup.Escape(cpuKeyPath) + "[/]\n[grey]Verified:[/] " + (verified ? "[springgreen3_1]yes[/]" : "[gold1]no[/]") + (verified ? "\n[grey]Verification pass:[/] [white]" + verificationPass + "/" + MaxVerificationDownloads + "[/]" : string.Empty);
		if (verified)
		{
			OperationFeedback.WriteSuccess("Keyvault export verified", details + "\n[springgreen3_1]Byte-for-byte verification passed.[/]\n[grey]Archive and manifest verified.[/]");
			return;
		}
		OperationFeedback.WriteSuccess("Keyvault export complete", details + "\n[gold1]Byte-for-byte verification skipped.[/]\n[grey]Archive and manifest verified.[/]");
	}

	private static void WriteFinalizationFeedback(XellConsoleFinalizationResult finalization, bool verified)
	{
		if (!finalization.Accepted)
		{
			if (verified)
			{
				OperationFeedback.WriteWarning("Auto reboot unavailable", "XeCLI finished and verified the keyvault export, but this XeLL payload did not accept the automatic return-to-dashboard request.");
			}
			else
			{
				OperationFeedback.WriteWarning("Manual reboot required", "XeCLI finished the keyvault export without byte-for-byte verification. The console was intentionally left in XeLL so the unverified result stays visible.");
			}
			return;
		}
		if (!verified)
		{
			AnsiConsole.MarkupLine("[grey]Console finalization:[/] [cyan]unverified completion status sent; console left in XeLL for manual reboot[/]");
			return;
		}
		AnsiConsole.MarkupLine("[grey]Console finalization:[/] [cyan]automatic return to dashboard requested[/]");
		if (finalization.RebootObserved)
		{
			OperationFeedback.WriteSuccess("Console reboot observed", "[springgreen3_1]XeLL stopped responding after the automatic reboot request.[/]");
			return;
		}
		OperationFeedback.WriteWarning("Auto reboot pending", "XeCLI finished the keyvault export, but the console stayed in XeLL for 15 seconds after the auto-reboot request. The export is valid; the return-to-dashboard path still needs hardware validation.");
	}

	private static async Task TryNotifyFailureAsync(XellHttpEndpoint? endpoint, string kvPath, bool verificationEnabled, int verificationPass, Exception exception, CancellationToken cancellationToken, bool json)
	{
		if (endpoint == null)
		{
			return;
		}
		if (!await XellHelpers.TryNotifyFailureAsync(endpoint, XellPayloadJobKind.KeyVault, BuildPayloadFailureSummary(exception, verificationEnabled, verificationPass), verificationPass, verificationEnabled ? MaxVerificationDownloads : 0, kvPath, cancellationToken))
		{
			if (!json)
			{
				OperationFeedback.WriteWarning("Payload failure status unavailable", "XeCLI could not update the XeLL screen with the failure state. The console was not asked to reboot.");
			}
			return;
		}
		if (!json)
		{
			AnsiConsole.MarkupLine("[grey]Console finalization:[/] [cyan]failure status sent to the XeCLI payload[/]");
			OperationFeedback.WriteWarning("Manual reboot required", "XeCLI left the console on the failure screen so the error stays visible until you review it.");
		}
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
			return verificationEnabled ? "XeCLI timed out during keyvault verification." : "XeCLI timed out during the keyvault export.";
		}
		if (message.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("xell", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "XeLL stopped responding before XeCLI finished.";
		}
		return verificationEnabled ? "XeCLI could not finish keyvault verification." : "XeCLI could not finish the keyvault export.";
	}

	private static string ResolveOutputPath(string? output, bool raw)
	{
		if (!string.IsNullOrWhiteSpace(output))
		{
			return Path.GetFullPath(output);
		}
		return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, (raw ? "kv_raw_" : "kv_") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bin"));
	}

	private static string ResolveSiblingPath(string outputPath, string suffixName)
	{
		string directoryName = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
		return Path.Combine(directoryName, fileNameWithoutExtension + "." + suffixName);
	}

	private static string BuildKvCpuKeyText(string cpuKey, string kvPath, string xellIp, bool verified, int verificationPass)
	{
		FileInfo fileInfo = new FileInfo(kvPath);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("CPU Key: " + cpuKey.Trim());
		stringBuilder.AppendLine("KV File: " + Path.GetFileName(kvPath));
		stringBuilder.AppendLine("KV Size: " + fileInfo.Length + " bytes");
		stringBuilder.AppendLine("KV SHA256: " + BackupPackageHelpers.ComputeSha256(kvPath));
		stringBuilder.AppendLine("XeLL IP: " + xellIp);
		stringBuilder.AppendLine("Verified: " + (verified ? "yes" : "no"));
		if (verified)
		{
			stringBuilder.AppendLine("Verification Pass: " + verificationPass + "/" + MaxVerificationDownloads);
		}
		stringBuilder.AppendLine("Captured: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		return stringBuilder.ToString();
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
