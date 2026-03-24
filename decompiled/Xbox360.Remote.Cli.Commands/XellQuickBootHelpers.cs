using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Xbox360.Remote.Cli;

namespace Xbox360.Remote.Cli.Commands;

internal sealed record XellQuickBootInstall(string RemoteLauncherPath, string? RemotePackagePath, string DisplayName, string ConfigPath, string? LauncherPath, XellLaunchPreflight? Preflight, bool TargetsFlash);

internal static class XellQuickBootHelpers
{
	private const string ContentRoot = "Hdd1:\\Content\\0000000000000000\\C0DE9999\\00007000";

	private const string LauncherRoot = "Hdd1:\\XeCLI\\QuickBoot";

	private static readonly string[] ContentSegments = new string[4] { "Content", "0000000000000000", "C0DE9999", "00007000" };

	private static readonly string[] LauncherSegments = new string[2] { "XeCLI", "QuickBoot" };

	public static bool IsEnabled(XellCommandSettings settings)
	{
		return settings.QuickBoot && !settings.ForceXell;
	}

	public static bool TargetsFlash(XellCommandSettings settings)
	{
		return string.Equals(settings.QuickBootTarget, "flash", StringComparison.OrdinalIgnoreCase);
	}

	public static string DescribeBootPath(XellCommandSettings settings)
	{
		if (TargetsFlash(settings))
		{
			return "[cyan]Build, upload, and launch a QuickBoot XeLL launcher targeting flash[/]";
		}
		return "[cyan]Build, upload, and launch a QuickBoot XeLL launcher targeting the resolved helper[/]";
	}

	public static string? DescribeConsoleFileChanges(XellCommandSettings settings)
	{
		string? text = null;
		if (IsEnabled(settings))
		{
			text = "[cyan]Upload a QuickBoot XeLL launcher to[/] [springgreen3_1]" + Markup.Escape(LauncherRoot) + "[/] [cyan]and a dashboard shortcut to[/] [springgreen3_1]" + Markup.Escape(ContentRoot) + "[/]";
			if (!TargetsFlash(settings) && string.IsNullOrWhiteSpace(settings.LauncherPath) && string.IsNullOrWhiteSpace(settings.StageLauncherPath))
			{
				text = AppendChange(text, "[cyan]Install bundled XellLaunch and its matching xell.bin automatically if the console does not already have them[/]");
			}
		}
		if (!string.IsNullOrWhiteSpace(settings.StageLauncherPath))
		{
			text = AppendChange(text, "[cyan]Upload the selected helper XEX to the console[/]");
		}
		if (!string.IsNullOrWhiteSpace(settings.StageXellBinPath))
		{
			text = AppendChange(text, "[cyan]Upload xell.bin beside the helper before booting[/]");
		}
		return text;
	}

	public static async Task<XellQuickBootInstall> StageQuickBootPackageAsync(XbdmClient client, XellCommandSettings settings, Func<Task<string?>> resolveLauncherAsync, Func<string, Task<XellLaunchPreflight>> preflightAsync, CancellationToken cancellationToken)
	{
		bool flag = TargetsFlash(settings);
		string text = flag ? "\\lhelper.xex" : string.Empty;
		string? text2 = null;
		XellLaunchPreflight? xellLaunchPreflight = null;
		if (!flag)
		{
			text2 = await resolveLauncherAsync();
			if (string.IsNullOrWhiteSpace(text2))
			{
				throw new InvalidOperationException("QuickBoot XeLL launch requested, but XeCLI could not resolve a helper XEX on the console. Stage a helper with --stage-launcher or point --launcher at an existing helper.");
			}
			xellLaunchPreflight = await preflightAsync(text2);
			text = BuildConfigPath(text2);
			if (LooksLikeBundleManagedLauncher(text2) && string.IsNullOrWhiteSpace(settings.StageXellBinPath))
			{
				string text14 = BuildSiblingXellBinPath(text2);
				await UploadLooseLauncherAsync(client, "Refreshing bundled xell.bin", GetBundledHelperAssetPath("xell.bin"), text14, cancellationToken);
				xellLaunchPreflight = await preflightAsync(text2);
			}
		}
		string text3 = flag ? "XeLLFlash" : "XeLLLaunch";
		string text4 = flag ? "XeLL Flash" : "XeLL Launch";
		string text5 = CreateStagingDirectory();
		try
		{
			string text6 = Path.Combine(text5, text3);
			string text7 = Path.Combine(text6, "config.ini");
			string text8 = Path.Combine(text6, "default.xex");
			Directory.CreateDirectory(text6);
			await WriteConfigAsync(text7, text, cancellationToken);
			File.Copy(GetAssetPath("default.xex"), text8, overwrite: true);
			string text9 = LauncherRoot + "\\" + text3;
			string text10 = text9 + "\\default.xex";
			await EnsureRemoteDirectoryTreeAsync(client, "Hdd1:", LauncherSegments, cancellationToken);
			try
			{
				await client.CreateDirectoryAsync(text9, cancellationToken);
			}
			catch
			{
			}
			await UploadLooseLauncherAsync(client, "Uploading QuickBoot launcher", text8, text10, cancellationToken);
			await UploadLooseLauncherAsync(client, "Uploading QuickBoot config", text7, text9 + "\\config.ini", cancellationToken);
			string? text11 = null;
			try
			{
				string text12 = Path.Combine(text5, text3 + ".live");
				await BuildPackageAsync(text12, text4, "XeLL shortcut created by XeCLI.", text, cancellationToken);
				await EnsureRemoteDirectoryTreeAsync(client, "Hdd1:", ContentSegments, cancellationToken);
				text11 = ContentRoot + "\\" + text3;
				await UploadLooseLauncherAsync(client, "Uploading XeLL dashboard shortcut", text12, text11, cancellationToken);
			}
			catch (Exception ex)
			{
				OperationFeedback.WriteWarning("QuickBoot dashboard shortcut skipped", "[grey]" + Markup.Escape(ex.Message) + "[/]");
			}
			string text13 = "[white]" + Markup.Escape(text4) + "[/] -> [cyan]" + Markup.Escape(text10) + "[/]\n[grey]Target:[/] [springgreen3_1]" + Markup.Escape(text) + "[/]";
			if (!string.IsNullOrWhiteSpace(text11))
			{
				text13 = text13 + "\n[grey]Dashboard shortcut:[/] [cyan]" + Markup.Escape(text11) + "[/]";
			}
			OperationFeedback.WriteSuccess("XeLL shortcut staged", text13);
			return new XellQuickBootInstall(text10, text11, text4, text, text2, xellLaunchPreflight, flag);
		}
		finally
		{
			TryDeleteDirectory(text5);
		}
	}

