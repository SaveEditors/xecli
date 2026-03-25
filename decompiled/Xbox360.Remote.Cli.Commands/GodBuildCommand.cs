using System;
using System.ComponentModel;
using System.IO;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.God;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GodBuildCommand : Command<GodBuildCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<ISO>")]
		[LocalizedDescription("Path to the ISO image.")]
		public string IsoPath { get; init; } = string.Empty;

		[CommandArgument(1, "<DEST>")]
		[LocalizedDescription("Output folder for the GOD package.")]
		public string DestDir { get; init; } = string.Empty;

		[CommandOption("--trim <MODE>")]
		[LocalizedDescription("Trim unused space: end or none (default: end).")]
		public string? Trim { get; init; }

		[CommandOption("-j|--threads <N>")]
		[LocalizedDescription("Parallel workers for part files (default: 1).")]
		public int Threads { get; init; } = 1;

		[CommandOption("--title <NAME>")]
		[LocalizedDescription("Override the package display title.")]
		public string? Title { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		string fullPath = Path.GetFullPath(settings.IsoPath);
		if (!File.Exists(fullPath))
		{
			AnsiConsole.MarkupLine("[red]ISO file not found.[/]");
			return 1;
		}
		string fullPath2 = Path.GetFullPath(settings.DestDir);
		TrimMode trimMode = ParseTrimMode(settings.Trim);
		if (trimMode == TrimMode.None && !string.Equals(settings.Trim, "none", StringComparison.OrdinalIgnoreCase) && settings.Trim != null)
		{
			AnsiConsole.MarkupLine("[red]Invalid trim mode. Use end or none.[/]");
			return 1;
		}
		AnsiConsole.MarkupLine("[cyan]Reading ISO metadata...[/]");
		object gate = new object();
		GodConversionResult godConversionResult = GodConverter.Convert(fullPath, fullPath2, new GodConvertOptions(trimMode, Math.Max(1, settings.Threads), settings.Title), ReportProgress);
		AnsiConsole.MarkupLine("[green]Package header written.[/]");
		AnsiConsole.Write(new Rule("[bold deepskyblue1]GOD Output[/]").RuleStyle("grey"));
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[grey]Field[/]"));
		table.AddColumn(new TableColumn("[white]Value[/]"));
		table.AddRow("[grey]Title ID[/]", $"[cyan]0x{godConversionResult.ExecutionInfo.TitleId:X8}[/]");
		table.AddRow("[grey]Media ID[/]", $"[cyan]0x{godConversionResult.ExecutionInfo.MediaId:X8}[/]");
		table.AddRow("[grey]Content Type[/]", $"[green]{godConversionResult.ContentType}[/]");
		table.AddRow("[grey]Parts[/]", godConversionResult.PartCount.ToString());
		table.AddRow("[grey]Data Dir[/]", "[green]" + Markup.Escape(godConversionResult.OutputDir) + "[/]");
		table.AddRow("[grey]Package[/]", "[green]" + Markup.Escape(godConversionResult.ConHeaderPath) + "[/]");
		AnsiConsole.Write(table);
		return 0;
		void ReportProgress(int done, int total)
		{
			lock (gate)
			{
				AnsiConsole.MarkupLine($"[grey]Parts[/] {done}/{total}");
			}
		}
	}

	private static TrimMode ParseTrimMode(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return TrimMode.FromEnd;
		}
		if (value.Equals("end", StringComparison.OrdinalIgnoreCase))
		{
			return TrimMode.FromEnd;
		}
		value.Equals("none", StringComparison.OrdinalIgnoreCase);
		return TrimMode.None;
	}
}
