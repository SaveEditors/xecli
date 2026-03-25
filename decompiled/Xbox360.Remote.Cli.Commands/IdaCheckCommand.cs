using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class IdaCheckCommand : AsyncCommand<IdaCheckCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <DIR>")]
		[Description("IDA install directory override.")]
		public string? Path { get; init; }

		[CommandOption("--python <EXE>")]
		[Description("Python interpreter override for idalib checks.")]
		public string? PythonPath { get; init; }

		[CommandOption("--user <DIR>")]
		[Description("IDAUSR override for loader discovery.")]
		public string? UserPath { get; init; }

		[CommandOption("--json")]
		[Description("Output JSON.")]
		public bool Json { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		CliConfig cliConfig = CliConfig.Load();
		string text = IdaHelpers.ResolveInstallPath(settings.Path, cliConfig);
		string text2 = IdaHelpers.ResolvePythonPath(settings.PythonPath, cliConfig);
		string text3 = IdaHelpers.ResolveUserPath(settings.UserPath, cliConfig) ?? IdaHelpers.DefaultUserDirectory;
		string text4 = text ?? "unknown";
		string text5 = text2 ?? "unconfigured";
		string text6 = IdaHelpers.FindBatchExecutable(text) ?? "missing";
		string text7 = IdaHelpers.FindActivationScript(text) ?? "missing";
		bool flag = IdaHelpers.HasIdaxexLoader(text, text3);
		bool flag2 = IdaHelpers.HasIdalibFiles(text);
		bool? flag3 = null;
		if (!string.IsNullOrWhiteSpace(text2))
		{
			try
			{
				flag3 = await IdaHelpers.CanImportIdaproAsync(text2, text, text3);
			}
			catch
			{
				flag3 = false;
			}
		}
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				InstallPath = text4,
				BatchExecutable = text6,
				ActivationScript = text7,
				UserPath = text3,
				Python = text5,
				PreferredBackend = cliConfig.IdaPreferredBackend ?? "auto",
				Idaxex = flag,
				IdalibFiles = flag2,
				IdalibImport = flag3
			});
			return 0;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]IDA Check[/]").RuleStyle("grey"));
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[green]Field[/]"));
		table.AddColumn(new TableColumn("[yellow]Value[/]"));
		table.AddRow("Install", Markup.Escape(text4));
		table.AddRow("Batch EXE", File.Exists(text6) ? ("[springgreen3_1]" + Markup.Escape(text6) + "[/]") : "[red]missing[/]");
		table.AddRow("Activation Script", File.Exists(text7) ? ("[springgreen3_1]" + Markup.Escape(text7) + "[/]") : "[red]missing[/]");
		table.AddRow("IDAUSR", Markup.Escape(text3));
		table.AddRow("Python", Markup.Escape(text5));
		table.AddRow("Preferred Backend", Markup.Escape(cliConfig.IdaPreferredBackend ?? "auto"));
		table.AddRow("idaxex Loader", flag ? "[green]present[/]" : "[red]missing[/]");
		table.AddRow("idalib Files", flag2 ? "[green]present[/]" : "[red]missing[/]");
		table.AddRow("idalib Import", flag3.HasValue ? (flag3.Value ? "[green]ok[/]" : "[red]failed[/]") : "[grey]not checked[/]");
		AnsiConsole.Write(table);
		return 0;
	}
}
