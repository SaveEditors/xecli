using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

internal static class NotifyHelpers {
    public static bool TryResolveLogo(string? iconName, string? logoValue, out int logo, out string? error) {
        error = null;
        logo = 0;

        if (!string.IsNullOrWhiteSpace(iconName)) {
            CliConfig cfg = CliConfig.Load();
            if (cfg.NotifyIcons == null || cfg.NotifyIcons.Count == 0) {
                error = "No notify icon presets configured. Use `rgh notify-icons add` to add presets or `rgh notify-icons list` to browse built-ins.";
                return false;
            }

            if (!cfg.NotifyIcons.TryGetValue(iconName, out logo)) {
                error = $"Unknown icon preset '{iconName}'. Use `rgh notify-icons list`.";
                return false;
            }

            return true;
        }

        if (!string.IsNullOrWhiteSpace(logoValue)) {
            if (NotifyCatalog.TryResolve(logoValue, out NotifyLogoDefinition? definition) && definition != null) {
                logo = definition.Id;
                return true;
            }

            if (NotifyCatalog.TryParseInt(logoValue, out logo))
                return true;

            error = "Invalid logo value. Use a decimal ID, 0x hex ID, or a built-in name from `rgh notify-icons list`.";
            return false;
        }

        logo = 0;
        return true;
    }

    public static string DescribeLogo(int logo) {
        return NotifyCatalog.Describe(logo);
    }

    public static async Task TrySendOperationNotificationAsync(
        XbdmClient client,
        bool enabled,
        string? iconName,
        string? logoValue,
        string message,
        CancellationToken cancellationToken) {
        if (!enabled)
            return;

        if (!TryResolveLogo(iconName, logoValue, out int logo, out _))
            logo = 14;

        try {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            await jrpc.ShowNotificationAsync(logo, message, cancellationToken);
        }
        catch {
            // ignored
        }
    }
}

public sealed class NotifySendCommand : AsyncCommand<NotifySendCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandArgument(0, "[message]")]
        [Description("Notification text.")]
        public string? MessageArgument { get; init; }

        [CommandArgument(1, "[logo]")]
        [Description("Optional icon id or built-in icon name.")]
        public string? LogoArgument { get; init; }

        [CommandOption("--message <TEXT>")]
        [Description("Notification text.")]
        public string? Message { get; init; }

        [CommandOption("--logo <ID>")]
        [Description("Notification logo id or built-in icon name.")]
        public string? Logo { get; init; }

        [CommandOption("--icon <NAME>")]
        [Description("Notification icon preset name from config.")]
        public string? Icon { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string? message = !string.IsNullOrWhiteSpace(settings.Message) ? settings.Message : settings.MessageArgument;
        if (string.IsNullOrWhiteSpace(message)) {
            AnsiConsole.MarkupLine("[red]Notification text is required. Use `rgh notify \"text\" 14` or `--message`.[/]");
            return 1;
        }

        string? logoValue = !string.IsNullOrWhiteSpace(settings.Logo) ? settings.Logo : settings.LogoArgument;
        if (!NotifyHelpers.TryResolveLogo(settings.Icon, logoValue, out int logo, out string? error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid notify options.")}[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            await jrpc.ShowNotificationAsync(logo, message, CancellationToken.None);
            AnsiConsole.MarkupLine($"[green]Notification sent.[/] [grey]Icon:[/] [aqua]{Markup.Escape(NotifyHelpers.DescribeLogo(logo))}[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class NotifyIconsListCommand : Command<NotifyIconsListCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--builtins-only")]
        [Description("Show only the built-in XNotify icon catalog.")]
        public bool BuiltinsOnly { get; init; }

        [CommandOption("--presets-only")]
        [Description("Show only user-defined preset aliases from config.")]
        public bool PresetsOnly { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (settings.BuiltinsOnly && settings.PresetsOnly) {
            AnsiConsole.MarkupLine("[red]Choose either --builtins-only or --presets-only.[/]");
            return 1;
        }

        if (!settings.PresetsOnly) {
            AnsiConsole.Write(new Rule("[bold deepskyblue1]Built-In XNotify Icons[/]").RuleStyle("silver"));
            Table builtins = CliOutput.CreateTable();
            builtins.AddColumn(new TableColumn("[white]ID[/]"));
            builtins.AddColumn(new TableColumn("[green]Name[/]"));
            builtins.AddColumn(new TableColumn("[deepskyblue1]Label[/]"));
            builtins.AddColumn(new TableColumn("[mediumpurple3]Notes[/]"));
            foreach (NotifyLogoDefinition definition in NotifyCatalog.All.OrderBy(x => x.Id)) {
                builtins.AddRow(
                    $"[white]{definition.Id.ToString(CultureInfo.InvariantCulture)}[/]",
                    $"[green]{Markup.Escape(definition.Key)}[/]",
                    $"[deepskyblue1]{Markup.Escape(definition.Label)}[/]",
                    $"[mediumpurple3]{Markup.Escape(definition.Notes ?? "-")}[/]");
            }
            AnsiConsole.Write(builtins);
        }

        if (settings.BuiltinsOnly)
            return 0;

        CliConfig cfg = CliConfig.Load();
        AnsiConsole.Write(new Rule("[bold deepskyblue1]Preset Aliases[/]").RuleStyle("silver"));
        if (cfg.NotifyIcons == null || cfg.NotifyIcons.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No notify icon presets configured.[/]");
            return 0;
        }

        Table presets = CliOutput.CreateTable();
        presets.AddColumn(new TableColumn("[green]Preset[/]"));
        presets.AddColumn(new TableColumn("[cyan]Logo ID[/]"));
        presets.AddColumn(new TableColumn("[deepskyblue1]Resolved[/]"));
        foreach ((string name, int id) in cfg.NotifyIcons.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)) {
            presets.AddRow(
                $"[green]{Markup.Escape(name)}[/]",
                $"[cyan]{id.ToString(CultureInfo.InvariantCulture)}[/]",
                $"[deepskyblue1]{Markup.Escape(NotifyHelpers.DescribeLogo(id))}[/]");
        }
        AnsiConsole.Write(presets);
        return 0;
    }
}

