using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using System.Globalization;

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
    private const int DiscoveryCeilingSeconds = 12;

    public override async Task<int> ExecuteAsync(CommandContext context, ConnectSettings settings) {
        CliConfig config = CliConfig.Load();
        string? target = settings.Target;

        if (string.IsNullOrWhiteSpace(target)) {
            IReadOnlyList<DiscoveredConsole> consoles = await DiscoverWithFeedbackAsync(settings);
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
            IReadOnlyList<DiscoveredConsole> consoles = await DiscoverWithFeedbackAsync(settings);
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

    private static async Task<IReadOnlyList<DiscoveredConsole>> DiscoverWithFeedbackAsync(DiscoverySettings settings) {
        string ports = string.IsNullOrWhiteSpace(settings.Ports) ? "730,731" : settings.Ports;
        int timeoutMs = settings.TimeoutMs ?? 400;
        AnsiConsole.MarkupLine($"[grey]Discovery started:[/] ports [deepskyblue1]{Markup.Escape(ports)}[/], per-host timeout [deepskyblue1]{timeoutMs.ToString(CultureInfo.InvariantCulture)} ms[/].");
        AnsiConsole.MarkupLine("[grey]Scanning local network...[/]");

        IReadOnlyList<DiscoveredConsole> consoles = Array.Empty<DiscoveredConsole>();
        try {
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(DiscoveryCeilingSeconds));
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Spectre.Console.Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .StartAsync("Discovering Xbox 360 consoles...", async _ => {
                    consoles = await DiscoveryHelpers.DiscoverAsync(settings, cts.Token);
                });
        }
        catch (OperationCanceledException) {
            AnsiConsole.MarkupLine($"[yellow]Discovery timed out after {DiscoveryCeilingSeconds.ToString(CultureInfo.InvariantCulture)}s.[/] You can enter the IP manually.");
            return Array.Empty<DiscoveredConsole>();
        }
        catch (Exception ex) {
            AnsiConsole.MarkupLine($"[yellow]Discovery failed:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[grey]You can still enter the IP manually.[/]");
            return Array.Empty<DiscoveredConsole>();
        }

        if (consoles.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No consoles discovered automatically.[/] You can enter an IP manually.");
        }
        else {
            AnsiConsole.MarkupLine($"[green]Discovery complete:[/] found [deepskyblue1]{consoles.Count.ToString(CultureInfo.InvariantCulture)}[/] console(s).");
        }

        return consoles;
    }
}
