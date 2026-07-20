using System.ComponentModel;
using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ReportProfileAddCommand : Command<ReportProfileAddCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandArgument(0, "<NAME>")]
        [LocalizedDescription("Profile name.")]
        public string? Name { get; init; }

        [CommandOption("--format <FORMAT>")]
        [LocalizedDescription("Report format: md, html, csv, or json.")]
        public string? Format { get; init; }

        [CommandOption("--include-modules")]
        [LocalizedDescription("Include loaded module inventory.")]
        public bool IncludeModules { get; init; }

        [CommandOption("--include-threads")]
        [LocalizedDescription("Include thread inventory.")]
        public bool IncludeThreads { get; init; }

        [CommandOption("--include-memory")]
        [LocalizedDescription("Include XBDM memory region map.")]
        public bool IncludeMemory { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Keep console and network identifiers visible instead of redacting them.")]
        public bool IncludePrivate { get; init; }

        [CommandOption("--since <DATE>")]
        [LocalizedDescription("Include only modules at or after the given UTC date/time.")]
        public string? Since { get; init; }

        [CommandOption("--limit <N>")]
        [LocalizedDescription("Limit the number of modules after filtering.")]
        public int? Limit { get; init; }

        [CommandOption("--status <VALUE>")]
        [LocalizedDescription("Only include optional report sections when the console execution state matches.")]
        public string? Status { get; init; }

        [CommandOption("--diff")]
        [LocalizedDescription("Suppress timestamps and volatile duration fields for stable diff output.")]
        public bool Diff { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!ReportProfileStore.TryValidateName(settings.Name, out string name, out string nameError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(nameError)}[/]");
            return 1;
        }

        if (!ReportProfileStore.TryValidateTarget(settings.Ip, settings.Port, out string ip, out int port, out string targetError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(targetError)}[/]");
            return 1;
        }

        string? format = null;
        if (!string.IsNullOrWhiteSpace(settings.Format) &&
            !ReportProfileStore.TryValidateFormat(settings.Format, out format, out string formatError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(formatError)}[/]");
            return 1;
        }

        if (!ReportProfileStore.TryValidateSince(settings.Since, out string? since, out string sinceError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(sinceError)}[/]");
            return 1;
        }

        if (!ReportProfileStore.TryValidateLimit(settings.Limit, out int? limit, out string limitError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(limitError)}[/]");
            return 1;
        }

        if (!ReportProfileStore.TryValidateStatus(settings.Status, out string? status, out string statusError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(statusError)}[/]");
            return 1;
        }

        ReportProfileStoreData store = ReportProfileStore.Load();
        ReportProfileRecord? existing = ReportProfileStore.FindProfile(store, name);
        format ??= existing?.Format;
        bool updated = ReportProfileStore.UpsertProfile(store, new ReportProfileRecord {
            Name = name,
            Ip = string.IsNullOrWhiteSpace(settings.Ip) ? existing?.Ip : ip,
            TargetProfileName = string.IsNullOrWhiteSpace(settings.Profile) ? existing?.TargetProfileName : settings.Profile?.Trim(),
            Port = settings.Port ?? existing?.Port,
            TimeoutMs = settings.TimeoutMs ?? existing?.TimeoutMs,
            Format = string.IsNullOrWhiteSpace(settings.Format) ? existing?.Format : format,
            IncludeModules = settings.IncludeModules || existing?.IncludeModules == true,
            IncludeThreads = settings.IncludeThreads || existing?.IncludeThreads == true,
            IncludeMemory = settings.IncludeMemory || existing?.IncludeMemory == true,
            IncludePrivate = settings.IncludePrivate || existing?.IncludePrivate == true,
            Since = string.IsNullOrWhiteSpace(settings.Since) ? existing?.Since : since,
            Limit = settings.Limit ?? existing?.Limit,
            Status = string.IsNullOrWhiteSpace(settings.Status) ? existing?.Status : status,
            Diff = settings.Diff || existing?.Diff == true,
            CreatedUtc = existing?.CreatedUtc ?? DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
        ReportProfileStore.Save(store);

        ReportProfileRecord profile = ReportProfileStore.FindProfile(store, name) ?? new ReportProfileRecord { Name = name };
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Action = updated ? "updated" : "added",
                Profile = ReportProfileStore.ToJsonObject(profile, includePrivate: true)
            });
            return 0;
        }

        AnsiConsole.MarkupLine(
            updated
                ? $"[green]Updated[/] report profile [white]{Markup.Escape(profile.Name)}[/] -> [cyan]{Markup.Escape(ReportProfileStore.DescribeConnection(profile, true))}[/]"
                : $"[green]Added[/] report profile [white]{Markup.Escape(profile.Name)}[/] -> [cyan]{Markup.Escape(ReportProfileStore.DescribeConnection(profile, true))}[/]");
        if (profile.IncludeModules || profile.IncludeThreads || profile.IncludeMemory || profile.Diff || profile.IncludePrivate) {
            AnsiConsole.MarkupLine($"[grey]Flags:[/] {Markup.Escape(ReportProfileStore.DescribeFlags(profile))}");
        }
        if (!string.IsNullOrWhiteSpace(profile.Format) || !string.IsNullOrWhiteSpace(profile.Since) || profile.Limit.HasValue || !string.IsNullOrWhiteSpace(profile.Status)) {
            AnsiConsole.MarkupLine($"[grey]Filters:[/] {Markup.Escape(ReportProfileStore.DescribeFilters(profile))}");
        }
        return 0;
    }
}

