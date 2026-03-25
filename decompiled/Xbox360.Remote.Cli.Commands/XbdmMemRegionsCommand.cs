using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmMemRegionsCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
			IReadOnlyList<XbdmMemoryRegion> readOnlyList = await client.GetMemoryRegionsAsync(cts.Token);
			if (settings.Json)
			{
				CliOutput.EmitJson(readOnlyList);
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Memory Regions[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[cyan]Base[/]"));
			table.AddColumn(new TableColumn("[cyan]Size[/]"));
			table.AddColumn(new TableColumn("[grey]Protect[/]"));
			table.AddColumn(new TableColumn("[grey]Phys[/]"));
			foreach (XbdmMemoryRegion item in readOnlyList)
			{
				table.AddRow($"[cyan]0x{item.BaseAddress:X8}[/]", $"[cyan]0x{item.Size:X8}[/]", $"[grey]0x{item.Protect:X8}[/]", $"[grey]0x{item.Phys:X8}[/]");
			}
			AnsiConsole.Write(table);
			return 0;
		}, CancellationToken.None);
	}
}
