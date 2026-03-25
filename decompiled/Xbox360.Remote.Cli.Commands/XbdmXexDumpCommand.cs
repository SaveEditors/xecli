using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmXexDumpCommand : AsyncCommand<XbdmXexDumpCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--path <XEX>")]
		[LocalizedDescription("Explicit XEX path. If omitted, uses the running title.")]
		public string? Path { get; init; }

		[CommandOption("--out <FILE>")]
		[LocalizedDescription("Output file path.")]
		public string? Output { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Output))
		{
			AnsiConsole.MarkupLine("[red]--out is required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientOnceAsync(settings, async delegate(XbdmClient client)
		{
			string text = settings.Path;
			if (text == null)
			{
				text = await client.GetRunningXexPathAsync(null, CancellationToken.None);
			}
			string path = text;
			if (string.IsNullOrWhiteSpace(path))
			{
				AnsiConsole.MarkupLine("[red]Unable to resolve running XEX. Use --path.[/]");
				return 1;
			}
			FileStream stream = new FileStream(settings.Output, FileMode.Create, FileAccess.Write, FileShare.None);
			try
			{
				await CliOutput.RunWithProgressAsync("Downloading " + path, null, (IProgress<long> progress) => client.DownloadFileAsync(path, stream, progress, CancellationToken.None));
				AnsiConsole.MarkupLine("[green]Downloaded[/] " + path + " to " + settings.Output);
				return 0;
			}
			finally
			{
				if (stream != null)
				{
					((IDisposable)stream).Dispose();
				}
			}
		}, CancellationToken.None);
	}
}
