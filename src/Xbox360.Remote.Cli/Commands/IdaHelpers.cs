using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xbox360.Remote.Cli.Commands;

internal enum IdaBackend
{
	Auto,
	Batch,
	Idalib
}

internal static class IdaHelpers
{
	private static readonly string[] BatchExecutableNames = new string[4] { "idat64.exe", "idat.exe", "ida64.exe", "ida.exe" };

	private static readonly string[] LoaderPatterns = new string[3] { "idaxex*.dll", "idaloader*.dll", "*xex*.dll" };

	private static readonly string[] LoaderSearchRoots = new string[3] { "loaders", "plugins", "." };

	public static string DefaultUserDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hex-Rays", "IDA Pro");

	public static string? ResolveInstallPath(string? overridePath, CliConfig cliConfig)
	{
		return NormalizeNullablePath(overridePath) ?? NormalizeNullablePath(cliConfig.IdaPath) ?? NormalizeNullablePath(Environment.GetEnvironmentVariable("IDADIR"));
	}

	public static string? ResolvePythonPath(string? overridePath, CliConfig cliConfig)
	{
		return NormalizeNullablePath(overridePath) ?? NormalizeNullablePath(cliConfig.IdaPythonPath);
	}

	public static string? ResolveUserPath(string? overridePath, CliConfig cliConfig)
	{
		return NormalizeNullablePath(overridePath) ?? NormalizeNullablePath(cliConfig.IdaUserPath);
	}

	public static IdaBackend ResolvePreferredBackend(string? overrideBackend, CliConfig cliConfig)
	{
		if (TryParseBackend(overrideBackend, out var backend))
		{
			return backend;
		}
		if (TryParseBackend(cliConfig.IdaPreferredBackend, out backend))
		{
			return backend;
		}
		return IdaBackend.Auto;
	}

