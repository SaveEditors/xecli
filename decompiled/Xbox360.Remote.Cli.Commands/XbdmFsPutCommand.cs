using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmFsPutCommand : AsyncCommand<XbdmFsPutCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--path <FILE>")]
		public string? Path { get; init; }

		[CommandOption("--in <FILE>")]
		public string? Input { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Input))
		{
			AnsiConsole.MarkupLine("[red]--path and --in are required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			FileStream stream = new FileStream(settings.Input, FileMode.Open, FileAccess.Read, FileShare.Read);
			try
			{
				await CliOutput.RunWithProgressAsync("Uploading " + settings.Input, (uint)stream.Length, (IProgress<long> progress) => client.UploadFileAsync(settings.Path, stream, stream.Length, progress, CancellationToken.None));
				AnsiConsole.MarkupLine("[green]Uploaded[/] " + settings.Input + " to " + settings.Path);
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
