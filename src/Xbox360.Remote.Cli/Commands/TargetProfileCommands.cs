using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class TargetProfileAddCommand : Command<TargetProfileAddCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<NAME>")]
        [LocalizedDescription("Profile name.")]
        public string? Name { get; init; }

        [CommandOption("--ip <IP>")]
        [LocalizedDescription("Target console host or IP.")]
        public string? Ip { get; init; }

        [CommandOption("--port <PORT>")]
        [LocalizedDescription("Target port (default: 730).")]
        public int? Port { get; init; }

        [CommandOption("--ftp-port <PORT>")]
        [LocalizedDescription("Optional FTP port for this profile.")]
        public int? FtpPort { get; init; }

        [CommandOption("--ftp-user <USER>")]
        [LocalizedDescription("Optional FTP username for this profile.")]
        public string? FtpUser { get; init; }

        [CommandOption("--ftp-pass <PASS>")]
        [LocalizedDescription("Optional FTP password for this profile.")]
        public string? FtpPassword { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show the target endpoint in full, including the port.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TargetProfileStore.TryValidateName(settings.Name, out string name, out string nameError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(nameError)}[/]");
            return 1;
        }

        if (!TargetProfileStore.TryValidateTarget(settings.Ip, settings.Port, out string ip, out int port, out string targetError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(targetError)}[/]");
            return 1;
        }

        if (!TargetProfileStore.TryValidateFtpPort(settings.FtpPort, out int? ftpPort, out string ftpPortError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ftpPortError)}[/]");
            return 1;
        }

        TargetProfileStoreData store = TargetProfileStore.Load();
        TargetProfileRecord? existing = TargetProfileStore.FindProfile(store, name);
        bool updated = TargetProfileStore.UpsertProfile(store, new TargetProfileRecord {
            Name = name,
            Ip = ip,
            Port = port,
            FtpPort = ftpPort ?? existing?.FtpPort,
            FtpUser = string.IsNullOrWhiteSpace(settings.FtpUser) ? existing?.FtpUser : settings.FtpUser,
            FtpPassword = string.IsNullOrWhiteSpace(settings.FtpPassword) ? existing?.FtpPassword : settings.FtpPassword,
            CreatedUtc = existing?.CreatedUtc ?? DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
        TargetProfileStore.Save(store);

        TargetProfileRecord profile = TargetProfileStore.FindProfile(store, name) ?? new TargetProfileRecord { Name = name, Ip = ip, Port = port };
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Action = updated ? "updated" : "added",
                Profile = TargetProfileStore.ToJsonObject(profile, settings.IncludePrivate, string.Equals(store.CurrentProfileName, profile.Name, StringComparison.OrdinalIgnoreCase))
            });
            return 0;
        }

        AnsiConsole.MarkupLine(
            updated
                ? $"[green]Updated[/] profile [white]{Markup.Escape(profile.Name)}[/] -> [cyan]{Markup.Escape(TargetProfileStore.FormatEndpoint(profile, settings.IncludePrivate))}[/]"
                : $"[green]Added[/] profile [white]{Markup.Escape(profile.Name)}[/] -> [cyan]{Markup.Escape(TargetProfileStore.FormatEndpoint(profile, settings.IncludePrivate))}[/]");
        return 0;
    }
}

