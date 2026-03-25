using System.ComponentModel;
using System.IO;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.God;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GodInfoCommand : Command<GodInfoCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<ISO>")]
		[LocalizedDescription("Path to the ISO image.")]
		public string IsoPath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		string fullPath = Path.GetFullPath(settings.IsoPath);
		if (!File.Exists(fullPath))
		{
			AnsiConsole.MarkupLine("[red]ISO file not found.[/]");
			return 1;
		}
		using FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		TitleInfo titleInfo = TitleInfo.FromImage(IsoReader.Read(stream));
		TitleIdDatabase.Instance.TryResolve(titleInfo.ExecutionInfo.TitleId, null, out TitleIdEntry entry);
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				Iso = fullPath,
				TitleId = $"0x{titleInfo.ExecutionInfo.TitleId:X8}",
				MediaId = $"0x{titleInfo.ExecutionInfo.MediaId:X8}",
				ContentType = titleInfo.ContentType.ToString(),
				Name = entry?.Name,
				Region = entry?.Region,
				Type = entry?.Type
			});
			return 0;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]ISO Details[/]").RuleStyle("grey"));
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[grey]Field[/]"));
		table.AddColumn(new TableColumn("[white]Value[/]"));
		table.AddRow("[grey]ISO[/]", "[cyan]" + Markup.Escape(fullPath) + "[/]");
		table.AddRow("[grey]Title ID[/]", $"[cyan]0x{titleInfo.ExecutionInfo.TitleId:X8}[/]");
		table.AddRow("[grey]Media ID[/]", $"[cyan]0x{titleInfo.ExecutionInfo.MediaId:X8}[/]");
		table.AddRow("[grey]Content Type[/]", $"[green]{titleInfo.ContentType}[/]");
		table.AddRow("[grey]Title Name[/]", ((object)entry != null && entry.Name != null) ? ("[green]" + Markup.Escape(entry.Name) + "[/]") : "[grey]unknown[/]");
		table.AddRow("[grey]Region[/]", ((object)entry != null && entry.Region != null) ? ("[grey]" + Markup.Escape(entry.Region) + "[/]") : "[grey]unknown[/]");
		table.AddRow("[grey]Type[/]", ((object)entry != null && entry.Type != null) ? ("[grey]" + Markup.Escape(entry.Type) + "[/]") : "[grey]unknown[/]");
		AnsiConsole.Write(table);
		return 0;
	}
}
