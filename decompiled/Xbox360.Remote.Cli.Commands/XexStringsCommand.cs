using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XexStringsCommand : AsyncCommand<XexStringsCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--in <FILE>")]
		[LocalizedDescription("Input XEX file.")]
		public string? Input { get; init; }

		[CommandOption("--ftp-path <PATH>")]
		[LocalizedDescription("Fetch the XEX via FTP before scanning (e.g. /Hdd1/Aurora/Aurora.xex).")]
		public string? FtpPath { get; init; }

		[CommandOption("--running")]
		[LocalizedDescription("Use the running title XEX (resolved via XBDM + FTP).")]
		public bool Running { get; init; }

		[CommandOption("--min <N>")]
		[LocalizedDescription("Minimum string length (default: 4).")]
		public int? MinLength { get; init; }

		[CommandOption("--max <N>")]
		[LocalizedDescription("Maximum strings to return (default: 200).")]
		public int? MaxCount { get; init; }

		[CommandOption("--unicode")]
		[LocalizedDescription("Include UTF-16 (LE/BE) strings.")]
		public bool Unicode { get; init; }

		[CommandOption("--out <FILE>")]
		[LocalizedDescription("Write output to a text file.")]
		public string? Output { get; init; }

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	private readonly record struct StringHit(int Offset, string Kind, string Text);

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		string text = await ResolveInputAsync(settings.Input, settings.FtpPath, settings.Running);
		if (string.IsNullOrWhiteSpace(text))
		{
			return 1;
		}
		int minLength = Math.Max(2, settings.MinLength ?? 4);
		int maxCount = Math.Max(1, settings.MaxCount ?? 200);
		byte[] data = await File.ReadAllBytesAsync(text);
		List<StringHit> list = new List<StringHit>();
		list.AddRange(ExtractAscii(data, minLength, maxCount));
		if (settings.Unicode)
		{
			list.AddRange(ExtractUtf16(data, minLength, maxCount, littleEndian: true));
			list.AddRange(ExtractUtf16(data, minLength, maxCount, littleEndian: false));
		}
		list = list.OrderBy((StringHit h) => h.Offset).Take(maxCount).ToList();
		if (!string.IsNullOrWhiteSpace(settings.Output))
		{
			using StreamWriter streamWriter = new StreamWriter(settings.Output, append: false, Encoding.UTF8);
			foreach (StringHit item in list)
			{
				streamWriter.WriteLine($"{item.Offset:X8}\t{item.Kind}\t{item.Text}");
			}
			AnsiConsole.MarkupLine("[green]Wrote[/] " + Markup.Escape(settings.Output));
		}
		if (settings.Json)
		{
			CliOutput.EmitJson(list.Select((StringHit h) => new
			{
				Offset = $"0x{h.Offset:X8}",
				Kind = h.Kind,
				Text = h.Text
			}).ToList());
			return 0;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]XEX Strings[/]").RuleStyle("grey"));
		if (list.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No strings found.[/]");
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[cyan]Offset[/]"));
		table.AddColumn(new TableColumn("[grey]Type[/]"));
		table.AddColumn(new TableColumn("[green]Text[/]"));
		foreach (StringHit item2 in list)
		{
			table.AddRow($"[cyan]0x{item2.Offset:X8}[/]", "[grey]" + Markup.Escape(item2.Kind) + "[/]", "[green]" + Markup.Escape(item2.Text) + "[/]");
		}
		AnsiConsole.Write(table);
		return 0;
	}

	private static async Task<string?> ResolveInputAsync(string? input, string? ftpPath, bool running)
	{
		if (!string.IsNullOrWhiteSpace(input))
		{
			if (!File.Exists(input))
			{
				AnsiConsole.MarkupLine("[red]Input file not found.[/]");
				return null;
			}
			return input;
		}
		if (!string.IsNullOrWhiteSpace(ftpPath))
		{
			return await GhidraInputHelpers.DownloadViaFtpAsync(ftpPath);
		}
		if (running)
		{
			string text = await GhidraInputHelpers.ResolveRunningFtpPathAsync();
			if (string.IsNullOrWhiteSpace(text))
			{
				AnsiConsole.MarkupLine("[red]Unable to resolve running XEX via FTP path.[/]");
				return null;
			}
			return await GhidraInputHelpers.DownloadViaFtpAsync(text);
		}
		AnsiConsole.MarkupLine("[red]Provide --in, --ftp-path, or --running.[/]");
		return null;
	}

	private static List<StringHit> ExtractAscii(byte[] data, int minLength, int maxCount)
	{
		List<StringHit> list = new List<StringHit>();
		int num = -1;
		for (int i = 0; i < data.Length; i++)
		{
			if (IsPrintable(data[i]))
			{
				if (num == -1)
				{
					num = i;
				}
			}
			else
			{
				if (num == -1)
				{
					continue;
				}
				int num2 = i - num;
				if (num2 >= minLength)
				{
					list.Add(new StringHit(num, "ascii", Encoding.ASCII.GetString(data, num, num2)));
					if (list.Count >= maxCount)
					{
						return list;
					}
				}
				num = -1;
			}
		}
		if (num != -1)
		{
			int num3 = data.Length - num;
			if (num3 >= minLength)
			{
				list.Add(new StringHit(num, "ascii", Encoding.ASCII.GetString(data, num, num3)));
			}
		}
		return list;
	}

	private static List<StringHit> ExtractUtf16(byte[] data, int minLength, int maxCount, bool littleEndian)
	{
		List<StringHit> list = new List<StringHit>();
		int num = -1;
		for (int i = 0; i + 1 < data.Length; i += 2)
		{
			bool num2;
			if (!littleEndian)
			{
				if (data[i] == 0)
				{
					num2 = IsPrintable(data[i + 1]);
					goto IL_0035;
				}
			}
			else if (data[i + 1] == 0)
			{
				num2 = IsPrintable(data[i]);
				goto IL_0035;
			}
			goto IL_003f;
			IL_0035:
			if (num2)
			{
				if (num == -1)
				{
					num = i;
				}
				continue;
			}
			goto IL_003f;
			IL_003f:
			if (num == -1)
			{
				continue;
			}
			int num3 = i - num;
			if (num3 / 2 >= minLength)
			{
				string text = DecodeUtf16(data, num, num3, littleEndian);
				list.Add(new StringHit(num, littleEndian ? "utf16le" : "utf16be", text));
				if (list.Count >= maxCount)
				{
					return list;
				}
			}
			num = -1;
		}
		if (num != -1)
		{
			int num4 = data.Length - num;
			if (num4 / 2 >= minLength)
			{
				string text2 = DecodeUtf16(data, num, num4, littleEndian);
				list.Add(new StringHit(num, littleEndian ? "utf16le" : "utf16be", text2));
			}
		}
		return list;
	}

	private static string DecodeUtf16(byte[] data, int offset, int length, bool littleEndian)
	{
		return (littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(data, offset, length).TrimEnd('\0');
	}

	private static bool IsPrintable(byte value)
	{
		if (value >= 32)
		{
			return value <= 126;
		}
		return false;
	}
}
