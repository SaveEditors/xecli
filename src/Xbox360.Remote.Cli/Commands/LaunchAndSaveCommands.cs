using System.ComponentModel;
using System.Globalization;
using System.Text;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class LaunchCommand : AsyncCommand<LaunchCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandArgument(0, "[XEX]")]
        [LocalizedDescription("XEX path to launch, for example Hdd1:\\Aurora\\Aurora.xex.")]
        public string? Xex { get; init; }

        [CommandOption("--xex <PATH>")]
        [LocalizedDescription("XEX path to launch, for example Hdd1:\\Aurora\\Aurora.xex.")]
        public string? XexPath { get; init; }

        [CommandOption("--directory <DIR>")]
        [LocalizedDescription("Working directory passed to XBDM. Defaults to the XEX folder.")]
        public string? Directory { get; init; }

        [CommandOption("--args <TEXT>")]
        [LocalizedDescription("Command-line arguments passed to the XEX.")]
        public string? Arguments { get; init; }

        [CommandOption("--titleid <TITLEID>")]
        [LocalizedDescription("Optional Title ID for display/logging.")]
        public string? TitleId { get; init; }

        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the generated XBDM command without executing it.")]
        public bool DryRun { get; init; }

        [CommandOption("--verify")]
        [LocalizedDescription("Require remote path verification before sending the launch request.")]
        public bool Verify { get; init; }

        [CommandOption("--no-verify")]
        [LocalizedDescription("Skip remote path verification for a known recovery path.")]
        public bool NoVerify { get; init; }

        [CommandOption("--wait")]
        [LocalizedDescription("Stop execution and require the requested active XEX plus observable transition evidence.")]
        public bool Wait { get; init; }

        [CommandOption("--wait-timeout <SEC>")]
        [LocalizedDescription("Maximum seconds to wait for a verified launch transition (default: 45).")]
        [DefaultValue(45)]
        public int WaitTimeoutSeconds { get; init; } = 45;

        [CommandOption("--notify")]
        [LocalizedDescription("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [LocalizedDescription("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [LocalizedDescription("Notification logo id (decimal or 0x hex).")]
        public string? NotifyLogo { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string? xexPath = !string.IsNullOrWhiteSpace(settings.XexPath) ? settings.XexPath : settings.Xex;
        if (string.IsNullOrWhiteSpace(xexPath)) {
            WriteLaunchValidationFailure(
                settings,
                "Provide a XEX path with `rgh launch <path>` or `--xex <path>`.",
                "LAUNCH_PATH_REQUIRED");
            return 1;
        }

        if (!TryValidateLaunchPath(xexPath, out string validationError)) {
            WriteLaunchValidationFailure(settings, validationError, "LAUNCH_PATH_INVALID");
            return 1;
        }

        string workingDirectory = !string.IsNullOrWhiteSpace(settings.Directory)
            ? settings.Directory
            : DeriveDirectory(xexPath);

        string command = BuildMagicBootCommand(xexPath, workingDirectory, settings.Arguments);
        string? titleSummary = TryFormatTitleId(settings.TitleId);

        if (settings.Verify && settings.NoVerify) {
            WriteLaunchValidationFailure(settings, "--verify and --no-verify cannot be used together.", "LAUNCH_VERIFY_CONFLICT");
            return 1;
        }
        if (settings.WaitTimeoutSeconds is < 1 or > 300) {
            WriteLaunchValidationFailure(settings, "--wait-timeout must be between 1 and 300 seconds.", "LAUNCH_WAIT_TIMEOUT_INVALID");
            return 1;
        }

        if (settings.DryRun) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "launch",
                    Mode = "dry-run",
                    DryRun = true,
                    RequestSent = false,
                    RequestAcknowledged = false,
                    Verified = false,
                    VerificationRequested = settings.Verify,
                    VerificationSkipped = settings.NoVerify,
                    WaitRequested = settings.Wait,
                    NotifyRequested = settings.Notify,
                    Xex = xexPath,
                    Directory = workingDirectory,
                    Arguments = settings.Arguments,
                    TitleId = titleSummary,
                    Command = command
                });
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Launch Preview[/]").RuleStyle("grey"));
            AnsiConsole.MarkupLine($"[grey]Command:[/] {Markup.Escape(command)}");
            if (!string.IsNullOrWhiteSpace(titleSummary))
                AnsiConsole.MarkupLine($"[grey]Title:[/] {Markup.Escape(titleSummary)}");
            return 0;
        }

        if (settings.Wait)
            return await ExecuteAndWaitAsync(settings, xexPath, workingDirectory, command, titleSummary);

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            try {
                bool verified = await ValidateLaunchTargetAsync(client, xexPath, settings.Verify, settings.NoVerify, CancellationToken.None);
                XbdmResponse response = await client.SendCommandAsync(command, CancellationToken.None);
                string acknowledgement = response.ExpectMessage(
                    command,
                    200,
                    XbdmResponseType.SingleResponse,
                    XbdmResponseBodyRequirement.RequiredNonEmpty);

                await NotifyHelpers.TrySendOperationNotificationAsync(
                    client,
                    settings.Notify,
                    settings.NotifyIcon,
                    settings.NotifyLogo,
                    "Success :)",
                    CancellationToken.None);

                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Operation = "launch",
                        Mode = "live",
                        DryRun = false,
                        RequestSent = true,
                        RequestAcknowledged = true,
                        Verified = verified,
                        VerificationRequested = settings.Verify,
                        VerificationSkipped = settings.NoVerify,
                        WaitRequested = false,
                        TransitionVerified = false,
                        NotifyRequested = settings.Notify,
                        Xex = xexPath,
                        Directory = workingDirectory,
                        Arguments = settings.Arguments,
                        TitleId = titleSummary,
                        Command = command,
                        ResponseStatusCode = response.StatusCode,
                        ResponseType = response.ResponseType.ToString(),
                        Acknowledgement = acknowledgement
                    });
                    return 0;
                }

                string detail = verified
                    ? $"[green]Target verified before request:[/] {Markup.Escape(xexPath)}"
                    : $"[grey]Target not verified before request:[/] {Markup.Escape(xexPath)}";
                OperationFeedback.WriteWarning("Launch request sent", detail);
                if (!string.IsNullOrWhiteSpace(titleSummary))
                    AnsiConsole.MarkupLine($"[grey]Target title:[/] {Markup.Escape(titleSummary)}");
                if (!string.IsNullOrWhiteSpace(settings.Arguments))
                    AnsiConsole.MarkupLine($"[grey]Arguments:[/] {Markup.Escape(settings.Arguments)}");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) {
                WriteLaunchFailure(settings, ex);
                return 1;
            }
        }, CancellationToken.None);
    }

    private static async Task<int> ExecuteAndWaitAsync(
        Settings settings,
        string xexPath,
        string workingDirectory,
        string command,
        string? titleSummary) {
        (string ip, int port, int timeout) target;
        try {
            target = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) {
            WriteLaunchFailure(settings, ex);
            return 1;
        }

        bool executionStopped = false;
        bool verified = false;
        uint? previousProcessId = null;
        string? previousRunningXex = null;
        XbdmResponse response = default;
        string? acknowledgement = null;
        try {
            using XbdmClient client = await CliHelpers.ConnectResolvedAsync(target.ip, target.port, target.timeout, CancellationToken.None);
            verified = await ValidateLaunchTargetAsync(client, xexPath, settings.Verify, settings.NoVerify, CancellationToken.None);
            previousProcessId = await client.GetCurrentProcessIdAsync(CancellationToken.None);
            if (!previousProcessId.HasValue)
                throw new IOException("XBDM did not return the active process ID before launch.");
            previousRunningXex = await client.GetRunningXexPathAsync(null, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(previousRunningXex))
                throw new IOException("XBDM did not return the active XEX before launch.");

            await NotifyHelpers.TrySendOperationNotificationAsync(
                client,
                settings.Notify,
                settings.NotifyIcon,
                settings.NotifyLogo,
                "Success :)",
                CancellationToken.None);
            await client.DebugStopAsync(CancellationToken.None);
            executionStopped = true;
            response = await client.SendCommandAsync(command, CancellationToken.None);
            acknowledgement = response.ExpectMessage(
                command,
                200,
                XbdmResponseType.SingleResponse,
                XbdmResponseBodyRequirement.RequiredNonEmpty);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) {
            if (executionStopped)
                await XbdmTransitionVerifier.TryResumeAsync(target, CancellationToken.None);
            WriteLaunchFailure(settings, ex);
            return 1;
        }

        XbdmTransitionObservation transition = await XbdmTransitionVerifier.WaitForProcessChangeAsync(
            target,
            previousProcessId,
            previousRunningXex,
            xexPath,
            settings.WaitTimeoutSeconds,
            CancellationToken.None);
        if (!transition.Succeeded) {
            bool resumeAttempted = executionStopped;
            bool resumeSucceeded = !resumeAttempted || await XbdmTransitionVerifier.TryResumeAsync(target, CancellationToken.None);
            WriteLaunchTransitionFailure(settings, xexPath, transition, resumeAttempted, resumeSucceeded);
            return 1;
        }

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Operation = "launch",
                Mode = "live",
                Status = "completed",
                DryRun = false,
                RequestSent = true,
                RequestAcknowledged = true,
                Verified = verified,
                VerificationRequested = settings.Verify,
                VerificationSkipped = settings.NoVerify,
                WaitRequested = true,
                TransitionVerified = true,
                ExecutionStoppedBeforeRequest = executionStopped,
                transition.DisconnectObserved,
                transition.PreviousProcessId,
                transition.CurrentProcessId,
                transition.PreviousRunningXex,
                transition.RunningXex,
                TransitionEvidence = transition.Evidence,
                TransitionElapsedMs = transition.ElapsedMilliseconds,
                NotifyRequested = settings.Notify,
                Xex = xexPath,
                Directory = workingDirectory,
                Arguments = settings.Arguments,
                TitleId = titleSummary,
                Command = command,
                ResponseStatusCode = response.StatusCode,
                ResponseType = response.ResponseType.ToString(),
                Acknowledgement = acknowledgement
            });
            return 0;
        }

        OperationFeedback.WriteSuccess("Launch completed", $"[green]Active XEX verified:[/] {Markup.Escape(transition.RunningXex ?? xexPath)}");
        return 0;
    }

    private static void WriteLaunchTransitionFailure(
        Settings settings,
        string xexPath,
        XbdmTransitionObservation transition,
        bool resumeAttempted,
        bool resumeSucceeded) {
        string message = $"The launch request was acknowledged, but XBDM did not verify the requested active XEX and transition evidence for {xexPath} within {settings.WaitTimeoutSeconds} seconds.";
        if (settings.Json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Launch transition not observed",
                message,
                "LAUNCH_TRANSITION_NOT_OBSERVED",
                new[] {
                    transition.LastError ?? "Inspect the active title before retrying.",
                    resumeAttempted
                        ? resumeSucceeded ? "XeCLI resumed execution after the failed transition check." : "XeCLI could not confirm that execution resumed; inspect the console immediately."
                        : "No execution resume was required."
                }));
            return;
        }

        OperationFeedback.WriteFailure("Launch transition not observed", message);
    }

    private static void WriteLaunchValidationFailure(Settings settings, string message, string code) {
        if (settings.Json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Launch failed",
                message,
                code,
                new[] { "Use a complete remote console path such as Hdd1:\\Aurora\\Aurora.xex." }));
            return;
        }

        if (code == "LAUNCH_PATH_REQUIRED") {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
            return;
        }

        OperationFeedback.WriteFailure("Launch failed", message);
    }

    private static void WriteLaunchFailure(Settings settings, Exception exception) {
        if (settings.Json) {
            string? targetDisplay = string.IsNullOrWhiteSpace(settings.Ip)
                ? null
                : $"{settings.Ip}:{settings.Port ?? 730}";
            if (ConnectionFailureModel.TryBuildXbdmError(
                    exception,
                    exception.Message,
                    targetDisplay,
                    out CliErrorEnvelope protocolError)) {
                CliOutput.EmitJsonError(protocolError);
                return;
            }

            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Launch failed",
                exception.Message,
                "LAUNCH_FAILED",
                new[] {
                    "Confirm the remote XEX exists and XBDM is responsive.",
                    "Retry with --verify to require a remote path check before the launch request."
                }));
            return;
        }

        string message = exception is XbdmProtocolViolationException violation && violation.StatusCode != 200
            ? $"Launch request rejected: {violation.RawResponse}"
            : exception.Message;
        OperationFeedback.WriteFailure("Launch failed", message);
    }

    private static bool TryValidateLaunchPath(string xexPath, out string error) {
        string trimmed = xexPath.Trim();
        if (trimmed.Length == 0) {
            error = "Launch target must be a remote console path like Hdd1:\\Games\\Foo\\default.xex or /Hdd1/Games/Foo/default.xex.";
            return false;
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal)) {
            string[] segments = trimmed.Trim('/').Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2) {
                error = string.Empty;
                return true;
            }

            error = "Launch target must be a remote console path like Hdd1:\\Games\\Foo\\default.xex or /Hdd1/Games/Foo/default.xex.";
            return false;
        }

        if (trimmed.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase)) {
            string[] segments = trimmed.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3) {
                error = string.Empty;
                return true;
            }

            error = "Launch target must be a remote console path like Hdd1:\\Games\\Foo\\default.xex or /Hdd1/Games/Foo/default.xex.";
            return false;
        }

        int colon = trimmed.IndexOf(':');
        if (colon > 0 && colon < trimmed.Length - 1) {
            string remainder = trimmed.Substring(colon + 1);
            if ((remainder[0] == '\\' || remainder[0] == '/') && remainder.TrimStart('\\', '/').Length > 0) {
                error = string.Empty;
                return true;
            }
        }

        error = "Launch target must be a remote console path like Hdd1:\\Games\\Foo\\default.xex or /Hdd1/Games/Foo/default.xex.";
        return false;
    }

    internal static string BuildMagicBootCommand(string xexPath, string workingDirectory, string? arguments) {
        List<string> parts = new List<string> {
            "magicboot",
            $"title=\"{EscapeQuoted(xexPath)}\""
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            parts.Add($"directory=\"{EscapeQuoted(workingDirectory)}\"");
        if (!string.IsNullOrWhiteSpace(arguments))
            parts.Add($"cmdline=\"{EscapeQuoted(arguments)}\"");

        return string.Join(' ', parts);
    }

    private static string EscapeQuoted(string text) {
        return text.Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    internal static string DeriveDirectory(string xexPath) {
        int slash = Math.Max(xexPath.LastIndexOf('\\'), xexPath.LastIndexOf('/'));
        return slash > 0 ? xexPath.Substring(0, slash) : xexPath;
    }

    private static async Task<bool> ValidateLaunchTargetAsync(
        XbdmClient client,
        string xexPath,
        bool requireVerification,
        bool skipVerification,
        CancellationToken cancellationToken) {
        if (skipVerification)
            return false;

        if (!TrySplitPath(xexPath, out string directory, out string fileName)) {
            if (requireVerification)
                throw new InvalidOperationException("Launch verification requires a full console path with a directory component.");

            return false;
        }

        IReadOnlyList<XbdmFileEntry> entries;
        try {
            entries = await client.GetDirectoryAsync(directory, cancellationToken);
        }
        catch (XbdmProtocolViolationException ex) when (!requireVerification && ex.StatusCode == 414) {
            return false;
        }
        catch (Exception ex) {
            throw new IOException($"Unable to verify launch target {Markup.Escape(xexPath)}.", ex);
        }

        if (!entries.Any(entry => string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase))) {
            throw new IOException($"Launch target not found: {xexPath}");
        }

        return true;
    }

    private static bool TrySplitPath(string xexPath, out string directory, out string fileName) {
        int slash = Math.Max(xexPath.LastIndexOf('\\'), xexPath.LastIndexOf('/'));
        if (slash <= 0 || slash >= xexPath.Length - 1) {
            directory = string.Empty;
            fileName = string.Empty;
            return false;
        }

        directory = xexPath.Substring(0, slash);
        fileName = xexPath.Substring(slash + 1);
        return !string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(fileName);
    }

    internal static string? TryFormatTitleId(string? text) {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (!TryParseHex(text, out uint titleId))
            return text;

        if (TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? entry) && entry != null)
            return $"{entry.Name} (0x{titleId:X8})";

        return $"0x{titleId:X8}";
    }

    private static bool TryParseHex(string text, out uint value) {
        string cleaned = text.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(2);
        return uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}