	private static string AppendChange(string? existing, string next)
	{
		if (string.IsNullOrWhiteSpace(existing))
		{
			return next;
		}
		return existing + "\n" + next;
	}

	private static string BuildConfigPath(string launcherPath)
	{
		string text = launcherPath.Trim().Replace('/', '\\');
		const string value = "\\Device\\Flash\\";
		if (text.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			return "\\" + text.Substring(value.Length).TrimStart('\\');
		}
		int num = text.IndexOf(':');
		if (num > 0)
		{
			return "\\" + text.Substring(num + 1).TrimStart('\\');
		}
		throw new InvalidOperationException("QuickBoot requires a FATX-style helper path such as Hdd1:\\XellLaunch\\default.xex or a flash path such as \\Device\\Flash\\lhelper.xex. XeCLI resolved: " + launcherPath);
	}

	private static bool LooksLikeBundleManagedLauncher(string launcherPath)
	{
		if (string.IsNullOrWhiteSpace(launcherPath))
		{
			return false;
		}
		string text = launcherPath.Trim().Replace('/', '\\');
		return text.IndexOf("\\XellLaunch\\", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static string BuildSiblingXellBinPath(string launcherPath)
	{
		string? directoryName = Path.GetDirectoryName(launcherPath.Trim().Replace('/', '\\'));
		if (string.IsNullOrWhiteSpace(directoryName))
		{
			throw new InvalidOperationException("Could not determine the helper directory for " + launcherPath);
		}
		return directoryName.TrimEnd('\\') + "\\xell.bin";
	}

	private static async Task EnsureRemoteDirectoryTreeAsync(XbdmClient client, string root, string[] segments, CancellationToken cancellationToken)
	{
		string text = root;
		foreach (string text2 in segments)
		{
			text = text + "\\" + text2;
			try
			{
				await client.CreateDirectoryAsync(text, cancellationToken);
			}
			catch
			{
			}
		}
	}

	private static string CreateStagingDirectory()
	{
		string text = Path.Combine(CliPaths.CachePath, "xell-quickboot", $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(text);
		return text;
	}

	private static async Task WriteConfigAsync(string path, string configPath, CancellationToken cancellationToken)
	{
		await File.WriteAllTextAsync(path, configPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
	}

	private static async Task BuildPackageAsync(string outputPath, string displayName, string description, string configPath, CancellationToken cancellationToken)
	{
		string directoryName = Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("QuickBoot output directory was missing.");
		string text = Path.Combine(directoryName, "config.ini");
		string text2 = Path.Combine(directoryName, "build-quickboot.ps1");
		await WriteConfigAsync(text, configPath, cancellationToken);
		await File.WriteAllTextAsync(text2, BuildScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = ResolvePowerShellExe(),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		processStartInfo.ArgumentList.Add("-NoProfile");
		processStartInfo.ArgumentList.Add("-ExecutionPolicy");
		processStartInfo.ArgumentList.Add("Bypass");
		processStartInfo.ArgumentList.Add("-File");
		processStartInfo.ArgumentList.Add(text2);
		processStartInfo.ArgumentList.Add("-X360Dll");
		processStartInfo.ArgumentList.Add(GetAssetPath("X360.dll"));
		processStartInfo.ArgumentList.Add("-DefaultXex");
		processStartInfo.ArgumentList.Add(GetAssetPath("default.xex"));
		processStartInfo.ArgumentList.Add("-ConfigFile");
		processStartInfo.ArgumentList.Add(text);
		processStartInfo.ArgumentList.Add("-OutputFile");
		processStartInfo.ArgumentList.Add(outputPath);
		processStartInfo.ArgumentList.Add("-DisplayName");
		processStartInfo.ArgumentList.Add(displayName);
		processStartInfo.ArgumentList.Add("-Description");
		processStartInfo.ArgumentList.Add(description);
		processStartInfo.WorkingDirectory = directoryName;
		var (num, stdOut, stdErr) = await IdaHelpers.RunProcessCaptureAsync(processStartInfo);
		if (num != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
		{
			string text3 = string.Join(Environment.NewLine, new string[2] { stdOut?.Trim(), stdErr?.Trim() }.Where((string? value) => !string.IsNullOrWhiteSpace(value)));
			throw new InvalidOperationException("QuickBoot package build failed." + (string.IsNullOrWhiteSpace(text3) ? string.Empty : (Environment.NewLine + text3)));
		}
	}

	private static string ResolvePowerShellExe()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
		if (File.Exists(text))
		{
			return text;
		}
		return "powershell.exe";
	}

	private static string GetAssetPath(string fileName)
	{
		string text = Path.Combine(AppContext.BaseDirectory, "Assets", "QuickBoot", fileName);
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("XeCLI QuickBoot asset not found: " + text, text);
		}
		return text;
	}

	private static string GetBundledHelperAssetPath(string fileName)
	{
		string text = Path.Combine(AppContext.BaseDirectory, "Assets", "XellLaunch", fileName);
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("XeCLI XellLaunch asset not found: " + text, text);
		}
		return text;
	}

	private static async Task UploadLooseLauncherAsync(XbdmClient client, string title, string localPath, string remotePath, CancellationToken cancellationToken)
	{
		await using FileStream fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		await CliOutput.RunWithProgressAsync(title, (uint)fileStream.Length, (IProgress<long> progress) => client.UploadFileAsync(remotePath, fileStream, fileStream.Length, progress, cancellationToken));
	}

	private static void TryDeleteDirectory(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static readonly string BuildScript = """
param(
    [Parameter(Mandatory = $true)][string]$X360Dll,
    [Parameter(Mandatory = $true)][string]$DefaultXex,
    [Parameter(Mandatory = $true)][string]$ConfigFile,
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [Parameter(Mandatory = $true)][string]$DisplayName,
    [Parameter(Mandatory = $true)][string]$Description
)

$ErrorActionPreference = 'Stop'

[void][Reflection.Assembly]::LoadFrom($X360Dll)

if (Test-Path $OutputFile) {
    Remove-Item -Force $OutputFile
}

New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($OutputFile)) | Out-Null
New-Item -ItemType File -Force -Path $OutputFile | Out-Null

$endian = [X360.IO.EndianType]::BigEndian
$session = [X360.STFS.CreateContents]::new()
$session.STFSType = [X360.STFS.STFSType]::Type0
$session.HeaderData.Description = $Description
$session.HeaderData.Title_Display = $DisplayName
$session.HeaderData.ThisType = [X360.STFS.PackageType]::GamesOnDemand
$session.HeaderData.TitleID = [Convert]::ToUInt32('C0DE9999', 16)
$session.HeaderData.Publisher = 'F586558'
$session.HeaderData.Title_Package = 'QuickBoot'
$session.HeaderData.SeriesID = [byte[]](0..15 | ForEach-Object { 0 })
$session.HeaderData.SeasonID = [byte[]](0..15 | ForEach-Object { 0 })
$session.HeaderData.DeviceID = [byte[]](0..19 | ForEach-Object { 0 })
$session.HeaderData.IDTransfer = [X360.STFS.TransferLock]::AllowTransfer

$stub = [X360.IO.DJsIO]::new($DefaultXex, [X360.IO.DJFileMode]::Open, $endian)
$config = [X360.IO.DJsIO]::new($ConfigFile, [X360.IO.DJFileMode]::Open, $endian)

try {
    if (-not $session.AddFile($stub, 'default.xex')) {
        throw 'Failed to add default.xex to the XeLL shortcut package.'
    }
    if (-not $session.AddFile($config, 'config.ini')) {
        throw 'Failed to add config.ini to the XeLL shortcut package.'
    }

    $outIo = [X360.IO.DJsIO]::new($OutputFile, [X360.IO.DJFileMode]::Open, $endian)
    try {
        $signing = [X360.STFS.RSAParams]::new([X360.STFS.StrongSigned]::LIVE)
        $log = [X360.Other.LogRecord]::new()
        $package = [X360.STFS.STFSPackage]::new($session, $signing, $outIo, $log)
        if (-not $package.ParseSuccess) {
            throw 'The XeLL shortcut package did not parse successfully after creation.'
        }
        [void]$package.CloseIO()
    }
    finally {
        if ($outIo) {
            $outIo.Close()
        }
    }
}
finally {
    if ($stub) {
        $stub.Close()
    }
    if ($config) {
        $config.Close()
    }
}
""";
}
