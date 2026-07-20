using System.Diagnostics;
using System.Text.Json;
using Xbox360.Remote.Cli.Homebrew;
using Xbox360.Remote.Cli.Logging;

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
                OperationFeedback.WriteFailure("Input file not found", CommandLogRedactor.RedactFreeText(fullPath));
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

    public static async Task RunBatchExportSymbolsAsync(IdaEnvironmentContext environment, string databasePath, string outputJsonPath, string moduleName, CancellationToken cancellationToken) {
        string cacheRoot = GetCacheRoot();
        string stem = Path.GetFileNameWithoutExtension(databasePath);
        string logPath = Path.Combine(cacheRoot, $"{stem}.export-symbols.log");
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "ida_scripts", "export_symbols.py");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("IDA symbol export helper script was not found.", scriptPath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputJsonPath)!);
        if (File.Exists(outputJsonPath))
            File.Delete(outputJsonPath);

        string arguments = IdaRuntimeHelpers.BuildBatchExportSymbolsArguments(logPath, scriptPath, outputJsonPath, moduleName, databasePath);
        ProcessStartInfo psi = IdaRuntimeHelpers.CreateIdaProcessStartInfo(environment.BatchExe, arguments, databasePath, environment.UserDirectory);
        ProcessRunResult result = await ReverseEngineeringProcessHelpers.RunAsync(psi, null, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"IDA symbol export exited with code {result.ExitCode}. See {logPath}");

        if (!File.Exists(outputJsonPath))
            throw new InvalidOperationException($"IDA symbol export did not produce the requested JSON file. See {logPath}");
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

    public static void InstallSupportedIdaxexArchive(
        string archivePath,
        string idaHome,
        IdaxexBuildSpec build,
        string expectedArchiveSha256) {
        string extractRoot = Path.Combine(GetCacheRoot(), $"extract-{Guid.NewGuid():N}");
        try {
            if (!ReverseEngineeringDownloadHelpers.VerifySha256(archivePath, expectedArchiveSha256, out _, out string archiveHashError))
                throw new InvalidDataException($"The {build.Label} archive failed authentication. {archiveHashError}");

            HomebrewPackageService.ExtractArchive(archivePath, extractRoot);
            string baseRoot = HomebrewPackageService.CollapseRootDirectory(extractRoot);
            foreach (IdaxexPayloadSpec payload in build.Payloads) {
                string sourcePath = ResolveRelativePath(baseRoot, payload.ArchivePath);
                string destinationPath = ResolveRelativePath(idaHome, payload.InstallPath);
                CopyAuthenticatedPayload(sourcePath, destinationPath, payload);
            }
        }
        finally {
            ReverseEngineeringDownloadHelpers.TryDeleteDirectory(extractRoot);
        }
    }

    private static void CopyAuthenticatedPayload(string sourcePath, string destinationPath, IdaxexPayloadSpec payload) {
        if (!File.Exists(sourcePath))
            throw new InvalidDataException($"The authenticated archive did not contain required payload {payload.ArchivePath}.");
        if (!ReverseEngineeringDownloadHelpers.VerifySha256(sourcePath, payload.Sha256, out _, out string sourceHashError))
            throw new InvalidDataException($"Payload {payload.ArchivePath} failed authentication. {sourceHashError}");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);

        if (!ReverseEngineeringDownloadHelpers.VerifySha256(destinationPath, payload.Sha256, out _, out string destinationHashError))
            throw new InvalidDataException($"Installed payload {payload.InstallPath} failed post-copy authentication. {destinationHashError}");
    }

    private static string ResolveRelativePath(string root, string relativePath) {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"IDA payload path must be relative: {relativePath}");

        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
            throw new InvalidDataException($"IDA payload path is unsafe: {relativePath}");

        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(segments)));
        string rootPrefix = fullRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"IDA payload path escapes its root: {relativePath}");

        return candidate;
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

    private static T? LoadJson<T>(string path) {
        if (!File.Exists(path))
            return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });
    }
}
