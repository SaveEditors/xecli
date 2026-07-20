using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Spectre.Console;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

internal sealed record IdaxexPayloadSpec(
    string ArchivePath,
    string InstallPath,
    string Sha256);

internal sealed record IdaxexBuildSpec(
    string Label,
    string Owner,
    string Repository,
    string Tag,
    string Version,
    string ArchiveFileName,
    string ArchiveUrl,
    string ArchiveSha256,
    string DllSha256,
    string IdaDisplayVersion,
    string[] SupportedIdaVersionPrefixes,
    IReadOnlyList<IdaxexPayloadSpec> Payloads);

internal static class ReverseEngineeringSupportConstants {
    private static readonly IdaxexBuildSpec[] SupportedIdaxexBuilds = new[] {
        new IdaxexBuildSpec(
            "SaveEditors idaxex 9.3",
            "SaveEditors",
            "idaxex",
            "ida-pro-9.3-saveeditors-1",
            "ida-pro-9.3-saveeditors-1",
            "idaxex-ida-pro-9.3-saveeditors-1.zip",
            "https://github.com/SaveEditors/idaxex/releases/download/ida-pro-9.3-saveeditors-1/idaxex-ida-pro-9.3-saveeditors-1.zip",
            "1734277D26FF4985F15931B6855368312EC11D619D53D2C104F9F7C5753BBD7C",
            "13F73208707B5736666930C46A3F9A5EC213D38A56917247A4BCFF15A76226A0",
            "IDA Pro 9.3",
            new[] { "9.3." },
            new[] {
                new IdaxexPayloadSpec("loaders/idaxex.dll", "loaders/idaxex.dll", "13F73208707B5736666930C46A3F9A5EC213D38A56917247A4BCFF15A76226A0"),
                new IdaxexPayloadSpec("til/x360.til", "til/x360.til", "86BE7BA91AA9B4D793252155E53AD1C42967741EA54078C6060DD0ED69732DCA"),
                new IdaxexPayloadSpec("til/xkelib.til", "til/xkelib.til", "86BE7BA91AA9B4D793252155E53AD1C42967741EA54078C6060DD0ED69732DCA")
            }),
        new IdaxexBuildSpec(
            "emoose idaxex 0.42b legacy archive",
            "emoose",
            "idaxex",
            "0.42b",
            "0.42b",
            "idaxex+xex1tool-0.42b_ida91.zip",
            "https://github.com/emoose/idaxex/releases/download/0.42b/idaxex%2Bxex1tool-0.42b_ida91.zip",
            "49F7C519C4A0BF7E90AF3411554B00591C0C628676E1CBA62A7EAA6216BDCD89",
            "97581B47D3E1C7306B8BAA289C6A4EF68D736078C87ACCC63F70A396CC497946",
            "IDA Pro 9.1.250226",
            new[] { "9.1." },
            new[] {
                new IdaxexPayloadSpec("ida91/loaders/idaxex.dll", "loaders/idaxex.dll", "97581B47D3E1C7306B8BAA289C6A4EF68D736078C87ACCC63F70A396CC497946"),
                new IdaxexPayloadSpec("ida91/loaders/idaxex.so", "loaders/idaxex.so", "91AFEC579D1F8D55F2B47FAF2E6572796764F425695EC52C5BBDD53F180BE02E"),
                new IdaxexPayloadSpec("ida91/til/ppc/x360.til", "til/ppc/x360.til", "EB129196E9821C772285560EF0AD5137D8C25D7529C1C73458320FB19DEB284E"),
                new IdaxexPayloadSpec("ida91/til/ppc/xkelib.til", "til/ppc/xkelib.til", "D830B1ECA9B80942815A8E42E73A329CFB38D52D406039131573A08E2FFF73C6"),
                new IdaxexPayloadSpec("xex1tool.exe", "xex1tool.exe", "74A62DF6B3DC80E35BF69924D3A826D113AA38BC8934DD2F776854763E39A279")
            })
    };

