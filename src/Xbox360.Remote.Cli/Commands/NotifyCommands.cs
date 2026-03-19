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
                error = "No notify icons configured. Use `rgh notify-icons add` to add presets.";
                return false;
            }

            if (!cfg.NotifyIcons.TryGetValue(iconName, out logo)) {
                error = $"Unknown icon '{iconName}'. Use `rgh notify-icons list`.";
                return false;
            }

            return true;
        }

        if (!string.IsNullOrWhiteSpace(logoValue)) {
            if (logoValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(logoValue.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out logo))
                    return true;
            }
            else if (int.TryParse(logoValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out logo)) {
                return true;
            }

            error = "Invalid --logo value.";
            return false;
        }

        logo = 0;
        return true;
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
            logo = 0;

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

        [CommandOption("--message <TEXT>")]
        [Description("Notification text.")]
        public string? Message { get; init; }

        [CommandOption("--logo <ID>")]
        [Description("Notification logo id (decimal or 0x hex).")]
        public string? Logo { get; init; }

        [CommandOption("--icon <NAME>")]
        [Description("Notification icon preset name.")]
        public string? Icon { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string? message = !string.IsNullOrWhiteSpace(settings.Message) ? settings.Message : settings.MessageArgument;
        if (string.IsNullOrWhiteSpace(message)) {
            AnsiConsole.MarkupLine("[red]--message is required.[/]");
            return 1;
        }

        if (!NotifyHelpers.TryResolveLogo(settings.Icon, settings.Logo, out int logo, out string? error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid notify options.")}[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            await jrpc.ShowNotificationAsync(logo, message, CancellationToken.None);
            AnsiConsole.MarkupLine("[green]Notification sent.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class NotifyIconsListCommand : Command<NotifyIconsListCommand.Settings> {
    public sealed class Settings : CommandSettings {
    }

    public override int Execute(CommandContext context, Settings settings) {
        CliConfig cfg = CliConfig.Load();
        if (cfg.NotifyIcons == null || cfg.NotifyIcons.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No notify icon presets configured.[/]");
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Notify Icons[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[green]Name[/]"));
        table.AddColumn(new TableColumn("[cyan]Logo ID[/]"));
        foreach ((string name, int id) in cfg.NotifyIcons.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)) {
            table.AddRow($"[green]{Markup.Escape(name)}[/]", $"[cyan]{id}[/]");
        }
        AnsiConsole.Write(table);
        return 0;
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
        AnsiConsole.MarkupLine($"[green]Added icon[/] {Markup.Escape(settings.Name)} = {logo}");
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
            AnsiConsole.MarkupLine($"[green]Removed icon[/] {Markup.Escape(settings.Name)}");
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]Icon not found:[/] {Markup.Escape(settings.Name)}");
        return 1;
    }
}
