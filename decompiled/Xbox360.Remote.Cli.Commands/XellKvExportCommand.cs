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
	public sealed class Settings : XellCommandSettings
	{
		[CommandOption("--output <FILE>")]
		[Description("Output keyvault filename. Defaults to kv_yyyyMMdd_HHmmss.bin.")]
		public string? Output { get; init; }

		[CommandOption("--raw")]
		[Description("Export the raw keyvault block instead of the decrypted KV.")]
		public bool Raw { get; init; }

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
		try
		{
			(string, int, int) tuple = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
			XellSessionResult xellSessionResult = await XellWorkflowHelpers.EnsureXellSessionAsync(tuple, settings, "Launch XeLL Reloaded, export the keyvault, capture the CPU key, and package a verified backup set.", CancellationToken.None);
			XellHttpEndpoint endpoint = xellSessionResult.Endpoint;
			string text = ResolveOutputPath(settings.Output, settings.Raw);
			string text2 = Path.Combine(Path.GetDirectoryName(text) ?? Environment.CurrentDirectory, "KV+CPU KEY.txt");
			string text3 = ResolveSiblingPath(text, "CPUKEY.txt");
			string text4 = ResolveSiblingPath(text, "fuses.txt");
			string text5 = ResolveSiblingPath(text, "sha256.txt");
			Directory.CreateDirectory(Path.GetDirectoryName(text) ?? Environment.CurrentDirectory);
			await XellHelpers.DownloadKeyVaultAsync(endpoint, text, settings.Raw, settings.Raw ? "XeLL keyvault export (raw)" : "XeLL keyvault export", CancellationToken.None);
			string? text6 = await XellHelpers.TryReadCpuKeyAsync(endpoint, CancellationToken.None);
			string? text7 = await XellHelpers.TryReadFuseTextAsync(endpoint, CancellationToken.None);
			if (string.IsNullOrWhiteSpace(text6))
			{
				throw new InvalidOperationException("XeLL did not expose a CPU key. The keyvault export set would be incomplete.");
			}
			string text8 = BackupPackageHelpers.WriteText(text2, BuildKvCpuKeyText(text6, text, endpoint.Ip));
			string text9 = BackupPackageHelpers.WriteText(text3, text6.Trim() + Environment.NewLine);
			string? text10 = BackupPackageHelpers.SaveOptionalText(text4, text7);
			List<BackupArtifact> list = new List<BackupArtifact>
			{
				new BackupArtifact(text, settings.Raw ? "KV_RAW.bin" : "KV.bin"),
				new BackupArtifact(text9, "CPUKEY.txt"),
				new BackupArtifact(text8, "KV+CPU KEY.txt")
			};
			if (!string.IsNullOrWhiteSpace(text10))
			{
				list.Add(new BackupArtifact(text10, "fuses.txt"));
			}
			string text11 = BackupPackageHelpers.WriteSha256Manifest(text5, list);
			list.Add(new BackupArtifact(text11, "manifest.sha256.txt"));
			string text12 = BackupPackageHelpers.CreateZip(text, list);
			BackupPackageHelpers.VerifyZipContents(text12, list);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					XellIp = endpoint.Ip,
					KeyVault = text,
					CpuKey = text9,
					CombinedText = text8,
					Archive = text12,
					Manifest = text11,
					Raw = settings.Raw
				});
				return 0;
			}
			OperationFeedback.WriteSuccess("Keyvault export complete", "[grey]KV:[/] [cyan]" + Markup.Escape(text) + "[/]\n[grey]CPU key:[/] [cyan]" + Markup.Escape(text9) + "[/]\n[grey]Combined text:[/] [cyan]" + Markup.Escape(text8) + "[/]\n[grey]Archive:[/] [cyan]" + Markup.Escape(text12) + "[/]\n[grey]Manifest:[/] [cyan]" + Markup.Escape(text11) + "[/]");
			return 0;
		}
		catch (Exception ex)
		{
			OperationFeedback.WriteFailure("XeLL keyvault export", ex.Message);
			XellWorkflowHelpers.WriteManualLaunchGuidance("rgh xell kv export");
			return 1;
		}
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

	private static string BuildKvCpuKeyText(string cpuKey, string kvPath, string xellIp)
	{
		FileInfo fileInfo = new FileInfo(kvPath);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("CPU Key: " + cpuKey.Trim());
		stringBuilder.AppendLine("KV File: " + Path.GetFileName(kvPath));
		stringBuilder.AppendLine("KV Size: " + fileInfo.Length + " bytes");
		stringBuilder.AppendLine("KV SHA256: " + BackupPackageHelpers.ComputeSha256(kvPath));
		stringBuilder.AppendLine("XeLL IP: " + xellIp);
		stringBuilder.AppendLine("Captured: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		return stringBuilder.ToString();
	}
}
