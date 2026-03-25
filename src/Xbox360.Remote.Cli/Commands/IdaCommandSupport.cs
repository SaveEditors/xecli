using System.Diagnostics;
using System.Text.Json;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

internal static class IdaCommandSupport {
    internal sealed class IdaAnalyzeResult {
        public string? Database { get; set; }
        public int Segments { get; set; }
        public int Functions { get; set; }
    }

    internal sealed class IdaDecompileResult {
        public string? Database { get; set; }
        public string? OutputDirectory { get; set; }
        public string Backend { get; set; } = "unknown";
        public int Decompiled { get; set; }
        public int Failed { get; set; }
        public List<string>? Files { get; set; }
        public List<string>? Errors { get; set; }
    }

    public static string GetCacheRoot() {
        string cacheRoot = Path.Combine(CliPaths.CachePath, "ida");
        Directory.CreateDirectory(cacheRoot);
        return cacheRoot;
    }

    public static string GetDefaultDatabasePath(string inputPath) {
        return Path.Combine(GetCacheRoot(), $"{Path.GetFileNameWithoutExtension(inputPath)}.i64");
    }

    public static async Task<string?> ResolveInputAsync(string? input, string? ftpPath, bool running) {
        if (!string.IsNullOrWhiteSpace(input)) {
            string fullPath = Path.GetFullPath(input);
            if (!File.Exists(fullPath)) {
                OperationFeedback.WriteFailure("Input file not found", fullPath);
                return null;
            }
            return fullPath;
        }

        if (!string.IsNullOrWhiteSpace(ftpPath))
            return await GhidraInputHelpers.DownloadViaFtpAsync(ftpPath);

        if (running) {
            string? path = await GhidraInputHelpers.ResolveRunningFtpPathAsync();
            if (string.IsNullOrWhiteSpace(path)) {
                OperationFeedback.WriteFailure("Running XEX not resolved", "Could not map the active title to an FTP path.");
                return null;
            }
            return await GhidraInputHelpers.DownloadViaFtpAsync(path);
        }

        OperationFeedback.WriteFailure("Missing input", "Provide --in, --ftp-path, or --running.");
        return null;
    }

    public static async Task<IdaAnalyzeResult> RunBatchAnalyzeAsync(IdaEnvironmentContext environment, string inputPath, string outputDatabase, CancellationToken cancellationToken) {
        string cacheRoot = GetCacheRoot();
        Directory.CreateDirectory(Path.GetDirectoryName(outputDatabase)!);
        string stem = Path.GetFileNameWithoutExtension(outputDatabase);
        string logPath = Path.Combine(cacheRoot, $"{stem}.analyze.log");
        string resultJsonPath = Path.Combine(cacheRoot, $"{stem}.analyze.json");
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "ida_scripts", "analyze_xex.py");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("IDA analyze helper script was not found.", scriptPath);

        if (File.Exists(outputDatabase))
            File.Delete(outputDatabase);
        if (File.Exists(resultJsonPath))
            File.Delete(resultJsonPath);

        string arguments = IdaRuntimeHelpers.BuildAnalyzeArguments(logPath, outputDatabase, scriptPath, resultJsonPath, inputPath);
        ProcessStartInfo psi = IdaRuntimeHelpers.CreateIdaProcessStartInfo(environment.BatchExe, arguments, inputPath, environment.UserDirectory);
        ProcessRunResult result = await ReverseEngineeringProcessHelpers.RunAsync(psi, null, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"IDA batch analysis exited with code {result.ExitCode}. See {logPath}");

