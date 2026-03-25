using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmThreadSuspendCommand : AsyncCommand<XbdmThreadSuspendCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--id <ID>")]
		[LocalizedDescription("Thread ID in hex or decimal.")]
		public string? ThreadId { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseThreadId(settings.ThreadId, out var threadId))
		{
			AnsiConsole.MarkupLine("[red]--id is required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
			await client.SuspendThreadAsync(threadId, cts.Token);
			AnsiConsole.MarkupLine($"[green]Suspended thread[/] 0x{threadId:X8}");
			return 0;
		}, CancellationToken.None);
	}
}
