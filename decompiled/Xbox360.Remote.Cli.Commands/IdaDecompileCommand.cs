using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class IdaDecompileCommand : AsyncCommand<IdaDecompileCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--in <FILE>")]
		[Description("Input file to analyze (XEX/XBE/etc.).")]
		public string? Input { get; init; }

		[CommandOption("--ftp-path <PATH>")]
		[Description("Fetch the executable via FTP before decompilation.")]
		public string? FtpPath { get; init; }

		[CommandOption("--running")]
		[Description("Use the running title XEX (resolved via XBDM + FTP).")]
		public bool Running { get; init; }

		[CommandOption("--out <DIR>")]
		[Description("Output directory for decompiled C files.")]
		public string? Output { get; init; }

		[CommandOption("--max <N>")]
		[Description("Maximum number of functions to decompile.")]
		public int? MaxFunctions { get; init; }

		[CommandOption("--backend <MODE>")]
		[Description("Backend: auto, batch, or idalib.")]
		public string? Backend { get; init; }

		[CommandOption("--out-db <FILE>")]
		[Description("Persist the generated IDA database (batch backend only).")]
		public string? OutputDatabase { get; init; }

		[CommandOption("--keep-db")]
		[Description("Keep the generated cache database when --out-db is omitted (batch backend only).")]
		public bool KeepDatabase { get; init; }

		[CommandOption("--overwrite-db")]
		[Description("Overwrite an existing output database.")]
		public bool OverwriteDatabase { get; init; }

		[CommandOption("--path <DIR>")]
		[Description("IDA install directory override.")]
		public string? IdaPath { get; init; }

		[CommandOption("--python <EXE>")]
		[Description("Python interpreter override for idalib.")]
		public string? PythonPath { get; init; }

		[CommandOption("--user <DIR>")]
		[Description("IDAUSR override for plugins/loaders.")]
		public string? UserPath { get; init; }

		[CommandOption("--script-path <DIR>")]
		[Description("Override script path (default: ida_scripts next to rgh.exe).")]
		public string? ScriptPath { get; init; }

		[CommandOption("--log <FILE>")]
		[Description("IDA log file path (batch backend). Defaults to XeCLI cache.")]
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
		if (string.IsNullOrWhiteSpace(settings.Output))
		{
			AnsiConsole.MarkupLine("[red]--out is required.[/]");
			return 1;
		}
		string text = await ResolveInputAsync(settings.Input, settings.FtpPath, settings.Running);
		if (string.IsNullOrWhiteSpace(text))
		{
			return 1;
		}
		Directory.CreateDirectory(settings.Output);
		CliConfig cliConfig = CliConfig.Load();
		string text2 = IdaHelpers.ResolveInstallPath(settings.IdaPath, cliConfig);
		string text3 = IdaHelpers.ResolvePythonPath(settings.PythonPath, cliConfig);
		string text4 = IdaHelpers.ResolveUserPath(settings.UserPath, cliConfig);
		IdaBackend idaBackend = IdaHelpers.ResolvePreferredBackend(settings.Backend, cliConfig);
		if (idaBackend == IdaBackend.Auto)
		{
			idaBackend = await SelectAutomaticBackendAsync(text2, text3, text4);
		}
		if (idaBackend == IdaBackend.Idalib && (!string.IsNullOrWhiteSpace(settings.OutputDatabase) || settings.KeepDatabase))
		{
			AnsiConsole.MarkupLine("[red]--out-db and --keep-db are only supported with the batch backend.[/]");
			return 1;
		}
		if (idaBackend == IdaBackend.Batch)
		{
			return await ExecuteBatchAsync(text, settings, text2, text4);
		}
		return await ExecuteIdalibAsync(text, settings, text2, text3, text4);
	}

	private static async Task<int> ExecuteBatchAsync(string inputPath, Settings settings, string? idaHome, string? userPath)
	{
		if (string.IsNullOrWhiteSpace(idaHome))
		{
			AnsiConsole.MarkupLine("[red]IDA path not set.[/] Use `rgh ida config --path <dir>`.");
			return 1;
		}
		string text = IdaHelpers.FindBatchExecutable(idaHome);
		if (string.IsNullOrWhiteSpace(text))
		{
			AnsiConsole.MarkupLine("[red]Unable to locate an IDA batch executable under[/] " + Markup.Escape(idaHome));
			return 1;
		}
		if (GhidraLoaderHelpers.IsXexFile(inputPath) && !IdaHelpers.HasIdaxexLoader(idaHome, userPath ?? IdaHelpers.DefaultUserDirectory))
		{
			AnsiConsole.MarkupLine("[red]idaxex loader not found.[/] Install it into your IDA folder or IDAUSR before decompiling XEX files.");
			return 1;
		}
		string text2 = IdaHelpers.ResolveScriptsDirectory(settings.ScriptPath);
		string text3 = Path.Combine(text2, "decompile_xex.py");
		if (!File.Exists(text3))
		{
			AnsiConsole.MarkupLine("[red]IDA decompile script not found at[/] " + Markup.Escape(text3));
			return 1;
		}
		string text4 = settings.OutputDatabase ?? IdaHelpers.BuildDefaultDatabasePath(inputPath);
		bool flag = !string.IsNullOrWhiteSpace(settings.OutputDatabase);
		if (File.Exists(text4))
		{
			if (!settings.OverwriteDatabase)
			{
				AnsiConsole.MarkupLine("[red]Output database already exists.[/] Use [white]--overwrite-db[/] or choose a different [white]--out-db[/].");
				return 1;
			}
			File.Delete(text4);
		}
		Directory.CreateDirectory(Path.GetDirectoryName(text4) ?? IdaHelpers.EnsureCacheDirectory());
		string text5 = settings.LogPath ?? IdaHelpers.BuildDefaultLogPath(inputPath, "decompile");
		Directory.CreateDirectory(Path.GetDirectoryName(text5) ?? IdaHelpers.EnsureCacheDirectory());
		string text6 = IdaHelpers.BuildDefaultResultPath(inputPath, "decompile");
		if (File.Exists(text6))
		{
			File.Delete(text6);
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(text)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		processStartInfo.ArgumentList.Add("-A");
		processStartInfo.ArgumentList.Add("-L" + text5);
		processStartInfo.ArgumentList.Add("-o" + text4);
		if (!string.IsNullOrWhiteSpace(settings.FileType))
		{
			processStartInfo.ArgumentList.Add("-T" + settings.FileType);
		}
		if (!string.IsNullOrWhiteSpace(settings.Processor))
		{
			processStartInfo.ArgumentList.Add("-p" + settings.Processor);
		}
		processStartInfo.ArgumentList.Add("-S" + text3);
		processStartInfo.ArgumentList.Add(inputPath);
		IdaHelpers.ApplyIdaEnvironment(processStartInfo, idaHome, userPath);
		processStartInfo.Environment["RGH_IDA_OUT"] = settings.Output!;
		processStartInfo.Environment["RGH_IDA_RESULT_JSON"] = text6;
		if (settings.MaxFunctions.HasValue)
		{
			processStartInfo.Environment["RGH_IDA_MAX"] = settings.MaxFunctions.Value.ToString();
		}
		int num = await IdaHelpers.RunProcessStreamingAsync(processStartInfo, AnsiConsole.WriteLine, delegate(string line)
		{
			AnsiConsole.MarkupLine("[red]" + Markup.Escape(line) + "[/]");
		});
		if (num != 0)
		{
			return num;
		}
		(int attempted, int succeeded, int failed) = ReadDecompileSummary(text6);
		OperationFeedback.WriteSuccess("IDA decompile complete", $"[grey]Backend:[/] [white]batch[/]\n[grey]Output:[/] [white]{Markup.Escape(settings.Output!)}[/]\n[grey]Database:[/] [white]{Markup.Escape(text4)}[/]\n[grey]Log:[/] [white]{Markup.Escape(text5)}[/]\n[grey]Functions:[/] [springgreen3_1]{succeeded}[/] ok / [gold1]{failed}[/] failed / [white]{attempted}[/] attempted");
		if (!flag && !settings.KeepDatabase && failed == 0)
		{
			try
			{
				File.Delete(text4);
			}
			catch
			{
			}
		}
		return 0;
	}

	private static async Task<int> ExecuteIdalibAsync(string inputPath, Settings settings, string? idaHome, string? pythonPath, string? userPath)
	{
		if (string.IsNullOrWhiteSpace(idaHome))
		{
			AnsiConsole.MarkupLine("[red]IDA path not set.[/] Use `rgh ida config --path <dir>`.");
			return 1;
		}
		if (string.IsNullOrWhiteSpace(pythonPath))
		{
			AnsiConsole.MarkupLine("[red]Python path not set for idalib.[/] Use `rgh ida config --python <exe>` or pass [white]--python[/].");
			return 1;
		}
		if (!IdaHelpers.HasIdalibFiles(idaHome))
		{
			AnsiConsole.MarkupLine("[red]idalib files were not found under the configured IDA install.[/]");
			return 1;
		}
		if (GhidraLoaderHelpers.IsXexFile(inputPath) && !IdaHelpers.HasIdaxexLoader(idaHome, userPath ?? IdaHelpers.DefaultUserDirectory))
		{
			AnsiConsole.MarkupLine("[red]idaxex loader not found.[/] Install it into your IDA folder or IDAUSR before decompiling XEX files.");
			return 1;
		}
		if (!await IdaHelpers.EnsureIdalibActivatedAsync(pythonPath, idaHome, userPath))
		{
			AnsiConsole.MarkupLine("[red]Failed to activate idalib for the configured IDA install.[/]");
			return 1;
		}
		if (!await IdaHelpers.CanImportIdaproAsync(pythonPath, idaHome, userPath))
		{
			AnsiConsole.MarkupLine("[red]Python could not import `idapro`.[/] Verify idalib is installed and activated.");
			return 1;
		}
		string text = IdaHelpers.ResolveScriptsDirectory(settings.ScriptPath);
		string text2 = Path.Combine(text, "idalib_decompile.py");
		if (!File.Exists(text2))
		{
			AnsiConsole.MarkupLine("[red]idalib decompile script not found at[/] " + Markup.Escape(text2));
			return 1;
		}
		string text3 = IdaHelpers.BuildDefaultJsonPath(inputPath, "idalib");
		if (File.Exists(text3))
		{
			File.Delete(text3);
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(pythonPath)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		processStartInfo.ArgumentList.Add(text2);
		processStartInfo.ArgumentList.Add("--file");
		processStartInfo.ArgumentList.Add(inputPath);
		processStartInfo.ArgumentList.Add("--out");
		processStartInfo.ArgumentList.Add(settings.Output!);
		processStartInfo.ArgumentList.Add("--result-json");
		processStartInfo.ArgumentList.Add(text3);
		if (settings.MaxFunctions.HasValue)
		{
			processStartInfo.ArgumentList.Add("--max");
			processStartInfo.ArgumentList.Add(settings.MaxFunctions.Value.ToString());
		}
		IdaHelpers.ApplyIdaEnvironment(processStartInfo, idaHome, userPath);
		int num = await IdaHelpers.RunProcessStreamingAsync(processStartInfo, AnsiConsole.WriteLine, delegate(string line)
		{
			AnsiConsole.MarkupLine("[red]" + Markup.Escape(line) + "[/]");
		});
		if (num != 0)
		{
			return num;
		}
		(int attempted, int succeeded, int failed) = ReadDecompileSummary(text3);
		OperationFeedback.WriteSuccess("IDA decompile complete", $"[grey]Backend:[/] [white]idalib[/]\n[grey]Output:[/] [white]{Markup.Escape(settings.Output!)}[/]\n[grey]Functions:[/] [springgreen3_1]{succeeded}[/] ok / [gold1]{failed}[/] failed / [white]{attempted}[/] attempted");
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

	private static async Task<IdaBackend> SelectAutomaticBackendAsync(string? idaHome, string? pythonPath, string? userPath)
	{
		if (!string.IsNullOrWhiteSpace(idaHome) && !string.IsNullOrWhiteSpace(pythonPath) && IdaHelpers.HasIdalibFiles(idaHome))
		{
			try
			{
				if (await IdaHelpers.CanImportIdaproAsync(pythonPath, idaHome, userPath))
				{
					return IdaBackend.Idalib;
				}
			}
			catch
			{
			}
		}
		return IdaBackend.Batch;
	}

	private static (int Attempted, int Succeeded, int Failed) ReadDecompileSummary(string path)
	{
		if (!File.Exists(path))
		{
			return (0, 0, 0);
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
		JsonElement rootElement = jsonDocument.RootElement;
		int num = rootElement.TryGetProperty("attempted", out var value) ? value.GetInt32() : 0;
		int num2 = rootElement.TryGetProperty("succeeded", out value) ? value.GetInt32() : 0;
		int num3 = rootElement.TryGetProperty("failed", out value) ? value.GetInt32() : 0;
		return (num, num2, num3);
	}
}
