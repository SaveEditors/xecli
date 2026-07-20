using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program {
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    [STAThread]
    public static async Task<int> Main(string[] args) {
        AttachToParentConsoleForCli(args);

        if (IsVersionRequest(args)) {
            Console.WriteLine($"XeTerminal {GetApplicationVersion()}");
            return 0;
        }

        string baseDirectory = AppContext.BaseDirectory;
        if (!TryResolveRghPath(baseDirectory, out string rghPath, out string missingRghMessage)) {
            MessageBox.Show(
                missingRghMessage,
                "XeTerminal",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        bool useParentConsole = ShouldUseParentConsole(args);
        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = rghPath,
            WorkingDirectory = baseDirectory,
            UseShellExecute = false,
            CreateNoWindow = !useParentConsole,
            RedirectStandardOutput = useParentConsole,
            RedirectStandardError = useParentConsole
        };

        if (args.Length == 0 || !IsTerminalEntryPoint(args[0]))
            startInfo.ArgumentList.Add("terminal");

        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using Process? process = Process.Start(startInfo);
        if (process == null)
            return 1;

        if (useParentConsole) {
            Task stdout = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
            Task stderr = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
            await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
        }
        else {
            await process.WaitForExitAsync();
        }
        return process.ExitCode;
    }

    private static bool IsVersionRequest(string[] args) {
        return args.Length == 1 && (
            args[0].Equals("--version", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("-v", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("version", StringComparison.OrdinalIgnoreCase));
    }

    private static void AttachToParentConsoleForCli(string[] args) {
        if (!ShouldUseParentConsole(args))
            return;

        try {
            IntPtr outputHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            IntPtr errorHandle = GetStdHandle(STD_ERROR_HANDLE);
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
                return;

            RestoreInheritedHandle(STD_OUTPUT_HANDLE, outputHandle);
            RestoreInheritedHandle(STD_ERROR_HANDLE, errorHandle);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch (DllNotFoundException) {
        }
        catch (EntryPointNotFoundException) {
        }
    }

    private static void RestoreInheritedHandle(int standardHandle, IntPtr handle) {
        if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            SetStdHandle(standardHandle, handle);
    }

    private static bool ShouldUseParentConsole(string[] args) {
        if (args.Length == 0)
            return false;
        if (IsVersionRequest(args) || args.Any(IsHelpOption))
            return true;

        return !IsTerminalEntryPoint(args[0]);
    }

    private static bool IsHelpOption(string arg) {
        return arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("?", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetApplicationVersion() {
        return typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private static bool IsTerminalEntryPoint(string arg) {
        return arg.Equals("terminal", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("shell", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveRghPath(string baseDirectory, out string rghPath, out string missingRghMessage) {
        string runtimeFolder = Environment.Is64BitProcess ? "win-x64" : "win-x86";
        string[] candidates = [
            Path.Combine(baseDirectory, "rgh.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Xbox360.Remote.Cli", "bin", "Release", "net10.0-windows", runtimeFolder, "rgh.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Xbox360.Remote.Cli", "bin", "Release", "net10.0-windows", runtimeFolder, "publish", "rgh.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Xbox360.Remote.Cli", "bin", "Release", "net10.0-windows", "rgh.exe"))
        ];

        foreach (string candidate in candidates) {
            if (File.Exists(candidate)) {
                rghPath = candidate;
                missingRghMessage = string.Empty;
                return true;
            }
        }

        rghPath = string.Empty;
        missingRghMessage =
            "XeTerminal could not find rgh.exe. Expected it next to XeTerminal.exe or in the sibling Xbox360.Remote.Cli Release output.\n" +
            "Checked:\n  " + string.Join("\n  ", candidates) + "\n" +
            "Build or publish Xbox360.Remote.Cli first, or copy the release rgh.exe beside XeTerminal.exe.";
        return false;
    }
}
