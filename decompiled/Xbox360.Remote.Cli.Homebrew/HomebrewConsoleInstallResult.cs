using System.Collections.Generic;

namespace Xbox360.Remote.Cli.Homebrew;

internal sealed record HomebrewConsoleInstallResult(string Ip, string DeviceRoot, string DeviceAlias, string LaunchIniPath, HomebrewLaunchIniMode LaunchIniMode, IReadOnlyList<InstalledHomebrewPackage> Packages, bool PluginsUploaded, bool LaunchIniWritten, bool LaunchIniBackedUp);
