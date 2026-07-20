using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Xbox360.Remote.Cli.Commands;

namespace Xbox360.Remote.Cli;

[SupportedOSPlatform("windows")]
internal static class InstallHelpers {
    private const string OwnedFilesManifestName = "xecli-owned-files.txt";
    private const string PortableMarkerName = "xecli.portable";
    private const string ReleaseManifestName = "release-manifest.json";
    private const string InstallTransactionDirectoryName = ".xecli-install-transaction";
    private const string InstallTransactionJournalName = "transaction.json";
    private const string InstallTransactionStageDirectoryName = "stage";
    private const string InstallTransactionBackupDirectoryName = "backup";
    private const int InstallTransactionSchemaVersion = 1;
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

        return TryResolveInstalledDirectory() != null;
    }

    public static string? TryResolveRegisteredCommandPath() {
        return EnumerateRegisteredCommandPaths().FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateRegisteredCommandPaths() {
        string? env = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(env)) {
            foreach (string entry in env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                string candidateExe;
                string candidateCmd;
                try {
                    candidateExe = Path.Combine(entry, "rgh.exe");
                    candidateCmd = Path.Combine(entry, "rgh.cmd");
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException) {
                    continue;
                }

                if (File.Exists(candidateExe))
                    yield return candidateExe;
                if (File.Exists(candidateCmd))
                    yield return candidateCmd;
            }
        }

        if (File.Exists(ShimPath))
            yield return ShimPath;
    }

    public static string? TryResolveInstalledDirectory() {
        foreach (string commandPath in EnumerateRegisteredCommandPaths()) {
            if (commandPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                string directory = NormalizeDirectory(Path.GetDirectoryName(commandPath)!);
                if (HasInstallIdentity(directory))
                    return directory;
            }

            if (commandPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) {
                string? targetExe = TryReadShimTarget(commandPath);
                if (!string.IsNullOrWhiteSpace(targetExe) && File.Exists(targetExe)) {
                    string directory = NormalizeDirectory(Path.GetDirectoryName(targetExe)!);
                    if (HasInstallIdentity(directory))
                        return directory;
                }
            }
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

    public static bool TryUninstallUserShim(out string message) {
        if (!File.Exists(ShimPath)) {
            message = "The per-user command shim was not present.";
            return true;
        }

        try {
            File.Delete(ShimPath);
            if (File.Exists(ShimPath)) {
                message = $"The per-user command shim still exists: {ShimPath}";
                return false;
            }

            message = "Removed the per-user command shim.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            message = $"Unable to remove the per-user command shim: {ex.Message}";
            return false;
        }
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
                    "The source runtime configuration is missing runtimeOptions and does not look like a valid publish output.",
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
                    "The selected source is framework-dependent, not self-contained. Installing it can fail on systems without the matching x64 .NET desktop runtime.",
                    CreatePublishCommandHint());
            }

            return new InstallSourceValidation(
                false,
                "The selected source does not look like a supported self-contained release layout.",
                CreatePublishCommandHint());
        }
        catch (Exception) {
            return new InstallSourceValidation(
                false,
                "The source runtime configuration could not be parsed. Fix the publish output and try again.",
                CreatePublishCommandHint());
        }
    }

    public static InstallSourceValidation InspectPublishedInstallSource(string sourceDirectory) {
        InstallSourceValidation layoutValidation = InspectInstallSource(sourceDirectory);
        if (!layoutValidation.IsValid)
            return layoutValidation;

        string source;
        try {
            source = CanonicalizeDirectory(sourceDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) {
            return InvalidPublishedSource($"The source directory is invalid: {ex.Message}");
        }

        if (TryFindReparseAncestor(source, out string sourceReparsePath)) {
            return InvalidPublishedSource(
                $"The portable source cannot be inside a symbolic link or reparse point: {sourceReparsePath}");
        }

        string portableMarkerPath = Path.Combine(source, PortableMarkerName);
        if (!IsRegularFile(portableMarkerPath) || new FileInfo(portableMarkerPath).Length != 0) {
            return InvalidPublishedSource(
                $"The source must contain an empty {PortableMarkerName} marker from the published portable package.");
        }

        string releaseManifestPath = Path.Combine(source, ReleaseManifestName);
        if (!IsRegularFile(releaseManifestPath)) {
            return InvalidPublishedSource($"The source is missing {ReleaseManifestName}.");
        }

        ReleaseManifest manifest;
        try {
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(releaseManifestPath))
                ?? throw new InvalidDataException("The release manifest is empty.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) {
            return InvalidPublishedSource($"The release manifest could not be validated: {ex.Message}");
        }

        if (manifest.SchemaVersion != 2 ||
            !string.Equals(manifest.Runtime, "win-x64", StringComparison.Ordinal) ||
            manifest.PortableMarkerValidated != true ||
            manifest.PortableBodyValidated != true ||
            manifest.ReleaseKind is not ("local-unsigned" or "public-unsigned" or "promotion-signed") ||
            string.IsNullOrWhiteSpace(manifest.AppVersion) ||
            manifest.Files is null) {
            return InvalidPublishedSource(
                "The release manifest does not describe a validated XeCLI win-x64 portable package.");
        }

        Dictionary<string, ReleaseManifestFile> declaredFiles = new(StringComparer.OrdinalIgnoreCase);
        try {
            foreach (ReleaseManifestFile file in manifest.Files) {
                string relativePath = NormalizeOwnedRelativePath(file.Path);
                if (!declaredFiles.TryAdd(relativePath, file))
                    throw new InvalidDataException($"The release manifest contains a duplicate path: {relativePath}");
                if (file.Size < 0 || !IsSha256(file.Sha256))
                    throw new InvalidDataException($"The release manifest has invalid metadata for {relativePath}.");

                string fullPath = ResolveOwnedPath(source, relativePath);
                if (!IsRegularFile(fullPath))
                    throw new InvalidDataException($"The published source file is missing or unsafe: {relativePath}");

                FileInfo info = new(fullPath);
                if (info.Length != file.Size ||
                    !string.Equals(ComputeSha256(fullPath), file.Sha256, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidDataException($"The published source file failed integrity validation: {relativePath}");
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException) {
            return InvalidPublishedSource(ex.Message);
        }

        if (!declaredFiles.ContainsKey(PortableMarkerName))
            return InvalidPublishedSource($"The release manifest does not declare {PortableMarkerName}.");

        string ownedManifestPath = Path.Combine(source, OwnedFilesManifestName);
        if (!TryReadOwnedFilesManifest(ownedManifestPath, out IReadOnlyList<string> ownedFiles, out string ownedError))
            return InvalidPublishedSource(ownedError);

        HashSet<string> expectedOwnedFiles = declaredFiles.Keys
            .Where(IsInstallerOwnedReleasePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        expectedOwnedFiles.Add(ReleaseManifestName);

        if (!expectedOwnedFiles.SetEquals(ownedFiles)) {
            return InvalidPublishedSource(
                $"{OwnedFilesManifestName} does not match the release manifest's installer-owned file set.");
        }

        return new InstallSourceValidation(
            true,
            $"Validated XeCLI {manifest.AppVersion} {manifest.Runtime} portable release ({ownedFiles.Count} installer-owned files).",
            null) {
            OwnedFiles = ownedFiles
        };
    }

    public static InstallDestinationValidation InspectInstallDestination(
        string sourceDirectory,
        string targetDirectory,
        IReadOnlyCollection<string> newOwnedFiles) {
        string source;
        string target;
        try {
            source = CanonicalizeDirectory(sourceDirectory);
            target = CanonicalizeDirectory(targetDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) {
            return new InstallDestinationValidation(false, $"The install directory is invalid: {ex.Message}", []);
        }

        if (IsSameOrNestedDirectory(source, target) || IsSameOrNestedDirectory(target, source)) {
            return new InstallDestinationValidation(
                false,
                "The source and install directories must be separate and cannot contain one another.",
                []);
        }

        if (!IsSafeInstallDirectory(target)) {
            return new InstallDestinationValidation(
                false,
                "Choose a dedicated XeCLI folder. Drive roots, Windows directories, and profile data roots are not valid install targets.",
                []);
        }

        if (TryFindReparseAncestor(target, out string targetReparsePath)) {
            return new InstallDestinationValidation(
                false,
                $"The install directory cannot be inside a symbolic link or reparse point: {targetReparsePath}",
                []);
        }

        if (TryFindFileAncestor(target, out string targetFileAncestor)) {
            return new InstallDestinationValidation(
                false,
                $"An install directory parent is a file: {targetFileAncestor}",
                []);
        }

        if (File.Exists(target))
            return new InstallDestinationValidation(false, "The selected install path is a file, not a directory.", []);

        try {
            RecoverInterruptedInstall(source, target);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) {
            return new InstallDestinationValidation(
                false,
                $"The interrupted install transaction could not be recovered safely: {ex.Message}",
                []);
        }

        if (!Directory.Exists(target))
            return new InstallDestinationValidation(true, "The install directory will be created.", []);

        if (HasReparsePoint(target))
            return new InstallDestinationValidation(false, "The install directory cannot be a symbolic link or reparse point.", []);

        if (!Directory.EnumerateFileSystemEntries(target).Any())
            return new InstallDestinationValidation(true, "The existing install directory is empty.", []);

        string existingOwnedManifest = Path.Combine(target, OwnedFilesManifestName);
        if (!TryReadOwnedFilesManifest(existingOwnedManifest, out IReadOnlyList<string> previousOwnedFiles, out _)) {
            return new InstallDestinationValidation(
                false,
                $"The selected directory is not empty and does not contain a valid {OwnedFilesManifestName}. Choose an empty dedicated folder or the exact directory of an existing XeCLI installation.",
                []);
        }

        HashSet<string> previousOwnedSet = previousOwnedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (previousOwnedSet.Any(IsInstallTransactionPath) ||
            !previousOwnedSet.Contains(OwnedFilesManifestName) ||
            !previousOwnedSet.Contains(ReleaseManifestName) ||
            !previousOwnedSet.Contains("rgh.exe") ||
            !IsRegularFile(Path.Combine(target, "rgh.exe")) ||
            !IsRegularFile(Path.Combine(target, ReleaseManifestName))) {
            return new InstallDestinationValidation(
                false,
                "The selected directory does not contain a complete XeCLI ownership identity and cannot be updated safely.",
                []);
        }

        foreach (string relativePath in newOwnedFiles) {
            string destination;
            try {
                destination = ResolveOwnedPath(target, relativePath);
                EnsureNoReparsePath(target, destination);
                EnsureDestinationParentsAreDirectories(target, destination);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
                return new InstallDestinationValidation(false, ex.Message, []);
            }

            if (Directory.Exists(destination)) {
                return new InstallDestinationValidation(
                    false,
                    $"An installer-owned file collides with a directory: {relativePath}",
                    []);
            }
            if (!previousOwnedSet.Contains(relativePath) && (File.Exists(destination) || Directory.Exists(destination))) {
                return new InstallDestinationValidation(
                    false,
                    $"The update would overwrite an unowned path: {relativePath}",
                    []);
            }
        }

        foreach (string relativePath in previousOwnedSet.Where(path => !newOwnedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))) {
            try {
                string stalePath = ResolveOwnedPath(target, relativePath);
                EnsureNoReparsePath(target, stalePath);
                if (Directory.Exists(stalePath))
                    throw new IOException($"An installer-owned file was replaced by a directory: {relativePath}");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
                return new InstallDestinationValidation(false, ex.Message, []);
            }
        }

        string portableMarker = Path.Combine(target, PortableMarkerName);
        if (Directory.Exists(portableMarker) || (File.Exists(portableMarker) && HasReparsePoint(portableMarker))) {
            return new InstallDestinationValidation(
                false,
                $"The existing {PortableMarkerName} path is not a regular file.",
                []);
        }

        return new InstallDestinationValidation(true, "Validated the existing XeCLI ownership inventory.", previousOwnedFiles);
    }

    public static InstallCopyResult InstallOwnedRelease(
        string sourceDirectory,
        string targetDirectory,
        IReadOnlyCollection<string> newOwnedFiles) {
        string source = CanonicalizeDirectory(sourceDirectory);
        string target = CanonicalizeDirectory(targetDirectory);
        string[] normalizedNewOwnedFiles = newOwnedFiles
            .Select(NormalizeOwnedRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (normalizedNewOwnedFiles.Any(IsInstallTransactionPath)) {
            throw new InvalidDataException(
                $"The ownership inventory uses the reserved {InstallTransactionDirectoryName} install-transaction path.");
        }

        InstallDestinationValidation destinationValidation = InspectInstallDestination(
            source,
            target,
            normalizedNewOwnedFiles);
        if (!destinationValidation.IsValid)
            throw new InvalidOperationException(destinationValidation.Message);

        bool targetExisted = Directory.Exists(target);
        Directory.CreateDirectory(target);
        HashSet<string> newOwnedSet = normalizedNewOwnedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> previousOwnedSet = destinationValidation.PreviousOwnedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] metadataFiles = [ReleaseManifestName, OwnedFilesManifestName];
        string[] staleOwnedFiles = previousOwnedSet
            .Where(path => !newOwnedSet.Contains(path))
            .OrderByDescending(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] affectedFiles = newOwnedSet
            .Concat(previousOwnedSet)
            .Append(PortableMarkerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] initiallyMissingDirectories = FindInitiallyMissingOwnedDirectories(target, affectedFiles);
        string transactionRoot = Path.Combine(target, InstallTransactionDirectoryName);
        string stageRoot = Path.Combine(transactionRoot, InstallTransactionStageDirectoryName);
        string backupRoot = Path.Combine(transactionRoot, InstallTransactionBackupDirectoryName);
        InstallTransactionJournal? journal = null;
        int copiedFiles = 0;

        if (File.Exists(transactionRoot) || Directory.Exists(transactionRoot)) {
            throw new InvalidOperationException(
                $"The reserved install-transaction path is already in use: {transactionRoot}");
        }

        try {
            Directory.CreateDirectory(transactionRoot);
            EnsureNoReparsePath(target, transactionRoot);
            journal = new InstallTransactionJournal {
                SchemaVersion = InstallTransactionSchemaVersion,
                TargetDirectory = target,
                TargetExisted = targetExisted,
                State = InstallTransactionState.Preparing,
                Files = affectedFiles
                    .Select(path => new InstallTransactionFile {
                        Path = path,
                        Existed = IsRegularFile(ResolveOwnedPath(target, path))
                    })
                    .ToList(),
                InitiallyMissingDirectories = initiallyMissingDirectories.ToList()
            };
            WriteInstallTransactionJournal(transactionRoot, journal);

            Directory.CreateDirectory(stageRoot);
            Directory.CreateDirectory(backupRoot);
            foreach (string relativePath in normalizedNewOwnedFiles)
                CopyOwnedFile(source, stageRoot, relativePath);

            foreach (InstallTransactionFile file in journal.Files.Where(file => file.Existed))
                CopyOwnedFile(target, backupRoot, file.Path);

            SetInstallTransactionState(transactionRoot, journal, InstallTransactionState.Committing);

            foreach (string relativePath in normalizedNewOwnedFiles.Where(path => !metadataFiles.Contains(path, StringComparer.OrdinalIgnoreCase))) {
                CopyOwnedFile(stageRoot, target, relativePath);
                copiedFiles++;
            }

            foreach (string relativePath in staleOwnedFiles)
                DeleteOwnedFileAndEmptyParents(target, relativePath);

            RemovePortableMarker(target);

            foreach (string relativePath in metadataFiles) {
                if (!newOwnedSet.Contains(relativePath))
                    throw new InvalidDataException($"The new ownership inventory is missing required metadata: {relativePath}");
                CopyOwnedFile(stageRoot, target, relativePath);
                copiedFiles++;
            }

            SetInstallTransactionState(transactionRoot, journal, InstallTransactionState.Committed);
        }
        catch (Exception installException) {
            Exception? rollbackException = null;
            try {
                if (journal?.State == InstallTransactionState.Committing)
                    RollBackInstallTransaction(target, transactionRoot, journal);
                else
                    CleanInstallTransaction(target, transactionRoot, targetExisted);
            }
            catch (Exception ex) {
                rollbackException = ex;
            }

            if (rollbackException is not null) {
                throw new IOException(
                    "The install transaction failed and automatic rollback could not finish. " +
                    "Retry the same install after releasing any locked files; XeCLI will recover the saved transaction before updating.",
                    new AggregateException(installException, rollbackException));
            }

            throw new IOException(
                targetExisted
                    ? "The install transaction failed; the previous XeCLI installation was restored."
                    : "The install transaction failed; the incomplete new installation was removed.",
                installException);
        }

        CleanInstallTransaction(target, transactionRoot, targetExisted: true);
        return new InstallCopyResult(copiedFiles, staleOwnedFiles.Length, destinationValidation.PreviousOwnedFiles.Count > 0);
    }

    internal static void RecoverInterruptedInstall(string source, string target) {
        if (!IsSafeInstallDirectory(target) ||
            IsSameOrNestedDirectory(source, target) ||
            IsSameOrNestedDirectory(target, source)) {
            return;
        }

        if (TryFindReparseAncestor(target, out string targetReparsePath))
            throw new InvalidOperationException(
                $"An interrupted install cannot be recovered through a symbolic link or reparse point: {targetReparsePath}");
        if (TryFindFileAncestor(target, out string targetFileAncestor))
            throw new InvalidOperationException(
                $"An interrupted install cannot be recovered because an install-directory parent is a file: {targetFileAncestor}");

        string transactionRoot = Path.Combine(target, InstallTransactionDirectoryName);
        if (!File.Exists(transactionRoot) && !Directory.Exists(transactionRoot))
            return;
        if (!Directory.Exists(transactionRoot) || HasReparsePoint(transactionRoot)) {
            throw new InvalidOperationException(
                $"The reserved install-transaction path is unsafe and must be inspected manually: {transactionRoot}");
        }

        string journalPath = Path.Combine(transactionRoot, InstallTransactionJournalName);
        if (!IsRegularFile(journalPath) && IsDiscardableUnjournaledTransaction(transactionRoot)) {
            CleanInstallTransaction(target, transactionRoot, targetExisted: true);
            return;
        }

        InstallTransactionJournal journal = ReadInstallTransactionJournal(transactionRoot, target);
        if (journal.State == InstallTransactionState.Committed) {
            CleanInstallTransaction(target, transactionRoot, targetExisted: true);
            return;
        }

        if (journal.State == InstallTransactionState.Committing) {
            RollBackInstallTransaction(target, transactionRoot, journal);
            return;
        }

        CleanInstallTransaction(target, transactionRoot, journal.TargetExisted);
    }

    private static string[] FindInitiallyMissingOwnedDirectories(
        string targetRoot,
        IEnumerable<string> affectedFiles) {
        HashSet<string> missing = new(StringComparer.OrdinalIgnoreCase);
        foreach (string relativePath in affectedFiles) {
            string? relativeParent = Path.GetDirectoryName(
                NormalizeOwnedRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar));
            while (!string.IsNullOrWhiteSpace(relativeParent) && relativeParent != ".") {
                string normalizedParent = NormalizeOwnedRelativePath(relativeParent);
                string fullParent = ResolveOwnedPath(targetRoot, normalizedParent);
                if (File.Exists(fullParent))
                    throw new IOException($"An install path parent is a file: {normalizedParent}");
                if (!Directory.Exists(fullParent))
                    missing.Add(normalizedParent);
                relativeParent = Path.GetDirectoryName(relativeParent);
            }
        }

        return missing
            .OrderByDescending(path => path.Count(character => character == '/'))
            .ThenByDescending(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void RollBackInstallTransaction(
        string targetRoot,
        string transactionRoot,
        InstallTransactionJournal journal) {
        string backupRoot = Path.Combine(transactionRoot, InstallTransactionBackupDirectoryName);
        string[] metadataFiles = [ReleaseManifestName, OwnedFilesManifestName];
        IEnumerable<InstallTransactionFile> orderedFiles = journal.Files
            .Where(file => !metadataFiles.Contains(file.Path, StringComparer.OrdinalIgnoreCase))
            .Concat(journal.Files.Where(file => metadataFiles.Contains(file.Path, StringComparer.OrdinalIgnoreCase)));

        Directory.CreateDirectory(targetRoot);
        foreach (InstallTransactionFile file in orderedFiles) {
            string targetPath = ResolveOwnedPath(targetRoot, file.Path);
            EnsureNoReparsePath(targetRoot, targetPath);
            if (Directory.Exists(targetPath))
                throw new IOException($"An install-transaction file was replaced by a directory: {file.Path}");

            if (file.Existed) {
                string backupPath = ResolveOwnedPath(backupRoot, file.Path);
                if (!IsRegularFile(backupPath))
                    throw new IOException($"The install-transaction backup is missing or unsafe: {file.Path}");
                if (!IsRegularFile(targetPath) || !FilesHaveSameContent(targetPath, backupPath))
                    CopyOwnedFile(backupRoot, targetRoot, file.Path);
            }
            else if (File.Exists(targetPath)) {
                File.Delete(targetPath);
            }
        }

        foreach (string relativeDirectory in journal.InitiallyMissingDirectories) {
            string directory = ResolveOwnedPath(targetRoot, relativeDirectory);
            EnsureNoReparsePath(targetRoot, directory);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }

        CleanInstallTransaction(targetRoot, transactionRoot, journal.TargetExisted);
    }

    private static void CleanInstallTransaction(
        string targetRoot,
        string transactionRoot,
        bool targetExisted) {
        if (File.Exists(transactionRoot))
            throw new IOException($"The reserved install-transaction path is a file: {transactionRoot}");
        string? detachedTransactionRoot = null;
        if (Directory.Exists(transactionRoot)) {
            EnsureTransactionTreeHasNoReparsePoints(transactionRoot);
            string? targetParent = Path.GetDirectoryName(targetRoot);
            if (string.IsNullOrWhiteSpace(targetParent))
                throw new IOException($"The install directory has no parent: {targetRoot}");
            detachedTransactionRoot = Path.Combine(
                targetParent,
                $".{Path.GetFileName(targetRoot)}.xecli-install-cleanup-{Guid.NewGuid():N}");
            Directory.Move(transactionRoot, detachedTransactionRoot);
        }

        if (!targetExisted &&
            Directory.Exists(targetRoot) &&
            !Directory.EnumerateFileSystemEntries(targetRoot).Any()) {
            Directory.Delete(targetRoot);
        }

        if (detachedTransactionRoot is not null) {
            EnsureTransactionTreeHasNoReparsePoints(detachedTransactionRoot);
            Directory.Delete(detachedTransactionRoot, recursive: true);
        }
    }

    private static bool IsDiscardableUnjournaledTransaction(string transactionRoot) {
        EnsureTransactionTreeHasNoReparsePoints(transactionRoot);
        foreach (string entry in Directory.EnumerateFileSystemEntries(transactionRoot)) {
            if (Directory.Exists(entry))
                return false;

            string name = Path.GetFileName(entry);
            if (!name.StartsWith($"{InstallTransactionJournalName}.", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }

    private static void EnsureTransactionTreeHasNoReparsePoints(string transactionRoot) {
        Stack<string> pending = new();
        pending.Push(transactionRoot);
        while (pending.Count > 0) {
            string current = pending.Pop();
            if (HasReparsePoint(current))
                throw new IOException($"The install transaction contains a reparse point: {current}");

            foreach (string entry in Directory.EnumerateFileSystemEntries(current)) {
                if (HasReparsePoint(entry))
                    throw new IOException($"The install transaction contains a reparse point: {entry}");
                if (Directory.Exists(entry))
                    pending.Push(entry);
            }
        }
    }

    private static void WriteInstallTransactionJournal(
        string transactionRoot,
        InstallTransactionJournal journal) {
        string journalPath = Path.Combine(transactionRoot, InstallTransactionJournalName);
        string temporaryPath = journalPath + $".{Guid.NewGuid():N}.tmp";
        try {
            string json = JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json);
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.Read,
                       bufferSize: 4096,
                       FileOptions.WriteThrough)) {
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, journalPath, overwrite: true);
        }
        finally {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void SetInstallTransactionState(
        string transactionRoot,
        InstallTransactionJournal journal,
        InstallTransactionState state) {
        InstallTransactionState previousState = journal.State;
        journal.State = state;
        try {
            WriteInstallTransactionJournal(transactionRoot, journal);
        }
        catch {
            journal.State = previousState;
            throw;
        }
    }

    private static InstallTransactionJournal ReadInstallTransactionJournal(
        string transactionRoot,
        string expectedTarget) {
        string journalPath = Path.Combine(transactionRoot, InstallTransactionJournalName);
        if (!IsRegularFile(journalPath))
            throw new InvalidOperationException(
                $"The interrupted install journal is missing or unsafe: {journalPath}");

        InstallTransactionJournal journal;
        try {
            journal = JsonSerializer.Deserialize<InstallTransactionJournal>(File.ReadAllText(journalPath))
                ?? throw new InvalidDataException("The install transaction journal is empty.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) {
            throw new InvalidOperationException(
                $"The interrupted install journal could not be validated: {ex.Message}",
                ex);
        }

        if (journal.SchemaVersion != InstallTransactionSchemaVersion ||
            !Enum.IsDefined(journal.State) ||
            string.IsNullOrWhiteSpace(journal.TargetDirectory) ||
            !IsSameDirectory(journal.TargetDirectory, expectedTarget)) {
            throw new InvalidOperationException("The interrupted install journal has an invalid identity or state.");
        }

        if (journal.Files is null || journal.InitiallyMissingDirectories is null)
            throw new InvalidOperationException("The interrupted install journal is missing its rollback inventory.");

        HashSet<string> seenFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstallTransactionFile file in journal.Files) {
            file.Path = NormalizeOwnedRelativePath(file.Path);
            if (IsInstallTransactionPath(file.Path) || !seenFiles.Add(file.Path))
                throw new InvalidOperationException("The interrupted install journal contains an unsafe or duplicate file path.");
        }

        HashSet<string> seenDirectories = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < journal.InitiallyMissingDirectories.Count; index++) {
            string path = NormalizeOwnedRelativePath(journal.InitiallyMissingDirectories[index]);
            if (IsInstallTransactionPath(path) || !seenDirectories.Add(path))
                throw new InvalidOperationException("The interrupted install journal contains an unsafe or duplicate directory path.");
            journal.InitiallyMissingDirectories[index] = path;
        }

        journal.InitiallyMissingDirectories = journal.InitiallyMissingDirectories
            .OrderByDescending(path => path.Count(character => character == '/'))
            .ThenByDescending(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return journal;
    }

    private static bool IsInstallTransactionPath(string relativePath) {
        return relativePath.Equals(InstallTransactionDirectoryName, StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith($"{InstallTransactionDirectoryName}/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool FilesHaveSameContent(string leftPath, string rightPath) {
        FileInfo left = new(leftPath);
        FileInfo right = new(rightPath);
        return left.Length == right.Length &&
               string.Equals(ComputeSha256(leftPath), ComputeSha256(rightPath), StringComparison.OrdinalIgnoreCase);
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

    private static InstallSourceValidation InvalidPublishedSource(string message) {
        return new InstallSourceValidation(false, message, CreatePublishCommandHint());
    }

    private static string CanonicalizeDirectory(string path) {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Directory path is required.", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    }

    private static string NormalizeOwnedRelativePath(string value) {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("An ownership path is empty.");

        string normalized = value.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized) || normalized.Contains(':'))
            throw new InvalidDataException($"Ownership path is not relative: {value}");

        string[] segments = normalized.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidDataException($"Ownership path is unsafe: {value}");

        return string.Join('/', segments);
    }

    private static string ResolveOwnedPath(string rootDirectory, string relativePath) {
        string root = CanonicalizeDirectory(rootDirectory);
        string normalized = NormalizeOwnedRelativePath(relativePath);
        string fullPath = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsStrictlyInsideDirectory(fullPath, root))
            throw new InvalidDataException($"Ownership path escapes the install directory: {relativePath}");
        return fullPath;
    }

    private static bool TryReadOwnedFilesManifest(
        string manifestPath,
        out IReadOnlyList<string> ownedFiles,
        out string error) {
        ownedFiles = [];
        error = string.Empty;
        if (!IsRegularFile(manifestPath)) {
            error = $"The ownership manifest is missing or unsafe: {manifestPath}";
            return false;
        }

        try {
            List<string> normalized = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(manifestPath)) {
                if (string.IsNullOrWhiteSpace(line))
                    throw new InvalidDataException("The ownership manifest contains an empty line.");
                string relativePath = NormalizeOwnedRelativePath(line);
                if (!seen.Add(relativePath))
                    throw new InvalidDataException($"The ownership manifest contains a duplicate path: {relativePath}");
                normalized.Add(relativePath);
            }

            if (normalized.Count == 0)
                throw new InvalidDataException("The ownership manifest is empty.");

            ownedFiles = normalized;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
            error = $"The ownership manifest could not be validated: {ex.Message}";
            return false;
        }
    }

    private static bool IsInstallerOwnedReleasePath(string relativePath) {
        if (relativePath.Equals(PortableMarkerName, StringComparison.OrdinalIgnoreCase) ||
            IsInstallTransactionPath(relativePath))
            return false;
        string topLevel = relativePath.Split('/', 2)[0];
        return !topLevel.Equals("UserData", StringComparison.OrdinalIgnoreCase) &&
               !topLevel.Equals("logs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRegularFile(string path) {
        if (!File.Exists(path))
            return false;
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static bool HasReparsePoint(string path) {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool TryFindReparseAncestor(string path, out string reparsePath) {
        string? current = CanonicalizeDirectory(path);
        while (!string.IsNullOrWhiteSpace(current)) {
            if ((Directory.Exists(current) || File.Exists(current)) && HasReparsePoint(current)) {
                reparsePath = current;
                return true;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || IsSameDirectory(parent, current))
                break;
            current = parent;
        }

        reparsePath = string.Empty;
        return false;
    }

    private static bool TryFindFileAncestor(string path, out string filePath) {
        string? current = Path.GetDirectoryName(CanonicalizeDirectory(path));
        while (!string.IsNullOrWhiteSpace(current)) {
            if (File.Exists(current)) {
                filePath = current;
                return true;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || IsSameDirectory(parent, current))
                break;
            current = parent;
        }

        filePath = string.Empty;
        return false;
    }

    private static bool HasInstallIdentity(string directory) {
        if (!IsRegularFile(Path.Combine(directory, "rgh.exe")) ||
            !IsRegularFile(Path.Combine(directory, ReleaseManifestName)) ||
            !TryReadOwnedFilesManifest(
                Path.Combine(directory, OwnedFilesManifestName),
                out IReadOnlyList<string> ownedFiles,
                out _)) {
            return false;
        }

        HashSet<string> ownedSet = ownedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ownedSet.Contains("rgh.exe") &&
               ownedSet.Contains(ReleaseManifestName) &&
               ownedSet.Contains(OwnedFilesManifestName);
    }

    private static bool IsSha256(string? value) {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }

    private static string ComputeSha256(string path) {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsStrictlyInsideDirectory(string candidate, string parent) {
        string relative = Path.GetRelativePath(parent, candidate);
        return relative != "." &&
               relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static bool IsSameOrNestedDirectory(string candidate, string parent) {
        return IsSameDirectory(candidate, parent) || IsStrictlyInsideDirectory(candidate, parent);
    }

    private static bool IsSafeInstallDirectory(string target) {
        string? root = Path.GetPathRoot(target);
        if (string.IsNullOrWhiteSpace(root) || IsSameDirectory(target, root))
            return false;

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows) && IsSameOrNestedDirectory(target, windows))
            return false;

        string[] unsafeRoots = [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        ];
        return !unsafeRoots.Any(path => !string.IsNullOrWhiteSpace(path) && IsSameDirectory(target, path));
    }

    private static void EnsureNoReparsePath(string rootDirectory, string fullPath) {
        string root = CanonicalizeDirectory(rootDirectory);
        if (Directory.Exists(root) && HasReparsePoint(root))
            throw new InvalidDataException($"The install directory is a reparse point: {root}");

        string relative = Path.GetRelativePath(root, fullPath);
        string current = root;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && HasReparsePoint(current))
                throw new InvalidDataException($"An install path is a reparse point: {relative}");
        }
    }

    private static void EnsureDestinationParentsAreDirectories(string rootDirectory, string fullPath) {
        string root = CanonicalizeDirectory(rootDirectory);
        string? current = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrWhiteSpace(current) && IsStrictlyInsideDirectory(current, root)) {
            if (File.Exists(current))
                throw new IOException($"An install path parent is a file: {Path.GetRelativePath(root, current)}");
            current = Path.GetDirectoryName(current);
        }
    }

    private static void CopyOwnedFile(string sourceRoot, string targetRoot, string relativePath) {
        string source = ResolveOwnedPath(sourceRoot, relativePath);
        string destination = ResolveOwnedPath(targetRoot, relativePath);
        EnsureNoReparsePath(sourceRoot, source);
        EnsureNoReparsePath(targetRoot, destination);
        if (!IsRegularFile(source))
            throw new FileNotFoundException($"Installer-owned source file is missing or unsafe: {relativePath}", source);
        if (Directory.Exists(destination))
            throw new IOException($"Installer-owned file collides with a directory: {relativePath}");

        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new IOException($"Installer-owned destination has no parent: {relativePath}");
        Directory.CreateDirectory(destinationDirectory);
        EnsureNoReparsePath(targetRoot, destinationDirectory);

        string temporary = destination + $".xecli-install-{Guid.NewGuid():N}.tmp";
        try {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void DeleteOwnedFileAndEmptyParents(string targetRoot, string relativePath) {
        string target = ResolveOwnedPath(targetRoot, relativePath);
        EnsureNoReparsePath(targetRoot, target);
        if (Directory.Exists(target))
            throw new IOException($"Installer-owned file was replaced by a directory: {relativePath}");
        if (File.Exists(target))
            File.Delete(target);

        string root = CanonicalizeDirectory(targetRoot);
        string? parent = Path.GetDirectoryName(target);
        while (!string.IsNullOrWhiteSpace(parent) && IsStrictlyInsideDirectory(parent, root)) {
            EnsureNoReparsePath(root, parent);
            if (!Directory.Exists(parent) || Directory.EnumerateFileSystemEntries(parent).Any())
                break;
            Directory.Delete(parent);
            parent = Path.GetDirectoryName(parent);
        }
    }

    private static void RemovePortableMarker(string targetRoot) {
        string marker = Path.Combine(CanonicalizeDirectory(targetRoot), PortableMarkerName);
        if (Directory.Exists(marker) || (File.Exists(marker) && HasReparsePoint(marker)))
            throw new IOException($"The installed portable marker is not a regular file: {marker}");
        if (File.Exists(marker))
            File.Delete(marker);
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
        return "Use the root of an extracted official XeCLI portable package, or use the portable stage created by tools\\build-release.ps1 (stage\\XeCLI-<version>-win-x64).";
    }

    private enum InstallTransactionState {
        Preparing,
        Committing,
        Committed
    }

    private sealed class InstallTransactionJournal {
        public int SchemaVersion { get; set; }
        public string TargetDirectory { get; set; } = string.Empty;
        public bool TargetExisted { get; set; }
        public InstallTransactionState State { get; set; }
        public List<InstallTransactionFile> Files { get; set; } = [];
        public List<string> InitiallyMissingDirectories { get; set; } = [];
    }

    private sealed class InstallTransactionFile {
        public string Path { get; set; } = string.Empty;
        public bool Existed { get; set; }
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
    string? PublishCommandHint) {
    public IReadOnlyList<string> OwnedFiles { get; init; } = [];
}

internal sealed record InstallDestinationValidation(
    bool IsValid,
    string Message,
    IReadOnlyList<string> PreviousOwnedFiles);

internal sealed record InstallCopyResult(
    int CopiedFileCount,
    int RemovedStaleFileCount,
    bool WasUpgrade);
