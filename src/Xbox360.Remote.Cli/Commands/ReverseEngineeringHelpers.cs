using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

internal static class ReverseEngineeringSupportConstants {
    public const string SupportedIdaDisplayVersion = "IDA Pro 9.1.250226";
    public const string SupportedIdaProductVersion = "9.1.25.0226";
    public const string SupportedIdaxexLabel = "SaveEditors idaxex 0.42b-compat";
    public const string SupportedIdaxexArchiveUrl = "https://github.com/SaveEditors/idaxex/releases/download/0.42b-compat/idaxex%2Bxex1tool-0.42b-compat_ida91.zip";
    public const string SupportedIdaxexDllSha256 = "97581B47D3E1C7306B8BAA289C6A4EF68D736078C87ACCC63F70A396CC497946";
    public const string KnownIdaxex43DllSha256 = "DA2BC0245A3A06721CCE3804777D63E31B3CF33A4507A1B3B07A966B16F09030";
    public const string GhidraLatestLoaderApiUrl = "https://api.github.com/repos/SaveEditors/XEXLoaderWV/releases/latest";
    public const string GhidraOfficialUrl = "https://ghidra-sre.org/";
    public const string IdaOfficialUrl = "https://hex-rays.com/ida-pro";
    public const string XboxReversingUrl = "https://github.com/emoose/xbox-reversing/tree/master";
}

internal sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed record IdaEnvironmentContext(
    string Home,
    string BatchExe,
    string PythonCommand,
    string UserDirectory,
    string PreferredBackend);

internal sealed record IdaLoaderStatus(
    string? LoaderPath,
    string? Sha256,
    string Label,
    bool IsSupported,
    bool IsKnownNewerVariant);

internal static class ReverseEngineeringNoticeHelpers {
    public static void WriteGhidraNotice() {
        AnsiConsole.MarkupLine(
            "[grey]Ghidra is [white]Free[/] and external. Install Ghidra separately, then run [cyan]rgh ghidra install-loader[/] after configuring the install path to add the XEX helper loader.[/]");
    }

    public static void WriteIdaNotice() {
        AnsiConsole.MarkupLine(
            $"[grey]IDA support is pinned to [white]{Markup.Escape(ReverseEngineeringSupportConstants.SupportedIdaDisplayVersion)}[/] with [white]{Markup.Escape(ReverseEngineeringSupportConstants.SupportedIdaxexLabel)}[/]. Install IDA Pro separately, then run [cyan]rgh ida install-loader[/] after configuring the install path.[/]");
    }
}

internal static class IdaPathHelpers {
    public static string? ResolveIdaHome(CliConfig config, string? overridePath) {
        string? candidate = FirstNonEmpty(overridePath, config.IdaPath, Environment.GetEnvironmentVariable("IDA_HOME"), Environment.GetEnvironmentVariable("IDADIR"));
        return string.IsNullOrWhiteSpace(candidate) ? null : Path.GetFullPath(candidate);
    }

    public static string ResolveBatchExe(string idaHome) {
        return Path.Combine(idaHome, "idat.exe");
    }

    public static string ResolvePythonCommand(CliConfig config, string? overridePython) {
        return FirstNonEmpty(overridePython, config.IdaPythonPath, "python")!;
    }

