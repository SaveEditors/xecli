using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace Xbox360.Remote.Cli;

internal static class InstallHelpers {
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

        return File.Exists(ShimPath) && IsDirectoryOnProcessPath(WindowsAppsDir);
    }

    public static bool IsAdministrator() {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void InstallUserShim(string executableDirectory) {
        string exePath = Path.Combine(NormalizeDirectory(executableDirectory), "rgh.exe");
        Directory.CreateDirectory(WindowsAppsDir);
        File.WriteAllText(ShimPath, $"@echo off{Environment.NewLine}\"{exePath}\" %*{Environment.NewLine}");
    }

    public static void UninstallUserShim() {
        if (File.Exists(ShimPath))
            File.Delete(ShimPath);
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
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(executableDirectory);
        psi.ArgumentList.Add("--quiet");

        using Process? process = Process.Start(psi);
        if (process is null)
            return 1;

        process.WaitForExit();
        return process.ExitCode;
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
