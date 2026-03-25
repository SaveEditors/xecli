using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Spectre.Console;
using Color = Spectre.Console.Color;

namespace Xbox360.Remote.Cli;

internal static class CliOutput
{
	internal readonly record struct TransferProgressUpdate(long Value, string? Status = null);

	internal readonly record struct TransferBatchItem(string Label, long Size);

	private static readonly TimeZoneInfo EasternTimeZone = GetEasternTimeZone();

	public static void EmitJson(object value)
	{
		AnsiConsole.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	public static async Task RunWithProgressAsync(string title, uint? totalBytes, Func<IProgress<long>, Task> operation)
	{
		await RunWithProgressAsync(title, totalBytes.HasValue ? new long?(totalBytes.Value) : ((long?)null), async delegate(IProgress<TransferProgressUpdate> progress)
		{
			Progress<long> arg = new Progress<long>(delegate(long value)
			{
				progress.Report(new TransferProgressUpdate(value));
			});
			await operation(arg);
		});
	}

	public static async Task RunWithProgressAsync(string title, long? totalBytes, Func<IProgress<TransferProgressUpdate>, Task> operation)
	{
		Progress progress = AnsiConsole.Progress().AutoClear(enabled: false);
		ProgressColumn[] obj = new ProgressColumn[7]
		{
			new TaskDescriptionColumn(),
			null,
			null,
			null,
			null,
			null,
			null
		};
		ProgressBarColumn progressBarColumn = new ProgressBarColumn();
		Color? foreground = Color.SpringGreen3_1;
		Decoration? decoration = Decoration.Bold;
		progressBarColumn.CompletedStyle = new Style(foreground, null, decoration);
		Color? foreground2 = Color.SpringGreen3_1;
		decoration = Decoration.Bold;
		progressBarColumn.FinishedStyle = new Style(foreground2, null, decoration);
		progressBarColumn.RemainingStyle = new Style(Color.Grey35);
		obj[1] = progressBarColumn;
		obj[2] = new PercentageColumn();
		obj[3] = new DownloadedColumn();
		obj[4] = new TransferSpeedColumn();
		obj[5] = new RemainingTimeColumn();
		obj[6] = new SpinnerColumn();
		await progress.Columns(obj).StartAsync(async delegate(ProgressContext ctx)
		{
			ProgressTask task = ctx.AddTask(FormatTransferDescription(title, "starting"), autoStart: true, totalBytes ?? 1);
			if (!totalBytes.HasValue)
			{
				task.IsIndeterminate = true;
			}
			Progress<TransferProgressUpdate> arg = new Progress<TransferProgressUpdate>(delegate(TransferProgressUpdate update)
			{
				if (totalBytes.HasValue)
				{
					task.Value = Math.Min(update.Value, totalBytes.Value);
				}
				if (!string.IsNullOrWhiteSpace(update.Status))
				{
					task.Description = FormatTransferDescription(title, update.Status);
				}
			});
			await operation(arg);
			task.Description = FormatTransferDescription(title, "done");
			task.Value = task.MaxValue;
		});
	}

	public static async Task RunBatchProgressAsync(string title, IReadOnlyList<TransferBatchItem> items, Func<TransferBatchScope, Task> operation)
	{
		long totalBytes = items.Sum((TransferBatchItem item) => Math.Max(0L, item.Size));
		await RunWithProgressAsync(title, (totalBytes > 0) ? new long?(totalBytes) : ((long?)null), async delegate(IProgress<TransferProgressUpdate> progress)
		{
			TransferBatchScope scope = new TransferBatchScope(progress, items.Count, totalBytes);
			await operation(scope);
			scope.Finish();
		});
	}

	private static string FormatTransferDescription(string title, string? status)
	{
		string text = Markup.Escape(title);
		if (string.IsNullOrWhiteSpace(status))
		{
			return "[bold springgreen3_1]" + text + "[/]";
		}
		return $"[bold springgreen3_1]{text}[/] [silver]{Markup.Escape(status)}[/]";
	}

	public static void RenderDiscovery(IReadOnlyList<DiscoveredConsole> consoles, bool json)
	{
		if (json)
		{
			EmitJson(consoles.Select((DiscoveredConsole c) => new
			{
				Ip = c.Ip.ToString(),
				Port = c.Port,
				DebugName = c.DebugName,
				ConsoleId = c.ConsoleId,
				Source = c.Source
			}));
			return;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]Detected Consoles[/]").RuleStyle("silver"));
		Table table = CreateTable();
		table.AddColumn(AlignableExtensions.Centered(new TableColumn("[white]#[/]")));
		table.AddColumn(new TableColumn("[cyan1]IP[/]"));
		table.AddColumn(new TableColumn("[deepskyblue1]Port[/]"));
		table.AddColumn(new TableColumn("[springgreen3_1]Name[/]"));
		table.AddColumn(new TableColumn("[gold1]Console ID/Serial[/]"));
		table.AddColumn(new TableColumn("[white]Source[/]"));
		int num = 1;
		foreach (DiscoveredConsole console in consoles)
		{
			string text = ((console.DebugName != null) ? Markup.Escape(console.DebugName) : "unknown");
			string text2 = ((console.ConsoleId != null) ? Markup.Escape(console.ConsoleId) : "unknown");
			table.AddRow($"[white]{num}[/]", $"[cyan1]{console.Ip}[/]", $"[deepskyblue1]{console.Port}[/]", (console.DebugName != null) ? ("[springgreen3_1]" + text + "[/]") : "[grey70]unknown[/]", (console.ConsoleId != null) ? ("[gold1]" + text2 + "[/]") : "[grey70]unknown[/]", "[silver]" + console.Source + "[/]");
			num++;
		}
		AnsiConsole.Write(table);
	}

	public static string? PromptForConsole(IReadOnlyList<DiscoveredConsole> consoles)
	{
		if (consoles.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No consoles discovered. Enter IP manually:[/]");
			return AnsiConsole.Ask<string>("IP:");
		}
		SelectionPrompt<DiscoveredConsole> obj = new SelectionPrompt<DiscoveredConsole>().Title("Select a console").PageSize(10);
		Color? foreground = Color.DeepSkyBlue1;
		Decoration? decoration = Decoration.Bold;
		SelectionPrompt<DiscoveredConsole> selectionPrompt = obj.HighlightStyle(new Style(foreground, null, decoration)).UseConverter((DiscoveredConsole c) => $"{c.Ip} | {c.DebugName ?? "unknown"} | {c.ConsoleId ?? "unknown"} | {c.Source}");
		selectionPrompt.AddChoices(consoles);
		return AnsiConsole.Prompt(selectionPrompt).Ip.ToString();
	}

	public static void RenderHexDump(uint baseAddress, byte[] data, int width = 16)
	{
		char[] array = new char[width];
		for (int i = 0; i < data.Length; i += width)
		{
			int num = Math.Min(width, data.Length - i);
			for (int j = 0; j < num; j++)
			{
				byte b = data[i + j];
				array[j] = (char)((b >= 32 && b <= 126) ? b : 46);
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int k = 0; k < width; k++)
			{
				if (k < num)
				{
					stringBuilder.Append(data[i + k].ToString("X2")).Append(' ');
				}
				else
				{
					stringBuilder.Append("   ");
				}
			}
			string value = Markup.Escape(new string(array, 0, num));
			AnsiConsole.MarkupLine($"[white]0x{(int)baseAddress + i:X8}[/] [deepskyblue1]{stringBuilder}[/] [springgreen3_1]{value}[/]");
		}
	}

	public static Table CreateTable()
	{
		return new Table().Border(TableBorder.Rounded).BorderColor(Color.Silver).Expand();
	}

	public static string FormatTimestamp(DateTime? value)
	{
		if (!value.HasValue)
		{
			return "[grey]unknown[/]";
		}
		DateTime value2 = value.Value;
		string text = TimeZoneInfo.ConvertTimeFromUtc(value2.Kind switch
		{
			DateTimeKind.Utc => value2, 
			DateTimeKind.Local => value2.ToUniversalTime(), 
			_ => DateTime.SpecifyKind(value2, DateTimeKind.Local).ToUniversalTime(), 
		}, EasternTimeZone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		return "[green]" + Markup.Escape(text) + "[/]";
	}

	private static TimeZoneInfo GetEasternTimeZone()
	{
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
		}
		catch
		{
			return TimeZoneInfo.Local;
		}
	}
}