        IdaAnalyzeResult? summary = LoadJson<IdaAnalyzeResult>(resultJsonPath);
        if (summary == null)
            throw new InvalidOperationException($"IDA analysis did not produce a summary JSON file. See {logPath}");
        if (!File.Exists(summary.Database ?? outputDatabase))
            throw new InvalidOperationException($"IDA did not create the requested database. See {logPath}");
        summary.Database ??= outputDatabase;
        return summary;
    }

    public static async Task<IdaDecompileResult> RunBatchDecompileAsync(IdaEnvironmentContext environment, string databasePath, string outputDirectory, int? maxFunctions, CancellationToken cancellationToken) {
        string cacheRoot = GetCacheRoot();
        string stem = Path.GetFileNameWithoutExtension(databasePath);
        string logPath = Path.Combine(cacheRoot, $"{stem}.decompile.log");
        string resultJsonPath = Path.Combine(cacheRoot, $"{stem}.decompile.json");
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "ida_scripts", "decompile_xex.py");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("IDA batch decompile helper script was not found.", scriptPath);

        if (File.Exists(resultJsonPath))
            File.Delete(resultJsonPath);
        Directory.CreateDirectory(outputDirectory);

        string arguments = IdaRuntimeHelpers.BuildBatchDecompileArguments(logPath, scriptPath, outputDirectory, maxFunctions, resultJsonPath, databasePath);
        ProcessStartInfo psi = IdaRuntimeHelpers.CreateIdaProcessStartInfo(environment.BatchExe, arguments, databasePath, environment.UserDirectory);
        ProcessRunResult result = await ReverseEngineeringProcessHelpers.RunAsync(psi, null, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"IDA batch decompile exited with code {result.ExitCode}. See {logPath}");

        IdaDecompileResult? summary = LoadJson<IdaDecompileResult>(resultJsonPath);
        if (summary == null)
            throw new InvalidOperationException($"IDA batch decompile did not produce a summary JSON file. See {logPath}");
        summary.Backend = "batch";
        return summary;
    }

    public static async Task<IdaDecompileResult> RunIdalibDecompileAsync(IdaEnvironmentContext environment, string databasePath, string outputDirectory, int? maxFunctions, CancellationToken cancellationToken) {
        await IdaRuntimeHelpers.EnsureIdaproAvailableAsync(environment.PythonCommand, environment.Home, cancellationToken);

        string cacheRoot = GetCacheRoot();
        string stem = Path.GetFileNameWithoutExtension(databasePath);
        string logPath = Path.Combine(cacheRoot, $"{stem}.idalib.log");
        string resultJsonPath = Path.Combine(cacheRoot, $"{stem}.idalib.json");
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "ida_scripts", "idalib_decompile.py");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("IDALIB decompile helper script was not found.", scriptPath);

        if (File.Exists(resultJsonPath))
            File.Delete(resultJsonPath);
        Directory.CreateDirectory(outputDirectory);

        ProcessStartInfo psi = new ProcessStartInfo(environment.PythonCommand) {
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? Environment.CurrentDirectory
        };
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("--db");
        psi.ArgumentList.Add(databasePath);
        psi.ArgumentList.Add("--out");
        psi.ArgumentList.Add(outputDirectory);
        psi.ArgumentList.Add("--max");
        psi.ArgumentList.Add((maxFunctions ?? 0).ToString());
        psi.ArgumentList.Add("--json-out");
        psi.ArgumentList.Add(resultJsonPath);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["IDAUSR"] = environment.UserDirectory;

        ProcessRunResult result = await ReverseEngineeringProcessHelpers.RunAsync(psi, logPath, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"idalib decompile exited with code {result.ExitCode}. See {logPath}");

        IdaDecompileResult? summary = LoadJson<IdaDecompileResult>(resultJsonPath);
        if (summary == null)
            throw new InvalidOperationException($"idalib did not produce a summary JSON file. See {logPath}");
        summary.Backend = "idalib";
        return summary;
    }

    public static void InstallSupportedIdaxexArchive(string archivePath, string idaHome) {
        string extractRoot = Path.Combine(GetCacheRoot(), $"extract-{Guid.NewGuid():N}");
        try {
            HomebrewPackageService.ExtractArchive(archivePath, extractRoot);
            string baseRoot = HomebrewPackageService.CollapseRootDirectory(extractRoot);
            string ida91Root = Path.Combine(baseRoot, "ida91");
            if (!Directory.Exists(ida91Root))
                ida91Root = Directory.EnumerateDirectories(baseRoot, "ida91", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidDataException("The archive did not contain an ida91 payload.");

            CopyRequiredFile(Path.Combine(ida91Root, "loaders", "idaxex.dll"), Path.Combine(idaHome, "loaders", "idaxex.dll"));
            CopyRequiredFile(Path.Combine(ida91Root, "loaders", "idaxex.so"), Path.Combine(idaHome, "loaders", "idaxex.so"));
            CopyRequiredFile(Path.Combine(ida91Root, "til", "ppc", "x360.til"), Path.Combine(idaHome, "til", "ppc", "x360.til"));
            CopyRequiredFile(Path.Combine(ida91Root, "til", "ppc", "xkelib.til"), Path.Combine(idaHome, "til", "ppc", "xkelib.til"));

            string xex1toolFromRoot = Path.Combine(baseRoot, "xex1tool.exe");
            if (!File.Exists(xex1toolFromRoot))
                xex1toolFromRoot = Directory.EnumerateFiles(baseRoot, "xex1tool.exe", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidDataException("The archive did not contain xex1tool.exe.");
            CopyRequiredFile(xex1toolFromRoot, Path.Combine(idaHome, "xex1tool.exe"));
        }
        finally {
            ReverseEngineeringDownloadHelpers.TryDeleteDirectory(extractRoot);
        }
    }

    public static void TryDeleteDatabaseArtifacts(string databasePath) {
        foreach (string path in new[] {
                     databasePath,
                     Path.ChangeExtension(databasePath, ".id0"),
                     Path.ChangeExtension(databasePath, ".id1"),
                     Path.ChangeExtension(databasePath, ".nam"),
                     Path.ChangeExtension(databasePath, ".til")
                 }) {
            try {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch {
                // best-effort cleanup
            }
        }
    }

    private static void CopyRequiredFile(string sourcePath, string destinationPath) {
        if (!File.Exists(sourcePath))
            throw new InvalidDataException($"Required file was not found in the archive: {sourcePath}");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static T? LoadJson<T>(string path) {
        if (!File.Exists(path))
            return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });
    }
}
