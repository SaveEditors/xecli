using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmModulesPendingCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		CliConfig cliConfig = CliConfig.Load();
		CliConfig.PendingModuleOperationInfo pending = cliConfig.PendingModuleOperation;
		if (pending == null || string.IsNullOrWhiteSpace(pending.ModuleName) || string.IsNullOrWhiteSpace(pending.Action))
		{
			AnsiConsole.MarkupLine("[grey]No pending module operation.[/]");
			return 0;
		}
		var (item, item2, item3) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		return await CliHelpers.WithClientOnceAsync((Ip: item, Port: item2, TimeoutMs: item3), settings, async delegate(XbdmClient client)
		{
			IReadOnlyList<XbdmModuleInfo> source = await client.GetModulesAsync(includeSections: false, CancellationToken.None);
			bool flag = source.Any((XbdmModuleInfo m) => string.Equals(m.Name, pending.ModuleName, StringComparison.OrdinalIgnoreCase));
			if (string.Equals(pending.Action, "load", StringComparison.OrdinalIgnoreCase) ? flag : (!flag))
			{
				if (string.Equals(pending.Action, "load", StringComparison.OrdinalIgnoreCase))
				{
					XbdmModuleInfo xbdmModuleInfo = source.First((XbdmModuleInfo m) => string.Equals(m.Name, pending.ModuleName, StringComparison.OrdinalIgnoreCase));
					ModuleCommandHelpers.ClearPendingIfMatch(pending.Action, pending.ModuleName);
					OperationFeedback.WriteSuccess("Pending module load verified", $"[green]{Markup.Escape(xbdmModuleInfo.Name)}[/] [grey]at[/] [cyan]0x{xbdmModuleInfo.BaseAddress:X8}[/]");
				}
				else
				{
					ModuleCommandHelpers.ClearPendingIfMatch(pending.Action, pending.ModuleName);
					OperationFeedback.WriteSuccess("Pending module unload verified", "[green]" + Markup.Escape(pending.ModuleName) + "[/] [grey]is no longer loaded.[/]");
				}
				return 0;
			}
			OperationFeedback.WriteWarning("Module operation still pending", "[yellow]" + Markup.Escape(pending.ModuleName) + "[/] [grey]has not reached the expected state yet.[/]");
			return 1;
		}, CancellationToken.None);
	}
}
