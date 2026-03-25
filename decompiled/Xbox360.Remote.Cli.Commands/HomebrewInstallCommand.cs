using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

public sealed class HomebrewInstallCommand : Command<HomebrewInstallCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Package to install or stage: aurora, dashlaunch, xexmenu, fsd, xm360, timefixer, simple360, xelllaunch, or all.")]
		public string Package { get; init; } = string.Empty;

		[CommandOption("--usb <TARGET>")]
		[Description("Stage to a removable USB drive, drive letter, selection number, or host folder path.")]
		public string? UsbTarget { get; init; }

		[CommandOption("--device <DEVICE>")]
		[Description("Install directly to a detected console device such as Hdd1, Usb0, Usb1, or Usb2.")]
		public string? Device { get; init; }

		[CommandOption("--ini-mode <MODE>")]
		[Description("Console install launch.ini behavior: generated, merge, or skip.")]
		public string? IniMode { get; init; }

		[CommandOption("--ini <PATH>")]
		[Description("Console launch.ini path (default: /Hdd1/launch.ini).")]
		public string? IniPath { get; init; }

		[CommandOption("--cache <DIR>")]
		[Description("Package download cache directory.")]
		public string? CacheDirectory { get; init; }

		[CommandOption("--force-download")]
		[Description("Redownload archives even when they already exist in the cache.")]
		public bool ForceDownload { get; init; }

		[CommandOption("--auto-confirm")]
		[Description("Skip confirmation prompts.")]
		public bool AutoConfirm { get; init; }

		public override ValidationResult Validate()
		{
			if (!string.IsNullOrWhiteSpace(UsbTarget) && !string.IsNullOrWhiteSpace(Device))
			{
				return ValidationResult.Error("Use either --usb for local staging or --device for console install, not both.");
			}
			if (HomebrewPackageService.IsKnownPackageId(Package))
			{
				return ValidationResult.Success();
			}
			return ValidationResult.Error("Unknown package '" + Package + "'. Expected aurora, dashlaunch, xexmenu, fsd, xm360, timefixer, simple360, xelllaunch, or all.");
		}
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(settings.UsbTarget))
			{
				string targetRoot = HostDriveService.ResolveTargetRoot(settings.UsbTarget, settings.AutoConfirm);
				HomebrewInstallResult result = HomebrewPackageService.InstallAsync(settings.Package, targetRoot, settings.CacheDirectory, settings.ForceDownload, settings.AutoConfirm, CancellationToken.None).GetAwaiter().GetResult();
				if (settings.Json)
				{
					CliOutput.EmitJson(new
					{
						Mode = "local",
						TargetRoot = result.TargetRoot,
						LaunchIniWritten = result.LaunchIniWritten,
						PluginsCopied = result.PluginsCopied,
						Packages = result.Packages.Select((InstalledHomebrewPackage package) => new { package.Id, package.DisplayName, package.InstallFolderName, package.ArchivePath, package.InstallPath, package.FileCount, package.TotalBytes })
					});
					return 0;
				}
				HomebrewPackageService.RenderInstallResult(result);
				return 0;
			}
			HomebrewConsoleInstallResult result2 = HomebrewConsoleInstallService.InstallAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Mode = "console",
					Ip = result2.Ip,
					DeviceRoot = result2.DeviceRoot,
					DeviceAlias = result2.DeviceAlias,
					LaunchIniMode = result2.LaunchIniMode.ToString(),
					LaunchIniPath = result2.LaunchIniPath,
					LaunchIniWritten = result2.LaunchIniWritten,
					LaunchIniBackedUp = result2.LaunchIniBackedUp,
					PluginsUploaded = result2.PluginsUploaded,
					Packages = result2.Packages.Select((InstalledHomebrewPackage package) => new { package.Id, package.DisplayName, package.InstallFolderName, package.ArchivePath, package.InstallPath, package.FileCount, package.TotalBytes })
				});
				return 0;
			}
			HomebrewConsoleInstallService.RenderInstallResult(result2);
			return 0;
		}
		catch (OperationCanceledException)
		{
			OperationFeedback.WriteWarning("Install cancelled", "No changes were made.");
			return 1;
		}
		catch (Exception ex2)
		{
			OperationFeedback.WriteFailure("Package install", ex2.Message);
			return 1;
		}
	}
}