public sealed class TargetProfileListCommand : Command<TargetProfileListCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--csv")]
        [LocalizedDescription("Output CSV to stdout.")]
        public bool Csv { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show the target endpoint in full, including the port.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        TargetProfileStoreData store = TargetProfileStore.Load();
        TargetProfileCommandHelpers.TryLoadConfig(out CliConfig config);
        TargetProfileRecord? current = TargetProfileStore.ResolveCurrentProfile(store, config);
        if (settings.Json) {
            CliOutput.EmitJson(new {
                CurrentProfileName = store.CurrentProfileName,
                Profiles = store.Profiles.Select(profile =>
                    TargetProfileStore.ToJsonObject(profile, settings.IncludePrivate,
                        current != null && string.Equals(current.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
            });
            return 0;
        }

        if (settings.Csv) {
            Console.Write(RenderCsv(store.Profiles, current?.Name, settings.IncludePrivate));
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Target Profiles[/]").RuleStyle("silver"));
        if (store.Profiles.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No target profiles saved.[/]");
            return 0;
        }

        if (current != null && string.IsNullOrWhiteSpace(store.CurrentProfileName)) {
            if (string.IsNullOrWhiteSpace(current.Name))
                AnsiConsole.MarkupLine("[grey]Current target comes from the saved default target, not a named profile.[/]");
            else
                AnsiConsole.MarkupLine($"[grey]Current target follows the saved default target and matches [white]{Markup.Escape(current.Name)}[/].[/]");
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Name[/]"));
        table.AddColumn(new TableColumn("[cyan]Target[/]"));
        table.AddColumn(new TableColumn("[green]Selected[/]"));
        foreach (TargetProfileRecord profile in store.Profiles) {
            bool isCurrent = current != null && string.Equals(current.Name, profile.Name, StringComparison.OrdinalIgnoreCase);
            table.AddRow(
                $"[white]{Markup.Escape(profile.Name)}[/]",
                $"[cyan]{Markup.Escape(TargetProfileStore.FormatEndpoint(profile, settings.IncludePrivate))}[/]",
                isCurrent ? "[green]yes[/]" : "[grey70]no[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }

    internal static string RenderCsv(IReadOnlyList<TargetProfileRecord> profiles, string? currentProfileName, bool includePrivate) {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Name,Target,Selected");
        foreach (TargetProfileRecord profile in profiles) {
            bool isCurrent = !string.IsNullOrWhiteSpace(currentProfileName) &&
                             string.Equals(currentProfileName, profile.Name, StringComparison.OrdinalIgnoreCase);
            builder.Append(Csv(profile.Name)).Append(',');
            builder.Append(Csv(TargetProfileStore.FormatEndpoint(profile, includePrivate))).Append(',');
            builder.AppendLine(Csv(isCurrent ? "yes" : "no"));
        }

        return builder.ToString();
    }

    private static string Csv(string value) {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public sealed class TargetProfileValidateCommand : Command<TargetProfileValidateCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show the target endpoint in full, including the port.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        TargetProfileLoadResult load = TargetProfileStore.LoadRaw();
        TargetProfileStoreData store = load.Data;
        TargetProfileValidationReport report = TargetProfileStore.ValidateProfiles(store);
        if (load.Issue != null)
            report.Issues.Insert(0, load.Issue);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                IsValid = report.IsValid,
                CurrentProfileName = report.CurrentProfileName,
                Profiles = report.Profiles.Select(profile =>
                    TargetProfileStore.ToJsonObject(profile, settings.IncludePrivate,
                        current: !string.IsNullOrWhiteSpace(report.CurrentProfileName) &&
                                 string.Equals(report.CurrentProfileName, profile.Name, StringComparison.OrdinalIgnoreCase))),
                Issues = report.Issues.Select(issue => new {
                    issue.Name,
                    issue.Message
                })
            });
            return report.IsValid ? 0 : 1;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Target Profile Validation[/]").RuleStyle("silver"));
        if (report.Profiles.Count == 0) {
            if (load.Issue != null)
                AnsiConsole.MarkupLine("[yellow]Stored target-profiles.json is malformed.[/]");
            else
                AnsiConsole.MarkupLine("[yellow]No target profiles saved.[/]");
        }
        else {
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[white]Name[/]"));
            table.AddColumn(new TableColumn("[cyan]Target[/]"));
            table.AddColumn(new TableColumn("[green]Selected[/]"));
            foreach (TargetProfileRecord profile in report.Profiles) {
                bool isCurrent = !string.IsNullOrWhiteSpace(report.CurrentProfileName) &&
                                 string.Equals(report.CurrentProfileName, profile.Name, StringComparison.OrdinalIgnoreCase);
                table.AddRow(
                    $"[white]{Markup.Escape(profile.Name)}[/]",
                    $"[cyan]{Markup.Escape(TargetProfileStore.FormatEndpoint(profile, settings.IncludePrivate))}[/]",
                    isCurrent ? "[green]yes[/]" : "[grey70]no[/]");
            }

            AnsiConsole.Write(table);
        }

        if (report.Issues.Count == 0) {
            AnsiConsole.MarkupLine("[green]No validation issues found.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]{report.Issues.Count} validation issue(s) found.[/]");
        foreach (TargetProfileValidationIssue issue in report.Issues) {
            string label = string.IsNullOrWhiteSpace(issue.Name) ? "entry" : issue.Name;
            AnsiConsole.MarkupLine($"[yellow]-[/] [white]{Markup.Escape(label)}[/]: {Markup.Escape(issue.Message)}");
        }

        return 1;
    }
}

public sealed class TargetProfileShowCommand : Command<TargetProfileShowCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<NAME>")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show the target endpoint in full, including the port.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TargetProfileStore.TryValidateName(settings.Name, out string name, out string error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        TargetProfileStoreData store = TargetProfileStore.Load();
        TargetProfileCommandHelpers.TryLoadConfig(out CliConfig config);
        TargetProfileRecord? profile = TargetProfileStore.FindProfile(store, name);
        if (profile == null) {
            AnsiConsole.MarkupLine($"[yellow]Profile not found:[/] {Markup.Escape(name)}");
            return 1;
        }

        bool current = TargetProfileStore.ResolveCurrentProfile(store, config) is TargetProfileRecord currentProfile &&
                       string.Equals(currentProfile.Name, profile.Name, StringComparison.OrdinalIgnoreCase);
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Profile = TargetProfileStore.ToJsonObject(profile, settings.IncludePrivate, current)
            });
            return 0;
        }

        AnsiConsole.Write(new Rule($"[bold deepskyblue1]Target Profile[/] [grey]{Markup.Escape(profile.Name)}[/]").RuleStyle("silver"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Field[/]"));
        table.AddColumn(new TableColumn("[green]Value[/]"));
        table.AddRow("[white]Name[/]", $"[green]{Markup.Escape(profile.Name)}[/]");
        table.AddRow("[white]Target[/]", $"[cyan]{Markup.Escape(TargetProfileStore.FormatEndpoint(profile, settings.IncludePrivate))}[/]");
        table.AddRow("[white]Selected[/]", current ? "[green]yes[/]" : "[grey70]no[/]");
        if (current) {
            table.AddRow(
                "[white]Source[/]",
                string.IsNullOrWhiteSpace(store.CurrentProfileName)
                    ? "[yellow]saved default target[/]"
                    : "[green]saved current profile[/]");
        }
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class TargetProfileRemoveCommand : Command<TargetProfileRemoveCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<NAME>")]
        public string? Name { get; init; }

        [CommandOption("--force")]
        public bool Force { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TargetProfileStore.TryValidateName(settings.Name, out string name, out string error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        TargetProfileStoreData store = TargetProfileStore.Load();
        if (!TargetProfileCommandHelpers.TryLoadConfig(out CliConfig config)) {
            AnsiConsole.MarkupLine("[red]Config file is present but could not be parsed.[/]");
            return 1;
        }

        if (!TargetProfileStore.TryRemoveProfile(store, name, config, settings.Force, out TargetProfileRecord? removedProfile, out bool clearedDefaultTarget, out string removeError)) {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(removeError)}[/]");
            return 1;
        }

        TargetProfileStore.Save(store);
        config.Save();
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Removed = true,
                Force = settings.Force,
                Name = removedProfile?.Name ?? name,
                CurrentProfileName = store.CurrentProfileName,
                ClearedDefaultTarget = clearedDefaultTarget
            });
            return 0;
        }

        string message = $"[green]Removed[/] profile [white]{Markup.Escape(removedProfile?.Name ?? name)}[/]";
        if (clearedDefaultTarget) {
            message += " and cleared the saved default target.";
        }
        AnsiConsole.MarkupLine(message);
        return 0;
    }
}

public sealed class TargetProfileUseCommand : Command<TargetProfileUseCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<NAME>")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show the target endpoint in full, including the port.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TargetProfileStore.TryValidateName(settings.Name, out string name, out string error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        TargetProfileStoreData store = TargetProfileStore.Load();
        if (!TargetProfileStore.TrySetCurrentProfile(store, name, out TargetProfileRecord? profile) || profile == null) {
            AnsiConsole.MarkupLine($"[yellow]Profile not found:[/] {Markup.Escape(name)}");
            return 1;
        }

        if (!TargetProfileCommandHelpers.TryLoadConfig(out CliConfig config)) {
            AnsiConsole.MarkupLine("[red]Config file is present but could not be parsed.[/]");
            return 1;
        }

        config.DefaultIp = profile.Ip;
        config.DefaultPort = profile.Port;
        config.Save();
        TargetProfileStore.Save(store);
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Applied = true,
                Profile = TargetProfileStore.ToJsonObject(profile, settings.IncludePrivate, current: true),
                Target = new {
                    Ip = settings.IncludePrivate ? config.DefaultIp : "redacted",
                    Port = config.DefaultPort
                }
            });
            return 0;
        }

        AnsiConsole.MarkupLine(
            $"[green]Current profile set to[/] [white]{Markup.Escape(profile.Name)}[/] -> [cyan]{Markup.Escape(TargetProfileStore.FormatEndpoint(profile, settings.IncludePrivate))}[/] [grey](saved default target updated)[/]");
        return 0;
    }
}