public sealed class ReportProfileListCommand : Command<ReportProfileListCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show saved endpoints in full.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        ReportProfileStoreData store = ReportProfileStore.Load();
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Profiles = store.Profiles.Select(profile => ReportProfileStore.ToJsonObject(profile, settings.IncludePrivate))
            });
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Report Profiles[/]").RuleStyle("silver"));
        if (store.Profiles.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No report profiles saved.[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Name[/]"));
        table.AddColumn(new TableColumn("[cyan]Target[/]"));
        table.AddColumn(new TableColumn("[white]Format[/]"));
        table.AddColumn(new TableColumn("[white]Flags[/]"));
        table.AddColumn(new TableColumn("[white]Filters[/]"));
        foreach (ReportProfileRecord profile in store.Profiles) {
            table.AddRow(
                $"[white]{Markup.Escape(profile.Name)}[/]",
                $"[cyan]{Markup.Escape(ReportProfileStore.DescribeConnection(profile, settings.IncludePrivate))}[/]",
                string.IsNullOrWhiteSpace(profile.Format) ? "[grey70]md[/]" : $"[white]{Markup.Escape(profile.Format)}[/]",
                $"[grey]{Markup.Escape(ReportProfileStore.DescribeFlags(profile))}[/]",
                $"[grey]{Markup.Escape(ReportProfileStore.DescribeFilters(profile))}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class ReportProfileShowCommand : Command<ReportProfileShowCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<NAME>")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show saved endpoints in full.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!ReportProfileStore.TryValidateName(settings.Name, out string name, out string error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        ReportProfileStoreData store = ReportProfileStore.Load();
        ReportProfileRecord? profile = ReportProfileStore.FindProfile(store, name);
        if (profile == null) {
            AnsiConsole.MarkupLine($"[yellow]Report profile not found:[/] {Markup.Escape(name)}");
            return 1;
        }

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Profile = ReportProfileStore.ToJsonObject(profile, settings.IncludePrivate)
            });
            return 0;
        }

        AnsiConsole.Write(new Rule($"[bold deepskyblue1]Report Profile[/] [grey]{Markup.Escape(profile.Name)}[/]").RuleStyle("silver"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Field[/]"));
        table.AddColumn(new TableColumn("[green]Value[/]"));
        table.AddRow("[white]Name[/]", $"[green]{Markup.Escape(profile.Name)}[/]");
        table.AddRow("[white]Target[/]", $"[cyan]{Markup.Escape(ReportProfileStore.DescribeConnection(profile, settings.IncludePrivate))}[/]");
        table.AddRow("[white]Format[/]", string.IsNullOrWhiteSpace(profile.Format) ? "[grey70]md[/]" : $"[white]{Markup.Escape(profile.Format)}[/]");
        table.AddRow("[white]Flags[/]", $"[grey]{Markup.Escape(ReportProfileStore.DescribeFlags(profile))}[/]");
        table.AddRow("[white]Filters[/]", $"[grey]{Markup.Escape(ReportProfileStore.DescribeFilters(profile))}[/]");
        table.AddRow("[white]Updated[/]", profile.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class ReportProfileRemoveCommand : Command<ReportProfileRemoveCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<NAME>")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!ReportProfileStore.TryValidateName(settings.Name, out string name, out string error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        ReportProfileStoreData store = ReportProfileStore.Load();
        ReportProfileRecord? removedProfile = ReportProfileStore.FindProfile(store, name);
        if (removedProfile == null) {
            AnsiConsole.MarkupLine($"[yellow]Report profile not found:[/] {Markup.Escape(name)}");
            return 1;
        }

        if (!ReportProfileStore.RemoveProfile(store, name)) {
            AnsiConsole.MarkupLine($"[yellow]Report profile not found:[/] {Markup.Escape(name)}");
            return 1;
        }

        ReportProfileStore.Save(store);
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Removed = true,
                Name = removedProfile.Name
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Removed[/] report profile [white]{Markup.Escape(removedProfile.Name)}[/]");
        return 0;
    }
}
