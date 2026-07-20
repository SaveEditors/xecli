using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ReleaseCheckCommand : Command<ReleaseCheckCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--publish-dir <DIR>")]
        [LocalizedDescription("Published release directory to validate.")]
        public string PublishDir { get; init; } = Path.Combine("out", "win-x64");

        [CommandOption("--project <FILE>")]
        [LocalizedDescription("Project file used for AppVersion comparison.")]
        public string? ProjectPath { get; init; } = Path.Combine("src", "Xbox360.Remote.Cli", "Xbox360.Remote.Cli.csproj");

        [CommandOption("--zip <FILE>")]
        [LocalizedDescription("Release zip to validate against its SHA256 sidecar; required with --promotion.")]
        public string? ZipPath { get; init; }

        [CommandOption("--hash <FILE>")]
        [LocalizedDescription("SHA256 sidecar for --zip; required with --promotion and otherwise defaults to <zip>.sha256.")]
        public string? HashPath { get; init; }

        [CommandOption("--summary <FILE>")]
        [LocalizedDescription("Release summary whose artifact sidecars must be validated; required with --promotion.")]
        public string? SummaryPath { get; init; }

        [CommandOption("--expected-release-kind <KIND>")]
        [LocalizedDescription("Require an exact recognized ReleaseKind.")]
        public string? ExpectedReleaseKind { get; init; }

        [CommandOption("--promotion")]
        [LocalizedDescription("Apply fail-closed public promotion policy checks.")]
        public bool Promotion { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        ReleaseCheckRequest request = ReleaseCheckRequest.From(settings);
        ReleaseCheckReport report = ReleaseCheckEngine.Check(request);

        if (settings.Json) {
            CliOutput.EmitJson(report);
            return report.Passed ? 0 : 1;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Release Check[/]").RuleStyle("silver"));

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Check[/]"));
        table.AddColumn(new TableColumn("[white]Status[/]"));
        table.AddColumn(new TableColumn("[white]Code[/]"));
        table.AddColumn(new TableColumn("[white]Detail[/]"));

        foreach (ReleaseCheckItem check in report.Checks) {
            table.AddRow(
                $"[white]{Markup.Escape(check.Name)}[/]",
                $"[{check.StatusColor}]{Markup.Escape(check.Status)}[/]",
                $"[grey]{Markup.Escape(check.Code)}[/]",
                Markup.Escape(check.Detail));
        }

        AnsiConsole.Write(table);

        if (report.Passed) {
            if (report.WarningCount > 0) {
                OperationFeedback.WriteSuccess(
                    "Release check passed with warnings",
                    $"{report.WarningCount} warning(s); {report.Checks.Count} check(s) reviewed.");
            }
            else {
                OperationFeedback.WriteSuccess(
                    "Release check passed",
                    $"{report.Checks.Count} check(s) reviewed.");
            }
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold red]Failed Checks[/]").RuleStyle("red"));
        foreach (ReleaseCheckItem failure in report.Checks.Where(check => check.Status.Equals("fail", StringComparison.OrdinalIgnoreCase))) {
            AnsiConsole.MarkupLine($"[red]- {Markup.Escape(failure.Code)}[/] {Markup.Escape(failure.Detail)}");
        }
        OperationFeedback.WriteFailure(
            "Release check failed",
            $"{report.FailureCount} check(s) failed; {report.WarningCount} warning(s).");
        return 1;
    }
}

internal sealed record ReleaseCheckRequest(
    string PublishDir,
    string? ProjectPath,
    string? ZipPath,
    string? HashPath,
    string? SummaryPath,
    string? ExpectedReleaseKind,
    bool Promotion) {
    public ReleaseCheckRequest(string publishDir, string? projectPath, string? zipPath, string? hashPath)
        : this(publishDir, projectPath, zipPath, hashPath, null, null, false) { }

    public static ReleaseCheckRequest From(ReleaseCheckCommand.Settings settings) {
        return new ReleaseCheckRequest(
            Path.GetFullPath(settings.PublishDir),
            string.IsNullOrWhiteSpace(settings.ProjectPath) ? null : Path.GetFullPath(settings.ProjectPath),
            string.IsNullOrWhiteSpace(settings.ZipPath) ? null : Path.GetFullPath(settings.ZipPath),
            string.IsNullOrWhiteSpace(settings.HashPath) ? null : Path.GetFullPath(settings.HashPath),
            string.IsNullOrWhiteSpace(settings.SummaryPath) ? null : Path.GetFullPath(settings.SummaryPath),
            string.IsNullOrWhiteSpace(settings.ExpectedReleaseKind) ? null : settings.ExpectedReleaseKind.Trim(),
            settings.Promotion);
    }
}

internal sealed record ReleaseCheckReport(
    string PublishDir,
    string? ProjectPath,
    string? ZipPath,
    string? HashPath,
    IReadOnlyList<ReleaseCheckItem> Checks) {
    public bool Passed => FailureCount == 0;
    public int FailureCount => Checks.Count(check => check.Status.Equals("fail", StringComparison.OrdinalIgnoreCase));
    public int WarningCount => Checks.Count(check => check.Status.Equals("warn", StringComparison.OrdinalIgnoreCase));
}

internal sealed record ReleaseCheckItem(
    string Name,
    string Status,
    string Detail,
    string Code) {
    [JsonIgnore]
    public string StatusColor => Status.ToLowerInvariant() switch {
        "ok" => "green",
        "warn" => "yellow",
        "fail" => "red",
        "skipped" => "grey",
        _ => "white"
    };
}

internal static class ReleaseCheckEngine {
    private const string PublicUnsignedWarning = "WARNING: Publisher is Unknown. Windows SmartScreen may warn or block this unsigned public release. Verify the SHA256 inventory before running any artifact.";
    private const string QuickBootX360DllSha256 = "F90691B92A91FFA941BB111039DE031907A82A510C777231FE9C6D356DA437A9";
    private const string InstallerOwnedFilesManifestName = "xecli-owned-files.txt";
    private const string InstallerOwnedHashesManifestName = "xecli-owned-hashes.sha256";
    private const string ReleaseManifestFileName = "release-manifest.json";
    private const string FirstPartyPayloadManifestPath = "PROVENANCE/CONSOLE-PAYLOADS.sha256";
    private const string CorrespondingSourceNoticePath = "PROVENANCE/CORRESPONDING-SOURCE.md";
    private const string PublicRepositoryUrl = "https://github.com/SaveEditors/xecli";
    private static readonly HashSet<string> RecognizedReleaseKinds = new(StringComparer.Ordinal) {
        "local-unsigned",
        "public-unsigned",
        "promotion-signed"
    };
    private static readonly HashSet<string> RecognizedInstallerStatuses = new(StringComparer.Ordinal) {
        "not-requested",
        "built-unsigned",
        "built-signed"
    };

