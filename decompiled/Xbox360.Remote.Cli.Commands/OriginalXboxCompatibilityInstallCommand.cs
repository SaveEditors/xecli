using System;
using System.ComponentModel;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

public sealed class OriginalXboxCompatibilityInstallCommand : Command<OriginalXboxCompatibilityInstallCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandArgument(0, "<SET>")]
		[LocalizedDescription("Compatibility set to install: hacked, hud, or retail.")]
		public string SetId { get; init; } = string.Empty;

		[CommandOption("--usb <TARGET>")]
		[LocalizedDescription("Stage to a removable USB drive, drive letter, selection number, or host folder path.")]
		public string? UsbTarget { get; init; }

		[CommandOption("--include-fixer")]
		[LocalizedDescription("Also stage or install HDD Compatibility Partition Fixer.")]
		public bool IncludeFixer { get; init; }

		[CommandOption("--cache <DIR>")]
		[LocalizedDescription("Download cache directory.")]
		public string? CacheDirectory { get; init; }

		[CommandOption("--force-download")]
		[LocalizedDescription("Redownload archives even when they already exist in the cache.")]
		public bool ForceDownload { get; init; }

		[CommandOption("--auto-confirm")]
		[LocalizedDescription("Skip confirmation prompts.")]
		public bool AutoConfirm { get; init; }

		public override ValidationResult Validate()
		{
			if (!OriginalXboxCompatibilityService.IsKnownSetId(SetId))
			{
				return ValidationResult.Error("Unknown set. Expected hacked, hud, or retail.");
			}
			return ValidationResult.Success();
		}
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(settings.UsbTarget))
			{
				string targetRoot = HostDriveService.ResolveTargetRoot(settings.UsbTarget, settings.AutoConfirm);
				OriginalXboxCompatibilityInstallResult result = OriginalXboxCompatibilityService.InstallToHostAsync(settings.SetId, targetRoot, settings.CacheDirectory, settings.IncludeFixer, settings.ForceDownload, settings.AutoConfirm, CancellationToken.None).GetAwaiter().GetResult();
				if (settings.Json)
				{
					CliOutput.EmitJson(result);
					return 0;
				}
				OriginalXboxCompatibilityService.RenderInstallResult(result);
				return 0;
			}
			OriginalXboxCompatibilityInstallResult result2 = OriginalXboxCompatibilityService.InstallToConsoleAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
			if (settings.Json)
			{
				CliOutput.EmitJson(result2);
				return 0;
			}
			OriginalXboxCompatibilityService.RenderInstallResult(result2);
			return 0;
		}
		catch (OperationCanceledException)
		{
			OperationFeedback.WriteWarning("Original Xbox compatibility install cancelled", "No changes were made.");
			return 1;
		}
		catch (Exception ex2)
		{
			OperationFeedback.WriteFailure("Original Xbox compatibility install", ex2.Message);
			return 1;
		}
	}
}
