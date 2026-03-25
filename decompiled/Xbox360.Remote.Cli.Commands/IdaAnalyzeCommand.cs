using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class IdaAnalyzeCommand : AsyncCommand<IdaAnalyzeCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--in <FILE>")]
		[Description("Input file to analyze (XEX/XBE/etc.).")]
		public string? Input { get; init; }

		[CommandOption("--ftp-path <PATH>")]
		[Description("Fetch the executable via FTP before analysis.")]
		public string? FtpPath { get; init; }

		[CommandOption("--running")]
		[Description("Use the running title XEX (resolved via XBDM + FTP).")]
		public bool Running { get; init; }

		[CommandOption("--out-db <FILE>")]
		[Description("Output IDA database path (.i64). Defaults to XeCLI cache.")]
		public string? OutputDatabase { get; init; }

		[CommandOption("--overwrite")]
		[Description("Overwrite an existing output database.")]
		public bool Overwrite { get; init; }

		[CommandOption("--path <DIR>")]
		[Description("IDA install directory override.")]
		public string? IdaPath { get; init; }

		[CommandOption("--user <DIR>")]
		[Description("IDAUSR override for plugins/loaders.")]
		public string? UserPath { get; init; }

		[CommandOption("--script-path <DIR>")]
		[Description("Override script path (default: ida_scripts next to rgh.exe).")]
		public string? ScriptPath { get; init; }

		[CommandOption("--log <FILE>")]
		[Description("IDA log file path. Defaults to XeCLI cache.")]
		public string? LogPath { get; init; }

		[CommandOption("--file-type <TYPE>")]
		[Description("Explicit IDA file type override.")]
		public string? FileType { get; init; }

		[CommandOption("--processor <NAME>")]
		[Description("Explicit processor override.")]
		public string? Processor { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		string text = await ResolveInputAsync(settings.Input, settings.FtpPath, settings.Running);
		if (string.IsNullOrWhiteSpace(text))
		{
			return 1;
		}
		CliConfig cliConfig = CliConfig.Load();
		string text2 = IdaHelpers.ResolveInstallPath(settings.IdaPath, cliConfig);
		if (string.IsNullOrWhiteSpace(text2))
		{
			AnsiConsole.MarkupLine("[red]IDA path not set.[/] Use `rgh ida config --path <dir>`.");
			return 1;
		}
		string text3 = IdaHelpers.FindBatchExecutable(text2);
		if (string.IsNullOrWhiteSpace(text3))
		{
			AnsiConsole.MarkupLine("[red]Unable to locate an IDA batch executable under[/] " + Markup.Escape(text2));
			return 1;
		}
		string text4 = IdaHelpers.ResolveUserPath(settings.UserPath, cliConfig) ?? IdaHelpers.DefaultUserDirectory;
		if (GhidraLoaderHelpers.IsXexFile(text) && !IdaHelpers.HasIdaxexLoader(text2, text4))
		{
			AnsiConsole.MarkupLine("[red]idaxex loader not found.[/] Install it into your IDA folder or IDAUSR before analyzing XEX files.");
			return 1;
		}
		string text5 = IdaHelpers.ResolveScriptsDirectory(settings.ScriptPath);
		string text6 = Path.Combine(text5, "analyze_xex.py");
		if (!File.Exists(text6))
		{
			AnsiConsole.MarkupLine("[red]IDA analyze script not found at[/] " + Markup.Escape(text6));
			return 1;
		}
		string text7 = settings.OutputDatabase ?? IdaHelpers.BuildDefaultDatabasePath(text);
		if (File.Exists(text7))
		{
			if (!settings.Overwrite)
			{
				AnsiConsole.MarkupLine("[red]Output database already exists.[/] Use [white]--overwrite[/] or choose a different [white]--out-db[/].");
				return 1;
			}
			File.Delete(text7);
		}
		Directory.CreateDirectory(Path.GetDirectoryName(text7) ?? IdaHelpers.EnsureCacheDirectory());
		string text8 = settings.LogPath ?? IdaHelpers.BuildDefaultLogPath(text, "analyze");
		Directory.CreateDirectory(Path.GetDirectoryName(text8) ?? IdaHelpers.EnsureCacheDirectory());
		string text9 = IdaHelpers.BuildDefaultResultPath(text, "analyze");
		if (File.Exists(text9))
		{
			File.Delete(text9);
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(text3)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		processStartInfo.ArgumentList.Add("-A");
		processStartInfo.ArgumentList.Add("-L" + text8);
		processStartInfo.ArgumentList.Add("-o" + text7);
		if (!string.IsNullOrWhiteSpace(settings.FileType))
		{
			processStartInfo.ArgumentList.Add("-T" + settings.FileType);
		}
		if (!string.IsNullOrWhiteSpace(settings.Processor))
		{
			processStartInfo.ArgumentList.Add("-p" + settings.Processor);
		}
		processStartInfo.ArgumentList.Add("-S" + text6);
		processStartInfo.ArgumentList.Add(text);
		IdaHelpers.ApplyIdaEnvironment(processStartInfo, text2, text4);
		processStartInfo.Environment["RGH_IDA_RESULT_JSON"] = text9;
		int num = await IdaHelpers.RunProcessStreamingAsync(processStartInfo, AnsiConsole.WriteLine, delegate(string line)
		{
			AnsiConsole.MarkupLine("[red]" + Markup.Escape(line) + "[/]");
		});
		if (num != 0)
		{
			return num;
		}
		int num2 = 0;
		int num3 = 0;
		if (File.Exists(text9))
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(text9));
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.TryGetProperty("segments", out var value))
			{
				num2 = value.GetInt32();
			}
			if (rootElement.TryGetProperty("functions", out value))
			{
				num3 = value.GetInt32();
			}
		}
		OperationFeedback.WriteSuccess("IDA analysis complete", $"[grey]Database:[/] [white]{Markup.Escape(text7)}[/]\n[grey]Log:[/] [white]{Markup.Escape(text8)}[/]\n[grey]Segments:[/] [springgreen3_1]{num2}[/]\n[grey]Functions:[/] [springgreen3_1]{num3}[/]");
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
			return await ReverseEngineeringInputHelpers.DownloadViaFtpAsync(ftpPath);
		}
		if (running)
		{
			string text = await ReverseEngineeringInputHelpers.ResolveRunningFtpPathAsync();
			if (string.IsNullOrWhiteSpace(text))
			{
				AnsiConsole.MarkupLine("[red]Unable to resolve running XEX via FTP path.[/]");
				return null;
			}
			return await ReverseEngineeringInputHelpers.DownloadViaFtpAsync(text);
		}
		AnsiConsole.MarkupLine("[red]Provide --in, --ftp-path, or --running.[/]");
		return null;
	}
}
