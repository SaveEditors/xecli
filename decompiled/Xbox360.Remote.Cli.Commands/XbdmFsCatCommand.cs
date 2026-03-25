using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmFsCatCommand : AsyncCommand<XbdmFsCatCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
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

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Path))
		{
			AnsiConsole.MarkupLine("[red]--path is required.[/]");
			return 1;
		}
		int maxBytes = (settings.MaxBytes.HasValue ? Math.Max(1, settings.MaxBytes.Value) : 65536);
		Encoding encoding = ((!(settings.EncodingName?.ToLowerInvariant() == "ascii")) ? Encoding.UTF8 : Encoding.ASCII);
		Encoding encoding2 = encoding;
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using MemoryStream ms = new MemoryStream();
			await client.DownloadFileAsync(settings.Path, ms, null, CancellationToken.None);
			byte[] array = ms.ToArray();
			int num = Math.Min(maxBytes, array.Length);
			if (settings.Hex)
			{
				CliOutput.RenderHexDump(0u, array.AsSpan(0, num).ToArray());
			}
			else
			{
				AnsiConsole.WriteLine(encoding2.GetString(array, 0, num));
			}
			if (array.Length > maxBytes)
			{
				AnsiConsole.MarkupLine($"[yellow]Output truncated to {maxBytes} bytes.[/]");
			}
			return 0;
		}, CancellationToken.None);
	}
}