public sealed class TargetProfileCurrentCommand : Command<TargetProfileCurrentCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show the target endpoint in full, including the port.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        TargetProfileStoreData store = TargetProfileStore.Load();
        TargetProfileCommandHelpers.TryLoadConfig(out CliConfig config);
        TargetProfileRecord? profile = TargetProfileStore.ResolveCurrentProfile(store, config);
        if (profile == null) {
            AnsiConsole.MarkupLine("[yellow]No current profile or default target is saved.[/]");
            return 1;
        }

        bool isStoredProfile = !string.IsNullOrWhiteSpace(profile.Name);
        bool hasSelectedProfile = !string.IsNullOrWhiteSpace(store.CurrentProfileName);
        if (settings.Json) {
            CliOutput.EmitJson(new {
                CurrentProfileName = store.CurrentProfileName,
                Target = new {
                    Ip = settings.IncludePrivate ? profile.Ip : "redacted",
                    profile.Port
                },
                Profile = isStoredProfile
                    ? TargetProfileStore.ToJsonObject(profile, settings.IncludePrivate, current: true)
                    : null
            });
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Current Target Profile[/]").RuleStyle("silver"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Field[/]"));
        table.AddColumn(new TableColumn("[green]Value[/]"));
        table.AddRow("[white]Source[/]", hasSelectedProfile ? "[green]saved current profile[/]" : isStoredProfile ? "[yellow]saved default target[/]" : "[grey70]manual default target[/]");
        table.AddRow("[white]Profile[/]", isStoredProfile ? $"[white]{Markup.Escape(profile.Name)}[/]" : "[grey70]manual[/]");
        table.AddRow("[white]Target[/]", $"[cyan]{Markup.Escape(TargetProfileStore.FormatEndpoint(profile, settings.IncludePrivate))}[/]");
        if (hasSelectedProfile)
            table.AddRow("[white]Selected[/]", $"[green]{Markup.Escape(store.CurrentProfileName!)}[/]");
        AnsiConsole.Write(table);
        return 0;
    }
}

internal static class TargetProfileCommandHelpers {
    public static bool TryLoadConfig(out CliConfig config) {
        return CliConfig.TryLoad(out config);
    }
}
