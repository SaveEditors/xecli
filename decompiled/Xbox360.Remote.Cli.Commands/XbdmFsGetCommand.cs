using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmFsGetCommand : AsyncCommand<XbdmFsGetCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--path <FILE>")]
		public string? Path { get; init; }

		[CommandOption("--out <FILE>")]
		public string? Output { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Output))
		{
			AnsiConsole.MarkupLine("[red]--path and --out are required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			FileStream stream = new FileStream(settings.Output, FileMode.Create, FileAccess.Write, FileShare.None);
			try
			{
				await CliOutput.RunWithProgressAsync("Downloading " + settings.Path, null, (IProgress<long> progress) => client.DownloadFileAsync(settings.Path, stream, progress, CancellationToken.None));
				AnsiConsole.MarkupLine("[green]Downloaded[/] " + settings.Path + " to " + settings.Output);
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
