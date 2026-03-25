using System;
using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class IdaConfigCommand : Command<IdaConfigCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <DIR>")]
		[LocalizedDescription("IDA install directory.")]
		public string? Path { get; init; }

		[CommandOption("--python <EXE>")]
		[LocalizedDescription("Python interpreter for idalib workflows.")]
		public string? PythonPath { get; init; }

		[CommandOption("--user <DIR>")]
		[LocalizedDescription("IDAUSR override for plugins/loaders.")]
		public string? UserPath { get; init; }

		[CommandOption("--backend <MODE>")]
		[LocalizedDescription("Preferred backend: auto, batch, or idalib.")]
		public string? Backend { get; init; }

		[CommandOption("--clear")]
		[LocalizedDescription("Clear stored IDA settings.")]
		public bool Clear { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		CliConfig cliConfig = CliConfig.Load();
		IdaBackend backend = IdaBackend.Auto;
		if (settings.Clear)
		{
			cliConfig.IdaPath = null;
			cliConfig.IdaPythonPath = null;
			cliConfig.IdaUserPath = null;
			cliConfig.IdaPreferredBackend = null;
			cliConfig.Save();
			AnsiConsole.MarkupLine("[green]IDA settings cleared.[/]");
			return 0;
		}
		if (!string.IsNullOrWhiteSpace(settings.Backend) && !IdaHelpers.TryParseBackend(settings.Backend, out backend))
		{
			AnsiConsole.MarkupLine("[red]Invalid backend.[/] Use [white]auto[/], [white]batch[/], or [white]idalib[/].");
			return 1;
		}
		bool flag = false;
		if (!string.IsNullOrWhiteSpace(settings.Path))
		{
			cliConfig.IdaPath = settings.Path;
			flag = true;
		}
		if (!string.IsNullOrWhiteSpace(settings.PythonPath))
		{
			cliConfig.IdaPythonPath = settings.PythonPath;
			flag = true;
		}
		if (!string.IsNullOrWhiteSpace(settings.UserPath))
		{
			cliConfig.IdaUserPath = settings.UserPath;
			flag = true;
		}
		if (!string.IsNullOrWhiteSpace(settings.Backend))
		{
			cliConfig.IdaPreferredBackend = IdaHelpers.FormatBackend(backend);
			flag = true;
		}
		if (flag)
		{
			cliConfig.Save();
			AnsiConsole.MarkupLine("[green]IDA settings updated.[/]");
			return 0;
		}
		string text = cliConfig.IdaPath ?? Environment.GetEnvironmentVariable("IDADIR") ?? "unknown";
		string text2 = cliConfig.IdaPythonPath ?? "unknown";
		string text3 = cliConfig.IdaUserPath ?? IdaHelpers.DefaultUserDirectory;
		string text4 = cliConfig.IdaPreferredBackend ?? "auto";
		AnsiConsole.MarkupLine("[green]IDA:[/] " + Markup.Escape(text));
		AnsiConsole.MarkupLine("[green]Python:[/] " + Markup.Escape(text2));
		AnsiConsole.MarkupLine("[green]IDAUSR:[/] " + Markup.Escape(text3));
		AnsiConsole.MarkupLine("[green]Backend:[/] " + Markup.Escape(text4));
		return 0;
	}
}
