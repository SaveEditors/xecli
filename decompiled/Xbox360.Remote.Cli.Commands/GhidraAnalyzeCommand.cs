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

public sealed class GhidraAnalyzeCommand : AsyncCommand<GhidraAnalyzeCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--in <FILE>")]
		[LocalizedDescription("Input file to analyze (XEX).")]
		public string? Input { get; init; }

		[CommandOption("--ftp-path <PATH>")]
		[LocalizedDescription("Fetch the XEX via FTP before analysis (e.g. /Hdd1/Aurora/Aurora.xex).")]
		public string? FtpPath { get; init; }

		[CommandOption("--running")]
		[LocalizedDescription("Use the running title XEX (resolved via XBDM + FTP).")]
		public bool Running { get; init; }

		[CommandOption("--project <NAME>")]
		[LocalizedDescription("Project name (default: <file>_headless).")]
		public string? ProjectName { get; init; }

		[CommandOption("--projects <DIR>")]
		[LocalizedDescription("Project root directory override.")]
		public string? ProjectsPath { get; init; }

		[CommandOption("--path <DIR>")]
		[LocalizedDescription("Ghidra install directory override.")]
		public string? GhidraPath { get; init; }

		[CommandOption("--java <DIR>")]
		[LocalizedDescription("JAVA_HOME override.")]
		public string? JavaPath { get; init; }

		[CommandOption("--loader <NAME>")]
		[LocalizedDescription("Explicit loader name.")]
		public string? Loader { get; init; }

		[CommandOption("--timeout <SEC>")]
		[LocalizedDescription("Analysis timeout per file (seconds, default: 5000).")]
		public int? TimeoutSeconds { get; init; } = 5000;

		[CommandOption("--delete-project")]
		[LocalizedDescription("Delete the project before import.")]
		public bool DeleteProject { get; init; }

		[CommandOption("--overwrite")]
		[LocalizedDescription("Overwrite existing file in project.")]
		public bool Overwrite { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
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
		string value = settings.JavaPath ?? cliConfig.GhidraJavaPath ?? Environment.GetEnvironmentVariable("JAVA_HOME");
		string text4 = Quote(text3) + " " + string.Join(" ", list.Select(Quote));
		ProcessStartInfo processStartInfo = new ProcessStartInfo("cmd.exe", "/c " + text4)
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
		string text5 = Path.Combine(CliPaths.CachePath, "ghidra_user");
		Directory.CreateDirectory(text5);
		processStartInfo.Environment["GHIDRA_USER_DIR"] = text5;
		processStartInfo.Environment["APPDATA"] = text5;
		processStartInfo.Environment["LOCALAPPDATA"] = text5;
		processStartInfo.Environment["USERPROFILE"] = text5;
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
