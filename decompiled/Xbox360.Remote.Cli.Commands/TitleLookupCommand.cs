using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Xbox360.Remote.Cli.Commands;

public sealed class TitleLookupCommand : AsyncCommand<TitleLookupCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandArgument(0, "[TITLEID]")]
		[LocalizedDescription("Title ID (hex, with or without 0x). Omit it or pass 'active' to resolve the current title.")]
		public string TitleId { get; init; } = string.Empty;

		[CommandArgument(1, "[MEDIAID]")]
		[LocalizedDescription("Optional media ID (hex).")]
		public string? MediaId { get; init; }

		[CommandOption("--active")]
		[LocalizedDescription("Resolve the currently active title from the connected console.")]
		public bool Active { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		uint? mediaId = null;
		if (!string.IsNullOrWhiteSpace(settings.MediaId))
		{
			if (!TryParseHex(settings.MediaId, out var value))
			{
				AnsiConsole.MarkupLine("[red]Invalid Media ID.[/]");
				return 1;
			}
			mediaId = value;
		}
		string text = null;
		uint value2;
		string text2;
		if (settings.Active || string.IsNullOrWhiteSpace(settings.TitleId) || string.Equals(settings.TitleId, "active", StringComparison.OrdinalIgnoreCase))
		{
			(uint, string) obj = await ResolveActiveTitleAsync(settings);
			value2 = obj.Item1;
			text = obj.Item2;
			text2 = "active";
		}
		else
		{
			if (!TryParseHex(settings.TitleId, out value2))
			{
				AnsiConsole.MarkupLine("[red]Invalid Title ID.[/]");
				return 1;
			}
			text2 = "manual";
		}
		IReadOnlyList<TitleIdEntry> readOnlyList = TitleIdDatabase.Instance.FindAll(value2);
		if (mediaId.HasValue)
		{
			readOnlyList = readOnlyList.Where((TitleIdEntry e) => e.MediaId == mediaId.Value).ToList();
		}
		string text3 = ProfileHelpers.TryGetTitleFallbackName(value2, text, null);
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				Source = text2,
				TitleId = $"0x{value2:X8}",
				MediaId = (mediaId.HasValue ? $"0x{mediaId.Value:X8}" : null),
				RunningXex = text,
				Matches = readOnlyList,
				FallbackTitle = text3
			});
			return (readOnlyList.Count == 0 && string.IsNullOrWhiteSpace(text3)) ? 1 : 0;
		}
		if (readOnlyList.Count == 0)
		{
			if (!string.IsNullOrWhiteSpace(text3))
			{
				AnsiConsole.Write(new Panel($"[bold green]{Markup.Escape(text3)}[/]\n[grey]Title ID[/] [cyan]0x{value2:X8}[/]").Header("[bold deepskyblue1]Active Title[/]").BorderColor(Color.Grey));
				if (!string.IsNullOrWhiteSpace(text))
				{
					AnsiConsole.MarkupLine("[grey]Running XEX:[/] [green]" + Markup.Escape(text) + "[/]");
				}
				AnsiConsole.MarkupLine("[yellow]No bundled database entry matched this title yet.[/]");
				return 0;
			}
			AnsiConsole.MarkupLine("[yellow]No matches found.[/]");
			return 1;
		}
		TitleIdEntry titleIdEntry = readOnlyList[0];
		string text4 = ((text2 == "active" && !string.IsNullOrWhiteSpace(text3)) ? text3 : titleIdEntry.Name);
		string text5 = ((text2 == "active") ? "Active Title" : "Title Lookup");
		string text6 = (titleIdEntry.MediaId.HasValue ? $"0x{titleIdEntry.MediaId.Value:X8}" : "unknown");
		AnsiConsole.Write(new Panel($"[bold green]{Markup.Escape(text4)}[/]\n[grey]Title ID[/] [cyan]0x{titleIdEntry.TitleId:X8}[/]  [grey]Media ID[/] [cyan]{Markup.Escape(text6)}[/]").Header("[bold deepskyblue1]" + text5 + "[/]").BorderColor(Color.Grey));
		if (!string.IsNullOrWhiteSpace(text))
		{
			AnsiConsole.MarkupLine("[grey]Running XEX:[/] [green]" + Markup.Escape(text) + "[/]");
		}
		if (!string.Equals(text4, titleIdEntry.Name, StringComparison.OrdinalIgnoreCase))
		{
			AnsiConsole.MarkupLine("[grey]Database entry:[/] [mediumpurple3]" + Markup.Escape(titleIdEntry.Name) + "[/]");
		}
		Table table = CliOutput.CreateTable();
		table.Border(TableBorder.Rounded);
		table.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
		table.AddColumn(new TableColumn("[bold deepskyblue1]Title ID[/]"));
		table.AddColumn(new TableColumn("[bold deepskyblue1]Media ID[/]"));
		table.AddColumn(new TableColumn("[bold gold1]Region[/]"));
		table.AddColumn(new TableColumn("[bold mediumpurple3]Type[/]"));
		table.AddColumn(new TableColumn("[bold grey]Serial[/]"));
		table.AddColumn(new TableColumn("[bold grey]Wave[/]"));
		foreach (TitleIdEntry item in readOnlyList)
		{
			table.AddRow("[springgreen3_1]" + Markup.Escape(item.Name) + "[/]", $"[deepskyblue1]0x{item.TitleId:X8}[/]", item.MediaId.HasValue ? $"[deepskyblue1]0x{item.MediaId.Value:X8}[/]" : "[grey]unknown[/]", string.IsNullOrWhiteSpace(item.Region) ? "[grey]unknown[/]" : ("[gold1]" + Markup.Escape(item.Region) + "[/]"), string.IsNullOrWhiteSpace(item.Type) ? "[grey]unknown[/]" : ("[mediumpurple3]" + Markup.Escape(item.Type) + "[/]"), string.IsNullOrWhiteSpace(item.Serial) ? "[grey]unknown[/]" : ("[grey]" + Markup.Escape(item.Serial) + "[/]"), string.IsNullOrWhiteSpace(item.Wave) ? "[grey]unknown[/]" : ("[grey]" + Markup.Escape(item.Wave) + "[/]"));
		}
		AnsiConsole.Write(table);
		return 0;
	}

	private static async Task<(uint TitleId, string? RunningXex)> ResolveActiveTitleAsync(Settings settings)
	{
		var (host, port, timeoutMs) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		(uint TitleId, string? RunningXex) result;
		await using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
		{
			Host = host,
			Port = port,
			TimeoutMs = timeoutMs
		}, CancellationToken.None))
		{
			string runningXex = null;
			try
			{
				runningXex = await client.GetRunningXexPathAsync(null, CancellationToken.None);
			}
			catch
			{
			}
			result = (TitleId: await new Jrpc2Client(client).GetTitleIdAsync(CancellationToken.None), RunningXex: runningXex);
		}
		return result;
	}

	private static bool TryParseHex(string text, out uint value)
	{
		value = 0u;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		if (text2.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text2 = text2.Substring(2);
		}
		return uint.TryParse(text2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
	}
}
