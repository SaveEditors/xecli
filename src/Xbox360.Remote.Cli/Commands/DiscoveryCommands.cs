using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ScanCommand : AsyncCommand<DiscoverySettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, DiscoverySettings settings) {
        IReadOnlyList<DiscoveredConsole> consoles;
        try {
            consoles = await DiscoveryHelpers.DiscoverAsync(settings, CancellationToken.None);
        }
        catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]Discovery failed:[/] {Markup.Escape(ex.Message)}");
            consoles = Array.Empty<DiscoveredConsole>();
        }
        CliOutput.RenderDiscovery(consoles, settings.Json);
        return 0;
    }
}

public sealed class StartCommand : AsyncCommand<DiscoverySettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, DiscoverySettings settings) {
        IReadOnlyList<DiscoveredConsole> consoles;
        try {
            consoles = await DiscoveryHelpers.DiscoverAsync(settings, CancellationToken.None);
        }
        catch {
            consoles = Array.Empty<DiscoveredConsole>();
        }
        string? selectedIp = CliOutput.PromptForConsole(consoles);
        if (string.IsNullOrWhiteSpace(selectedIp)) {
            AnsiConsole.MarkupLine("[red]No console selected.[/]");
            return 1;
        }

        CliConfig config = CliConfig.Load();
        config.DefaultIp = selectedIp;
        config.DefaultPort ??= 730;
        config.Save();

        AnsiConsole.MarkupLine($"[green]Default console set to[/] {selectedIp}");
        return 0;
    }
}

public sealed class ConnectCommand : AsyncCommand<ConnectSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectSettings settings) {
        CliConfig config = CliConfig.Load();
        string? target = settings.Target;

        if (string.IsNullOrWhiteSpace(target)) {
            IReadOnlyList<DiscoveredConsole> consoles;
            try {
                consoles = await DiscoveryHelpers.DiscoverAsync(settings, CancellationToken.None);
            }
            catch {
                consoles = Array.Empty<DiscoveredConsole>();
            }
            string? selectedIp = CliOutput.PromptForConsole(consoles);
            if (string.IsNullOrWhiteSpace(selectedIp)) {
                AnsiConsole.MarkupLine("[red]No console selected.[/]");
                return 1;
            }

            config.DefaultIp = selectedIp;
            config.DefaultPort ??= 730;
            config.Save();
            AnsiConsole.MarkupLine($"[green]Default console set to[/] {selectedIp}");
            return 0;
        }

        if (int.TryParse(target, out int index)) {
            IReadOnlyList<DiscoveredConsole> consoles;
            try {
                consoles = await DiscoveryHelpers.DiscoverAsync(settings, CancellationToken.None);
            }
            catch {
                consoles = Array.Empty<DiscoveredConsole>();
            }
            if (index <= 0 || index > consoles.Count) {
                AnsiConsole.MarkupLine("[red]Invalid index.[/]");
                return 1;
            }

            string ip = consoles[index - 1].Ip.ToString();
            config.DefaultIp = ip;
            config.DefaultPort ??= 730;
            config.Save();
            AnsiConsole.MarkupLine($"[green]Default console set to[/] {ip}");
            return 0;
        }

        config.DefaultIp = target;
        config.DefaultPort ??= 730;
        config.Save();
        AnsiConsole.MarkupLine($"[green]Default console set to[/] {target}");
        return 0;
    }
}
