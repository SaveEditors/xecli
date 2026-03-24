using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace Xbox360.Remote.Cli;

[SupportedOSPlatform("windows")]
internal static class InstallHelpers
{
	private const string UserEnvironmentKeyPath = "Environment";

	private const string EnvironmentKeyPath = "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment";

	private const uint HwndBroadcast = 65535u;

	private const uint WmSettingChange = 26u;

	private const uint SmtoAbortIfHung = 2u;

	public static string WindowsAppsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");

	public static string ShimPath => Path.Combine(WindowsAppsDir, "rgh.cmd");

	public static string DefaultUserInstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "XeCLI");

	public static string DefaultMachineInstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "XeCLI");

	public static string NormalizeDirectory(string path)
	{
		return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	public static bool IsDirectoryOnProcessPath(string directory)
	{
		string environmentVariable = Environment.GetEnvironmentVariable("PATH");
		if (string.IsNullOrWhiteSpace(environmentVariable))
		{
			return false;
		}
		string normalized = NormalizeDirectory(directory);
		return environmentVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any((string p) => string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase));
	}

	public static bool IsCommandAvailable(string executableDirectory)
	{
		string normalized = NormalizeDirectory(executableDirectory);
		if (IsDirectoryOnProcessPath(normalized) && File.Exists(Path.Combine(normalized, "rgh.exe")))
		{
			return true;
		}
		return !string.IsNullOrWhiteSpace(TryResolveRegisteredCommandPath());
	}

	public static string? TryResolveRegisteredCommandPath()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("PATH");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			string[] array = environmentVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			foreach (string path in array)
			{
				string text = Path.Combine(path, "rgh.exe");
				if (File.Exists(text))
				{
					return text;
				}
				string text2 = Path.Combine(path, "rgh.cmd");
				if (!File.Exists(text2))
				{
					continue;
				}
				if (HasValidShimTarget(text2))
				{
					return text2;
				}
				TryDeleteBrokenShim(text2);
			}
		}
		if (!File.Exists(ShimPath))
		{
			return null;
		}
		if (HasValidShimTarget(ShimPath))
		{
			return ShimPath;
		}
		TryDeleteBrokenShim(ShimPath);
		return null;
	}

	public static string? TryResolveInstalledDirectory()
	{
		string text = TryResolveRegisteredCommandPath();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return NormalizeDirectory(Path.GetDirectoryName(text));
		}
		if (text.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = TryReadShimTarget(text);
			if (!string.IsNullOrWhiteSpace(text2) && File.Exists(text2))
			{
				return NormalizeDirectory(Path.GetDirectoryName(text2));
			}
		}
		return null;
	}

	public static bool IsAdministrator()
	{
		using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	public static bool RemoveBrokenUserShim()
	{
		if (!File.Exists(ShimPath) || HasValidShimTarget(ShimPath))
		{
			return false;
		}
		return TryDeleteBrokenShim(ShimPath);
	}

	public static void InstallUserShim(string executableDirectory)
	{
		string value = Path.Combine(NormalizeDirectory(executableDirectory), "rgh.exe");
		Directory.CreateDirectory(WindowsAppsDir);
		string text = Path.Combine(WindowsAppsDir, $"rgh.{Guid.NewGuid():N}.cmd");
		File.WriteAllText(text, $"@echo off{Environment.NewLine}\"{value}\" %*{Environment.NewLine}");
		RunDelayedCmd($"/c ping 127.0.0.1 -n 2 >nul & move /y \"{text}\" \"{ShimPath}\" >nul");
	}

	public static void UninstallUserShim()
	{
		if (File.Exists(ShimPath))
		{
			RunDelayedCmd("/c ping 127.0.0.1 -n 2 >nul & del /f /q \"" + ShimPath + "\" >nul 2>nul");
		}
	}

	public static bool AddUserPathEntry(string executableDirectory, out string message)
	{
		string normalized = NormalizeDirectory(executableDirectory);
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
		if (registryKey == null)
		{
			message = "Unable to open the current-user PATH registry key.";
			return false;
		}
		List<string> list = (registryKey.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		if (list.Any((string p) => string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase)))
		{
			message = "Current-user PATH already includes this directory.";
			return true;
		}
		list.Add(normalized);
		registryKey.SetValue("Path", string.Join(';', list), RegistryValueKind.ExpandString);
		BroadcastEnvironmentChange();
		message = "Added the XeCLI directory to the current-user PATH.";
		return true;
	}

	public static bool RemoveUserPathEntry(string executableDirectory, out string message)
	{
		string normalized = NormalizeDirectory(executableDirectory);
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
		if (registryKey == null)
		{
			message = "Unable to open the current-user PATH registry key.";
			return false;
		}
		List<string> values = (from p in (registryKey.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			where !string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase)
			select p).ToList();
		registryKey.SetValue("Path", string.Join(';', values), RegistryValueKind.ExpandString);
		BroadcastEnvironmentChange();
		message = "Removed the XeCLI directory from the current-user PATH.";
		return true;
	}

	public static bool IsSameDirectory(string left, string right)
	{
		return string.Equals(NormalizeDirectory(left), NormalizeDirectory(right), StringComparison.OrdinalIgnoreCase);
	}

	public static void MirrorDirectory(string sourceDirectory, string targetDirectory)
	{
		string source = NormalizeDirectory(sourceDirectory);
		string text = NormalizeDirectory(targetDirectory);
		if (IsSameDirectory(source, text))
		{
			return;
		}
		Directory.CreateDirectory(text);
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
		foreach (string text2 in files)
		{
			string relativePath = Path.GetRelativePath(source, text2);
			hashSet.Add(relativePath);
			string text3 = Path.Combine(text, relativePath);
			string directoryName = Path.GetDirectoryName(text3);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.Copy(text2, text3, overwrite: true);
		}
		files = Directory.GetFiles(text, "*", SearchOption.AllDirectories);
		foreach (string path in files)
		{
			string relativePath2 = Path.GetRelativePath(text, path);
			if (!hashSet.Contains(relativePath2))
			{
				File.Delete(path);
			}
		}
		HashSet<string> hashSet2 = (from dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)
			select Path.GetRelativePath(source, dir)).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in from text4 in Directory.GetDirectories(text, "*", SearchOption.AllDirectories)
			orderby text4.Length descending
			select text4)
		{
			string relativePath3 = Path.GetRelativePath(text, item);
			if (!hashSet2.Contains(relativePath3))
			{
				Directory.Delete(item, recursive: true);
			}
		}
	}

	public static bool AddMachinePathEntry(string executableDirectory, out string message)
	{
		string normalized = NormalizeDirectory(executableDirectory);
		using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment", writable: true);
		if (registryKey == null)
		{
			message = "Unable to open the machine PATH registry key.";
			return false;
		}
		List<string> list = (registryKey.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		if (list.Any((string p) => string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase)))
		{
			message = "Machine PATH already includes this directory.";
			return true;
		}
		list.Add(normalized);
		string value = string.Join(';', list);
		registryKey.SetValue("Path", value, RegistryValueKind.ExpandString);
		BroadcastEnvironmentChange();
		message = "Added the XeCLI directory to the machine PATH. Open a new terminal to use `rgh` globally.";
		return true;
	}

	public static bool RemoveMachinePathEntry(string executableDirectory, out string message)
	{
		string normalized = NormalizeDirectory(executableDirectory);
		using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment", writable: true);
		if (registryKey == null)
		{
			message = "Unable to open the machine PATH registry key.";
			return false;
		}
		List<string> values = (from p in (registryKey.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			where !string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase)
			select p).ToList();
		registryKey.SetValue("Path", string.Join(';', values), RegistryValueKind.ExpandString);
		BroadcastEnvironmentChange();
		message = "Removed the XeCLI directory from the machine PATH.";
		return true;
	}

	public static int RunElevatedMachinePathInstall(string currentExePath, string executableDirectory)
	{
		using Process process = Process.Start(new ProcessStartInfo(currentExePath)
		{
			UseShellExecute = true,
			Verb = "runas",
			ArgumentList = { "install", "--machine-path", "--source", executableDirectory, "--quiet" }
		});
		if (process == null)
		{
			return 1;
		}
		process.WaitForExit();
		return process.ExitCode;
	}

	public static int RunElevatedInstall(string currentExePath, string sourceDirectory, string installDirectory, bool addToPath)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(currentExePath)
		{
			UseShellExecute = true,
			Verb = "runas"
		};
		processStartInfo.ArgumentList.Add("install");
		processStartInfo.ArgumentList.Add("--machine");
		processStartInfo.ArgumentList.Add("--source");
		processStartInfo.ArgumentList.Add(sourceDirectory);
		processStartInfo.ArgumentList.Add("--path");
		processStartInfo.ArgumentList.Add(installDirectory);
		if (!addToPath)
		{
			processStartInfo.ArgumentList.Add("--no-path");
		}
		processStartInfo.ArgumentList.Add("--quiet");
		using Process process = Process.Start(processStartInfo);
		if (process == null)
		{
			return 1;
		}
		process.WaitForExit();
		return process.ExitCode;
	}

	private static string? TryReadShimTarget(string shimPath)
	{
		try
		{
			string text = File.ReadAllText(shimPath).Trim();
			int num = text.IndexOf('"');
			if (num < 0)
			{
				return null;
			}
			int num2 = text.IndexOf('"', num + 1);
			if (num2 <= num)
			{
				return null;
			}
			return text.Substring(num + 1, num2 - num - 1);
		}
		catch
		{
			return null;
		}
	}

	private static bool HasValidShimTarget(string shimPath)
	{
		string text = TryReadShimTarget(shimPath);
		return !string.IsNullOrWhiteSpace(text) && File.Exists(text);
	}

	private static bool TryDeleteBrokenShim(string shimPath)
	{
		if (!string.Equals(NormalizeDirectory(Path.GetDirectoryName(shimPath) ?? string.Empty), NormalizeDirectory(WindowsAppsDir), StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		try
		{
			File.Delete(shimPath);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void RunDelayedCmd(string arguments)
	{
		using Process process = Process.Start(new ProcessStartInfo("cmd.exe", arguments)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		});
		process?.Dispose();
	}

	private static void BroadcastEnvironmentChange()
	{
		SendMessageTimeout(65535, 26u, IntPtr.Zero, "Environment", 2u, 5000u, out var _);
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, string lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);
}
