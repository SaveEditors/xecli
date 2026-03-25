using System.ComponentModel;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmFsCatCommand : AsyncCommand<XbdmFsCatCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--path <FILE>")]
        public string? Path { get; init; }

        [CommandOption("--max <BYTES>")]
        [LocalizedDescription("Maximum bytes to display (default 65536).")]
        public int? MaxBytes { get; init; }

        [CommandOption("--hex")]
        [LocalizedDescription("Render as hex instead of text.")]
        public bool Hex { get; init; }

        [CommandOption("--encoding <ENC>")]
        [LocalizedDescription("Text encoding: utf8|ascii (default utf8).")]
        public string? EncodingName { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        int maxBytes = settings.MaxBytes.HasValue ? Math.Max(1, settings.MaxBytes.Value) : 65536;
        Encoding encoding = settings.EncodingName?.ToLowerInvariant() switch {
            "ascii" => Encoding.ASCII,
            _ => Encoding.UTF8
        };

        return await CliHelpers.WithClientAsync(settings, async client => {
            using MemoryStream ms = new MemoryStream();
            await client.DownloadFileAsync(settings.Path, ms, null, CancellationToken.None);
            byte[] data = ms.ToArray();
            int length = Math.Min(maxBytes, data.Length);

            if (settings.Hex) {
                CliOutput.RenderHexDump(0, data.AsSpan(0, length).ToArray());
            }
            else {
                string text = encoding.GetString(data, 0, length);
                AnsiConsole.WriteLine(text);
            }

            if (data.Length > maxBytes) {
                AnsiConsole.MarkupLine($"[yellow]Output truncated to {maxBytes} bytes.[/]");
            }

            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmFsDeleteCommand : AsyncCommand<XbdmFsDeleteCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            await client.DeleteAsync(settings.Path, CancellationToken.None);
            AnsiConsole.MarkupLine($"[green]Deleted[/] {settings.Path}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmFsMkdirCommand : AsyncCommand<XbdmFsMkdirCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            await client.CreateDirectoryAsync(settings.Path, CancellationToken.None);
            AnsiConsole.MarkupLine($"[green]Created directory[/] {settings.Path}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmFsMoveCommand : AsyncCommand<XbdmFsMoveCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--from <PATH>")]
        public string? Source { get; init; }

        [CommandOption("--to <PATH>")]
        public string? Destination { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Source) || string.IsNullOrWhiteSpace(settings.Destination)) {
            AnsiConsole.MarkupLine("[red]--from and --to are required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            await client.MoveAsync(settings.Source, settings.Destination, CancellationToken.None);
            AnsiConsole.MarkupLine($"[green]Moved[/] {settings.Source} -> {settings.Destination}");
            return 0;
        }, CancellationToken.None);
    }
}

