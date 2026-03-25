using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace Xbox360.Remote.Cli;

[SupportedOSPlatform("windows")]
internal static class InstallHelpers {
    private const string UserEnvironmentKeyPath = @"Environment";
    private const string EnvironmentKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const uint HwndBroadcast = 0xffff;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    public static string WindowsAppsDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps");

    public static string ShimPath => Path.Combine(WindowsAppsDir, "rgh.cmd");

    public static string DefaultUserInstallDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "XeCLI");

    public static string DefaultMachineInstallDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "XeCLI");

    public static string NormalizeDirectory(string path) {
        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsDirectoryOnProcessPath(string directory) {
        string? env = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(env))
            return false;

        string normalized = NormalizeDirectory(directory);
        string[] parts = env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(p => string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsCommandAvailable(string executableDirectory) {
        if (IsDirectoryOnProcessPath(executableDirectory))
            return true;

        string? commandPath = TryResolveRegisteredCommandPath();
        return !string.IsNullOrWhiteSpace(commandPath) && File.Exists(commandPath);
    }

    public static string? TryResolveRegisteredCommandPath() {
        string? env = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(env)) {
            foreach (string entry in env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                string candidateExe = Path.Combine(entry, "rgh.exe");
                if (File.Exists(candidateExe))
                    return candidateExe;

                string candidateCmd = Path.Combine(entry, "rgh.cmd");
                if (File.Exists(candidateCmd))
                    return candidateCmd;
            }
        }

        return File.Exists(ShimPath) ? ShimPath : null;
    }

    public static string? TryResolveInstalledDirectory() {
        string? commandPath = TryResolveRegisteredCommandPath();
        if (string.IsNullOrWhiteSpace(commandPath))
            return null;

        if (commandPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return NormalizeDirectory(Path.GetDirectoryName(commandPath)!);

        if (commandPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) {
            string? targetExe = TryReadShimTarget(commandPath);
            if (!string.IsNullOrWhiteSpace(targetExe) && File.Exists(targetExe))
                return NormalizeDirectory(Path.GetDirectoryName(targetExe)!);
        }

        return null;
    }

    public static bool IsAdministrator() {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void InstallUserShim(string executableDirectory) {
        string exePath = Path.Combine(NormalizeDirectory(executableDirectory), "rgh.exe");
        Directory.CreateDirectory(WindowsAppsDir);
        string tempPath = Path.Combine(WindowsAppsDir, $"rgh.{Guid.NewGuid():N}.cmd");
        File.WriteAllText(tempPath, $"@echo off{Environment.NewLine}\"{exePath}\" %*{Environment.NewLine}");
        RunDelayedCmd($"/c ping 127.0.0.1 -n 2 >nul & move /y \"{tempPath}\" \"{ShimPath}\" >nul");
    }

    public static void UninstallUserShim() {
        if (File.Exists(ShimPath))
            RunDelayedCmd($"/c ping 127.0.0.1 -n 2 >nul & del /f /q \"{ShimPath}\" >nul 2>nul");
    }

    public static bool AddUserPathEntry(string executableDirectory, out string message) {
        string normalized = NormalizeDirectory(executableDirectory);
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UserEnvironmentKeyPath, writable: true);
        if (key is null) {
            message = "Unable to open the current-user PATH registry key.";
            return false;
        }

        string current = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
        List<string> entries = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (entries.Any(p => string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase))) {
            message = "Current-user PATH already includes this directory.";
            return true;
        }

        entries.Add(normalized);
        key.SetValue("Path", string.Join(';', entries), RegistryValueKind.ExpandString);
        BroadcastEnvironmentChange();
        message = "Added the XeCLI directory to the current-user PATH.";
        return true;
    }

    public static bool RemoveUserPathEntry(string executableDirectory, out string message) {
        string normalized = NormalizeDirectory(executableDirectory);
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UserEnvironmentKeyPath, writable: true);
        if (key is null) {
            message = "Unable to open the current-user PATH registry key.";
            return false;
        }

        string current = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
        List<string> entries = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        key.SetValue("Path", string.Join(';', entries), RegistryValueKind.ExpandString);
        BroadcastEnvironmentChange();
        message = "Removed the XeCLI directory from the current-user PATH.";
        return true;
    }

    public static bool IsSameDirectory(string left, string right) {
        return string.Equals(
            NormalizeDirectory(left),
            NormalizeDirectory(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public static InstallSourceValidation InspectInstallSource(string sourceDirectory) {
        string source = NormalizeDirectory(sourceDirectory);
        string exePath = Path.Combine(source, "rgh.exe");
        if (!File.Exists(exePath)) {
            return new InstallSourceValidation(
                false,
                "rgh.exe was not found in the selected source directory.",
                CreatePublishCommandHint());
        }

        string runtimeConfigPath = Path.Combine(source, "rgh.runtimeconfig.json");
        if (!File.Exists(runtimeConfigPath)) {
            return new InstallSourceValidation(
                true,
                "Detected a single-file self-contained publish.",
                null);
        }

        try {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));
            if (!document.RootElement.TryGetProperty("runtimeOptions", out JsonElement runtimeOptions)) {
                return new InstallSourceValidation(
                    false,
                    "The source runtime configuration is missing runtimeOptions and does not look like a valid published release.",
                    CreatePublishCommandHint());
            }

            if (runtimeOptions.TryGetProperty("includedFrameworks", out _)) {
                return new InstallSourceValidation(
                    true,
                    "Detected a multi-file self-contained publish.",
                    null);
            }

            if (runtimeOptions.TryGetProperty("framework", out _) || runtimeOptions.TryGetProperty("frameworks", out _)) {
                return new InstallSourceValidation(
                    false,
                    "The selected source is a framework-dependent build output, not a self-contained publish. Installing it can fail on systems without the matching x64 .NET desktop runtime.",
                    CreatePublishCommandHint());
            }

            return new InstallSourceValidation(
                false,
                "The selected source does not advertise included frameworks and does not look like a supported self-contained release layout.",
                CreatePublishCommandHint());
        }
        catch (Exception ex) {
            return new InstallSourceValidation(
                false,
                $"The source runtime configuration could not be parsed: {ex.Message}",
                CreatePublishCommandHint());
        }
    }

    public static void MirrorDirectory(string sourceDirectory, string targetDirectory) {
        string source = NormalizeDirectory(sourceDirectory);
        string target = NormalizeDirectory(targetDirectory);
        if (IsSameDirectory(source, target))
            return;

        Directory.CreateDirectory(target);

        HashSet<string> sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
            string relative = Path.GetRelativePath(source, file);
            sourceFiles.Add(relative);
            string destination = Path.Combine(target, relative);
            string? destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDir))
                Directory.CreateDirectory(destinationDir);
            File.Copy(file, destination, overwrite: true);
        }

        foreach (string file in Directory.GetFiles(target, "*", SearchOption.AllDirectories)) {
            string relative = Path.GetRelativePath(target, file);
            if (!sourceFiles.Contains(relative))
                File.Delete(file);
        }

        HashSet<string> sourceDirs = Directory
            .GetDirectories(source, "*", SearchOption.AllDirectories)
            .Select(dir => Path.GetRelativePath(source, dir))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string directory in Directory.GetDirectories(target, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length)) {
            string relative = Path.GetRelativePath(target, directory);
            if (!sourceDirs.Contains(relative))
                Directory.Delete(directory, recursive: true);
        }
    }

    public static bool AddMachinePathEntry(string executableDirectory, out string message) {
        string normalized = NormalizeDirectory(executableDirectory);
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(EnvironmentKeyPath, writable: true);
        if (key is null) {
            message = "Unable to open the machine PATH registry key.";
            return false;
        }

        string current = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
        List<string> entries = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (entries.Any(p => string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase))) {
            message = "Machine PATH already includes this directory.";
            return true;
        }

        entries.Add(normalized);
        string updated = string.Join(';', entries);
        key.SetValue("Path", updated, RegistryValueKind.ExpandString);
        BroadcastEnvironmentChange();
        message = "Added the XeCLI directory to the machine PATH. Open a new terminal to use `rgh` globally.";
        return true;
    }

    public static bool RemoveMachinePathEntry(string executableDirectory, out string message) {
        string normalized = NormalizeDirectory(executableDirectory);
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(EnvironmentKeyPath, writable: true);
        if (key is null) {
            message = "Unable to open the machine PATH registry key.";
            return false;
        }

        string current = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
        List<string> entries = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.Equals(NormalizeDirectory(p), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        key.SetValue("Path", string.Join(';', entries), RegistryValueKind.ExpandString);
        BroadcastEnvironmentChange();
        message = "Removed the XeCLI directory from the machine PATH.";
        return true;
    }

    public static int RunElevatedMachinePathInstall(string currentExePath, string executableDirectory) {
        ProcessStartInfo psi = new ProcessStartInfo(currentExePath) {
            UseShellExecute = true,
            Verb = "runas"
        };
        psi.ArgumentList.Add("install");
        psi.ArgumentList.Add("--machine-path");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(executableDirectory);
        psi.ArgumentList.Add("--quiet");

        using Process? process = Process.Start(psi);
        if (process is null)
            return 1;

        process.WaitForExit();
        return process.ExitCode;
    }

    public static int RunElevatedInstall(string currentExePath, string sourceDirectory, string installDirectory, bool addToPath) {
        ProcessStartInfo psi = new ProcessStartInfo(currentExePath) {
            UseShellExecute = true,
            Verb = "runas"
        };
        psi.ArgumentList.Add("install");
        psi.ArgumentList.Add("--machine");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(sourceDirectory);
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(installDirectory);
        if (!addToPath)
            psi.ArgumentList.Add("--no-path");
        psi.ArgumentList.Add("--quiet");

        using Process? process = Process.Start(psi);
        if (process is null)
            return 1;

        process.WaitForExit();
        return process.ExitCode;
    }

    private static string? TryReadShimTarget(string shimPath) {
        try {
            string content = File.ReadAllText(shimPath).Trim();
            int firstQuote = content.IndexOf('"');
            if (firstQuote < 0)
                return null;

            int secondQuote = content.IndexOf('"', firstQuote + 1);
            if (secondQuote <= firstQuote)
                return null;

            return content.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }
        catch {
            return null;
        }
    }

    private static void RunDelayedCmd(string arguments) {
        using Process? process = Process.Start(new ProcessStartInfo("cmd.exe", arguments) {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        process?.Dispose();
    }

    private static void BroadcastEnvironmentChange() {
        SendMessageTimeout(
            (nint)HwndBroadcast,
            WmSettingChange,
            nint.Zero,
            "Environment",
            SmtoAbortIfHung,
            5000,
            out _);
    }

    private static string CreatePublishCommandHint() {
        return "dotnet publish src/Xbox360.Remote.Cli/Xbox360.Remote.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o <publish-dir>";
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(
        nint hWnd,
        uint msg,
        nint wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out nint lpdwResult);
}

internal sealed record InstallSourceValidation(
    bool IsValid,
    string Message,
    string? PublishCommandHint);