	public static bool TryParseBackend(string? value, out IdaBackend backend)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			backend = IdaBackend.Auto;
			return true;
		}
		if (text.Equals("batch", StringComparison.OrdinalIgnoreCase))
		{
			backend = IdaBackend.Batch;
			return true;
		}
		if (text.Equals("idalib", StringComparison.OrdinalIgnoreCase))
		{
			backend = IdaBackend.Idalib;
			return true;
		}
		backend = IdaBackend.Auto;
		return false;
	}

	public static string FormatBackend(IdaBackend backend)
	{
		return backend switch
		{
			IdaBackend.Batch => "batch", 
			IdaBackend.Idalib => "idalib", 
			_ => "auto", 
		};
	}

	public static string? FindBatchExecutable(string? idaHome)
	{
		if (string.IsNullOrWhiteSpace(idaHome) || !Directory.Exists(idaHome))
		{
			return null;
		}
		foreach (string batchExecutableName in BatchExecutableNames)
		{
			string text = Path.Combine(idaHome, batchExecutableName);
			if (File.Exists(text))
			{
				return text;
			}
		}
		try
		{
			return Directory.EnumerateFiles(idaHome, "ida*.exe", SearchOption.TopDirectoryOnly).OrderBy(ScoreExecutablePath).FirstOrDefault();
		}
		catch
		{
			return null;
		}
	}

	public static string? FindActivationScript(string? idaHome)
	{
		if (string.IsNullOrWhiteSpace(idaHome) || !Directory.Exists(idaHome))
		{
			return null;
		}
		string[] array = new string[2]
		{
			Path.Combine(idaHome, "py-activate-idalib.py"),
			Path.Combine(idaHome, "idalib", "python", "py-activate-idalib.py")
		};
		foreach (string text in array)
		{
			if (File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}

	public static bool HasIdalibFiles(string? idaHome)
	{
		if (string.IsNullOrWhiteSpace(idaHome) || !Directory.Exists(idaHome))
		{
			return false;
		}
		if (FindActivationScript(idaHome) != null)
		{
			return true;
		}
		return Directory.Exists(Path.Combine(idaHome, "idalib", "python"));
	}

	public static bool HasIdaxexLoader(string? idaHome, string? userPaths)
	{
		if (SearchForLoader(idaHome))
		{
			return true;
		}
		foreach (string item in ExpandUserPaths(string.IsNullOrWhiteSpace(userPaths) ? DefaultUserDirectory : userPaths))
		{
			if (SearchForLoader(item))
			{
				return true;
			}
		}
		return false;
	}

	public static string ResolveScriptsDirectory(string? overridePath)
	{
		return NormalizeNullablePath(overridePath) ?? Path.Combine(AppContext.BaseDirectory, "ida_scripts");
	}

	public static string EnsureCacheDirectory()
	{
		string text = Path.Combine(CliPaths.CachePath, "ida");
		Directory.CreateDirectory(text);
		return text;
	}

	public static string BuildDefaultDatabasePath(string inputPath)
	{
		return Path.Combine(EnsureCacheDirectory(), SanitizeFileStem(Path.GetFileNameWithoutExtension(inputPath)) + ".i64");
	}

	public static string BuildDefaultLogPath(string inputPath, string operation)
	{
		return Path.Combine(EnsureCacheDirectory(), SanitizeFileStem(Path.GetFileNameWithoutExtension(inputPath)) + "." + operation + ".log");
	}

	public static string BuildDefaultResultPath(string inputPath, string operation)
	{
		return Path.Combine(EnsureCacheDirectory(), SanitizeFileStem(Path.GetFileNameWithoutExtension(inputPath)) + "." + operation + ".json");
	}

	public static string BuildDefaultJsonPath(string inputPath, string operation)
	{
		return Path.Combine(EnsureCacheDirectory(), SanitizeFileStem(Path.GetFileNameWithoutExtension(inputPath)) + "." + operation + ".manifest.json");
	}

	public static async Task<bool> CanImportIdaproAsync(string pythonPath, string? idaHome, string? userPath)
	{
		if (string.IsNullOrWhiteSpace(pythonPath))
		{
			return false;
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(pythonPath)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		processStartInfo.ArgumentList.Add("-c");
		processStartInfo.ArgumentList.Add("import idapro; print('OK')");
		ApplyIdaEnvironment(processStartInfo, idaHome, userPath);
		var (exitCode, stdOut, _) = await RunProcessCaptureAsync(processStartInfo);
		return exitCode == 0 && stdOut.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	public static async Task<bool> EnsureIdalibActivatedAsync(string pythonPath, string? idaHome, string? userPath)
	{
		string? text = FindActivationScript(idaHome);
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(pythonPath)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		processStartInfo.ArgumentList.Add(text);
		if (!string.IsNullOrWhiteSpace(idaHome))
		{
			processStartInfo.ArgumentList.Add("-d");
			processStartInfo.ArgumentList.Add(idaHome);
		}
		ApplyIdaEnvironment(processStartInfo, idaHome, userPath);
		var (exitCode, _, _) = await RunProcessCaptureAsync(processStartInfo);
		return exitCode == 0;
	}

	public static void ApplyIdaEnvironment(ProcessStartInfo startInfo, string? idaHome, string? userPath)
	{
		if (!string.IsNullOrWhiteSpace(idaHome))
		{
			startInfo.Environment["IDADIR"] = idaHome;
		}
		if (!string.IsNullOrWhiteSpace(userPath))
		{
			startInfo.Environment["IDAUSR"] = userPath;
		}
		startInfo.Environment["IDA_LOADALL"] = "1";
	}

	public static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessCaptureAsync(ProcessStartInfo startInfo)
	{
		using Process process = new Process
		{
			StartInfo = startInfo
		};
		process.Start();
		Task<string> task = process.StandardOutput.ReadToEndAsync();
		Task<string> task2 = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();
		return (process.ExitCode, await task, await task2);
	}

	public static async Task<int> RunProcessStreamingAsync(ProcessStartInfo startInfo, Action<string>? onStdOut, Action<string>? onStdErr)
	{
		using Process proc = new Process
		{
			StartInfo = startInfo
		};
		proc.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				onStdOut?.Invoke(e.Data);
			}
		};
		proc.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				onStdErr?.Invoke(e.Data);
			}
		};
		proc.Start();
		proc.BeginOutputReadLine();
		proc.BeginErrorReadLine();
		await proc.WaitForExitAsync();
		return proc.ExitCode;
	}

	private static string? NormalizeNullablePath(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		return value.Trim();
	}

	private static IEnumerable<string> ExpandUserPaths(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			yield break;
		}
		string[] array = value.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			string text2 = text.Trim();
			if (text2.Length != 0)
			{
				yield return text2;
			}
		}
	}

	private static bool SearchForLoader(string? root)
	{
		if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
		{
			return false;
		}
		try
		{
			foreach (string loaderSearchRoot in LoaderSearchRoots)
			{
				string text = ((loaderSearchRoot == ".") ? root : Path.Combine(root, loaderSearchRoot));
				if (!Directory.Exists(text))
				{
					continue;
				}
				foreach (string loaderPattern in LoaderPatterns)
				{
					if (Directory.EnumerateFiles(text, loaderPattern, SearchOption.AllDirectories).Any())
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static int ScoreExecutablePath(string path)
	{
		string fileName = Path.GetFileName(path);
		if (fileName.Equals("idat64.exe", StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (fileName.Equals("idat.exe", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		if (fileName.Equals("ida64.exe", StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		if (fileName.Equals("ida.exe", StringComparison.OrdinalIgnoreCase))
		{
			return 3;
		}
		return 10;
	}

	private static string SanitizeFileStem(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "ida";
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
			{
				stringBuilder.Append(c);
			}
			else
			{
				stringBuilder.Append('_');
			}
		}
		return stringBuilder.ToString().Trim('_');
	}
}