public sealed class NotifyIconsShowCommand : Command<NotifyIconsShowCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<value>")]
        [Description("A built-in icon name, preset alias, decimal id, or 0x hex id.")]
        public string? Value { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Value)) {
            AnsiConsole.MarkupLine("[red]A logo value is required.[/]");
            return 1;
        }

        CliConfig cfg = CliConfig.Load();
        if (cfg.NotifyIcons != null && cfg.NotifyIcons.TryGetValue(settings.Value, out int presetId)) {
            RenderResolvedTable(settings.Value, presetId, "preset");
            return 0;
        }

        if (!NotifyHelpers.TryResolveLogo(null, settings.Value, out int logo, out string? error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Unknown notify icon.")}[/]");
            return 1;
        }

        RenderResolvedTable(settings.Value, logo, "built-in/raw");
        return 0;
    }

    private static void RenderResolvedTable(string input, int logo, string source) {
        NotifyCatalog.TryGet(logo, out NotifyLogoDefinition? definition);
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Field[/]"));
        table.AddColumn(new TableColumn("[deepskyblue1]Value[/]"));
        table.AddRow("[white]Input[/]", $"[deepskyblue1]{Markup.Escape(input)}[/]");
        table.AddRow("[white]Source[/]", $"[deepskyblue1]{Markup.Escape(source)}[/]");
        table.AddRow("[white]Logo ID[/]", $"[deepskyblue1]{logo.ToString(CultureInfo.InvariantCulture)}[/]");
        table.AddRow("[white]Name[/]", $"[deepskyblue1]{Markup.Escape(definition?.Key ?? "-")}[/]");
        table.AddRow("[white]Label[/]", $"[deepskyblue1]{Markup.Escape(definition?.Label ?? "Unknown / custom")}[/]");
        table.AddRow("[white]Notes[/]", $"[deepskyblue1]{Markup.Escape(definition?.Notes ?? "-")}[/]");
        AnsiConsole.Write(table);
    }
}

public sealed class NotifyIconsAddCommand : Command<NotifyIconsAddCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--logo <ID>")]
        public string? Logo { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Logo)) {
            AnsiConsole.MarkupLine("[red]--name and --logo are required.[/]");
            return 1;
        }

        if (!NotifyHelpers.TryResolveLogo(null, settings.Logo, out int logo, out string? error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid --logo.")}[/]");
            return 1;
        }

        CliConfig cfg = CliConfig.Load();
        cfg.NotifyIcons ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        cfg.NotifyIcons[settings.Name] = logo;
        cfg.Save();
        AnsiConsole.MarkupLine($"[green]Added icon preset[/] {Markup.Escape(settings.Name)} [grey]->[/] [aqua]{Markup.Escape(NotifyHelpers.DescribeLogo(logo))}[/]");
        return 0;
    }
}

public sealed class NotifyIconsRemoveCommand : Command<NotifyIconsRemoveCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Name)) {
            AnsiConsole.MarkupLine("[red]--name is required.[/]");
            return 1;
        }

        CliConfig cfg = CliConfig.Load();
        if (cfg.NotifyIcons != null && cfg.NotifyIcons.Remove(settings.Name)) {
            cfg.Save();
            AnsiConsole.MarkupLine($"[green]Removed icon preset[/] {Markup.Escape(settings.Name)}");
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]Icon preset not found:[/] {Markup.Escape(settings.Name)}");
        return 1;
    }
}