    public const string KnownIdaxex43DllSha256 = "DA2BC0245A3A06721CCE3804777D63E31B3CF33A4507A1B3B07A966B16F09030";
    public const string GhidraLoaderOwner = "SaveEditors";
    public const string GhidraLoaderRepository = "XEXLoaderWV";
    public const string GhidraLoaderTag = "13.0.0";
    public const string GhidraLoaderVersion = "13.0.0";
    public const string GhidraLoaderArchiveFileName = "ghidra_12.0.4_PUBLIC_20260325_XEXLoaderWV.zip";
    public const string GhidraLoaderArchiveUrl = "https://github.com/SaveEditors/XEXLoaderWV/releases/download/13.0.0/ghidra_12.0.4_PUBLIC_20260325_XEXLoaderWV.zip";
    public const string GhidraLoaderArchiveSha256 = "498B9C2A2430585CC49A13DB33603B6A46CFE84B157985F9BE2C4360F917FA5A";
    public const string GhidraOfficialUrl = "https://ghidra-sre.org/";
    public const string IdaOfficialUrl = "https://hex-rays.com/ida-pro";
    public const string XboxReversingUrl = "https://github.com/emoose/xbox-reversing/tree/master";

    public static IdaxexBuildSpec CurrentIdaxexBuild => SupportedIdaxexBuilds[0];
    public static IReadOnlyList<IdaxexBuildSpec> SupportedIdaxexBuildsView => SupportedIdaxexBuilds;
    public static string SupportedIdaDisplayVersion => CurrentIdaxexBuild.IdaDisplayVersion;
    public static string SupportedIdaxexLabel => CurrentIdaxexBuild.Label;
    public static string SupportedIdaxexArchiveUrl => CurrentIdaxexBuild.ArchiveUrl;
    public static string SupportedIdaxexArchiveSha256 => CurrentIdaxexBuild.ArchiveSha256;
    public static string SupportedIdaxexDllSha256 => CurrentIdaxexBuild.DllSha256;
    public static string SupportedIdaSummary => string.Join(", ", SupportedIdaxexBuilds.Select(build => $"{build.IdaDisplayVersion} / {build.Label}"));

    public static IdaxexBuildSpec? ResolveSupportedIdaxexBuild(string sha256) {
        return SupportedIdaxexBuilds.FirstOrDefault(build => string.Equals(build.DllSha256, sha256, StringComparison.OrdinalIgnoreCase));
    }

    public static IdaxexBuildSpec? ResolveSupportedIdaxexBuildByArchiveUrl(string archiveUrl) {
        return SupportedIdaxexBuilds.FirstOrDefault(build => string.Equals(build.ArchiveUrl, archiveUrl, StringComparison.OrdinalIgnoreCase));
    }

