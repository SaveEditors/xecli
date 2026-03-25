using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

public sealed class OriginalXboxCompatibilityListCommand : Command<OriginalXboxCompatibilityListCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Emit machine-readable output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Sets = OriginalXboxCompatibilityService.Catalog.Select(definition => new {
                    definition.Id,
                    definition.DisplayName,
                    definition.Description,
                    definition.Notes,
                    definition.PrimaryUrl,
                    definition.MirrorUrl
                }),
                PartitionFixer = new {
                    Id = "fixer",
                    DisplayName = "HDD Compatibility Partition Fixer",
                    Description = "Creates the HddX compatibility partition required on non-standard drives."
                }
            });
            return 0;
        }

        OriginalXboxCompatibilityService.RenderList();
        AnsiConsole.MarkupLine("[grey]Use[/] [springgreen3_1]rgh ogxbox install hacked --usb E:[/] [grey]to stage files locally, or omit[/] [springgreen3_1]--usb[/] [grey]to install directly to the console's HddX partition.[/]");
        return 0;
    }
}

public sealed class OriginalXboxCompatibilityInstallCommand : Command<OriginalXboxCompatibilityInstallCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
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

        public override ValidationResult Validate() {
            if (!OriginalXboxCompatibilityService.IsKnownSetId(SetId))
                return ValidationResult.Error("Unknown set. Expected hacked, hud, or retail.");
            return ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings) {
        try {
            if (!string.IsNullOrWhiteSpace(settings.UsbTarget)) {
                string targetRoot = HostDriveService.ResolveTargetRoot(settings.UsbTarget, settings.AutoConfirm);
                OriginalXboxCompatibilityInstallResult result = OriginalXboxCompatibilityService
                    .InstallToHostAsync(
                        settings.SetId,
                        targetRoot,
                        settings.CacheDirectory,
                        settings.IncludeFixer,
                        settings.ForceDownload,
                        settings.AutoConfirm,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                if (settings.Json) {
                    CliOutput.EmitJson(result);
                    return 0;
                }

                OriginalXboxCompatibilityService.RenderInstallResult(result);
                return 0;
            }

            OriginalXboxCompatibilityInstallResult consoleResult = OriginalXboxCompatibilityService
                .InstallToConsoleAsync(settings, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (settings.Json) {
                CliOutput.EmitJson(consoleResult);
                return 0;
            }

            OriginalXboxCompatibilityService.RenderInstallResult(consoleResult);
            return 0;
        }
        catch (OperationCanceledException) {
            OperationFeedback.WriteWarning("Original Xbox compatibility install cancelled", "No changes were made.");
            return 1;
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("Original Xbox compatibility install", ex.Message);
            return 1;
        }
    }
}

