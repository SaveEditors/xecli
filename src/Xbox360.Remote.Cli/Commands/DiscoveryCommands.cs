using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using System.Globalization;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ScanCommand : AsyncCommand<DiscoverySettings> {
    internal static Func<DiscoverySettings, CancellationToken, Task<IReadOnlyList<DiscoveredConsole>>> DiscoverAsync { get; set; } = DiscoveryHelpers.DiscoverAsync;

    public override async Task<int> ExecuteAsync(CommandContext context, DiscoverySettings settings) {
        IReadOnlyList<DiscoveredConsole> consoles;
        try {
            consoles = await DiscoverAsync(settings, CancellationToken.None);
        }
        catch (ArgumentException ex) {
            CliValidationOutput.Write(
                settings.Json,
                "Discovery validation failed",
                ex.Message,
                "DISCOVERY_VALIDATION_FAILED",
                "Correct the discovery options and retry.");
            return 1;
        }
        catch (Exception) {
            if (settings.Json) {
                CliOutput.EmitJsonError(new CliErrorEnvelope(
                    "Discovery failed",
                    "Console discovery failed unexpectedly.",
                    "DISCOVERY_FAILED",
                    new[] { "Check the network interface and retry the scan." }));
                return 1;
            }

            AnsiConsole.MarkupLine("[yellow]Discovery failed. Try again.[/]");
            return 1;
        }
        CliOutput.RenderDiscovery(consoles, settings.Json);
        return 0;
    }
}

