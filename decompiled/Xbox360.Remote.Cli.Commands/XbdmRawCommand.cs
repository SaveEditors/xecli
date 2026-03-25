using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmRawCommand : AsyncCommand<XbdmRawCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--cmd <COMMAND>")]
		public string? Command { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Command))
		{
			AnsiConsole.MarkupLine("[red]--cmd is required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			var (xbdmResponse, readOnlyList) = await client.SendRawAsync(settings.Command, CancellationToken.None);
			AnsiConsole.MarkupLine($"[grey]({xbdmResponse.StatusCode})[/] {xbdmResponse.Message}");
			if (readOnlyList != null)
			{
				foreach (string item in readOnlyList)
				{
					AnsiConsole.WriteLine(item);
				}
			}
			return 0;
		}, CancellationToken.None);
	}
}