public sealed class SaveListCommand : AsyncCommand<SaveListCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--titleid <TITLEID>")]
        [LocalizedDescription("Title ID in hex (for example 4D530805).")]
        public string? TitleId { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [LocalizedDescription("Restrict to one 16-character profile ID.")]
        public string? ProfileId { get; init; }

        [CommandOption("--device <ROOTS>")]
        [LocalizedDescription("Optional comma-separated storage roots, for example Hdd1 or Hdd1,Usb0.")]
        public string? Devices { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.TitleId)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save list", "--titleid is required.", "SAVE_TITLE_ID_REQUIRED");
        }

        if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint titleId)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save list", "Invalid --titleid.", "SAVE_TITLE_ID_INVALID");
        }

        if (!string.IsNullOrWhiteSpace(settings.ProfileId) && !SaveHelpers.IsProfileId(settings.ProfileId)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save list", "--profile must be a 16-character profile id.", "SAVE_PROFILE_ID_INVALID");
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        List<SaveHelpers.SaveFileRecord> files = await SaveHelpers.EnumerateSaveFilesAsync(ip, port, user, pass, timeout, titleId, settings.ProfileId, settings.Devices);
        if (settings.Json) {
            CliOutput.EmitJson(files);
            return 0;
        }

        string titleLabel = SaveHelpers.FormatTitleLabel(titleId);
        AnsiConsole.Write(new Rule($"[bold deepskyblue1]Save Files[/] [grey]{Markup.Escape(titleLabel)}[/]").RuleStyle("grey"));
        if (files.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No save files found for this title.[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[green]Path[/]"));
        table.AddColumn(new TableColumn("[grey]Profile[/]"));
        table.AddColumn(new TableColumn("[cyan]Size[/]"));
        table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
        foreach (SaveHelpers.SaveFileRecord file in files.OrderBy(f => f.Device, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.ProfileId, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)) {
            table.AddRow(
                $"[green]{Markup.Escape(file.RemotePath)}[/]",
                $"[grey]{Markup.Escape(file.ProfileId)}[/]",
                $"[cyan]{FtpHelpers.FormatBytes(file.Size)}[/]",
                file.Modified != DateTime.MinValue ? CliOutput.FormatTimestamp(file.Modified) : "[grey]unknown[/]");
        }
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class SaveExtractCommand : AsyncCommand<SaveExtractCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--titleid <TITLEID>")]
        [LocalizedDescription("Title ID in hex (for example 4D530805).")]
        public string? TitleId { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [LocalizedDescription("Restrict to one 16-character profile ID.")]
        public string? ProfileId { get; init; }

        [CommandOption("--out <DIR>")]
        [LocalizedDescription("Destination directory.")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--overwrite")]
        [LocalizedDescription("Overwrite files that already exist.")]
        public bool Overwrite { get; init; }

        [CommandOption("--device <ROOTS>")]
        [LocalizedDescription("Optional comma-separated storage roots, for example Hdd1 or Hdd1,Usb0.")]
        public string? Devices { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.TitleId)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save extract", "--titleid is required.", "SAVE_TITLE_ID_REQUIRED");
        }

        if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint titleId)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save extract", "Invalid --titleid.", "SAVE_TITLE_ID_INVALID");
        }

        if (string.IsNullOrWhiteSpace(settings.OutputDirectory)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save extract", "--out is required.", "SAVE_OUTPUT_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(settings.ProfileId) || !SaveHelpers.IsProfileId(settings.ProfileId)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save extract", "--profile must be a 16-character profile id.", "SAVE_PROFILE_ID_INVALID");
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        List<SaveHelpers.SaveFileRecord> files = await SaveHelpers.EnumerateSaveFilesAsync(ip, port, user, pass, timeout, titleId, settings.ProfileId, settings.Devices);
        if (files.Count == 0) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "save-extract",
                    Status = "no-files",
                    TitleId = titleId.ToString("X8", CultureInfo.InvariantCulture),
                    settings.ProfileId,
                    OutputDirectory = Path.GetFullPath(settings.OutputDirectory),
                    ExtractedCount = 0,
                    SkippedCount = 0,
                    Bytes = 0L,
                    Files = Array.Empty<object>()
                });
                return 0;
            }

            AnsiConsole.MarkupLine("[yellow]No save files found for this title.[/]");
            return 0;
        }

        string titleFolder = SaveHelpers.BuildOutputFolderName(titleId);
        string outputRoot = Path.Combine(settings.OutputDirectory!, titleFolder);
        string outputDisplayName = Path.GetFileName(outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(outputDisplayName))
            outputDisplayName = outputRoot;
        Directory.CreateDirectory(outputRoot);

        List<(SaveHelpers.SaveFileRecord File, string LocalPath)> plan = new List<(SaveHelpers.SaveFileRecord, string)>();
        int skipped = 0;
        foreach (SaveHelpers.SaveFileRecord file in files) {
            string localPath = Path.Combine(outputRoot, file.Device, file.ProfileId, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string? localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(localDir))
                Directory.CreateDirectory(localDir);

            if (!settings.Overwrite && File.Exists(localPath)) {
                skipped++;
                continue;
            }

            plan.Add((file, localPath));
        }

        if (plan.Count == 0) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "save-extract",
                    Status = "skipped-existing",
                    TitleId = titleId.ToString("X8", CultureInfo.InvariantCulture),
                    settings.ProfileId,
                    OutputDirectory = Path.GetFullPath(outputRoot),
                    ExtractedCount = 0,
                    SkippedCount = skipped,
                    Bytes = 0L,
                    Files = Array.Empty<object>()
                });
                return 0;
            }

            OperationFeedback.WriteWarning("Save extract skipped", $"all matching files already exist in [grey]{Markup.Escape(outputDisplayName)}[/]");
            return 0;
        }

        if (settings.Json) {
            foreach ((SaveHelpers.SaveFileRecord file, string localPath) in plan)
                await SaveHelpers.DownloadFileAsync(ip, port, user, pass, timeout, file.RemotePath, localPath);

            CliOutput.EmitJson(new {
                Operation = "save-extract",
                Status = "completed",
                TitleId = titleId.ToString("X8", CultureInfo.InvariantCulture),
                settings.ProfileId,
                OutputDirectory = Path.GetFullPath(outputRoot),
                ExtractedCount = plan.Count,
                SkippedCount = skipped,
                Bytes = plan.Sum(item => item.File.Size),
                Files = plan.Select(item => new {
                    RemotePath = item.File.RemotePath,
                    LocalPath = Path.GetFullPath(item.LocalPath),
                    item.File.Size
                }).ToArray()
            });
            return 0;
        }

        await CliOutput.RunBatchProgressAsync(
            $"Save extract {SaveHelpers.FormatTitleLabel(titleId)}",
            plan.Select(item => new CliOutput.TransferBatchItem(item.File.RemotePath, item.File.Size)).ToList(),
            async batch => {
                foreach ((SaveHelpers.SaveFileRecord file, string localPath) in plan) {
                    batch.StartFile(file.RemotePath, file.Size);
                    Progress<long> perFile = new Progress<long>(value => batch.ReportFileProgress(value));
                    await SaveHelpers.DownloadFileAsync(ip, port, user, pass, timeout, file.RemotePath, localPath, perFile);
                    batch.CompleteFile();
                }
            });

        OperationFeedback.WriteSuccess(
            "Save extract complete",
            $"[green]{plan.Count}[/] file(s)  [silver]{FtpHelpers.FormatBytes(plan.Sum(item => item.File.Size))}[/] -> [white]{Markup.Escape(outputDisplayName)}[/]");
        if (skipped > 0)
            OperationFeedback.WriteWarning("Save extract skipped existing files", skipped.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}

