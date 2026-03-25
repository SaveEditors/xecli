using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class IdaVerifyCommand : Command<IdaVerifyCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--dir <DIR>")]
		[LocalizedDescription("Directory containing decompiled output to verify.")]
		public string? Directory { get; init; }

		[CommandOption("--pattern <REGEX>")]
		[LocalizedDescription("Regex pattern to flag (default: bad instruction|JUMPOUT|decompilation failed|<UNKNOWN>).")]
		public string Pattern { get; init; } = "bad instruction|JUMPOUT|decompilation failed|<UNKNOWN>";

		[CommandOption("--ext <EXT>")]
		[LocalizedDescription("File extension to scan (default: .c).")]
		public string Extension { get; init; } = ".c";

		[CommandOption("--max <N>")]
		[LocalizedDescription("Maximum matching files to display (default: 25).")]
		public int? MaxResults { get; init; }

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Directory))
		{
			AnsiConsole.MarkupLine("[red]--dir is required.[/]");
			return 1;
		}
		if (!System.IO.Directory.Exists(settings.Directory))
		{
			AnsiConsole.MarkupLine("[red]Directory not found.[/]");
			return 1;
		}
		string text = settings.Extension;
		if (!text.StartsWith(".", StringComparison.Ordinal))
		{
			text = "." + text;
		}
		Regex regex;
		try
		{
			regex = new Regex(settings.Pattern, RegexOptions.IgnoreCase);
		}
		catch (ArgumentException ex)
		{
			AnsiConsole.MarkupLine("[red]Invalid regex:[/] " + Markup.Escape(ex.Message));
			return 1;
		}
		int num = Math.Max(1, settings.MaxResults ?? 25);
		int num2 = 0;
		int num3 = 0;
		List<(string, string)> list = new List<(string, string)>();
		foreach (string item in System.IO.Directory.EnumerateFiles(settings.Directory, "*" + text, SearchOption.AllDirectories))
		{
			num2++;
			foreach (string item2 in File.ReadLines(item))
			{
				if (regex.IsMatch(item2))
				{
					num3++;
					if (list.Count < num)
					{
						list.Add((item, item2.Trim()));
					}
					break;
				}
			}
		}
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				Directory = settings.Directory,
				Files = num2,
				Matches = num3,
				Examples = list.Select(((string File, string Line) m) => new { m.File, m.Line }).ToList()
			});
			return 0;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]IDA Verify[/]").RuleStyle("grey"));
		AnsiConsole.MarkupLine($"[grey]Scanned:[/] {num2} files");
		AnsiConsole.MarkupLine($"[grey]Flagged:[/] {num3} files");
		if (num3 == 0)
		{
			AnsiConsole.MarkupLine("[green]No flagged files found.[/]");
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[green]File[/]"));
		table.AddColumn(new TableColumn("[yellow]Match[/]"));
		foreach (var (text2, text3) in list)
		{
			table.AddRow(Markup.Escape(text2), Markup.Escape(text3));
		}
		AnsiConsole.Write(table);
		return 0;
	}
}
