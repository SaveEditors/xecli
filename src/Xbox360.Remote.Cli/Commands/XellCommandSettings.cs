using System;
using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli;

namespace Xbox360.Remote.Cli.Commands;

public class XellCommandSettings : ConnectionSettings
{
	[CommandOption("--force-xell")]
	[LocalizedDescription("Force the direct XeLL reboot path instead of the XellLaunch shortcut when available.")]
	public bool ForceXell { get; init; }

	[CommandOption("--yes")]
	[LocalizedDescription("Skip the interactive XeLL launch confirmation prompt.")]
	public bool Yes { get; init; }

	[CommandOption("--launcher <PATH>")]
	[LocalizedDescription("Remote XEX path to use as the XeLL launch helper, for example Hdd1:\\XellLaunch\\default.xex.")]
	public string? LauncherPath { get; init; }

	[CommandOption("--stage-launcher <FILE>")]
	[LocalizedDescription("Upload a local XeLL launch helper XEX before booting. Defaults to Hdd1:\\XellLaunch\\default.xex when --launcher is omitted.")]
	public string? StageLauncherPath { get; init; }

	[CommandOption("--stage-xell-bin <FILE>")]
	[LocalizedDescription("Upload a local XeLL binary as xell.bin beside the launch helper before booting.")]
	public string? StageXellBinPath { get; init; }

	[CommandOption("--quickboot")]
	[LocalizedDescription("Build, upload, and launch a QuickBoot XeLL launcher, and also install a dashboard shortcut when possible.")]
	public bool QuickBoot { get; init; }

	[CommandOption("--quickboot-target <MODE>")]
	[LocalizedDescription("QuickBoot target: launcher (default) or flash.")]
	public string? QuickBootTarget { get; init; }

	[CommandOption("--allow-usb")]
	[LocalizedDescription("Allow XeLL launch even when removable USB storage is attached. Disabled by default because some consoles hang at Fat mount uda0.")]
	public bool AllowUsb { get; init; }

	public override ValidationResult Validate()
	{
		if (ForceXell && QuickBoot)
		{
			return ValidationResult.Error("--force-xell cannot be combined with --quickboot.");
		}
		if (!string.IsNullOrWhiteSpace(QuickBootTarget) && !QuickBootTarget.Equals("launcher", StringComparison.OrdinalIgnoreCase) && !QuickBootTarget.Equals("flash", StringComparison.OrdinalIgnoreCase))
		{
			return ValidationResult.Error("Invalid --quickboot-target. Use launcher or flash.");
		}
		return ValidationResult.Success();
	}
}
