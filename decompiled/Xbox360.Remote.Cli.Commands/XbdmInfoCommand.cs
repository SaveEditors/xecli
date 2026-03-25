using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmInfoCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		(string, int, int) tuple = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		var (item, port, _) = tuple;
		return await CliHelpers.WithClientAsync(new ValueTuple<string, int, int>(item3: tuple.Item3, item1: item, item2: port), settings, async delegate(XbdmClient client)
		{
			XbdmConsoleInfo info = await client.GetConsoleInfoAsync(CancellationToken.None);
			string dmVersion = null;
			try
			{
				dmVersion = await client.GetDmVersionAsync(CancellationToken.None);
			}
			catch
			{
			}
			uint? titleId = null;
			try
			{
				titleId = await new Jrpc2Client(client).GetTitleIdAsync(CancellationToken.None);
			}
			catch
			{
			}
			IReadOnlyList<XbdmDriveEntry> drives = Array.Empty<XbdmDriveEntry>();
			try
			{
				drives = await client.GetDrivesAsync(includeSize: true, CancellationToken.None);
			}
			catch
			{
			}
			IReadOnlyList<XbdmUserInfo> users = Array.Empty<XbdmUserInfo>();
			try
			{
				users = await client.GetUserListAsync(CancellationToken.None);
			}
			catch
			{
			}
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Console = info,
					Port = port,
					DmVersion = dmVersion,
					TitleId = (titleId.HasValue ? $"0x{titleId.Value:X8}" : null),
					Drives = drives,
					Users = users
				});
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Console[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Field[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			table.AddRow("[grey]Console ID[/]", (info.ConsoleId != null) ? ("[gold1]" + Markup.Escape(info.ConsoleId) + "[/]") : "[grey]unknown[/]");
			table.AddRow("[grey]Debug Name[/]", (info.DebugName != null) ? ("[green]" + Markup.Escape(info.DebugName) + "[/]") : "[grey]unknown[/]");
			table.AddRow("[grey]Execution State[/]", (info.ExecutionState != null) ? ("[cyan]" + Markup.Escape(info.ExecutionState) + "[/]") : "[grey]unknown[/]");
			table.AddRow("[grey]Title IP[/]", (info.TitleIp != null) ? ("[cyan]" + Markup.Escape(info.TitleIp) + "[/]") : "[grey]unknown[/]");
			table.AddRow("[grey]Process ID[/]", info.ProcessId?.ToString() ?? "[grey]unknown[/]");
			table.AddRow("[grey]Port[/]", port.ToString());
			table.AddRow("[grey]DM Version[/]", (dmVersion != null) ? ("[cyan]" + Markup.Escape(dmVersion) + "[/]") : "[grey]unknown[/]");
			table.AddRow("[grey]Title ID[/]", titleId.HasValue ? $"0x{titleId.Value:X8}" : "[grey]unknown[/]");
			AnsiConsole.Write(table);
			if (drives.Count > 0)
			{
				AnsiConsole.Write(new Rule("[bold deepskyblue1]Drives[/]").RuleStyle("grey"));
				Table table2 = CliOutput.CreateTable();
				table2.AddColumn(new TableColumn("[green]Name[/]"));
				table2.AddColumn(new TableColumn("[cyan]Total[/]"));
				table2.AddColumn(new TableColumn("[cyan]Free[/]"));
				foreach (XbdmDriveEntry item2 in drives)
				{
					string text = Markup.Escape(item2.Name);
					table2.AddRow("[green]" + text + "[/]", "[cyan]" + FormatBytes(item2.TotalBytes) + "[/]", "[cyan]" + FormatBytes(item2.FreeBytes) + "[/]");
				}
				AnsiConsole.Write(table2);
				List<XbdmDriveEntry> list = drives.Where((XbdmDriveEntry d) => d.Name.StartsWith("Usb", StringComparison.OrdinalIgnoreCase)).ToList();
				if (list.Count > 0)
				{
					AnsiConsole.Write(new Rule("[bold deepskyblue1]Connected USB[/]").RuleStyle("grey"));
					Table table3 = CliOutput.CreateTable();
					table3.AddColumn(new TableColumn("[green]Name[/]"));
					table3.AddColumn(new TableColumn("[cyan]Total[/]"));
					table3.AddColumn(new TableColumn("[cyan]Free[/]"));
					foreach (XbdmDriveEntry item3 in list)
					{
						string text2 = Markup.Escape(item3.Name);
						table3.AddRow("[green]" + text2 + "[/]", "[cyan]" + FormatBytes(item3.TotalBytes) + "[/]", "[cyan]" + FormatBytes(item3.FreeBytes) + "[/]");
					}
					AnsiConsole.Write(table3);
				}
			}
			if (users.Count > 0)
			{
				AnsiConsole.Write(new Rule("[bold deepskyblue1]Users[/]").RuleStyle("grey"));
				Table table4 = CliOutput.CreateTable();
				table4.AddColumn(new TableColumn("[green]Gamertag[/]"));
				table4.AddColumn(new TableColumn("[gold1]XUID[/]"));
				table4.AddColumn(new TableColumn("[grey]Raw[/]"));
				foreach (XbdmUserInfo item4 in users)
				{
					table4.AddRow((item4.Gamertag != null) ? ("[green]" + Markup.Escape(item4.Gamertag) + "[/]") : "[grey]unknown[/]", item4.Xuid.HasValue ? $"0x{item4.Xuid.Value:X16}" : "[grey]unknown[/]", Markup.Escape(item4.RawLine));
				}
				AnsiConsole.Write(table4);
			}
			return 0;
		}, CancellationToken.None);
	}

	private static string FormatBytes(ulong? value)
	{
		if (!value.HasValue)
		{
			return "unknown";
		}
		double num = value.Value;
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		return $"{num:0.##} {array[num2]}";
	}
}