public sealed class SaveInjectCommand : AsyncCommand<SaveInjectCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--titleid <TITLEID>")]
        [LocalizedDescription("Title ID in hex (for example 4D530805).")]
        public string? TitleId { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [LocalizedDescription("Destination 16-character profile ID.")]
        public string? ProfileId { get; init; }

        [CommandOption("--device <ROOT>")]
        [LocalizedDescription("Destination storage root, for example Hdd1 or Usb0 (default: Hdd1).")]
        public string? Device { get; init; }

        [CommandOption("--in <PATH>")]
        [LocalizedDescription("Local file or directory to upload.")]
        public string? InputPath { get; init; }

        [CommandOption("--remote-path <RELATIVE>")]
        [LocalizedDescription("Relative save path to use when --in points to a single file.")]
        public string? RemotePath { get; init; }

        [CommandOption("--overwrite")]
        [LocalizedDescription("Overwrite remote files that already exist.")]
        public bool Overwrite { get; init; }

        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the remote paths that would be uploaded without writing anything.")]
        public bool DryRun { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint titleId)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save inject", "--titleid is required.", "SAVE_TITLE_ID_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(settings.ProfileId) || !SaveHelpers.IsProfileId(settings.ProfileId)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save inject", "--profile must be a 16-character profile id.", "SAVE_PROFILE_ID_INVALID");
        }

        if (string.IsNullOrWhiteSpace(settings.InputPath)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save inject", "--in is required.", "SAVE_INPUT_REQUIRED");
        }

        string inputPath = Path.GetFullPath(settings.InputPath);
        bool isDirectory = Directory.Exists(inputPath);
        bool isFile = File.Exists(inputPath);
        if (!isDirectory && !isFile) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save inject", "Input path was not found.", "SAVE_INPUT_NOT_FOUND");
        }

        if (isDirectory && !string.IsNullOrWhiteSpace(settings.RemotePath)) {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save inject", "--remote-path can only be used when --in points to a single file.", "SAVE_REMOTE_PATH_INVALID");
        }

        string device = string.IsNullOrWhiteSpace(settings.Device) ? "Hdd1" : settings.Device.Trim();
        string titleRoot = SaveHelpers.BuildTitleRoot(device, settings.ProfileId, titleId);
        List<SaveHelpers.SaveUploadRecord> uploads;
        try {
            uploads = isDirectory
                ? SaveHelpers.BuildUploadPlanFromDirectory(inputPath, titleRoot)
                : SaveHelpers.BuildUploadPlanFromFile(inputPath, titleRoot, settings.RemotePath);
        }
        catch (ArgumentException ex) when (ex.ParamName == "relativeRemotePath") {
            return SaveCommandOutput.WriteValidationFailure(settings.Json, "save inject", ex.Message, "SAVE_REMOTE_PATH_INVALID");
        }

        if (uploads.Count == 0) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "save-inject",
                    Status = "no-files",
                    DryRun = settings.DryRun,
                    TitleId = titleId.ToString("X8", CultureInfo.InvariantCulture),
                    settings.ProfileId,
                    Device = device,
                    UploadedCount = 0,
                    SkippedCount = 0,
                    Bytes = 0L,
                    Files = Array.Empty<object>()
                });
                return 0;
            }

            AnsiConsole.MarkupLine("[yellow]No files found to upload.[/]");
            return 0;
        }

        if (settings.DryRun) {
            object payload = uploads.Select(u => new {
                u.LocalPath,
                u.RemotePath,
                u.Size
            }).ToList();

            if (settings.Json) {
                CliOutput.EmitJson(payload);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Save Inject Preview[/]").RuleStyle("grey"));
            Table preview = CliOutput.CreateTable();
            preview.AddColumn(new TableColumn("[green]Local[/]"));
            preview.AddColumn(new TableColumn("[cyan]Remote[/]"));
            preview.AddColumn(new TableColumn("[grey]Size[/]"));
            foreach (SaveHelpers.SaveUploadRecord upload in uploads) {
                preview.AddRow(
                    $"[green]{Markup.Escape(upload.LocalPath)}[/]",
                    $"[cyan]{Markup.Escape(upload.RemotePath)}[/]",
                    $"[grey]{FtpHelpers.FormatBytes(upload.Size)}[/]");
            }
            AnsiConsole.Write(preview);
            return 0;
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        List<SaveHelpers.SaveUploadRecord> plan = new List<SaveHelpers.SaveUploadRecord>();
        int skipped = 0;
        foreach (SaveHelpers.SaveUploadRecord upload in uploads) {
            if (!settings.Overwrite) {
                bool exists = await SaveHelpers.RemoteFileExistsAsync(ip, port, user, pass, timeout, upload.RemotePath);
                if (exists) {
                    skipped++;
                    continue;
                }
            }

            plan.Add(upload);
        }

        if (plan.Count == 0) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "save-inject",
                    Status = "skipped-existing",
                    DryRun = false,
                    TitleId = titleId.ToString("X8", CultureInfo.InvariantCulture),
                    settings.ProfileId,
                    Device = device,
                    UploadedCount = 0,
                    SkippedCount = skipped,
                    Bytes = 0L,
                    Files = Array.Empty<object>()
                });
                return 0;
            }

            OperationFeedback.WriteWarning("Save inject skipped", $"all target files already exist in [grey]{Markup.Escape(titleRoot)}[/]");
            return 0;
        }

        if (settings.Json) {
            foreach (SaveHelpers.SaveUploadRecord upload in plan)
                await SaveHelpers.UploadFileAsync(ip, port, user, pass, timeout, upload.LocalPath, upload.RemotePath, overwriteApproved: settings.Overwrite);

            CliOutput.EmitJson(new {
                Operation = "save-inject",
                Status = "completed",
                DryRun = false,
                TitleId = titleId.ToString("X8", CultureInfo.InvariantCulture),
                settings.ProfileId,
                Device = device,
                UploadedCount = plan.Count,
                SkippedCount = skipped,
                Bytes = plan.Sum(item => item.Size),
                Files = plan.Select(item => new {
                    item.LocalPath,
                    item.RemotePath,
                    item.Size
                }).ToArray()
            });
            return 0;
        }

        await CliOutput.RunBatchProgressAsync(
            $"Save inject {SaveHelpers.FormatTitleLabel(titleId)}",
            plan.Select(item => new CliOutput.TransferBatchItem(item.RemotePath, item.Size)).ToList(),
            async batch => {
                foreach (SaveHelpers.SaveUploadRecord upload in plan) {
                    batch.StartFile(upload.RemotePath, upload.Size);
                    Progress<long> perFile = new Progress<long>(value => batch.ReportFileProgress(value));
                    await SaveHelpers.UploadFileAsync(ip, port, user, pass, timeout, upload.LocalPath, upload.RemotePath, perFile, settings.Overwrite);
                    batch.CompleteFile();
                }
            });

        OperationFeedback.WriteSuccess(
            "Save inject complete",
            $"[green]{plan.Count}[/] file(s)  [silver]{FtpHelpers.FormatBytes(plan.Sum(item => item.Size))}[/] -> [white]{Markup.Escape(titleRoot)}[/]");
        if (skipped > 0)
            OperationFeedback.WriteWarning("Save inject skipped existing files", skipped.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}