    internal static readonly string[] RequiredPaths = [
        "rgh.exe",
        "rgh.dll",
        "rgh.deps.json",
        "rgh.runtimeconfig.json",
        "XeTerminal.exe",
        "XeTerminal.dll",
        "XeTerminal.deps.json",
        "XeTerminal.runtimeconfig.json",
        "Xbox360.Remote.dll",
        "Xbox360.Fatx.dll",
        "Assets",
        "Assets\\XellLaunch\\default.xex",
        "Assets\\XellLaunch\\xell.bin",
        "Assets\\QuickBoot\\default.xex",
        "Assets\\QuickBoot\\X360.dll",
        "ghidra_scripts",
        "ghidra_scripts\\DecompileAllToC.java",
        "ghidra_scripts\\ExportSymbols.java",
        "ida_scripts",
        "ida_scripts\\analyze_xex.py",
        "ida_scripts\\decompile_xex.py",
        "ida_scripts\\export_symbols.py",
        "ida_scripts\\idalib_decompile.py",
        "LICENSE",
        "THIRD-PARTY-NOTICES.md",
        "THIRD-PARTY-LICENSES",
        "THIRD-PARTY-LICENSES\\MIT.txt",
        "THIRD-PARTY-LICENSES\\dotnet-10.0.0-THIRD-PARTY-NOTICES.txt",
        "THIRD-PARTY-LICENSES\\XeLL",
        "THIRD-PARTY-LICENSES\\XeLL\\ADDITIONAL-NOTICES.txt",
        "THIRD-PARTY-LICENSES\\XeLL\\GCC-RUNTIME-LIBRARY-EXCEPTION.txt",
        "THIRD-PARTY-LICENSES\\XeLL\\GPL-2.0.txt",
        "THIRD-PARTY-LICENSES\\XeLL\\NEWLIB-NOTICES.txt",
        "PROVENANCE",
        "PROVENANCE\\CONSOLE-PAYLOADS.sha256",
        "PROVENANCE\\CORRESPONDING-SOURCE.md"
    ];
    internal static readonly string[] ForbiddenTitleDataPaths = [
        "Assets\\xbox360_gamelist.csv",
        "Assets\\xbox360_titleids.txt"
    ];
    internal static readonly string[] RequiredThirdPartyLicenseFiles = [
        "MIT.txt",
        "dotnet-10.0.0-THIRD-PARTY-NOTICES.txt",
        "XeLL/ADDITIONAL-NOTICES.txt",
        "XeLL/GCC-RUNTIME-LIBRARY-EXCEPTION.txt",
        "XeLL/GPL-2.0.txt",
        "XeLL/NEWLIB-NOTICES.txt"
    ];
    internal static readonly IReadOnlyDictionary<string, string> RequiredFileSha256 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["Assets/XellLaunch/xell.bin"] = "E364FAD816CD651B1F27C1A7DCD5AE8A0C3A3C70BF11D04B6A66123A38AF2C9C",
        ["Assets/XellLaunch/default.xex"] = "414492CE7DED99D9D8EFE7E35407168CDF4F357C0B4BBFF0CF6F27E473A7B2EC",
        ["Assets/QuickBoot/default.xex"] = "C225F3E174FBF551D76BAA64567B75F018CF70D2DBA4929697A69E04DE1FE6C3",
        ["Assets/QuickBoot/X360.dll"] = QuickBootX360DllSha256
    };
    private static readonly IReadOnlyDictionary<string, string> FirstPartyPayloadStagePaths = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["payload/xell.bin"] = "Assets/XellLaunch/xell.bin",
        ["launch/XellLaunch/default.xex"] = "Assets/XellLaunch/default.xex",
        ["launch/QuickBoot/default.xex"] = "Assets/QuickBoot/default.xex"
    };

    public static ReleaseCheckReport Check(ReleaseCheckRequest request) {
        List<ReleaseCheckItem> checks = [];
        string publishDir = request.PublishDir;

        ValidateRequestedPolicy(request, checks);

        try {
            EnsureNoReparsePointsInExistingPath(publishDir, "Publish directory");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) {
            checks.Add(Fail("Publish directory", "publish-dir-reparse-point", ex.Message));
            return new ReleaseCheckReport(publishDir, request.ProjectPath, request.ZipPath, request.HashPath, checks);
        }

        if (!Directory.Exists(publishDir)) {
            checks.Add(Fail("Publish directory", "publish-dir-missing", $"Publish directory was not found: {publishDir}"));
            return new ReleaseCheckReport(publishDir, request.ProjectPath, request.ZipPath, request.HashPath, checks);
        }

        try {
            int fileCount = EnumerateFilesWithoutReparsePoints(publishDir).Count();
            checks.Add(Ok("Staging filesystem", "staging-no-reparse-points", $"Validated {fileCount} staged file(s) without reparse points"));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) {
            checks.Add(Fail("Staging filesystem", "staging-reparse-point", ex.Message));
            return new ReleaseCheckReport(publishDir, request.ProjectPath, request.ZipPath, request.HashPath, checks);
        }

        ValidatePortableMarker(publishDir, checks);

        string? manifestPath = Path.Combine(publishDir, "release-manifest.json");
        ReleaseManifest? manifest = null;

        if (!File.Exists(manifestPath)) {
            checks.Add(Fail("Release manifest", "manifest-missing", $"Release manifest was not found at {manifestPath}"));
        }
        else {
            try {
                manifest = LoadManifest(manifestPath);
                checks.Add(Ok("Release manifest", "manifest-present", $"Loaded {Path.GetFileName(manifestPath)}"));
            }
            catch (Exception ex) {
                checks.Add(Fail("Release manifest", "manifest-invalid", $"Release manifest is malformed: {ex.Message}"));
            }
        }

        foreach (string relativePath in RequiredPaths) {
            string fullPath = Path.Combine(publishDir, relativePath);
            if (Directory.Exists(fullPath) || File.Exists(fullPath)) {
                checks.Add(Ok("Required file", $"required-{NormalizeCode(relativePath)}", relativePath));
            }
            else {
                checks.Add(Fail("Required file", $"missing-{NormalizeCode(relativePath)}", $"Missing required release item: {fullPath}"));
            }
        }

        if (manifest != null) {
            ValidateManifestMetadata(request, manifest, checks);
            ValidateManifestBody(publishDir, manifest, checks);
            ValidateFirstPartyPayloadProvenance(publishDir, manifest, checks);
            ValidateAppVersion(request, manifest, checks);
            ValidateGitRevision(request, manifest, checks);
        }
        else {
            checks.Add(Warn("Project version", "version-skipped", "Skipped because the release manifest was not readable."));
            checks.Add(Warn("Git revision", "git-skipped", "Skipped because the release manifest was not readable."));
        }

        if (!string.IsNullOrWhiteSpace(request.ZipPath)) {
            ValidateZipHash(request, checks);
            ValidateZipBody(request, checks);
        }

        foreach (string relativePath in ForbiddenTitleDataPaths) {
            string fullPath = Path.Combine(publishDir, relativePath);
            if (File.Exists(fullPath) || Directory.Exists(fullPath)) {
                checks.Add(Fail("Title data inventory", $"forbidden-{NormalizeCode(relativePath)}", $"Bundled title data must not appear in a release: {fullPath}"));
            }
            else {
                checks.Add(Ok("Title data inventory", $"absent-{NormalizeCode(relativePath)}", $"Absent as required: {relativePath}"));
            }
        }
        ValidateThirdPartyLicenseInventory(publishDir, checks);
        ValidateRequiredFileHashes(publishDir, checks);

        if (manifest != null && !string.IsNullOrWhiteSpace(request.SummaryPath)) {
            ValidateReleaseSummary(request, manifest, checks);
        }

        return new ReleaseCheckReport(publishDir, request.ProjectPath, request.ZipPath, request.HashPath, checks);
    }

    private static void ValidateRequiredFileHashes(string publishDir, List<ReleaseCheckItem> checks) {
        foreach ((string relativePath, string expectedHash) in RequiredFileSha256) {
            string fullPath = Path.Combine(publishDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                continue;

            try {
                string actualHash = ComputeSha256(fullPath);
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) {
                    checks.Add(Fail("Required file hash", $"hash-{NormalizeCode(relativePath)}", $"{relativePath} must have SHA256 {expectedHash}; found {actualHash}."));
                }
                else {
                    checks.Add(Ok("Required file hash", $"hash-{NormalizeCode(relativePath)}", $"{relativePath} matches {expectedHash}"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                checks.Add(Fail("Required file hash", $"hash-{NormalizeCode(relativePath)}", $"Could not hash {relativePath}: {ex.Message}"));
            }
        }
    }

    private static void ValidateRequestedPolicy(ReleaseCheckRequest request, List<ReleaseCheckItem> checks) {
        bool policyValid = true;
        if (!string.IsNullOrWhiteSpace(request.ExpectedReleaseKind) && !RecognizedReleaseKinds.Contains(request.ExpectedReleaseKind)) {
            checks.Add(Fail("Release policy", "expected-release-kind-unknown", $"Expected ReleaseKind is not recognized: {request.ExpectedReleaseKind}"));
            policyValid = false;
        }

        if (!request.Promotion) {
            if (policyValid) {
                checks.Add(Ok("Release policy", "release-policy-explicit", $"Expected ReleaseKind is {request.ExpectedReleaseKind ?? "not specified"}"));
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedReleaseKind)) {
            checks.Add(Fail("Release policy", "promotion-policy-missing", "Promotion validation requires --expected-release-kind public-unsigned or promotion-signed."));
            policyValid = false;
        }
        else if (request.ExpectedReleaseKind == "local-unsigned") {
            checks.Add(Fail("Release policy", "promotion-local-unsigned", "local-unsigned is never eligible for promotion."));
            policyValid = false;
        }
        else if (request.ExpectedReleaseKind is not ("public-unsigned" or "promotion-signed")) {
            checks.Add(Fail("Release policy", "promotion-release-kind-invalid", "Promotion validation only accepts public-unsigned or promotion-signed."));
            policyValid = false;
        }

        if (string.IsNullOrWhiteSpace(request.ZipPath)) {
            checks.Add(Fail("Release policy", "promotion-zip-missing", "Promotion validation requires a nonempty --zip path."));
            policyValid = false;
        }
        if (string.IsNullOrWhiteSpace(request.HashPath)) {
            checks.Add(Fail("Release policy", "promotion-hash-missing", "Promotion validation requires a nonempty --hash path."));
            policyValid = false;
        }
        if (string.IsNullOrWhiteSpace(request.SummaryPath)) {
            checks.Add(Fail("Release policy", "promotion-summary-missing", "Promotion validation requires a nonempty --summary path."));
            policyValid = false;
        }

        if (policyValid) {
            checks.Add(Ok("Release policy", "release-policy-explicit", $"Promotion expects {request.ExpectedReleaseKind}"));
        }
    }

    private static void ValidatePortableMarker(string publishDir, List<ReleaseCheckItem> checks) {
        string markerPath = Path.Combine(publishDir, CliPaths.PortableMarkerFileName);
        if (!File.Exists(markerPath)) {
            checks.Add(Fail("Portable marker", "portable-marker-missing", $"Portable staging requires {CliPaths.PortableMarkerFileName}."));
            return;
        }

        if (new FileInfo(markerPath).Length != 0) {
            checks.Add(Fail("Portable marker", "portable-marker-invalid", $"{CliPaths.PortableMarkerFileName} must be an empty marker file."));
            return;
        }

        checks.Add(Ok("Portable marker", "portable-marker-present", $"Loaded empty {CliPaths.PortableMarkerFileName}"));
    }

    private static void ValidateManifestMetadata(ReleaseCheckRequest request, ReleaseManifest manifest, List<ReleaseCheckItem> checks) {
        if (manifest.SchemaVersion != 2) {
            checks.Add(Fail("Manifest metadata", "manifest-schema-invalid", $"Unsupported release manifest schema: {manifest.SchemaVersion}"));
        }
        else {
            checks.Add(Ok("Manifest metadata", "manifest-schema-valid", "SchemaVersion is 2"));
        }

        if (!Regex.IsMatch(manifest.AppVersion, "^[0-9]+[.][0-9]+[.][0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?(?:[+][0-9A-Za-z][0-9A-Za-z.-]*)?$")) {
            checks.Add(Fail("Manifest metadata", "manifest-version-invalid", $"AppVersion is not an explicit release version: {manifest.AppVersion}"));
        }
        else {
            checks.Add(Ok("Manifest metadata", "manifest-version-valid", $"AppVersion is {manifest.AppVersion}"));
        }

        if (!string.Equals(manifest.Runtime, "win-x64", StringComparison.Ordinal)) {
            checks.Add(Fail("Manifest metadata", "manifest-runtime-invalid", $"Runtime must be win-x64, found {manifest.Runtime ?? "missing"}."));
        }
        else {
            checks.Add(Ok("Manifest metadata", "manifest-runtime-valid", "Runtime is win-x64"));
        }

        if (string.IsNullOrWhiteSpace(manifest.ReleaseKind) || !RecognizedReleaseKinds.Contains(manifest.ReleaseKind)) {
            checks.Add(Fail("Manifest metadata", "manifest-release-kind-invalid", $"ReleaseKind is not recognized: {manifest.ReleaseKind ?? "missing"}"));
        }
        else {
            checks.Add(Ok("Manifest metadata", "manifest-release-kind-valid", $"ReleaseKind is {manifest.ReleaseKind}"));
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedReleaseKind)
            && !string.Equals(manifest.ReleaseKind, request.ExpectedReleaseKind, StringComparison.Ordinal)) {
            checks.Add(Fail("Release policy", "release-kind-mismatch", $"Expected ReleaseKind {request.ExpectedReleaseKind} but found {manifest.ReleaseKind ?? "missing"}."));
        }

        if (request.Promotion && manifest.ReleaseKind == "local-unsigned") {
            checks.Add(Fail("Release policy", "promotion-manifest-local-unsigned", "A local-unsigned manifest cannot be promoted."));
        }

        if (string.IsNullOrWhiteSpace(manifest.ProvenanceStatus) || manifest.ProvenanceStatus is not ("resolved" or "unresolved")) {
            checks.Add(Fail("Manifest metadata", "manifest-provenance-invalid", $"ProvenanceStatus is not recognized: {manifest.ProvenanceStatus ?? "missing"}"));
        }
        else if ((manifest.ReleaseKind is "public-unsigned" or "promotion-signed") && manifest.ProvenanceStatus != "resolved") {
            checks.Add(Fail("Release policy", "promotion-provenance-unresolved", $"{manifest.ReleaseKind} requires ProvenanceStatus resolved."));
        }
        else {
            checks.Add(Ok("Manifest metadata", "manifest-provenance-valid", $"ProvenanceStatus is {manifest.ProvenanceStatus}"));
        }

        if (manifest.PortableMarkerValidated != true) {
            checks.Add(Fail("Manifest metadata", "portable-marker-gate-missing", "PortableMarkerValidated must be true."));
        }
        if (manifest.PortableBodyValidated != true) {
            checks.Add(Fail("Manifest metadata", "portable-body-gate-missing", "PortableBodyValidated must be true."));
        }
        if (manifest.PortableMarkerValidated == true && manifest.PortableBodyValidated == true) {
            checks.Add(Ok("Manifest metadata", "portable-gates-valid", "Portable marker and body gates are recorded as validated"));
        }

        ValidateInstallerStatus(request, manifest, checks);
        ValidatePublisherPolicy(manifest, checks);

        if (!DateTimeOffset.TryParse(manifest.BuiltUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)) {
            checks.Add(Fail("Manifest metadata", "manifest-built-utc-invalid", "BuiltUtc is missing or is not an ISO-8601 timestamp."));
        }

        if (string.IsNullOrWhiteSpace(manifest.GitRevision) || !Regex.IsMatch(manifest.GitRevision, "^[0-9A-Fa-f]{40,64}$")) {
            checks.Add(Fail("Manifest metadata", "manifest-git-revision-invalid", "GitRevision must be a complete 40-64 character hexadecimal revision."));
        }
    }

    private static void ValidateInstallerStatus(ReleaseCheckRequest request, ReleaseManifest manifest, List<ReleaseCheckItem> checks) {
        if (string.IsNullOrWhiteSpace(manifest.InstallerStatus) || !RecognizedInstallerStatuses.Contains(manifest.InstallerStatus)) {
            checks.Add(Fail("Manifest metadata", "installer-status-invalid", $"InstallerStatus is not recognized: {manifest.InstallerStatus ?? "missing"}"));
            return;
        }

        if (request.Promotion && manifest.InstallerStatus == "not-requested") {
            checks.Add(Fail("Release policy", "promotion-installer-not-built", "Promotion requires a built installer; InstallerStatus cannot be not-requested."));
            return;
        }

        if (manifest.ReleaseKind == "promotion-signed" && manifest.InstallerStatus == "built-unsigned") {
            checks.Add(Fail("Release policy", "signed-installer-unsigned", "A signed promotion cannot declare a built-unsigned installer."));
            return;
        }
        if ((manifest.ReleaseKind is "local-unsigned" or "public-unsigned") && manifest.InstallerStatus == "built-signed") {
            checks.Add(Fail("Release policy", "unsigned-installer-signed", $"{manifest.ReleaseKind} cannot declare a built-signed installer."));
            return;
        }

        checks.Add(Ok("Manifest metadata", "installer-status-valid", $"InstallerStatus is {manifest.InstallerStatus}"));
    }

    private static void ValidatePublisherPolicy(ReleaseManifest manifest, List<ReleaseCheckItem> checks) {
        if (manifest.ReleaseKind == "public-unsigned") {
            if (manifest.Publisher != "Unknown") {
                checks.Add(Fail("Release policy", "public-unsigned-publisher-invalid", "public-unsigned requires Publisher to be exactly Unknown."));
            }
            if (manifest.SmartScreenWarning != PublicUnsignedWarning) {
                checks.Add(Fail("Release policy", "public-unsigned-warning-invalid", "public-unsigned requires the full Unknown publisher and SmartScreen warning."));
            }
            if (manifest.Publisher == "Unknown" && manifest.SmartScreenWarning == PublicUnsignedWarning) {
                checks.Add(Ok("Release policy", "public-unsigned-warning-present", PublicUnsignedWarning));
            }
            return;
        }

        if (manifest.ReleaseKind == "promotion-signed" && (string.IsNullOrWhiteSpace(manifest.Publisher) || manifest.Publisher.Contains("Unknown", StringComparison.OrdinalIgnoreCase))) {
            checks.Add(Fail("Release policy", "signed-publisher-invalid", "promotion-signed requires the signing certificate publisher identity."));
        }
    }

    private static void ValidateManifestBody(string publishDir, ReleaseManifest manifest, List<ReleaseCheckItem> checks) {
        try {
            IReadOnlyDictionary<string, FileInfo> actualFiles = BuildFileInventory(publishDir, excludeManifest: true);
            Dictionary<string, ReleaseManifestFile> declaredFiles = new(StringComparer.OrdinalIgnoreCase);
            foreach (ReleaseManifestFile declaredFile in manifest.Files) {
                string relativePath = ValidateRelativePackagePath(declaredFile.Path);
                if (!declaredFiles.TryAdd(relativePath, declaredFile))
                    throw new InvalidDataException($"Manifest contains duplicate file path: {relativePath}");
                if (!actualFiles.TryGetValue(relativePath, out FileInfo? actualFile))
                    throw new InvalidDataException($"Manifest file is missing from staging: {relativePath}");
                if (declaredFile.Size != actualFile.Length)
                    throw new InvalidDataException($"Manifest size mismatch for {relativePath}.");
                if (!Regex.IsMatch(declaredFile.Sha256, "^[0-9A-Fa-f]{64}$"))
                    throw new InvalidDataException($"Manifest SHA256 is malformed for {relativePath}.");

                string actualHash = ComputeSha256(actualFile.FullName);
                if (!actualHash.Equals(declaredFile.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Manifest SHA256 mismatch for {relativePath}.");
            }

            foreach ((string requiredPath, string expectedHash) in RequiredFileSha256) {
                if (!declaredFiles.TryGetValue(requiredPath, out ReleaseManifestFile? declaredFile))
                    throw new InvalidDataException($"Manifest is missing required checksum-locked file: {requiredPath}");
                if (!declaredFile.Sha256.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Manifest must declare SHA256 {expectedHash} for {requiredPath}.");
            }

            string? undeclaredFile = actualFiles.Keys.FirstOrDefault(path => !declaredFiles.ContainsKey(path));
            if (undeclaredFile != null)
                throw new InvalidDataException($"Staged file is not declared in the manifest: {undeclaredFile}");
            if (declaredFiles.Count == 0)
                throw new InvalidDataException("Manifest file body is empty.");

            checks.Add(Ok("Manifest body", "manifest-body-valid", $"Validated {declaredFiles.Count} staged file(s) byte-for-byte"));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) {
            checks.Add(Fail("Manifest body", "manifest-body-invalid", ex.Message));
        }
    }

    private static void ValidateAppVersion(ReleaseCheckRequest request, ReleaseManifest manifest, List<ReleaseCheckItem> checks) {
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || !File.Exists(request.ProjectPath)) {
            ReleaseCheckItem result = request.Promotion ? Fail(
                "Project version",
                "promotion-project-missing",
                string.IsNullOrWhiteSpace(request.ProjectPath)
                    ? "Promotion requires a project file for version verification."
                    : $"Promotion project file was not found: {request.ProjectPath}") : Warn(
                "Project version",
                "project-missing",
                string.IsNullOrWhiteSpace(request.ProjectPath)
                    ? "Skipped because no project file was provided."
                    : $"Skipped because the project file was not found: {request.ProjectPath}");
            checks.Add(result);
            return;
        }

        string? projectVersion = TryGetProjectVersion(request.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectVersion)) {
            checks.Add(request.Promotion
                ? Fail("Project version", "promotion-project-version-missing", $"Promotion project has no Version entry: {request.ProjectPath}")
                : Warn("Project version", "project-version-missing", $"Skipped because no Version entry was found in {request.ProjectPath}"));
            return;
        }

        if (!string.Equals(projectVersion, manifest.AppVersion, StringComparison.OrdinalIgnoreCase)) {
            checks.Add(Fail(
                "Project version",
                "version-mismatch",
                $"Release manifest version mismatch: expected {projectVersion} but found {manifest.AppVersion}"));
            return;
        }

        checks.Add(Ok("Project version", "version-match", $"AppVersion matches {projectVersion}"));
    }

    private static void ValidateGitRevision(ReleaseCheckRequest request, ReleaseManifest manifest, List<ReleaseCheckItem> checks) {
        if (string.IsNullOrWhiteSpace(manifest.GitRevision)) {
            checks.Add(request.Promotion
                ? Fail("Git revision", "promotion-git-absent", "Promotion requires a complete manifest GitRevision.")
                : Warn("Git revision", "git-absent", "Skipped because the manifest does not contain a git revision."));
            return;
        }

        string? repoRoot = TryFindRepoRoot(request.ProjectPath ?? request.PublishDir);
        if (string.IsNullOrWhiteSpace(repoRoot)) {
            checks.Add(request.Promotion
                ? Fail("Git revision", "promotion-git-repo-missing", "Promotion requires a local git repository for revision verification.")
                : Warn("Git revision", "git-repo-missing", "Skipped because no local git repository was found."));
            return;
        }

        if (!TryGetGitRevision(repoRoot, out string? currentRevision, out string? error)) {
            string detail = string.IsNullOrWhiteSpace(error)
                ? "Git metadata was unavailable."
                : $"Git metadata was unavailable: {error}";
            checks.Add(request.Promotion
                ? Fail("Git revision", "promotion-git-unavailable", detail)
                : Warn("Git revision", "git-unavailable", $"Skipped because {char.ToLowerInvariant(detail[0])}{detail[1..]}"));
            return;
        }

        if (!string.Equals(currentRevision, manifest.GitRevision, StringComparison.OrdinalIgnoreCase)) {
            checks.Add(Fail(
                "Git revision",
                "git-mismatch",
                $"Release manifest git revision mismatch: expected {currentRevision} but found {manifest.GitRevision}"));
            return;
        }

        checks.Add(Ok("Git revision", "git-match", $"Git revision matches {currentRevision}"));
    }

    private static void ValidateZipHash(ReleaseCheckRequest request, List<ReleaseCheckItem> checks) {
        string zipPath = request.ZipPath!;
        if (request.Promotion && string.IsNullOrWhiteSpace(request.HashPath))
            return;

        try {
            EnsureNoReparsePointsInExistingPath(zipPath, "Release zip");
            if (!File.Exists(zipPath)) {
                checks.Add(Fail("Release zip", "zip-missing", $"Release zip was not found at {zipPath}"));
                return;
            }

            string hashPath = request.HashPath ?? $"{zipPath}.sha256";
            EnsureNoReparsePointsInExistingPath(hashPath, "Release hash");
            if (!File.Exists(hashPath)) {
                checks.Add(Fail("Release hash", "hash-missing", $"Release hash file was not found at {hashPath}"));
                return;
            }

            (string expectedHash, string expectedFileName) = ReadArtifactHashRecord(zipPath, hashPath);
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zipPath))).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase)) {
                checks.Add(Fail("Release hash", "hash-mismatch", $"Release zip SHA256 mismatch for {zipPath}"));
                return;
            }

            checks.Add(Ok("Release hash", "hash-match", $"SHA256 matches {expectedFileName}"));
        }
        catch (Exception ex) {
            checks.Add(Fail("Release hash", "hash-invalid", ex.Message));
        }
    }

    private static void ValidateZipBody(ReleaseCheckRequest request, List<ReleaseCheckItem> checks) {
        string zipPath = request.ZipPath!;
        try {
            EnsureNoReparsePointsInExistingPath(zipPath, "Release zip");
            if (!File.Exists(zipPath))
                return;

            IReadOnlyDictionary<string, FileInfo> stagedFiles = BuildFileInventory(request.PublishDir, excludeManifest: false);
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            Dictionary<string, ZipArchiveEntry> archiveFiles = ReadZipFileInventory(zipPath, archive);
            foreach ((string relativePath, FileInfo stagedFile) in stagedFiles) {
                if (!archiveFiles.TryGetValue(relativePath, out ZipArchiveEntry? entry))
                    throw new InvalidDataException($"Release zip is missing staged file: {relativePath}");
                if (entry.Length != stagedFile.Length)
                    throw new InvalidDataException($"Release zip size mismatch for {relativePath}.");

                using Stream stream = entry.Open();
                string archiveHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                string stagedHash = ComputeSha256(stagedFile.FullName);
                if (!archiveHash.Equals(stagedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Release zip content mismatch for {relativePath}.");
            }

            string? extraFile = archiveFiles.Keys.FirstOrDefault(path => !stagedFiles.ContainsKey(path));
            if (extraFile != null)
                throw new InvalidDataException($"Release zip contains an undeclared staged file: {extraFile}");
            if (!archiveFiles.TryGetValue(CliPaths.PortableMarkerFileName, out ZipArchiveEntry? marker) || marker.Length != 0)
                throw new InvalidDataException($"Release zip requires an empty {CliPaths.PortableMarkerFileName} marker.");

            checks.Add(Ok("Release zip body", "zip-body-valid", $"Validated {archiveFiles.Count} archived file(s) byte-for-byte"));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) {
            checks.Add(Fail("Release zip body", "zip-body-invalid", ex.Message));
        }
    }

    private static void ValidateReleaseSummary(ReleaseCheckRequest request, ReleaseManifest manifest, List<ReleaseCheckItem> checks) {
        string summaryPath = request.SummaryPath!;
        try {
            EnsureNoReparsePointsInExistingPath(summaryPath, "Release summary");
            if (!File.Exists(summaryPath)) {
                checks.Add(Fail("Release summary", "release-summary-missing", $"Release summary was not found at {summaryPath}"));
                return;
            }

            ReleaseMetadataDocument summary = LoadReleaseMetadataDocument(summaryPath);
            EnsureMetadataMatchesManifest(summary, manifest, "Release summary");
            if (!string.IsNullOrWhiteSpace(summary.SubjectArtifact))
                throw new InvalidDataException("Release summary must not declare SubjectArtifact.");

            string summaryRoot = Path.GetDirectoryName(summaryPath)
                ?? throw new InvalidDataException("Release summary has no parent directory.");
            ValidateArtifactInventory(request, manifest, summary, summaryRoot);
            checks.Add(Ok("Release summary", "release-summary-valid", $"Validated summary and {summary.Artifacts.Count} artifact metadata sidecar(s)"));
        }
        catch (Exception ex) {
            checks.Add(Fail("Release summary", "release-summary-invalid", ex.Message));
        }
    }

    private static void ValidateArtifactInventory(
        ReleaseCheckRequest request,
        ReleaseManifest manifest,
        ReleaseMetadataDocument summary,
        string summaryRoot) {
        if (summary.Artifacts.Count == 0)
            throw new InvalidDataException("Release summary artifact inventory is empty.");

        Dictionary<string, ReleaseArtifact> artifacts = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReleaseArtifact artifact in summary.Artifacts) {
            string relativePath = ValidateRelativePackagePath(artifact.Path);
            if (!artifacts.TryAdd(relativePath, artifact))
                throw new InvalidDataException($"Release summary contains duplicate artifact path: {relativePath}");
            if (artifact.Kind is not ("portable-zip" or "installer"))
                throw new InvalidDataException($"Release summary contains unknown artifact kind: {artifact.Kind}");

            string expectedHashSidecar = $"{relativePath}.sha256";
            string expectedMetadataSidecar = $"{relativePath}.release.json";
            if (!string.Equals(artifact.Sha256Sidecar, expectedHashSidecar, StringComparison.Ordinal))
                throw new InvalidDataException($"Artifact SHA256 sidecar path is not canonical for {relativePath}.");
            if (!string.Equals(artifact.MetadataSidecar, expectedMetadataSidecar, StringComparison.Ordinal))
                throw new InvalidDataException($"Artifact metadata sidecar path is not canonical for {relativePath}.");

            string artifactPath = ResolvePathWithinRoot(summaryRoot, relativePath);
            if (!File.Exists(artifactPath))
                throw new InvalidDataException($"Release artifact is missing: {artifactPath}");
            FileInfo artifactInfo = new(artifactPath);
            if (artifactInfo.Length != artifact.Size)
                throw new InvalidDataException($"Release artifact size mismatch for {relativePath}.");
            string actualHash = ComputeSha256(artifactPath);
            if (!actualHash.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Release artifact SHA256 mismatch for {relativePath}.");

            string hashSidecarPath = ResolvePathWithinRoot(summaryRoot, artifact.Sha256Sidecar);
            (string sidecarHash, string sidecarFileName) = ReadArtifactHashRecord(artifactPath, hashSidecarPath);
            if (!actualHash.Equals(sidecarHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Artifact SHA256 sidecar mismatch for {relativePath}.");
            if (!sidecarFileName.Equals(Path.GetFileName(artifactPath), StringComparison.Ordinal))
                throw new InvalidDataException($"Artifact SHA256 sidecar filename mismatch for {relativePath}.");

            string metadataSidecarPath = ResolvePathWithinRoot(summaryRoot, artifact.MetadataSidecar);
            ReleaseMetadataDocument sidecar = LoadReleaseMetadataDocument(metadataSidecarPath);
            EnsureMetadataMatchesManifest(sidecar, manifest, $"Metadata sidecar {artifact.MetadataSidecar}", compareFileInventory: false);
            if (!string.Equals(sidecar.SubjectArtifact, relativePath, StringComparison.Ordinal))
                throw new InvalidDataException($"Metadata sidecar SubjectArtifact mismatch for {relativePath}.");
            if (artifact.Kind == "portable-zip")
                EnsureFileInventoriesMatch(sidecar.Files, manifest.Files, $"Metadata sidecar {artifact.MetadataSidecar}");
            else
                ValidateInstallerFileInventory(sidecar.Files, manifest.Files, artifact.MetadataSidecar);
            EnsureArtifactInventoriesMatch(sidecar.Artifacts, summary.Artifacts, artifact.MetadataSidecar);
        }

        ReleaseArtifact[] portableArtifacts = summary.Artifacts.Where(artifact => artifact.Kind == "portable-zip").ToArray();
        if (portableArtifacts.Length != 1)
            throw new InvalidDataException("Release summary must declare exactly one portable-zip artifact.");
        if (string.IsNullOrWhiteSpace(request.ZipPath))
            throw new InvalidDataException("Release summary validation requires --zip.");

        string declaredZipPath = ResolvePathWithinRoot(summaryRoot, portableArtifacts[0].Path);
        if (!Path.GetFullPath(declaredZipPath).Equals(Path.GetFullPath(request.ZipPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Release summary portable-zip artifact does not match --zip.");

        int installerCount = summary.Artifacts.Count(artifact => artifact.Kind == "installer");
        if (manifest.InstallerStatus == "not-requested" && installerCount != 0)
            throw new InvalidDataException("InstallerStatus not-requested cannot declare an installer artifact.");
        if (manifest.InstallerStatus != "not-requested" && installerCount != 1)
            throw new InvalidDataException($"InstallerStatus {manifest.InstallerStatus} requires exactly one installer artifact.");
    }

    private static void EnsureMetadataMatchesManifest(
        ReleaseMetadataDocument metadata,
        ReleaseManifest manifest,
        string label,
        bool compareFileInventory = true) {
        bool metadataMatches = metadata.SchemaVersion == manifest.SchemaVersion
            && string.Equals(metadata.AppVersion, manifest.AppVersion, StringComparison.Ordinal)
            && string.Equals(metadata.Runtime, manifest.Runtime, StringComparison.Ordinal)
            && string.Equals(metadata.ReleaseKind, manifest.ReleaseKind, StringComparison.Ordinal)
            && string.Equals(metadata.GitRevision, manifest.GitRevision, StringComparison.OrdinalIgnoreCase)
            && string.Equals(metadata.BuiltUtc, manifest.BuiltUtc, StringComparison.Ordinal)
            && string.Equals(metadata.ProvenanceStatus, manifest.ProvenanceStatus, StringComparison.Ordinal)
            && metadata.PortableMarkerValidated == manifest.PortableMarkerValidated
            && metadata.PortableBodyValidated == manifest.PortableBodyValidated
            && string.Equals(metadata.InstallerStatus, manifest.InstallerStatus, StringComparison.Ordinal)
            && string.Equals(metadata.Publisher, manifest.Publisher, StringComparison.Ordinal)
            && string.Equals(metadata.SmartScreenWarning, manifest.SmartScreenWarning, StringComparison.Ordinal);
        if (!metadataMatches)
            throw new InvalidDataException($"{label} metadata does not match release-manifest.json.");

        if (compareFileInventory)
            EnsureFileInventoriesMatch(metadata.Files, manifest.Files, label);
    }

    private static void EnsureFileInventoriesMatch(
        IReadOnlyList<ReleaseManifestFile> actual,
        IReadOnlyList<ReleaseManifestFile> expected,
        string label) {
        Dictionary<string, ReleaseManifestFile> expectedFiles = expected.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        if (actual.Count != expectedFiles.Count)
            throw new InvalidDataException($"{label} SHA256 inventory count does not match release-manifest.json.");

        HashSet<string> actualPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReleaseManifestFile file in actual) {
            if (!actualPaths.Add(file.Path)
                || !expectedFiles.TryGetValue(file.Path, out ReleaseManifestFile? expectedFile)
                || file.Size != expectedFile.Size
                || !file.Sha256.Equals(expectedFile.Sha256, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException($"{label} SHA256 inventory does not match for {file.Path}.");
            }
        }
    }

    private static void ValidateFirstPartyPayloadProvenance(
        string publishDir,
        ReleaseManifest manifest,
        List<ReleaseCheckItem> checks) {
        string checksumPath = Path.Combine(publishDir, FirstPartyPayloadManifestPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(checksumPath)) {
            checks.Add(Fail("Console payload provenance", "console-payload-manifest-missing", $"Console payload checksum manifest was not found: {checksumPath}"));
            return;
        }

        try {
            Dictionary<string, string> records = new(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(checksumPath)) {
                Match match = Regex.Match(line, "^(?<hash>[0-9A-Fa-f]{64})  (?<path>[^\\r\\n]+)$", RegexOptions.CultureInvariant);
                if (!match.Success)
                    throw new InvalidDataException("Console payload checksum manifest contains an invalid record.");

                string sourcePath = match.Groups["path"].Value.Replace('\\', '/');
                if (!FirstPartyPayloadStagePaths.ContainsKey(sourcePath))
                    throw new InvalidDataException($"Console payload checksum manifest contains an unexpected path: {sourcePath}");
                if (!records.TryAdd(sourcePath, match.Groups["hash"].Value))
                    throw new InvalidDataException($"Console payload checksum manifest contains a duplicate path: {sourcePath}");
            }
            if (records.Count != FirstPartyPayloadStagePaths.Count)
                throw new InvalidDataException("Console payload checksum manifest does not contain the exact three required records.");

            Dictionary<string, ReleaseManifestFile> releaseFiles = manifest.Files.ToDictionary(
                file => ValidateRelativePackagePath(file.Path),
                StringComparer.OrdinalIgnoreCase);
            foreach ((string sourcePath, string stagePath) in FirstPartyPayloadStagePaths) {
                if (!records.TryGetValue(sourcePath, out string? expectedHash))
                    throw new InvalidDataException($"Console payload checksum manifest is missing: {sourcePath}");

                string fullPath = Path.Combine(publishDir, stagePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                    throw new InvalidDataException($"Console payload is missing: {stagePath}");
                string actualHash = ComputeSha256(fullPath);
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Console payload checksum mismatch for {stagePath}.");
                if (!releaseFiles.TryGetValue(stagePath, out ReleaseManifestFile? releaseFile)
                    || !releaseFile.Sha256.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidDataException($"Release manifest does not preserve the console payload checksum for {stagePath}.");
                }
            }

            string sourceNoticePath = Path.Combine(
                publishDir,
                CorrespondingSourceNoticePath.Replace('/', Path.DirectorySeparatorChar));
            string sourceNotice = File.ReadAllText(sourceNoticePath);
            string tag = $"v{manifest.AppVersion}";
            string[] requiredSourceNoticeText = [
                manifest.GitRevision ?? throw new InvalidDataException("Release manifest GitRevision is required by the corresponding-source notice."),
                $"{PublicRepositoryUrl}/tree/{tag}",
                $"{PublicRepositoryUrl}/archive/refs/tags/{tag}.zip",
                "third_party/X360",
                "XeCLI-XellFetch/source",
                QuickBootX360DllSha256,
                records["payload/xell.bin"]
            ];
            foreach (string requiredText in requiredSourceNoticeText) {
                if (!sourceNotice.Contains(requiredText, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Corresponding-source notice is missing required release identity: {requiredText}");
            }

            checks.Add(Ok("Console payload provenance", "console-payload-provenance-valid", "Validated all three console payloads and the matching-tag source directions"));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException) {
            checks.Add(Fail("Console payload provenance", "console-payload-provenance-invalid", ex.Message));
        }
    }

    private static void ValidateThirdPartyLicenseInventory(string publishDir, List<ReleaseCheckItem> checks) {
        string licenseRoot = Path.Combine(publishDir, "THIRD-PARTY-LICENSES");
        if (!Directory.Exists(licenseRoot)) {
            checks.Add(Fail("Third-party licenses", "third-party-license-directory-missing", $"Third-party license directory was not found: {licenseRoot}"));
            return;
        }

        try {
            EnsureNoReparsePointsInExistingPath(licenseRoot, "Third-party license directory");
            HashSet<string> actual = new(StringComparer.Ordinal);
            foreach (string entry in EnumerateFilesWithoutReparsePoints(licenseRoot)) {
                string relativePath = Path.GetRelativePath(licenseRoot, entry).Replace('\\', '/');
                relativePath = ValidateRelativePackagePath(relativePath);
                if (!actual.Add(relativePath))
                    throw new InvalidDataException($"THIRD-PARTY-LICENSES contains a duplicate file entry: {relativePath}");
            }

            HashSet<string> expected = RequiredThirdPartyLicenseFiles.ToHashSet(StringComparer.Ordinal);
            if (!actual.SetEquals(expected)) {
                string missing = string.Join(", ", expected.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal));
                string extra = string.Join(", ", actual.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal));
                throw new InvalidDataException($"THIRD-PARTY-LICENSES must contain exactly the required shared notices. Missing='{missing}'; extra='{extra}'.");
            }

            checks.Add(Ok("Third-party licenses", "third-party-license-inventory-valid", "Validated the exact shared and XeLL binary notice inventory"));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) {
            checks.Add(Fail("Third-party licenses", "third-party-license-inventory-invalid", ex.Message));
        }
    }

    private static void ValidateInstallerFileInventory(
        IReadOnlyList<ReleaseManifestFile> installerFiles,
        IReadOnlyList<ReleaseManifestFile> portableFiles,
        string label) {
        Dictionary<string, ReleaseManifestFile> portableByPath = portableFiles.ToDictionary(
            file => ValidateRelativePackagePath(file.Path),
            StringComparer.OrdinalIgnoreCase);
        if (!portableByPath.ContainsKey(CliPaths.PortableMarkerFileName))
            throw new InvalidDataException($"Portable inventory is missing {CliPaths.PortableMarkerFileName}.");
        if (!portableByPath.ContainsKey(InstallerOwnedFilesManifestName))
            throw new InvalidDataException($"Portable inventory is missing {InstallerOwnedFilesManifestName}.");

        Dictionary<string, ReleaseManifestFile> installerByPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReleaseManifestFile file in installerFiles) {
            string path = ValidateRelativePackagePath(file.Path);
            if (!installerByPath.TryAdd(path, file))
                throw new InvalidDataException($"{label} contains duplicate installer file path: {path}");
            if (file.Size < 0 || !Regex.IsMatch(file.Sha256, "^[0-9A-Fa-f]{64}$"))
                throw new InvalidDataException($"{label} contains invalid installer file metadata for {path}.");

            string topLevel = path.Split('/')[0];
            if (path.Equals(CliPaths.PortableMarkerFileName, StringComparison.OrdinalIgnoreCase)
                || topLevel.Equals("UserData", StringComparison.OrdinalIgnoreCase)
                || topLevel.Equals("logs", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException($"{label} contains a file excluded from installation: {path}");
            }
        }

        foreach ((string path, ReleaseManifestFile portableFile) in portableByPath) {
            if (path.Equals(CliPaths.PortableMarkerFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!installerByPath.TryGetValue(path, out ReleaseManifestFile? installerFile))
                throw new InvalidDataException($"{label} is missing installer file: {path}");

            if (path.Equals(InstallerOwnedFilesManifestName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (installerFile.Size != portableFile.Size
                || !installerFile.Sha256.Equals(portableFile.Sha256, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException($"{label} does not match the portable body for {path}.");
            }
        }

        foreach (string requiredPath in new[] { ReleaseManifestFileName, InstallerOwnedHashesManifestName }) {
            if (!installerByPath.TryGetValue(requiredPath, out ReleaseManifestFile? requiredFile)
                || requiredFile.Size <= 0) {
                throw new InvalidDataException($"{label} is missing installer-only metadata: {requiredPath}");
            }
        }

        int expectedCount = portableByPath.Count + 1;
        if (installerByPath.Count != expectedCount)
            throw new InvalidDataException($"{label} installer file inventory count is invalid.");
    }

    private static void EnsureArtifactInventoriesMatch(
        IReadOnlyList<ReleaseArtifact> actual,
        IReadOnlyList<ReleaseArtifact> expected,
        string label) {
        Dictionary<string, ReleaseArtifact> expectedArtifacts = expected.ToDictionary(artifact => artifact.Path, StringComparer.OrdinalIgnoreCase);
        if (actual.Count != expectedArtifacts.Count)
            throw new InvalidDataException($"Metadata sidecar artifact inventory count does not match {label}.");

        HashSet<string> actualPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReleaseArtifact artifact in actual) {
            if (!actualPaths.Add(artifact.Path)
                || !expectedArtifacts.TryGetValue(artifact.Path, out ReleaseArtifact? expectedArtifact)
                || artifact.Kind != expectedArtifact.Kind
                || artifact.Size != expectedArtifact.Size
                || !artifact.Sha256.Equals(expectedArtifact.Sha256, StringComparison.OrdinalIgnoreCase)
                || artifact.Sha256Sidecar != expectedArtifact.Sha256Sidecar
                || artifact.MetadataSidecar != expectedArtifact.MetadataSidecar) {
                throw new InvalidDataException($"Metadata sidecar artifact inventory does not match for {artifact.Path}.");
            }
        }
    }

    private static string ResolvePathWithinRoot(string root, string relativePath) {
        string normalizedPath = ValidateRelativePackagePath(relativePath);
        string rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidatePath = Path.GetFullPath(Path.Combine(rootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = rootPath + Path.DirectorySeparatorChar;
        if (!candidatePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Release metadata path is outside the summary root: {relativePath}");

        EnsureNoReparsePointsInExistingPath(rootPath, "Release summary root");
        EnsureNoReparsePointsInExistingPath(candidatePath, $"Release metadata path {relativePath}");
        return candidatePath;
    }

    private static Dictionary<string, ZipArchiveEntry> ReadZipFileInventory(string zipPath, ZipArchive archive) {
        string expectedRoot = Path.GetFileNameWithoutExtension(zipPath);
        Dictionary<string, ZipArchiveEntry> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries) {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string[] segments = entry.FullName.Split('/');
            if (segments.Length < 2 || !segments[0].Equals(expectedRoot, StringComparison.Ordinal))
                throw new InvalidDataException($"Release zip entry is outside the expected {expectedRoot} root: {entry.FullName}");

            string relativePath = ValidateRelativePackagePath(string.Join('/', segments.Skip(1)));
            if (!files.TryAdd(relativePath, entry))
                throw new InvalidDataException($"Release zip contains duplicate file path: {relativePath}");
        }

        return files;
    }

    private static IReadOnlyDictionary<string, FileInfo> BuildFileInventory(string root, bool excludeManifest) {
        string rootPath = Path.GetFullPath(root);
        Dictionary<string, FileInfo> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in EnumerateFilesWithoutReparsePoints(rootPath)) {
            string relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
            if (excludeManifest && relativePath.Equals("release-manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!files.TryAdd(relativePath, new FileInfo(filePath)))
                throw new InvalidDataException($"Staging contains duplicate file path: {relativePath}");
        }

        return files;
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string root) {
        string rootPath = Path.GetFullPath(root);
        EnsureNoReparsePointsInExistingPath(rootPath, "Filesystem inventory root");

        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(rootPath);
        while (pendingDirectories.Count > 0) {
            string currentDirectory = pendingDirectories.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(currentDirectory, "*", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)) {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Filesystem inventory contains a reparse point: {entry}");

                if ((attributes & FileAttributes.Directory) != 0) {
                    pendingDirectories.Push(entry);
                }
                else {
                    yield return entry;
                }
            }
        }
    }

    private static void EnsureNoReparsePointsInExistingPath(string path, string label) {
        string fullPath = Path.GetFullPath(path);
        string rootPath = Path.GetPathRoot(fullPath)
            ?? throw new InvalidDataException($"{label} has no filesystem root: {path}");

        AssertNotReparsePoint(rootPath, label);
        string remainder = fullPath[rootPath.Length..];
        string currentPath = rootPath;
        foreach (string segment in remainder.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries)) {
            currentPath = Path.Combine(currentPath, segment);
            if (!AssertNotReparsePoint(currentPath, label))
                break;
        }
    }

    private static bool AssertNotReparsePoint(string path, string label) {
        FileAttributes attributes;
        try {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException) {
            return false;
        }
        catch (DirectoryNotFoundException) {
            return false;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{label} contains a reparse point: {path}");
        return true;
    }

    private static string ValidateRelativePackagePath(string path) {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\'))
            throw new InvalidDataException($"Package path is not a normalized relative path: {path}");

        string[] segments = path.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains(':')))
            throw new InvalidDataException($"Package path contains an unsafe segment: {path}");
        return string.Join('/', segments);
    }

    private static string ComputeSha256(string filePath) {
        using FileStream stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static ReleaseManifest LoadManifest(string manifestPath) {
        using FileStream stream = File.OpenRead(manifestPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        int schemaVersion = root.GetProperty("SchemaVersion").GetInt32();
        string appVersion = root.GetProperty("AppVersion").GetString() ?? throw new InvalidDataException("AppVersion is missing.");
        string? runtime = root.TryGetProperty("Runtime", out JsonElement runtimeElement) ? runtimeElement.GetString() : null;
        string? releaseKind = root.TryGetProperty("ReleaseKind", out JsonElement kindElement) ? kindElement.GetString() : null;
        string? gitRevision = root.TryGetProperty("GitRevision", out JsonElement gitElement) ? gitElement.GetString() : null;
        string? builtUtc = root.TryGetProperty("BuiltUtc", out JsonElement builtUtcElement) ? builtUtcElement.GetString() : null;
        string? provenanceStatus = root.TryGetProperty("ProvenanceStatus", out JsonElement provenanceElement) ? provenanceElement.GetString() : null;
        bool? portableMarkerValidated = ReadOptionalBoolean(root, "PortableMarkerValidated");
        bool? portableBodyValidated = ReadOptionalBoolean(root, "PortableBodyValidated");
        string? installerStatus = root.TryGetProperty("InstallerStatus", out JsonElement installerElement) ? installerElement.GetString() : null;
        string? publisher = root.TryGetProperty("Publisher", out JsonElement publisherElement) ? publisherElement.GetString() : null;
        string? smartScreenWarning = root.TryGetProperty("SmartScreenWarning", out JsonElement warningElement) ? warningElement.GetString() : null;
        List<ReleaseManifestFile> files = ReadFileInventory(root);

        return new ReleaseManifest(
            schemaVersion,
            appVersion,
            runtime,
            releaseKind,
            gitRevision,
            builtUtc,
            provenanceStatus,
            portableMarkerValidated,
            portableBodyValidated,
            installerStatus,
            publisher,
            smartScreenWarning,
            files);
    }

    private static ReleaseMetadataDocument LoadReleaseMetadataDocument(string path) {
        if (!File.Exists(path))
            throw new InvalidDataException($"Release metadata sidecar was not found: {path}");

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        int schemaVersion = root.GetProperty("SchemaVersion").GetInt32();
        string appVersion = root.GetProperty("AppVersion").GetString() ?? throw new InvalidDataException("AppVersion is missing.");
        string? runtime = root.TryGetProperty("Runtime", out JsonElement runtimeElement) ? runtimeElement.GetString() : null;
        string? releaseKind = root.TryGetProperty("ReleaseKind", out JsonElement kindElement) ? kindElement.GetString() : null;
        string? gitRevision = root.TryGetProperty("GitRevision", out JsonElement gitElement) ? gitElement.GetString() : null;
        string? builtUtc = root.TryGetProperty("BuiltUtc", out JsonElement builtUtcElement) ? builtUtcElement.GetString() : null;
        string? provenanceStatus = root.TryGetProperty("ProvenanceStatus", out JsonElement provenanceElement) ? provenanceElement.GetString() : null;
        bool? portableMarkerValidated = ReadOptionalBoolean(root, "PortableMarkerValidated");
        bool? portableBodyValidated = ReadOptionalBoolean(root, "PortableBodyValidated");
        string? installerStatus = root.TryGetProperty("InstallerStatus", out JsonElement installerElement) ? installerElement.GetString() : null;
        string? publisher = root.TryGetProperty("Publisher", out JsonElement publisherElement) ? publisherElement.GetString() : null;
        string? smartScreenWarning = root.TryGetProperty("SmartScreenWarning", out JsonElement warningElement) ? warningElement.GetString() : null;
        string? subjectArtifact = root.TryGetProperty("SubjectArtifact", out JsonElement subjectElement) ? subjectElement.GetString() : null;
        List<ReleaseManifestFile> files = ReadFileInventory(root);

        JsonElement artifactsElement = root.GetProperty("Artifacts");
        if (artifactsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Artifacts must be an array.");

        List<ReleaseArtifact> artifacts = [];
        foreach (JsonElement artifactElement in artifactsElement.EnumerateArray()) {
            artifacts.Add(new ReleaseArtifact(
                artifactElement.GetProperty("Path").GetString() ?? throw new InvalidDataException("Artifact Path is missing."),
                artifactElement.GetProperty("Kind").GetString() ?? throw new InvalidDataException("Artifact Kind is missing."),
                artifactElement.GetProperty("Size").GetInt64(),
                artifactElement.GetProperty("Sha256").GetString() ?? throw new InvalidDataException("Artifact Sha256 is missing."),
                artifactElement.GetProperty("Sha256Sidecar").GetString() ?? throw new InvalidDataException("Artifact Sha256Sidecar is missing."),
                artifactElement.GetProperty("MetadataSidecar").GetString() ?? throw new InvalidDataException("Artifact MetadataSidecar is missing.")));
        }

        return new ReleaseMetadataDocument(
            schemaVersion,
            appVersion,
            runtime,
            releaseKind,
            gitRevision,
            builtUtc,
            provenanceStatus,
            portableMarkerValidated,
            portableBodyValidated,
            installerStatus,
            publisher,
            smartScreenWarning,
            files,
            artifacts,
            subjectArtifact);
    }

    private static List<ReleaseManifestFile> ReadFileInventory(JsonElement root) {
        JsonElement filesElement = root.GetProperty("Files");
        if (filesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Files must be an array.");

        List<ReleaseManifestFile> files = [];
        foreach (JsonElement fileElement in filesElement.EnumerateArray()) {
            files.Add(new ReleaseManifestFile(
                fileElement.GetProperty("Path").GetString() ?? throw new InvalidDataException("Manifest file Path is missing."),
                fileElement.GetProperty("Size").GetInt64(),
                fileElement.GetProperty("Sha256").GetString() ?? throw new InvalidDataException("Manifest file Sha256 is missing.")));
        }
        return files;
    }

    private static bool? ReadOptionalBoolean(JsonElement root, string propertyName) {
        if (!root.TryGetProperty(propertyName, out JsonElement element))
            return null;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"{propertyName} must be a boolean.");
        return element.GetBoolean();
    }

    private static string? TryGetProjectVersion(string projectPath) {
        XDocument document = XDocument.Load(projectPath);
        foreach (XElement propertyGroup in document.Root?.Elements("PropertyGroup") ?? Enumerable.Empty<XElement>()) {
            string? version = propertyGroup.Element("Version")?.Value;
            if (!string.IsNullOrWhiteSpace(version))
                return version.Trim();
        }

        return null;
    }

    private static bool TryGetGitRevision(string repoRoot, out string? revision, out string? error) {
        try {
            ProcessStartInfo psi = new ProcessStartInfo("git") {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = repoRoot
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"safe.directory={repoRoot.Replace('\\', '/')}");
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("HEAD");

            using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start git.");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0) {
                revision = null;
                error = string.IsNullOrWhiteSpace(stderr) ? $"git exited with code {process.ExitCode}." : stderr.Trim();
                return false;
            }

            revision = stdout.Trim();
            error = null;
            return !string.IsNullOrWhiteSpace(revision);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) {
            revision = null;
            error = ex.Message;
            return false;
        }
    }

    private static string? TryFindRepoRoot(string startPath) {
        string? current = Directory.Exists(startPath) ? Path.GetFullPath(startPath) : Path.GetDirectoryName(Path.GetFullPath(startPath));
        while (!string.IsNullOrWhiteSpace(current)) {
            string gitDir = Path.Combine(current, ".git");
            if (Directory.Exists(gitDir) || File.Exists(gitDir))
                return current;

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return null;
    }

    private static (string Hash, string FileName) ReadArtifactHashRecord(string artifactPath, string hashPath) {
        if (!File.Exists(hashPath))
            throw new InvalidDataException($"Release hash file was not found: {hashPath}");

        string hashText = File.ReadAllText(hashPath).Trim();
        Match match = Regex.Match(hashText, "^(?<Hash>[0-9A-Fa-f]{64}) [*](?<FileName>.+)$");
        if (!match.Success) {
            throw new InvalidDataException($"Release hash file is malformed: {hashPath}");
        }

        string expectedFileName = Path.GetFileName(artifactPath);
        if (!string.Equals(match.Groups["FileName"].Value, expectedFileName, StringComparison.Ordinal)) {
            throw new InvalidDataException($"Release hash file does not reference {expectedFileName}: {hashPath}");
        }

        return (match.Groups["Hash"].Value.ToLowerInvariant(), expectedFileName);
    }

    private static ReleaseCheckItem Ok(string name, string code, string detail) {
        return new ReleaseCheckItem(name, "ok", detail, code);
    }

    private static ReleaseCheckItem Warn(string name, string code, string detail) {
        return new ReleaseCheckItem(name, "warn", detail, code);
    }

    private static ReleaseCheckItem Fail(string name, string code, string detail) {
        return new ReleaseCheckItem(name, "fail", detail, code);
    }

    private static string NormalizeCode(string value) {
        Span<char> buffer = stackalloc char[value.Length];
        for (int i = 0; i < value.Length; i++) {
            char c = value[i];
            buffer[i] = char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-';
        }

        string code = new string(buffer);
        while (code.Contains("--", StringComparison.Ordinal))
            code = code.Replace("--", "-", StringComparison.Ordinal);
        return code.Trim('-');
    }
}

internal sealed record ReleaseManifest(
    int SchemaVersion,
    string AppVersion,
    string? Runtime,
    string? ReleaseKind,
    string? GitRevision,
    string? BuiltUtc,
    string? ProvenanceStatus,
    bool? PortableMarkerValidated,
    bool? PortableBodyValidated,
    string? InstallerStatus,
    string? Publisher,
    string? SmartScreenWarning,
    IReadOnlyList<ReleaseManifestFile> Files);

internal sealed record ReleaseManifestFile(
    string Path,
    long Size,
    string Sha256);

internal sealed record ReleaseMetadataDocument(
    int SchemaVersion,
    string AppVersion,
    string? Runtime,
    string? ReleaseKind,
    string? GitRevision,
    string? BuiltUtc,
    string? ProvenanceStatus,
    bool? PortableMarkerValidated,
    bool? PortableBodyValidated,
    string? InstallerStatus,
    string? Publisher,
    string? SmartScreenWarning,
    IReadOnlyList<ReleaseManifestFile> Files,
    IReadOnlyList<ReleaseArtifact> Artifacts,
    string? SubjectArtifact);

internal sealed record ReleaseArtifact(
    string Path,
    string Kind,
    long Size,
    string Sha256,
    string Sha256Sidecar,
    string MetadataSidecar);