    public static IdaxexBuildSpec? ResolveSupportedIdaxexBuildForProductVersion(string? productVersion) {
        if (string.IsNullOrWhiteSpace(productVersion))
            return null;

        return SupportedIdaxexBuilds.FirstOrDefault(build =>
            build.SupportedIdaVersionPrefixes.Any(prefix =>
                productVersion.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }
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
    public static void WriteGhidraNotice(bool json) {
        if (!json)
            WriteGhidraNotice();
    }

    public static void WriteGhidraNotice() {
        AnsiConsole.MarkupLine(
            $"[grey]Ghidra is [white]Free[/] and external. XeCLI installs the pinned [white]{Markup.Escape(ReverseEngineeringSupportConstants.GhidraLoaderOwner)}/{Markup.Escape(ReverseEngineeringSupportConstants.GhidraLoaderRepository)} {Markup.Escape(ReverseEngineeringSupportConstants.GhidraLoaderTag)}[/] XEXLoaderWV archive after configuring the Ghidra path.[/]");
    }

    public static void WriteIdaNotice(bool json) {
        if (!json)
            WriteIdaNotice();
    }

    public static void WriteIdaNotice() {
        AnsiConsole.MarkupLine(
            $"[grey]IDA is external and not bundled. Baseline support requires [white]{Markup.Escape(ReverseEngineeringSupportConstants.SupportedIdaDisplayVersion)}[/] with [white]{Markup.Escape(ReverseEngineeringSupportConstants.SupportedIdaxexLabel)}[/]; legacy support requires [white]IDA Pro 9.1.250226[/] with the pinned [white]emoose/idaxex 0.42b[/] archive, including xex1tool. Configure IDA, then run [cyan]rgh ida install-loader[/].[/]");
    }
}

internal static class IdaPathHelpers {
    private static readonly string[] BatchExecutableNames = new[] { "idat64.exe", "idat.exe", "ida64.exe", "ida.exe" };

    public static string? ResolveIdaHome(CliConfig config, string? overridePath) {
        string? candidate = FirstNonEmpty(overridePath, config.IdaPath, Environment.GetEnvironmentVariable("IDA_HOME"), Environment.GetEnvironmentVariable("IDADIR"));
        return string.IsNullOrWhiteSpace(candidate) ? null : Path.GetFullPath(candidate);
    }

    public static string ResolveBatchExe(string idaHome) {
        foreach (string executableName in BatchExecutableNames) {
            string candidate = Path.Combine(idaHome, executableName);
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(idaHome, "idat.exe");
    }

    public static string? ResolveActivationScript(string idaHome) {
        if (string.IsNullOrWhiteSpace(idaHome) || !Directory.Exists(idaHome))
            return null;

        foreach (string candidate in new[] {
                     Path.Combine(idaHome, "py-activate-idalib.py"),
                     Path.Combine(idaHome, "idalib", "python", "py-activate-idalib.py")
                 }) {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
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

    public static bool TryNormalizeSha256(string? value, out string normalized, out string errorMessage) {
        normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 64 || !normalized.All(IsHexDigit)) {
            normalized = string.Empty;
            errorMessage = "Expected a 64-character SHA256 hex digest.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public static bool VerifySha256(string filePath, string expectedSha256, out string actualSha256, out string errorMessage) {
        actualSha256 = ComputeSha256(filePath);
        if (!TryNormalizeSha256(expectedSha256, out string normalizedExpected, out errorMessage))
            return false;

        byte[] actualBytes = Convert.FromHexString(actualSha256);
        byte[] expectedBytes = Convert.FromHexString(normalizedExpected);
        if (!CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes)) {
            errorMessage = $"Expected SHA256 {normalizedExpected}, got {actualSha256}.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
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

    private static bool IsHexDigit(char value) {
        return (value >= '0' && value <= '9') ||
               (value >= 'A' && value <= 'F');
    }

    private static HttpClient CreateHttpClient() {
        HttpClient client = HomebrewPackageService.CreateHttpClient();
        CliHttpUserAgent.Apply(client);
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
        return TryGetProductVersion(
            batchExe,
            File.Exists,
            path => FileVersionInfo.GetVersionInfo(path).ProductVersion);
    }

    internal static string? TryGetProductVersion(
        string batchExe,
        Func<string, bool> fileExists,
        Func<string, string?> readProductVersion) {
        if (!fileExists(batchExe))
            return null;

        string? batchVersion = NormalizeProductVersion(readProductVersion(batchExe));
        if (!string.IsNullOrWhiteSpace(batchVersion))
            return batchVersion;

        string? installRoot = Path.GetDirectoryName(Path.GetFullPath(batchExe));
        if (string.IsNullOrWhiteSpace(installRoot))
            return null;

        foreach (string executableName in new[] { "ida64.exe", "ida.exe", "idat64.exe", "idat.exe" }) {
            string candidate = Path.Combine(installRoot, executableName);
            if (!fileExists(candidate))
                continue;

            string? siblingVersion = NormalizeProductVersion(readProductVersion(candidate));
            if (!string.IsNullOrWhiteSpace(siblingVersion))
                return siblingVersion;
        }

        return null;
    }

    private static string? NormalizeProductVersion(string? productVersion) {
        return string.IsNullOrWhiteSpace(productVersion) ? null : productVersion.Trim();
    }

    public static bool IsSupportedProductVersion(string? productVersion) {
        return ReverseEngineeringSupportConstants.ResolveSupportedIdaxexBuildForProductVersion(productVersion) != null;
    }

    public static string DescribeProductVersion(string? productVersion) {
        if (string.IsNullOrWhiteSpace(productVersion))
            return "unknown";

        IdaxexBuildSpec? supportedBuild = ReverseEngineeringSupportConstants.ResolveSupportedIdaxexBuildForProductVersion(productVersion);

        if (supportedBuild != null)
            return $"{productVersion} ({supportedBuild.Label}, supported)";

        return $"{productVersion} (unsupported)";
    }

    public static IdaLoaderStatus GetLoaderStatus(string idaHome) {
        string loaderPath = Path.Combine(idaHome, "loaders", "idaxex.dll");
        if (!File.Exists(loaderPath))
            return new IdaLoaderStatus(null, null, "missing", false, false);

        string sha256 = ReverseEngineeringDownloadHelpers.ComputeSha256(loaderPath);
        IdaxexBuildSpec? supportedBuild = ReverseEngineeringSupportConstants.ResolveSupportedIdaxexBuild(sha256);
        if (supportedBuild != null)
            return new IdaLoaderStatus(loaderPath, sha256, $"{supportedBuild.Label} (supported)", true, false);
        if (string.Equals(sha256, ReverseEngineeringSupportConstants.KnownIdaxex43DllSha256, StringComparison.OrdinalIgnoreCase))
            return new IdaLoaderStatus(loaderPath, sha256, "SaveEditors idaxex 0.43 / IDA 9.2 build detected", false, true);
        return new IdaLoaderStatus(loaderPath, sha256, "present but unrecognized", false, false);
    }

    public static bool IsLoaderCompatibleWithProductVersion(IdaLoaderStatus loaderStatus, string? productVersion) {
        IdaxexBuildSpec? expectedBuild = ReverseEngineeringSupportConstants.ResolveSupportedIdaxexBuildForProductVersion(productVersion);
        return loaderStatus.IsSupported &&
               expectedBuild != null &&
               string.Equals(loaderStatus.Sha256, expectedBuild.DllSha256, StringComparison.OrdinalIgnoreCase);
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

    public static bool IsPreferredBackendReady(string backendPreference, bool batchBackendReady, bool idalibBackendReady) {
        string normalized = IdaPathHelpers.NormalizeBackend(backendPreference);
        return normalized == "idalib" ? idalibBackendReady : batchBackendReady;
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

    public static string BuildBatchExportSymbolsArguments(string logPath, string scriptPath, string outputJsonPath, string moduleName, string databasePath) {
        string scriptCommand = string.Join(" ", new[] {
            QuoteArgument(scriptPath),
            QuoteArgument(outputJsonPath),
            QuoteArgument(moduleName)
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

    public static IReadOnlyList<string> FindInstalledXexLoaderRoots(string ghidraHome) {
        string modulesRoot = Path.Combine(ghidraHome, "Ghidra");
        if (!Directory.Exists(modulesRoot))
            return Array.Empty<string>();

        try {
            return Directory.EnumerateFiles(modulesRoot, "XEXLoaderWV.jar", SearchOption.AllDirectories)
                .Select(jarPath => Directory.GetParent(Path.GetDirectoryName(jarPath)!)?.FullName)
                .Where(root => !string.IsNullOrWhiteSpace(root) &&
                               string.Equals(Path.GetFileName(root), "XEXLoaderWV", StringComparison.OrdinalIgnoreCase) &&
                               File.Exists(Path.Combine(root, "extension.properties")))
                .Select(root => Path.GetFullPath(root!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException) {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException) {
            return Array.Empty<string>();
        }
    }

    public static string InstallXexLoader(string archivePath, string ghidraHome) {
        string extractRoot = Path.Combine(CliPaths.CachePath, "ghidra", $"extract-{Guid.NewGuid():N}");
        try {
            if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                ZipFile.ExtractToDirectory(archivePath, extractRoot, overwriteFiles: true);
            else
                HomebrewPackageService.ExtractArchive(archivePath, extractRoot);
            string loaderRoot = FindLoaderRoot(extractRoot);
            IReadOnlyList<string> installedRoots = FindInstalledXexLoaderRoots(ghidraHome);
            if (installedRoots.Count > 1) {
                throw new InvalidOperationException(
                    "Multiple XEXLoaderWV modules are installed. Remove the duplicate module before updating: " +
                    string.Join("; ", installedRoots));
            }

            string targetRoot = installedRoots.Count == 1
                ? installedRoots[0]
                : Path.Combine(ghidraHome, "Ghidra", "Extensions", "XEXLoaderWV");
            string sourceJarPath = Path.Combine(loaderRoot, "lib", "XEXLoaderWV.jar");
            string targetJarPath = Path.Combine(targetRoot, "lib", "XEXLoaderWV.jar");
            if (File.Exists(targetJarPath) && FilesHaveSameSha256(sourceJarPath, targetJarPath))
                return targetJarPath;

            Directory.CreateDirectory(Path.GetDirectoryName(targetRoot)!);
            InstallHelpers.MirrorDirectory(loaderRoot, targetRoot);

            if (!File.Exists(targetJarPath))
                throw new InvalidDataException("XEXLoaderWV.jar was not found after installation.");
            return targetJarPath;
        }
        finally {
            ReverseEngineeringDownloadHelpers.TryDeleteDirectory(extractRoot);
        }
    }

    private static bool FilesHaveSameSha256(string leftPath, string rightPath) {
        using FileStream left = File.OpenRead(leftPath);
        using FileStream right = File.OpenRead(rightPath);
        byte[] leftHash = SHA256.HashData(left);
        byte[] rightHash = SHA256.HashData(right);
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
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