public sealed class StartCommand : AsyncCommand<DiscoverySettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, DiscoverySettings settings) {
        if (settings.Json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Interactive start JSON mode is unavailable",
                "The start command prompts for a console and cannot emit a bounded JSON result.",
                "START_JSON_UNSUPPORTED",
                new[] { "Use `rgh scan --json`, then run `rgh connect <target> --json`." }));
            return 1;
        }

        if (!CliConfig.TryLoad(out CliConfig config)) {
            AnsiConsole.MarkupLine("[red]Config file is present but could not be parsed. Fix or remove config.json, then run rgh start again.[/]");
            return 1;
        }

        IReadOnlyList<DiscoveredConsole> consoles;
        try {
            consoles = await DiscoveryHelpers.DiscoverAsync(settings, CancellationToken.None);
        }
        catch (ArgumentException ex) {
            AnsiConsole.MarkupLine($"[red]Discovery failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
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
}

public sealed class ConnectCommand : AsyncCommand<ConnectSettings> {
    private const int DiscoveryCeilingSeconds = 12;
    internal static Func<DiscoverySettings, CancellationToken, Task<IReadOnlyList<DiscoveredConsole>>> DiscoverAsync { get; set; } = DiscoveryHelpers.DiscoverAsync;

    public override async Task<int> ExecuteAsync(CommandContext context, ConnectSettings settings) {
        CliConfig config = null!;
        string? target = settings.Target;

        if (string.IsNullOrWhiteSpace(target)) {
            if (!TryLoadConfig(settings.Json, out config))
                return 1;

            IReadOnlyList<DiscoveredConsole> consoles;
            try {
                consoles = await DiscoverWithFeedbackAsync(settings);
            }
            catch (ArgumentException ex) {
                return WriteConnectError(
                    settings.Json,
                    "Connect discovery failed",
                    ex.Message,
                    "CONNECT_DISCOVERY_FAILED",
                    "Use a valid --ports list, or pass a target IP directly.");
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Success = false,
                    Status = "selection-required",
                    Target = (string?) null,
                    Consoles = consoles.Select(c => new {
                        Ip = c.Ip.ToString(),
                        c.Port,
                        c.DebugName,
                        c.ConsoleId,
                        c.Source
                    }),
                    Error = new CliErrorEnvelope(
                        "Connect target required",
                        "JSON mode cannot prompt for a console selection. Pass a target IP or discovery index.",
                        "CONNECT_TARGET_REQUIRED",
                        new[] { "Re-run with `rgh connect <ip> --json`.", "Use `rgh scan --json` to list discovered consoles first." })
                });
                return 1;
            }

            string? selectedIp = CliOutput.PromptForConsole(consoles);
            if (string.IsNullOrWhiteSpace(selectedIp)) {
                return WriteConnectError(
                    settings.Json,
                    "Connect selection failed",
                    "No console selected.",
                    "CONNECT_SELECTION_REQUIRED",
                    "Choose a discovered console or pass a target IP directly.");
            }

            config.DefaultIp = selectedIp;
            config.DefaultPort ??= 730;
            config.Save();
            return WriteConnectSuccess(settings.Json, selectedIp, config.DefaultPort ?? 730, "prompt");
        }

        if (int.TryParse(target, out int index)) {
            if (!TryLoadConfig(settings.Json, out config))
                return 1;

            IReadOnlyList<DiscoveredConsole> consoles;
            try {
                consoles = await DiscoverWithFeedbackAsync(settings);
            }
            catch (ArgumentException ex) {
                return WriteConnectError(
                    settings.Json,
                    "Connect discovery failed",
                    ex.Message,
                    "CONNECT_DISCOVERY_FAILED",
                    "Use a valid --ports list, or pass a target IP directly.");
            }

            if (index <= 0 || index > consoles.Count) {
                return WriteConnectError(
                    settings.Json,
                    "Connect index failed",
                    "Invalid index.",
                    "CONNECT_INDEX_INVALID",
                    "Use an index from the current discovery list or pass a target IP directly.");
            }

            string ip = consoles[index - 1].Ip.ToString();
            config.DefaultIp = ip;
            config.DefaultPort ??= 730;
            config.Save();
            return WriteConnectSuccess(settings.Json, ip, config.DefaultPort ?? 730, "discovery-index");
        }

        if (!TargetProfileStore.TryValidateTarget(target, null, out string normalizedIp, out _, out string error)) {
            return WriteConnectError(
                settings.Json,
                "Connect target failed",
                error,
                "CONNECT_TARGET_INVALID",
                "Pass a valid console host name or IP address.");
        }

        if (!TryLoadConfig(settings.Json, out config))
            return 1;

        config.DefaultIp = normalizedIp;
        config.DefaultPort ??= 730;
        config.Save();
        return WriteConnectSuccess(settings.Json, normalizedIp, config.DefaultPort ?? 730, "direct");
    }

    private static async Task<IReadOnlyList<DiscoveredConsole>> DiscoverWithFeedbackAsync(DiscoverySettings settings) {
        if (!DiscoveryHelpers.TryParsePorts(settings.Ports, out _, out string portError))
            throw new ArgumentException(portError, nameof(settings.Ports));

        string ports = string.IsNullOrWhiteSpace(settings.Ports) ? "730,731" : settings.Ports;
        int timeoutMs = settings.TimeoutMs ?? 400;
        if (!settings.Json) {
            AnsiConsole.MarkupLine($"[grey]Discovery started:[/] ports [deepskyblue1]{Markup.Escape(ports)}[/], per-host timeout [deepskyblue1]{timeoutMs.ToString(CultureInfo.InvariantCulture)} ms[/].");
            AnsiConsole.MarkupLine("[grey]Scanning local network...[/]");
        }

        IReadOnlyList<DiscoveredConsole> consoles = Array.Empty<DiscoveredConsole>();
        try {
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(DiscoveryCeilingSeconds));
            if (settings.Json) {
                consoles = await DiscoverAsync(settings, cts.Token);
            }
            else {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(new Style(Spectre.Console.Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .StartAsync("Discovering Xbox 360 consoles...", async _ => {
                        consoles = await DiscoverAsync(settings, cts.Token);
                    });
            }
        }
        catch (ArgumentException) {
            throw;
        }
        catch (OperationCanceledException) {
            if (!settings.Json)
                AnsiConsole.MarkupLine($"[yellow]Discovery timed out after {DiscoveryCeilingSeconds.ToString(CultureInfo.InvariantCulture)}s.[/] You can enter the IP manually.");
            return Array.Empty<DiscoveredConsole>();
        }
        catch (Exception) {
            if (!settings.Json) {
                AnsiConsole.MarkupLine("[yellow]Discovery failed. You can still enter the IP manually.[/]");
                AnsiConsole.MarkupLine("[grey]You can still enter the IP manually.[/]");
            }
            return Array.Empty<DiscoveredConsole>();
        }

        if (!settings.Json) {
            if (consoles.Count == 0) {
                AnsiConsole.MarkupLine("[yellow]No consoles discovered automatically.[/] You can enter an IP manually.");
            }
            else {
                AnsiConsole.MarkupLine($"[green]Discovery complete:[/] found [deepskyblue1]{consoles.Count.ToString(CultureInfo.InvariantCulture)}[/] console(s).");
            }
        }

        return consoles;
    }

    private static bool TryLoadConfig(bool json, out CliConfig config) {
        if (CliConfig.TryLoad(out config))
            return true;

        WriteConnectError(
            json,
            "Connect config failed",
            "Config file is present but could not be parsed. Fix or remove config.json, then run rgh connect again.",
            "CONNECT_CONFIG_INVALID",
            "Fix or remove config.json, then run rgh connect again.");
        return false;
    }

    private static int WriteConnectSuccess(bool json, string target, int port, string source) {
        if (json) {
            CliOutput.EmitJson(new {
                Success = true,
                Status = "saved",
                Target = target,
                Port = port,
                Source = source
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Default console set to[/] {target}");
        return 0;
    }

    private static int WriteConnectError(bool json, string title, string message, string code, string nextStep) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] { nextStep }));
            return 1;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        return 1;
    }
}
