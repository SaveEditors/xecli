using System;
using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GhidraConfigCommand : Command<GhidraConfigCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <DIR>")]
		[LocalizedDescription("Ghidra install directory (contains support/analyzeHeadless.bat).")]
		public string? Path { get; init; }

		[CommandOption("--java <DIR>")]
		[LocalizedDescription("JAVA_HOME to use for Ghidra.")]
		public string? JavaPath { get; init; }

		[CommandOption("--projects <DIR>")]
		[LocalizedDescription("Default Ghidra projects directory.")]
		public string? ProjectsPath { get; init; }

		[CommandOption("--clear")]
		[LocalizedDescription("Clear stored Ghidra settings.")]
		public bool Clear { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (settings.Clear)
		{
			cliConfig.GhidraPath = null;
			cliConfig.GhidraJavaPath = null;
			cliConfig.GhidraProjectsPath = null;
			cliConfig.Save();
			AnsiConsole.MarkupLine("[green]Ghidra settings cleared.[/]");
			return 0;
		}
		bool flag = false;
		if (!string.IsNullOrWhiteSpace(settings.Path))
		{
			cliConfig.GhidraPath = settings.Path;
			flag = true;
		}
		if (!string.IsNullOrWhiteSpace(settings.JavaPath))
		{
			cliConfig.GhidraJavaPath = settings.JavaPath;
			flag = true;
		}
		if (!string.IsNullOrWhiteSpace(settings.ProjectsPath))
		{
			cliConfig.GhidraProjectsPath = settings.ProjectsPath;
			flag = true;
		}
		if (flag)
		{
			cliConfig.Save();
			AnsiConsole.MarkupLine("[green]Ghidra settings updated.[/]");
			return 0;
		}
		string text = cliConfig.GhidraPath ?? Environment.GetEnvironmentVariable("GHIDRA_HOME") ?? "unknown";
		string text2 = cliConfig.GhidraJavaPath ?? Environment.GetEnvironmentVariable("JAVA_HOME") ?? "unknown";
		string text3 = cliConfig.GhidraProjectsPath ?? "unknown";
		AnsiConsole.MarkupLine("[green]Ghidra:[/] " + Markup.Escape(text));
		AnsiConsole.MarkupLine("[green]JAVA_HOME:[/] " + Markup.Escape(text2));
		AnsiConsole.MarkupLine("[green]Projects:[/] " + Markup.Escape(text3));
		return 0;
	}
}
