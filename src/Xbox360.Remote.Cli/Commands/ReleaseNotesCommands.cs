using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;
using Table = Spectre.Console.Table;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ReleaseNotesCommand : Command<ReleaseNotesCommand.Settings> {
    private const string CatalogSource = "built-in release catalog";

    public sealed class Settings : CommandSettings {
        [CommandOption("--latest")]
        [LocalizedDescription("Show the latest built-in release notes.")]
        public bool Latest { get; init; }

        [CommandOption("--version <VERSION>")]
        [LocalizedDescription("Show a specific built-in release version, such as v2.0.0.")]
        public string? Version { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (settings.Latest && !string.IsNullOrWhiteSpace(settings.Version))
            return WriteError(settings.Json, "Release notes", "Use either --latest or --version <VERSION>, not both.", "RELEASE_NOTES_OPTION_CONFLICT");

        if (!ReleaseNotesCatalog.TryResolve(settings.Latest, settings.Version, out ReleaseNotesEntry entry))
            return WriteError(settings.Json, "Release notes", $"Unsupported release version '{Markup.Escape(settings.Version ?? string.Empty)}'. Available versions: v2.0.0 and archived v1.1.0.", "RELEASE_NOTES_VERSION_UNKNOWN");

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Version = entry.Version,
                Latest = entry.IsLatest,
                Archived = entry.IsArchived,
                Title = entry.Title,
                Summary = entry.Summary,
                Source = CatalogSource,
                Highlights = entry.Highlights
            });
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Release Notes[/]").RuleStyle("silver"));
        Panel summary = new Panel(
                $"[bold green]{Markup.Escape(entry.Version)}[/]\n" +
                $"{Markup.Escape(entry.Title)}\n\n" +
                $"{Markup.Escape(entry.Summary)}")
            .Header($"[bold deepskyblue1]{Markup.Escape(entry.Version)}[/]")
            .BorderColor(Color.Grey);
        AnsiConsole.Write(summary);

        Table metadata = CliOutput.CreateTable();
        metadata.AddColumn(new TableColumn("[bold white]Field[/]"));
        metadata.AddColumn(new TableColumn("[bold white]Value[/]"));
        metadata.AddRow("[white]Source[/]", $"[silver]{Markup.Escape(CatalogSource)}[/]");
        metadata.AddRow("[white]Latest[/]", entry.IsLatest ? "[green]yes[/]" : "[grey]no[/]");
        metadata.AddRow("[white]Archived[/]", entry.IsArchived ? "[gold1]yes[/]" : "[green]no[/]");
        AnsiConsole.Write(metadata);

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Highlights[/]").RuleStyle("silver"));
        foreach (string highlight in entry.Highlights)
            AnsiConsole.MarkupLine($"[cyan]-[/] {Markup.Escape(highlight)}");

        return 0;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                message,
                code,
                new[] {
                    "Use --latest for the current release, or --version v2.0.0 for the current built-in entry. The archived v1.1.0 entry remains available for history."
                }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        return 1;
    }
}

internal sealed record ReleaseNotesEntry(
    string Version,
    string Title,
    string Summary,
    bool IsLatest,
    bool IsArchived,
    IReadOnlyList<string> Highlights);

internal static class ReleaseNotesCatalog {
    private static readonly IReadOnlyDictionary<string, ReleaseNotesEntry> Entries =
        new Dictionary<string, ReleaseNotesEntry>(StringComparer.OrdinalIgnoreCase) {
            ["v2.0.0"] = new ReleaseNotesEntry(
                "v2.0.0",
                "v2.0.0 Stable Release",
                "v2.0.0 combines stricter command outcomes, expanded debugging and automation, a production XeTerminal workspace, safer file transfers, XeLL and local-content workflows, configurable preferences, and self-contained Windows packaging.",
                IsLatest: true,
                IsArchived: false,
                new[] {
                    "Hardened XBDM response handling so rejected, empty, truncated, and malformed replies are surfaced as failures instead of successful results.",
                    "Added explicit deadlines and size limits for XBDM connection, greeting, read, and multi-response paths.",
                    "Expanded live debugging with module, RVA, and Ghidra address resolution, memory sessions and bookmarks, map and dump comparisons, and trainer workflows.",
                    "Added reports and saved report profiles, script transcripts and offline transcript review, command-log export, shell completion, and redacted diagnostics and support bundles.",
                    "Expanded XeTerminal with coordinated connection state, console details, inventory, storage, traffic, local and remote file browsing, quick actions, history search, themes, and settings.",
                    "Added safer FTP conflict review, sync and resume workflows, remote hashing, local and remote comparison, and optional transfer checksum verification.",
                    "Changed FTP uploads and downloads to use staged destinations, size checks, and cleanup so incomplete transfers do not replace final files.",
                    "Added stricter XBDM and FTP path validation and atomic replacement for configuration and other saved JSON state.",
                    "Added network diagnostics, recent-target history, saved target profiles, and clearer connection recovery guidance.",
                    "Reworked XeLL launch and service inspection, expanded keyvault export, and moved the expanded NAND backup workflow to rgh xell nand dump.",
                    "Added SMC and JRPC2 hardware reporting that rejects incomplete, sentinel, and implausible responses.",
                    "Added optional operator-supplied Title Database support; CON and profile mutations that require signing use the operator's decrypted keyvault.",
                    "Expanded Ghidra and IDA workflows with symbol export, decompilation helpers, and portable XEX analysis bundles.",
                    "Moved NAND backup to rgh xell nand dump and made rgh xtaf the canonical built-in FATX command while retaining fatman and fatx aliases.",
                    "Added owned-file manifests and recoverable installer transactions while preserving unrelated files and user-owned state.",
                    "Published self-contained win-x64 installer and portable packages containing both rgh.exe and XeTerminal.exe, with package-local portable state unless XECLI_HOME selects a custom state directory."
                }),
            ["v1.1.0"] = new ReleaseNotesEntry(
                "v1.1.0",
                "Archived v1.1.0 XeTerminal Beta and Release Polish",
                "This archived release is kept for reference only.",
                IsLatest: false,
                IsArchived: true,
                new[] {
                    "Promoted XeTerminal.exe as the beta custom interface shipped alongside rgh, with the live shell, session, status, traffic, storage, inventory, and FTP/file surfaces integrated into the release build.",
                    "Added native Discord Rich Presence updates for connected and disconnected console state, using the installed Discord desktop client instead of a helper process.",
                    "Fixed screenshot output so normal .png saves are usable by default, the corrupted right-edge strip is automatically trimmed on the affected frame-buffer path, and XeTerminal names screenshots from the active title.",
                    "Improved XeTerminal UI fit, session rendering, selection behavior, language switching, console/status presentation, storage rendering, and screenshot capture behavior across the current beta surface.",
                    "Shipped XeTerminal directly in the installer with Start menu integration, optional desktop shortcut creation, and direct launcher support for installed and portable builds."
                })
        };

    public static bool TryResolve(bool latest, string? version, out ReleaseNotesEntry entry) {
        if (latest || string.IsNullOrWhiteSpace(version)) {
            entry = Entries["v2.0.0"];
            return true;
        }

        string normalized = NormalizeVersion(version);
        return Entries.TryGetValue(normalized, out entry!);
    }

    private static string NormalizeVersion(string version) {
        string value = version.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            value = value[1..];

        return value switch {
            "2.0" or "2.0.0" => "v2.0.0",
            "1.1.0" or "1.1" => "v1.1.0",
            _ => $"v{value}"
        };
    }
}
