using System.ComponentModel;
using System.Globalization;
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
        public string? MaxBytes { get; init; }

        [CommandOption("--hex")]
        [LocalizedDescription("Render as hex instead of text.")]
        public bool Hex { get; init; }

        [CommandOption("--encoding <ENC>")]
        [LocalizedDescription("Text encoding: utf8|ascii (default utf8).")]
        public string? EncodingName { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            return WriteValidationFailure(settings.Json, "--path is required.", "XBDM_FS_CAT_PATH_REQUIRED");
        }

        int maxBytes = 65536;
        if (settings.MaxBytes is not null) {
            string maxBytesText = settings.MaxBytes.Trim();
            if (maxBytesText.Length == 0 ||
                !int.TryParse(maxBytesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxBytes)) {
                return WriteValidationFailure(settings.Json, "Invalid --max value.", "XBDM_FS_CAT_MAX_INVALID");
            }

            if (maxBytes <= 0)
                return WriteValidationFailure(settings.Json, "--max must be greater than zero.", "XBDM_FS_CAT_MAX_INVALID");
            if (maxBytes > 16 * 1024 * 1024)
                return WriteValidationFailure(settings.Json, "--max must not exceed 16777216 bytes.", "XBDM_FS_CAT_MAX_INVALID");
        }

        if (!string.IsNullOrWhiteSpace(settings.EncodingName) &&
            !settings.EncodingName.Equals("utf8", StringComparison.OrdinalIgnoreCase) &&
            !settings.EncodingName.Equals("utf-8", StringComparison.OrdinalIgnoreCase) &&
            !settings.EncodingName.Equals("ascii", StringComparison.OrdinalIgnoreCase))
            return WriteValidationFailure(settings.Json, "Invalid --encoding value. Use utf8 or ascii.", "XBDM_FS_CAT_ENCODING_INVALID");

        Encoding encoding = settings.EncodingName?.ToLowerInvariant() switch {
            "ascii" => Encoding.ASCII,
            _ => Encoding.UTF8
        };

        return await CliHelpers.WithClientAsync(settings, async client => {
            using MemoryStream ms = new MemoryStream();
            await client.DownloadFileAsync(settings.Path, ms, null, CancellationToken.None);
            byte[] data = ms.ToArray();
            int length = Math.Min(maxBytes, data.Length);

            if (settings.Json) {
                byte[] selected = data.AsSpan(0, length).ToArray();
                CliOutput.EmitJson(new {
                    Path = settings.Path,
                    Mode = settings.Hex ? "hex" : "text",
                    Encoding = settings.Hex ? null : encoding.WebName,
                    BytesRead = length,
                    TotalBytes = data.Length,
                    Truncated = data.Length > maxBytes,
                    Content = settings.Hex ? null : encoding.GetString(selected),
                    Hex = settings.Hex ? Convert.ToHexString(selected) : null
                });
                return 0;
            }

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

    private static int WriteValidationFailure(bool json, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "XBDM file cat failed",
                message,
                code,
                new[] { "Correct the command arguments and retry." }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }
        return 1;
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

        if (ContainsLineBreak(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path must be a single line.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            await client.DeleteAsync(settings.Path, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Path = settings.Path
                });
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]Deleted[/] {settings.Path}");
            return 0;
        }, CancellationToken.None);
    }

    private static bool ContainsLineBreak(string value) {
        return value.Contains('\r') || value.Contains('\n');
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
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Path = settings.Path
                });
                return 0;
            }

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
        if (string.IsNullOrWhiteSpace(settings.Source)) {
            AnsiConsole.MarkupLine("[red]--from is required.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Destination)) {
            AnsiConsole.MarkupLine("[red]--to is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            await client.MoveAsync(settings.Source, settings.Destination, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Source = settings.Source,
                    Destination = settings.Destination
                });
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]Moved[/] {settings.Source} -> {settings.Destination}");
            return 0;
        }, CancellationToken.None);
    }
}

