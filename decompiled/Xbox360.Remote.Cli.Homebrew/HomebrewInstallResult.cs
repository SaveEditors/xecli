using System.Collections.Generic;

namespace Xbox360.Remote.Cli.Homebrew;

internal sealed record HomebrewInstallResult(string TargetRoot, IReadOnlyList<InstalledHomebrewPackage> Packages, bool LaunchIniWritten, bool PluginsCopied);