internal static class SaveCommandOutput {
    public static int WriteValidationFailure(bool json, string operation, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                $"{operation} failed",
                message,
                code,
                new[] { "Correct the command arguments and retry." }));
            return 1;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        return 1;
    }
}

internal static class SaveHelpers {
    private static readonly HashSet<string> SupportedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "Hdd1",
        "HddX",
        "Usb0",
        "Usb1",
        "Usb2",
        "UsbMu",
        "Mu",
        "IntMu",
        "MmcMu"
    };

    internal sealed record SaveFileRecord(
        string Device,
        string ProfileId,
        string RemotePath,
        string RelativePath,
        long Size,
        DateTime Modified);

    internal sealed record SaveUploadRecord(
        string LocalPath,
        string RemotePath,
        long Size);

    public static async Task<List<SaveFileRecord>> EnumerateSaveFilesAsync(string ip, int port, string user, string pass, int timeoutMs, uint titleId, string? profileId, string? devices) {
        string titleText = titleId.ToString("X8", CultureInfo.InvariantCulture);
        HashSet<string> filterProfiles = string.IsNullOrWhiteSpace(profileId)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(new[] { profileId }, StringComparer.OrdinalIgnoreCase);
        HashSet<string> requestedRoots = ParseRootFilter(devices);

        List<SaveFileRecord> files = new List<SaveFileRecord>();
        foreach (string root in await GetAvailableRootsAsync(ip, port, user, pass, timeoutMs, requestedRoots)) {
            string contentRoot = $"/{root}/Content";
            if (filterProfiles.Count > 0) {
                foreach (string filteredProfile in filterProfiles) {
                    string titleRoot = $"{contentRoot}/{filteredProfile}/{titleText}";
                    await CollectTitleFilesAsync(ip, port, user, pass, timeoutMs, root, filteredProfile, titleRoot, files);
                }
                continue;
            }

            (FtpListItem[] profiles, bool rootListing)? profileListing = await TryGetListingAsync(ip, port, user, pass, timeoutMs, contentRoot);
            if (profileListing == null || profileListing.Value.rootListing)
                continue;

            foreach (FtpListItem profile in profileListing.Value.profiles) {
                if (profile.Type != FtpObjectType.Directory || !IsProfileId(profile.Name))
                    continue;
                string titleRoot = $"{contentRoot}/{profile.Name}/{titleText}";
                await CollectTitleFilesAsync(ip, port, user, pass, timeoutMs, root, profile.Name, titleRoot, files);
            }
        }

        return files;
    }

    public static async Task DownloadFileAsync(string ip, int port, string user, string pass, int timeoutMs, string remotePath, string localPath, IProgress<long>? progress = null) {
        await WithFreshClientAsync(ip, port, user, pass, timeoutMs, async client => {
            Progress<FtpProgress>? ftpProgress = progress == null
                ? null
                : new Progress<FtpProgress>(p => {
                    if (p.TransferredBytes >= 0)
                        progress.Report(p.TransferredBytes);
                });
            await client.DownloadFile(localPath, remotePath, FtpLocalExists.Overwrite, FtpVerify.None, ftpProgress);
        });
    }

    public static async Task UploadFileAsync(string ip, int port, string user, string pass, int timeoutMs, string localPath, string remotePath, IProgress<long>? progress = null, bool overwriteApproved = false) {
        Progress<FtpProgress>? ftpProgress = progress == null
            ? null
            : new Progress<FtpProgress>(p => {
                if (p.TransferredBytes >= 0)
                    progress.Report(p.TransferredBytes);
            });
        await FtpHelpers.UploadFileVerifiedAsync(
            ip,
            port,
            user,
            pass,
            timeoutMs,
            localPath,
            remotePath,
            ensureRemoteDirectory: true,
            progress: ftpProgress,
            cancellationToken: CancellationToken.None,
            overwriteApproved: overwriteApproved);
    }

    public static async Task<bool> RemoteFileExistsAsync(string ip, int port, string user, string pass, int timeoutMs, string remotePath) {
        try {
            return await WithFreshClientAsync(ip, port, user, pass, timeoutMs, client => client.FileExists(remotePath));
        }
        catch {
            return false;
        }
    }

    public static string FormatTitleLabel(uint titleId) {
        if (TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? entry) && entry != null)
            return $"{entry.Name} (0x{titleId:X8})";
        return $"0x{titleId:X8}";
    }

    public static string BuildOutputFolderName(uint titleId) {
        string titleLabel = FormatTitleLabel(titleId);
        foreach (char invalid in Path.GetInvalidFileNameChars())
            titleLabel = titleLabel.Replace(invalid, '_');
        return titleLabel;
    }

    public static bool TryParseTitleId(string? text, out uint titleId) {
        titleId = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string cleaned = text.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(2);
        return uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out titleId);
    }

    public static bool IsProfileId(string name) {
        if (name.Length != 16)
            return false;
        return name.All(ch => Uri.IsHexDigit(ch));
    }

    public static string BuildTitleRoot(string device, string profileId, uint titleId) {
        string cleanedDevice = device.Trim().Trim(':', '\\', '/');
        return $"/{cleanedDevice}/Content/{profileId}/{titleId:X8}";
    }

    public static List<SaveUploadRecord> BuildUploadPlanFromDirectory(string localRoot, string remoteRoot) {
        List<SaveUploadRecord> uploads = new List<SaveUploadRecord>();
        string rootName = Path.GetFileName(localRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (string file in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)) {
            string relative = Path.GetRelativePath(localRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            string remotePath = NormalizeRemotePath($"{remoteRoot}/{rootName}/{relative}");
            uploads.Add(new SaveUploadRecord(file, remotePath, new FileInfo(file).Length));
        }
        return uploads;
    }

    public static List<SaveUploadRecord> BuildUploadPlanFromFile(string localPath, string remoteRoot, string? relativeRemotePath) {
        string relative = string.IsNullOrWhiteSpace(relativeRemotePath)
            ? Path.GetFileName(localPath)
            : ValidateRelativeRemotePath(relativeRemotePath);
        string remotePath = NormalizeRemotePath($"{remoteRoot}/{relative}");
        return new List<SaveUploadRecord> {
            new SaveUploadRecord(localPath, remotePath, new FileInfo(localPath).Length)
        };
    }

    private static string ValidateRelativeRemotePath(string relativeRemotePath) {
        string relative = relativeRemotePath.Trim().Replace('\\', '/');
        if (!IsValidRelativeRemotePath(relative))
            throw new ArgumentException("--remote-path must be a relative path without traversal segments.", nameof(relativeRemotePath));

        return relative;
    }

    private static HashSet<string> ParseRootFilter(string? devices) {
        if (string.IsNullOrWhiteSpace(devices))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            devices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(root => SupportedRoots.Contains(root)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<List<string>> GetAvailableRootsAsync(string ip, int port, string user, string pass, int timeoutMs, HashSet<string> requestedRoots) {
        List<string> roots = new List<string>();
        (FtpListItem[] items, bool _)? listing = await TryGetListingAsync(ip, port, user, pass, timeoutMs, "/");
        if (listing != null) {
            foreach (FtpListItem item in listing.Value.items.Where(i => i.Type == FtpObjectType.Directory)) {
                if (!SupportedRoots.Contains(item.Name))
                    continue;
                if (requestedRoots.Count > 0 && !requestedRoots.Contains(item.Name))
                    continue;
                roots.Add(item.Name);
            }
        }

        if (roots.Count > 0)
            return roots;

        if (requestedRoots.Count > 0)
            return requestedRoots.ToList();

        return SupportedRoots.ToList();
    }

    private static async Task<(FtpListItem[] items, bool rootListing)?> TryGetListingAsync(string ip, int port, string user, string pass, int timeoutMs, string path) {
        try {
            return await WithFreshClientAsync(ip, port, user, pass, timeoutMs, client => FtpHelpers.GetListingWithFallbackAsync(client, path));
        }
        catch {
            return null;
        }
    }

    private static async Task CollectTitleFilesAsync(string ip, int port, string user, string pass, int timeoutMs, string root, string profileId, string titleRoot, List<SaveFileRecord> files) {
        Queue<(string Path, string Relative)> pending = new Queue<(string, string)>();
        pending.Enqueue((titleRoot, string.Empty));
        while (pending.Count > 0) {
            (string current, string relative) = pending.Dequeue();
            (FtpListItem[] items, bool listingRoot)? listing = await TryGetListingAsync(ip, port, user, pass, timeoutMs, current);
            if (listing == null)
                continue;
            if (listing.Value.listingRoot && !string.Equals(current, titleRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (FtpListItem item in listing.Value.items) {
                if (item.Name is "." or "..")
                    continue;
                string nextRelative = string.IsNullOrWhiteSpace(relative) ? item.Name : $"{relative}/{item.Name}";
                string remotePath = string.IsNullOrWhiteSpace(item.FullName)
                    ? $"{current.TrimEnd('/')}/{item.Name}"
                    : item.FullName;
                remotePath = NormalizeRemotePath(remotePath);

                if (item.Type == FtpObjectType.Directory) {
                    pending.Enqueue((remotePath, nextRelative));
                    continue;
                }

                files.Add(new SaveFileRecord(root, profileId, remotePath, nextRelative, item.Size, item.Modified));
            }
        }
    }

    private static async Task<T> WithFreshClientAsync<T>(string ip, int port, string user, string pass, int timeoutMs, Func<AsyncFtpClient, Task<T>> action) {
        await using AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeoutMs);
        await client.Connect();
        return await action(client);
    }

    private static async Task WithFreshClientAsync(string ip, int port, string user, string pass, int timeoutMs, Func<AsyncFtpClient, Task> action) {
        await using AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeoutMs);
        await client.Connect();
        await action(client);
    }

    private static string NormalizeRemotePath(string path) {
        string normalized = path.Replace('\\', '/');
        while (normalized.Contains("/./", StringComparison.Ordinal))
            normalized = normalized.Replace("/./", "/", StringComparison.Ordinal);
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        return normalized.TrimEnd('/');
    }

    private static bool IsValidRelativeRemotePath(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (path.StartsWith("/", StringComparison.Ordinal))
            return false;

        foreach (string segment in path.Split('/')) {
            if (segment.Length == 0 || segment == "." || segment == "..")
                return false;
        }

        return true;
    }

}