    public static string ResolveUserDirectory(CliConfig config, string? overrideUserPath) {
        string path = FirstNonEmpty(overrideUserPath, config.IdaUserPath, Path.Combine(CliPaths.CachePath, "ida", "user"))!;
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public static string ResolvePreferredBackend(CliConfig config, string? overrideBackend) {
        return NormalizeBackend(FirstNonEmpty(overrideBackend, config.IdaPreferredBackend, "auto"));
    }

    public static string NormalizeBackend(string? backend) {
        string normalized = (backend ?? "auto").Trim().ToLowerInvariant();
        return normalized switch {
            "auto" => normalized,
            "batch" => normalized,
            "idalib" => normalized,
            _ => throw new InvalidOperationException("Unsupported backend. Expected auto, batch, or idalib.")
        };
    }

    private static string? FirstNonEmpty(params string?[] candidates) {
        return candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

internal static class ReverseEngineeringDownloadHelpers {
    public static string GetDownloadCacheDirectory(string toolName) {
        string path = Path.Combine(CliPaths.CachePath, toolName, "downloads");
        Directory.CreateDirectory(path);
        return path;
    }

    public static async Task<string> ResolveLatestGithubZipAssetUrlAsync(string apiUrl, Func<string, bool>? assetFilter, CancellationToken cancellationToken) {
        using HttpClient client = CreateHttpClient();
        using HttpResponseMessage response = await client.GetAsync(apiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub release metadata did not contain an assets list.");

        foreach (JsonElement asset in assets.EnumerateArray()) {
            string? name = asset.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null;
            string? url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;
            if (assetFilter != null && !assetFilter(name))
                continue;
            return url;
        }

        throw new InvalidOperationException("No matching asset was found in the latest GitHub release.");
    }

    public static async Task<string> DownloadToCacheAsync(string title, string url, string targetDirectory, CancellationToken cancellationToken) {
        Directory.CreateDirectory(targetDirectory);
        string fileName = GetFileNameFromUrl(url);
        string archivePath = Path.Combine(targetDirectory, fileName);

        using HttpClient client = CreateHttpClient();
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await CliOutput.RunWithProgressAsync(title, totalBytes, async progress => {
            await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            while (true) {
                int read = await responseStream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                progress.Report(new CliOutput.TransferProgressUpdate(total, "downloading"));
            }

            await fileStream.FlushAsync(cancellationToken);
        });

        return archivePath;
    }

    public static string ComputeSha256(string filePath) {
        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    public static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch {
            // best-effort cleanup
        }
    }

    public static string GetFileNameFromUrl(string url) {
        Uri uri = new Uri(url);
        string fileName = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(fileName) ? $"download-{Guid.NewGuid():N}.bin" : fileName;
    }

    private static HttpClient CreateHttpClient() {
        HttpClient client = HomebrewPackageService.CreateHttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XeCLI/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

internal static class ReverseEngineeringProcessHelpers {
    public static async Task<ProcessRunResult> RunAsync(ProcessStartInfo psi, string? logPath, CancellationToken cancellationToken) {
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;

        using Process process = new Process { StartInfo = psi };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(logPath)) {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            StringBuilder builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(stdout))
                builder.AppendLine(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr)) {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.AppendLine(stderr.TrimEnd());
            }
            File.WriteAllText(logPath, builder.ToString());
        }

        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }
}

internal static class IdaRuntimeHelpers {
    public static IdaEnvironmentContext ResolveEnvironment(CliConfig config, string? idaPathOverride, string? pythonOverride, string? userOverride, string? backendOverride) {
        string? idaHome = IdaPathHelpers.ResolveIdaHome(config, idaPathOverride);
        if (string.IsNullOrWhiteSpace(idaHome))
            throw new InvalidOperationException("IDA path not set. Use `rgh ida config --path <dir>`.");

        string batchExe = IdaPathHelpers.ResolveBatchExe(idaHome);
        string pythonCommand = IdaPathHelpers.ResolvePythonCommand(config, pythonOverride);
        string userDirectory = IdaPathHelpers.ResolveUserDirectory(config, userOverride);
        string preferredBackend = IdaPathHelpers.ResolvePreferredBackend(config, backendOverride);
        return new IdaEnvironmentContext(idaHome, batchExe, pythonCommand, userDirectory, preferredBackend);
    }

    public static string? TryGetProductVersion(string batchExe) {
        if (!File.Exists(batchExe))
            return null;
        return FileVersionInfo.GetVersionInfo(batchExe).ProductVersion;
    }

    public static bool IsSupportedProductVersion(string? productVersion) {
        return string.Equals(productVersion, ReverseEngineeringSupportConstants.SupportedIdaProductVersion, StringComparison.OrdinalIgnoreCase);
    }

    public static IdaLoaderStatus GetLoaderStatus(string idaHome) {
        string loaderPath = Path.Combine(idaHome, "loaders", "idaxex.dll");
        if (!File.Exists(loaderPath))
            return new IdaLoaderStatus(null, null, "missing", false, false);

        string sha256 = ReverseEngineeringDownloadHelpers.ComputeSha256(loaderPath);
        if (string.Equals(sha256, ReverseEngineeringSupportConstants.SupportedIdaxexDllSha256, StringComparison.OrdinalIgnoreCase))
            return new IdaLoaderStatus(loaderPath, sha256, "0.42b-compat / ida91 (supported)", true, false);
        if (string.Equals(sha256, ReverseEngineeringSupportConstants.KnownIdaxex43DllSha256, StringComparison.OrdinalIgnoreCase))
            return new IdaLoaderStatus(loaderPath, sha256, "0.43 / ida 9.2 build detected", false, true);
        return new IdaLoaderStatus(loaderPath, sha256, "present but unrecognized", false, false);
    }

    public static bool IsDatabasePath(string path) {
        string extension = Path.GetExtension(path);
        return extension.Equals(".i64", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".idb", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveDecompileBackend(string backendPreference, string inputPath, bool idalibAvailable) {
        string normalized = IdaPathHelpers.NormalizeBackend(backendPreference);
        if (normalized != "auto")
            return normalized;
        if (IsDatabasePath(inputPath) && idalibAvailable)
            return "idalib";
        return "batch";
    }

    public static string BuildAnalyzeArguments(string logPath, string outputDatabase, string scriptPath, string resultJsonPath, string inputPath) {
        string scriptCommand = string.Join(" ", new[] {
            QuoteArgument(scriptPath),
            QuoteArgument(resultJsonPath)
        });

        return string.Join(" ", new[] {
            "-A",
            "-c",
            "-Opdb:off",
            $"-L{QuoteArgument(logPath)}",
            $"-o{QuoteArgument(outputDatabase)}",
            $"-S{QuoteArgument(scriptCommand)}",
            QuoteArgument(inputPath)
        });
    }

    public static string BuildBatchDecompileArguments(string logPath, string scriptPath, string outputDirectory, int? maxFunctions, string resultJsonPath, string databasePath) {
        string limit = (maxFunctions ?? 0).ToString();
        string scriptCommand = string.Join(" ", new[] {
            QuoteArgument(scriptPath),
            QuoteArgument(outputDirectory),
            limit,
            QuoteArgument(resultJsonPath)
        });

        return string.Join(" ", new[] {
            "-A",
            $"-L{QuoteArgument(logPath)}",
            $"-S{QuoteArgument(scriptCommand)}",
            QuoteArgument(databasePath)
        });
    }

    public static ProcessStartInfo CreateIdaProcessStartInfo(string batchExe, string arguments, string inputPath, string userDirectory) {
        ProcessStartInfo psi = new ProcessStartInfo(batchExe) {
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Environment.CurrentDirectory
        };
        psi.Environment["IDAUSR"] = userDirectory;
        return psi;
    }

    public static async Task<bool> CanImportIdaproAsync(string pythonCommand, CancellationToken cancellationToken) {
        ProcessStartInfo psi = new ProcessStartInfo(pythonCommand);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("import idapro,sys; print('ok')");
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        ProcessRunResult result = await ReverseEngineeringProcessHelpers.RunAsync(psi, null, cancellationToken);
        return result.ExitCode == 0 &&
               (result.StandardOutput.Contains("ok", StringComparison.OrdinalIgnoreCase) ||
                result.StandardError.Contains("Loading IDA library", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task EnsureIdaproAvailableAsync(string pythonCommand, string idaHome, CancellationToken cancellationToken) {
        if (await CanImportIdaproAsync(pythonCommand, cancellationToken))
            return;

        string pythonModulePath = Path.Combine(idaHome, "idalib", "python");
        if (!Directory.Exists(pythonModulePath))
            throw new InvalidOperationException("IDALIB Python package was not found under the configured IDA install.");

        ProcessStartInfo pipInstall = new ProcessStartInfo(pythonCommand);
        pipInstall.ArgumentList.Add("-m");
        pipInstall.ArgumentList.Add("pip");
        pipInstall.ArgumentList.Add("install");
        pipInstall.ArgumentList.Add("--disable-pip-version-check");
        pipInstall.ArgumentList.Add("--upgrade");
        pipInstall.ArgumentList.Add(pythonModulePath);
        pipInstall.Environment["PYTHONIOENCODING"] = "utf-8";
        ProcessRunResult pipResult = await ReverseEngineeringProcessHelpers.RunAsync(
            pipInstall,
            Path.Combine(CliPaths.CachePath, "ida", "pip-install.log"),
            cancellationToken);
        if (pipResult.ExitCode != 0)
            throw new InvalidOperationException("Failed to install the idapro Python package. See the IDA pip log in the cache directory.");

        string activationScript = Path.Combine(pythonModulePath, "py-activate-idalib.py");
        ProcessStartInfo activate = new ProcessStartInfo(pythonCommand);
        activate.ArgumentList.Add(activationScript);
        activate.ArgumentList.Add("-d");
        activate.ArgumentList.Add(idaHome);
        activate.Environment["PYTHONIOENCODING"] = "utf-8";
        ProcessRunResult activationResult = await ReverseEngineeringProcessHelpers.RunAsync(
            activate,
            Path.Combine(CliPaths.CachePath, "ida", "idapro-activate.log"),
            cancellationToken);
        if (activationResult.ExitCode != 0)
            throw new InvalidOperationException("Failed to activate the idapro Python package for the configured IDA install.");

        if (!await CanImportIdaproAsync(pythonCommand, cancellationToken))
            throw new InvalidOperationException("The idapro Python module is still unavailable after activation.");
    }

    private static string QuoteArgument(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";
        return value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}

internal static class GhidraInstallHelpers {
    public static string? ResolveGhidraHome(CliConfig config, string? overridePath) {
        string? candidate = new[] { overridePath, config.GhidraPath, Environment.GetEnvironmentVariable("GHIDRA_HOME") }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(candidate) ? null : Path.GetFullPath(candidate);
    }

    public static string GetAnalyzeHeadlessPath(string ghidraHome) {
        return Path.Combine(ghidraHome, "support", "analyzeHeadless.bat");
    }

    public static string InstallXexLoader(string archivePath, string ghidraHome) {
        string extractRoot = Path.Combine(CliPaths.CachePath, "ghidra", $"extract-{Guid.NewGuid():N}");
        try {
            if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                ZipFile.ExtractToDirectory(archivePath, extractRoot, overwriteFiles: true);
            else
                HomebrewPackageService.ExtractArchive(archivePath, extractRoot);
            string loaderRoot = FindLoaderRoot(extractRoot);
            string targetRoot = Path.Combine(ghidraHome, "Ghidra", "Extensions", "XEXLoaderWV");
            Directory.CreateDirectory(Path.GetDirectoryName(targetRoot)!);
            InstallHelpers.MirrorDirectory(loaderRoot, targetRoot);

            string jarPath = Path.Combine(targetRoot, "lib", "XEXLoaderWV.jar");
            if (!File.Exists(jarPath))
                throw new InvalidDataException("XEXLoaderWV.jar was not found after installation.");
            return jarPath;
        }
        finally {
            ReverseEngineeringDownloadHelpers.TryDeleteDirectory(extractRoot);
        }
    }

    private static string FindLoaderRoot(string extractRoot) {
        string directJar = Path.Combine(extractRoot, "lib", "XEXLoaderWV.jar");
        string directProperties = Path.Combine(extractRoot, "extension.properties");
        if (File.Exists(directJar) && File.Exists(directProperties))
            return extractRoot;

        foreach (string jarPath in Directory.EnumerateFiles(extractRoot, "XEXLoaderWV.jar", SearchOption.AllDirectories)) {
            string libDir = Path.GetDirectoryName(jarPath)!;
            string? candidate = Directory.GetParent(libDir)?.FullName;
            if (!string.IsNullOrWhiteSpace(candidate) &&
                File.Exists(Path.Combine(candidate, "extension.properties"))) {
                return candidate;
            }
        }

        foreach (string file in Directory.EnumerateFiles(extractRoot, "extension.properties", SearchOption.AllDirectories)) {
            string candidate = Path.GetDirectoryName(file)!;
            if (File.Exists(Path.Combine(candidate, "lib", "XEXLoaderWV.jar")))
                return candidate;
        }

        throw new InvalidDataException("Could not locate an unpacked XEXLoaderWV extension folder in the archive.");
    }
}
