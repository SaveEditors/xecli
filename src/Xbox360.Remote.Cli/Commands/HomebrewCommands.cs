using System.ComponentModel;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

public sealed class HomebrewInstallCommand : Command<HomebrewInstallCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandArgument(0, "<PACKAGE>")]
        [LocalizedDescription("Package to install or stage: aurora, dashlaunch, xexmenu, fsd, xm360, timefixer, simple360, xelllaunch, or all.")]
        public string Package { get; init; } = string.Empty;

        [CommandOption("--usb <TARGET>")]
        [LocalizedDescription("Stage to a removable USB drive, drive letter, selection number, or host folder path.")]
        public string? UsbTarget { get; init; }

        [CommandOption("--device <DEVICE>")]
        [LocalizedDescription("Install directly to a detected console device such as Hdd1, Usb0, Usb1, or Usb2.")]
        public string? Device { get; init; }

        [CommandOption("--ini-mode <MODE>")]
        [LocalizedDescription("Console install launch.ini behavior: generated, merge, or skip.")]
        public string? IniMode { get; init; }

        [CommandOption("--ini <PATH>")]
        [LocalizedDescription("Console launch.ini path (default: /Hdd1/launch.ini).")]
        public string? IniPath { get; init; }

        [CommandOption("--cache <DIR>")]
        [LocalizedDescription("Package download cache directory.")]
        public string? CacheDirectory { get; init; }

        [CommandOption("--force-download")]
        [LocalizedDescription("Redownload archives even when they already exist in the cache.")]
        public bool ForceDownload { get; init; }

        [CommandOption("--auto-confirm")]
        [LocalizedDescription("Skip confirmation prompts.")]
        public bool AutoConfirm { get; init; }

        public override ValidationResult Validate() {
            if (!string.IsNullOrWhiteSpace(UsbTarget) && !string.IsNullOrWhiteSpace(Device))
                return ValidationResult.Error("Use either --usb for local staging or --device for console install, not both.");

            if (!string.IsNullOrWhiteSpace(IniMode) && !HomebrewConsoleInstallService.TryParseIniMode(IniMode, out _))
                return ValidationResult.Error("Invalid --ini-mode. Expected generated, create, new, merge, modify, skip, none, or download.");

            if (HomebrewPackageService.IsKnownPackageId(Package))
                return ValidationResult.Success();
            return ValidationResult.Error($"Unknown package '{Package}'. Expected aurora, dashlaunch, xexmenu, fsd, xm360, timefixer, simple360, xelllaunch, or all.");
        }
    }

    public override int Execute(CommandContext context, Settings settings) {
        try {
            if (!string.IsNullOrWhiteSpace(settings.UsbTarget)) {
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
                        Mode = "local",
                        result.TargetRoot,
                        result.LaunchIniWritten,
                        result.PluginsCopied,
                        Packages = result.Packages.Select(package => new {
                            package.Id,
                            package.DisplayName,
                            package.InstallFolderName,
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

            HomebrewConsoleInstallResult resultConsole = HomebrewConsoleInstallService
                .InstallAsync(settings, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Mode = "console",
                    resultConsole.Ip,
                    resultConsole.DeviceRoot,
                    resultConsole.DeviceAlias,
                    LaunchIniMode = resultConsole.LaunchIniMode.ToString(),
                    resultConsole.LaunchIniPath,
                    resultConsole.LaunchIniWritten,
                    resultConsole.LaunchIniBackedUp,
                    resultConsole.PluginsUploaded,
                    Packages = resultConsole.Packages.Select(package => new {
                        package.Id,
                        package.DisplayName,
                        package.InstallFolderName,
                        package.ArchivePath,
                        package.InstallPath,
                        package.FileCount,
                        package.TotalBytes
                    })
                });
                return 0;
            }

            HomebrewConsoleInstallService.RenderInstallResult(resultConsole);
            return 0;
        }
        catch (OperationCanceledException) {
            OperationFeedback.WriteWarning("Install cancelled", "No changes were made.");
            return 1;
        }
        catch (InvalidOperationException ex) {
            OperationFeedback.WriteFailure("Package install", ex.Message);
            return 1;
        }
        catch (Exception) {
            OperationFeedback.WriteFailure("Package install", "Install failed. Check console connectivity and try again.");
            return 1;
        }
    }
}

public sealed class HomebrewListCommand : Command<HomebrewListCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Emit machine-readable output.")]
        public bool Json { get; init; }

        [CommandOption("--csv")]
        [LocalizedDescription("Emit comma-separated values to stdout.")]
        public bool Csv { get; init; }
    }

    internal sealed record HomebrewCatalogRow(string Package, string Description, string Source);
    internal sealed record HomebrewCatalogJsonRow(string Id, string Package);

    public override int Execute(CommandContext context, Settings settings) {
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Packages = BuildJsonRows()
            });
            return 0;
        }

        if (settings.Csv) {
            Console.Write(RenderCsv(BuildCsvRows()));
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Package[/]"));
        table.AddColumn(new TableColumn("[bold white]Description[/]"));
        table.AddColumn(new TableColumn("[bold white]Source[/]"));
        foreach (var package in HomebrewPackageService.Catalog) {
            table.AddRow(
                $"[springgreen3_1]{Markup.Escape(package.Id)}[/]",
                $"[cyan]{Markup.Escape(package.Description)}[/]",
                $"[grey]{Markup.Escape(new Uri(package.PrimaryUrl).Host switch { "consolemods.org" => "ConsoleMods", "github.com" => "GitHub", _ => new Uri(package.PrimaryUrl).Host })}[/]");
        }
        table.AddRow("[springgreen3_1]all[/]", "[cyan]Installs every built-in homebrew package.[/]", "[grey]-[/]");
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Use[/] [springgreen3_1]rgh homebrew install <package> --usb E:[/] [grey]to stage locally, or omit[/] [springgreen3_1]--usb[/] [grey]to install directly to a detected console drive.[/]");
        return 0;
    }

    internal static IReadOnlyList<HomebrewCatalogRow> BuildCsvRows() {
        List<HomebrewCatalogRow> rows = new List<HomebrewCatalogRow>(HomebrewPackageService.Catalog.Count + 1);
        foreach (HomebrewPackageDefinition package in HomebrewPackageService.Catalog) {
            rows.Add(new HomebrewCatalogRow(
                package.Id,
                package.Description,
                HomebrewPackageService.GetSourceLabel(package.PrimaryUrl)));
        }

        rows.Add(new HomebrewCatalogRow("all", "Installs every built-in homebrew package.", "-"));
        return rows;
    }

    internal static IReadOnlyList<HomebrewCatalogJsonRow> BuildJsonRows() {
        return HomebrewPackageService.KnownPackageIds
            .Select(id => new HomebrewCatalogJsonRow(id, id))
            .ToArray();
    }

    internal static string RenderCsv(IReadOnlyList<HomebrewCatalogRow> rows) {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Package,Description,Source");
        foreach (HomebrewCatalogRow row in rows) {
            builder.Append(Csv(row.Package)).Append(',');
            builder.Append(Csv(row.Description)).Append(',');
            builder.AppendLine(Csv(row.Source));
        }

        return builder.ToString();
    }

    private static string Csv(string value) {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

