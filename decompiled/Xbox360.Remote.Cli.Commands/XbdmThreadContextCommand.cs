using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmThreadContextCommand : AsyncCommand<XbdmThreadContextCommand.Settings>
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
			IReadOnlyDictionary<string, string> readOnlyDictionary = await client.GetThreadContextAsync(threadId, cts.Token);
			if (settings.Json)
			{
				CliOutput.EmitJson(readOnlyDictionary);
				return 0;
			}
			AnsiConsole.Write(new Rule($"[bold deepskyblue1]Thread 0x{threadId:X8}[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Register[/]"));
			table.AddColumn(new TableColumn("[cyan]Value[/]"));
			foreach (var (text3, text4) in readOnlyDictionary.OrderBy<KeyValuePair<string, string>, string>((KeyValuePair<string, string> pair) => pair.Key, StringComparer.OrdinalIgnoreCase))
			{
				table.AddRow("[grey]" + Markup.Escape(text3) + "[/]", "[cyan]" + Markup.Escape(text4) + "[/]");
			}
			AnsiConsole.Write(table);
			return 0;
		}, CancellationToken.None);
	}
}
