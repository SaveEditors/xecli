using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GhidraDecompileCommand : AsyncCommand<GhidraDecompileCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--in <FILE>")]
		[Description("Input file to analyze (XEX).")]
		public string? Input { get; init; }

		[CommandOption("--ftp-path <PATH>")]
		[Description("Fetch the XEX via FTP before analysis (e.g. /Hdd1/Aurora/Aurora.xex).")]
		public string? FtpPath { get; init; }

		[CommandOption("--running")]
		[Description("Use the running title XEX (resolved via XBDM + FTP).")]
		public bool Running { get; init; }

		[CommandOption("--out <DIR>")]
		[Description("Output directory for decompiled C files.")]
		public string? Output { get; init; }

		[CommandOption("--max <N>")]
		[Description("Maximum number of functions to decompile (default: all).")]
		public int? MaxFunctions { get; init; }

		[CommandOption("--func-timeout <SEC>")]
		[Description("Decompile timeout per function (seconds, default: 5000).")]
		public int? FunctionTimeoutSeconds { get; init; } = 5000;

		[CommandOption("--project <NAME>")]
		[Description("Project name (default: <file>_headless).")]
		public string? ProjectName { get; init; }

		[CommandOption("--projects <DIR>")]
		[Description("Project root directory override.")]
		public string? ProjectsPath { get; init; }

		[CommandOption("--path <DIR>")]
		[Description("Ghidra install directory override.")]
		public string? GhidraPath { get; init; }

		[CommandOption("--java <DIR>")]
		[Description("JAVA_HOME override.")]
		public string? JavaPath { get; init; }

		[CommandOption("--loader <NAME>")]
		[Description("Explicit loader name.")]
		public string? Loader { get; init; }

		[CommandOption("--timeout <SEC>")]
		[Description("Analysis timeout per file (seconds, default: 5000).")]
		public int? TimeoutSeconds { get; init; } = 5000;

		[CommandOption("--delete-project")]
		[Description("Delete the project before import.")]
		public bool DeleteProject { get; init; }

		[CommandOption("--overwrite")]
		[Description("Overwrite existing file in project.")]
		public bool Overwrite { get; init; }

		[CommandOption("--script-path <DIR>")]
		[Description("Override script path (default: ghidra_scripts next to rgh.exe).")]
		public string? ScriptPath { get; init; }
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
		CliConfig cliConfig = CliConfig.Load();
		string text2 = settings.GhidraPath ?? cliConfig.GhidraPath ?? Environment.GetEnvironmentVariable("GHIDRA_HOME");
		if (string.IsNullOrWhiteSpace(text2))
		{
			AnsiConsole.MarkupLine("[red]Ghidra path not set. Use `rgh ghidra config --path <dir>`.[/]");
			return 1;
		}
		string text3 = Path.Combine(text2, "support", "analyzeHeadless.bat");
		if (!File.Exists(text3))
		{
			AnsiConsole.MarkupLine("[red]analyzeHeadless.bat not found at[/] " + Markup.Escape(text3));
			return 1;
		}
		string text4 = settings.ScriptPath ?? Path.Combine(AppContext.BaseDirectory, "ghidra_scripts");
		string text5 = Path.Combine(text4, "DecompileAllToC.java");
		if (!File.Exists(text5))
		{
			AnsiConsole.MarkupLine("[red]Decompile script not found at[/] " + Markup.Escape(text5));
			return 1;
		}
		string item = settings.ProjectsPath ?? cliConfig.GhidraProjectsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ghidra_projects");
		string item2 = settings.ProjectName ?? (Path.GetFileNameWithoutExtension(text) + "_headless");
		List<string> list = new List<string> { item, item2 };
		if (settings.DeleteProject)
		{
			list.Add("-deleteProject");
		}
		if (settings.Overwrite)
		{
			list.Add("-overwrite");
		}
		list.Add("-import");
		list.Add(text);
		if (!string.IsNullOrWhiteSpace(settings.Loader))
		{
			list.Add("-loader");
			list.Add(settings.Loader);
		}
		else if (GhidraLoaderHelpers.IsXexFile(text) && GhidraLoaderHelpers.HasXexLoader(text2))
		{
			AnsiConsole.MarkupLine("[grey]XEX loader detected; using auto-detect.[/]");
		}
		if (settings.TimeoutSeconds.HasValue)
		{
			list.Add("-analysisTimeoutPerFile");
			list.Add(settings.TimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture));
		}
		list.Add("-scriptPath");
		list.Add(text4);
		list.Add("-postScript");
		list.Add("DecompileAllToC.java");
		list.Add(settings.Output);
		if (settings.MaxFunctions.HasValue)
		{
			list.Add(settings.MaxFunctions.Value.ToString(CultureInfo.InvariantCulture));
		}
		if (settings.FunctionTimeoutSeconds.HasValue)
		{
			list.Add(settings.FunctionTimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture));
		}
		string value = settings.JavaPath ?? cliConfig.GhidraJavaPath ?? Environment.GetEnvironmentVariable("JAVA_HOME");
		string text6 = Quote(text3) + " " + string.Join(" ", list.Select(Quote));
		ProcessStartInfo processStartInfo = new ProcessStartInfo("cmd.exe", "/c " + text6)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		if (!string.IsNullOrWhiteSpace(value))
		{
			processStartInfo.Environment["JAVA_HOME"] = value;
		}
		string text7 = Path.Combine(CliPaths.CachePath, "ghidra_user");
		Directory.CreateDirectory(text7);
		processStartInfo.Environment["GHIDRA_USER_DIR"] = text7;
		processStartInfo.Environment["APPDATA"] = text7;
		processStartInfo.Environment["LOCALAPPDATA"] = text7;
		processStartInfo.Environment["USERPROFILE"] = text7;
		using Process proc = new Process
		{
			StartInfo = processStartInfo
		};
		proc.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				AnsiConsole.WriteLine(e.Data);
			}
		};
		proc.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				AnsiConsole.MarkupLine("[red]" + Markup.Escape(e.Data) + "[/]");
			}
		};
		proc.Start();
		proc.BeginOutputReadLine();
		proc.BeginErrorReadLine();
		await proc.WaitForExitAsync();
		return proc.ExitCode;
	}

	private static string Quote(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "\"\"";
		}
		if (!value.Contains(' ') && !value.Contains('"'))
		{
			return value;
		}
		return "\"" + value.Replace("\"", "\\\"") + "\"";
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
}
