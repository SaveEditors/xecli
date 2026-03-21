using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

public sealed class HomebrewInstallCommand : Command<HomebrewInstallCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<PACKAGE>")]
        [Description("Package to stage: aurora, dashlaunch, xexmenu, fsd, or all.")]
        public string Package { get; init; } = string.Empty;

        [CommandOption("--usb <TARGET>")]
        [Description("USB drive, drive letter, selection number, or folder path.")]
        public string? UsbTarget { get; init; }

        [CommandOption("--cache <DIR>")]
        [Description("Package download cache directory.")]
        public string? CacheDirectory { get; init; }

        [CommandOption("--force-download")]
        [Description("Redownload archives even when they already exist in the cache.")]
        public bool ForceDownload { get; init; }

        [CommandOption("--auto-confirm")]
        [Description("Skip confirmation prompts.")]
        public bool AutoConfirm { get; init; }

        [CommandOption("--json")]
        [Description("Emit machine-readable output.")]
        public bool Json { get; init; }

        public override ValidationResult Validate() {
            if (HomebrewPackageService.IsKnownPackageId(Package))
                return ValidationResult.Success();
            return ValidationResult.Error($"Unknown package '{Package}'. Expected aurora, dashlaunch, xexmenu, fsd, or all.");
        }
    }

    public override int Execute(CommandContext context, Settings settings) {
        try {
            string targetRoot = HostDriveService.ResolveTargetRoot(settings.UsbTarget, settings.AutoConfirm);
            HomebrewInstallResult result = HomebrewPackageService
                .InstallAsync(
                    settings.Package,
                    targetRoot,
                    settings.CacheDirectory,
                    settings.ForceDownload,
                    settings.AutoConfirm,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    result.TargetRoot,
                    result.LaunchIniWritten,
                    result.PluginsCopied,
                    Packages = result.Packages.Select(package => new {
                        package.Id,
                        package.DisplayName,
                        package.ArchivePath,
                        package.InstallPath,
                        package.FileCount,
                        package.TotalBytes
                    })
                });
                return 0;
            }

            HomebrewPackageService.RenderInstallResult(result);
            return 0;
        }
        catch (OperationCanceledException) {
            OperationFeedback.WriteWarning("Install cancelled", "No changes were made.");
            return 1;
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("Package install", ex.Message);
            return 1;
        }
    }
}

public sealed class HomebrewListCommand : Command<HomebrewListCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [Description("Emit machine-readable output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        IReadOnlyList<string> packageIds = HomebrewPackageService.KnownPackageIds;
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Packages = packageIds.Select(id => new {
                    Id = id,
                    Package = id
                })
            });
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Package[/]"));
        table.AddColumn(new TableColumn("[bold white]Description[/]"));
        table.AddRow("[springgreen3_1]aurora[/]", "[cyan]Aurora 0.7b.2 release package[/]");
        table.AddRow("[springgreen3_1]dashlaunch[/]", "[cyan]DashLaunch 3.21[/]");
        table.AddRow("[springgreen3_1]xexmenu[/]", "[cyan]XeXMenu 1.2[/]");
        table.AddRow("[springgreen3_1]fsd[/]", "[cyan]Freestyle Dash 3[/]");
        table.AddRow("[springgreen3_1]all[/]", "[cyan]Aurora, DashLaunch, XeXMenu, and Freestyle Dash[/]");
        AnsiConsole.Write(table);
        return 0;
    }
}
