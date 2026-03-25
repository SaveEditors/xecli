using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmThreadsListCommand : AsyncCommand<XbdmThreadsListCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--no-names")]
		[LocalizedDescription("Skip resolving thread names.")]
		public bool NoNames { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
			IReadOnlyList<XbdmThreadInfo> readOnlyList = await client.GetThreadsAsync(!settings.NoNames, cts.Token);
			if (settings.Json)
			{
				CliOutput.EmitJson(readOnlyList);
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Threads[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(AlignableExtensions.Centered(new TableColumn("[grey]#[/]")));
			table.AddColumn(new TableColumn("[cyan]ID[/]"));
			table.AddColumn(new TableColumn("[grey]Suspend[/]"));
			table.AddColumn(new TableColumn("[grey]Priority[/]"));
			table.AddColumn(new TableColumn("[grey]CPU[/]"));
			table.AddColumn(new TableColumn("[green]Name[/]"));
			table.AddColumn(new TableColumn("[cyan]Stack Base[/]"));
			table.AddColumn(new TableColumn("[cyan]Stack Limit[/]"));
			int num = 1;
			foreach (XbdmThreadInfo item in readOnlyList)
			{
				table.AddRow($"[grey]{num}[/]", $"[cyan]0x{item.Id:X8}[/]", $"[grey]{item.SuspendCount}[/]", $"[grey]{item.Priority}[/]", $"[grey]{item.CurrentProcessor}[/]", (item.Name != null) ? ("[green]" + Markup.Escape(item.Name) + "[/]") : "[grey]unknown[/]", $"[cyan]0x{item.BaseAddress:X8}[/]", $"[cyan]0x{item.StackLimit:X8}[/]");
				num++;
			}
			AnsiConsole.Write(table);
			return 0;
		}, CancellationToken.None);
	}
}
