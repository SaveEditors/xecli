using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Logging;

internal sealed record CommandLogEntry {
    public DateTimeOffset TimestampUtc { get; init; }
    public string? CommandName { get; init; }
    public string? CommandLine { get; init; }
    public bool Succeeded { get; init; }
    public int? ExitCode { get; init; }
    public long DurationMs { get; init; }
    public string? Error { get; init; }
}

internal sealed record CommandLogInvocation {
    public required DateTimeOffset StartedUtc { get; init; }
    public required string CommandName { get; init; }
    public required string CommandLine { get; init; }
}

internal sealed record CommandLogHealthSnapshot(
    bool Present,
    bool Readable,
    int Entries,
    int MalformedLines,
    DateTimeOffset? NewestEntryUtc);

internal static class CommandLogPaths {
    public static string LogDirectory {
        get {
            if (CliConfig.TryLoad(out CliConfig config) && !string.IsNullOrWhiteSpace(config.CommandLogDirectory)) {
                try {
                    return Path.GetFullPath(config.CommandLogDirectory);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
                }
            }
            return Path.Combine(CliPaths.CachePath, "logging");
        }
    }

    public static string LogPath => Path.Combine(LogDirectory, "command-log.jsonl");
}

internal static class CommandLogRedactor {
    private static readonly Regex Ipv4Regex = new(@"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Ipv6CandidateRegex = new(@"(?<![0-9A-Fa-f:])(?<value>\[(?<bracketed>[0-9A-Fa-f:.%]+)\](?::\d+)?|(?<bare>(?:[0-9A-Fa-f]{0,4}:){2,}[0-9A-Fa-f:.%]*[0-9A-Fa-f]))(?![0-9A-Fa-f:])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ConsoleIdRegex = new(@"\b[0-9a-fA-F]{16,32}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsPathRegex = new(@"(?i)\b[a-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]+\\?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsUncPathRegex = new(@"(?i)(?<!\w)\\\\[^\\/:*?""<>|\r\n]+\\[^\\/:*?""<>|\r\n]+(?:\\[^\\/:*?""<>|\r\n]+)*\\?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsDriveRootRegex = new(@"(?i)(?<!\w)[a-z]:\\?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnixPathRegex = new(@"(?<![\w:/])(?:/[^/\s]+)+/?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsBareHomePathRegex = new(@"(?i)^[a-z]:\\(?:users|documents and settings)\\[^\\/:*?""<>|\r\n]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnixBareHomePathRegex = new(@"^/(?:home|Users)/[^/\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UrlCredentialRegex = new(@"(?<scheme>\b[a-z][a-z0-9+.-]*://)(?<user>[^/\s:@]+):(?<password>[^/\s@]+)@", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SecretAssignmentRegex = new(@"(?<name>\b(?:password|pass|token|key|secret|cpu-key|kv)\b)(?<separator>\s*(?:=|:)\s*)(?:(?<quote>"")(?<quoted>(?:\\.|[^""\\])*)""|(?<quote>')(?<quoted>(?:\\.|[^'\\])*)'|(?<value>[^\s,;&)\]}]+))", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> SecretOptions = new(StringComparer.OrdinalIgnoreCase) {
        "--pass",
        "--password",
        "--token",
        "--key",
        "--secret",
        "--cpu-key",
        "--kv"
    };

    public static string RedactCommandLine(IEnumerable<string> arguments) {
        List<string> redacted = [];
        string? pendingSecretOption = null;

        foreach (string? argument in arguments) {
            if (string.IsNullOrWhiteSpace(argument))
                continue;

            string token = argument.Trim();
            if (pendingSecretOption != null) {
                if (TryRedactSecretOption(token, out string nestedSecretToken, out bool nestedConsumesFollowingValue)) {
                    redacted.Add(pendingSecretOption);
                    redacted.Add(nestedSecretToken);
                    pendingSecretOption = nestedConsumesFollowingValue ? token : null;
                    continue;
                }

                redacted.Add("[redacted]");
                pendingSecretOption = null;
                continue;
            }

            if (TryRedactSecretOption(token, out string secretToken, out bool consumesFollowingValue)) {
                redacted.Add(secretToken);
                if (consumesFollowingValue)
                    pendingSecretOption = token;

                continue;
            }

            redacted.Add(RedactToken(token));
        }

        if (pendingSecretOption != null)
            redacted.Add(pendingSecretOption);

        return string.Join(" ", redacted);
    }

    public static string RedactToken(string? token) {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        string redacted = RedactFreeText(token.Trim());
        if (redacted.Length == 0)
            return string.Empty;

        if (LooksLikePath(token) && redacted.Equals(token.Trim(), StringComparison.Ordinal)) {
            return RedactPathValue(token);
        }

        return redacted;
    }

    public static string RedactFreeText(string? text) {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string redacted = SecretAssignmentRegex.Replace(text, RedactSecretAssignment);
        redacted = UrlCredentialRegex.Replace(redacted, match => match.Groups["scheme"].Value + match.Groups["user"].Value + ":[redacted]@");
        redacted = Ipv6CandidateRegex.Replace(redacted, RedactIpv6Candidate);
        redacted = Ipv4Regex.Replace(redacted, "redacted");
        redacted = ConsoleIdRegex.Replace(redacted, "redacted");
        redacted = WindowsUncPathRegex.Replace(redacted, match => RedactPathValue(match.Value));
        redacted = WindowsPathRegex.Replace(redacted, match => RedactPathValue(match.Value));
        redacted = WindowsDriveRootRegex.Replace(redacted, match => RedactPathValue(match.Value));
        redacted = UnixPathRegex.Replace(redacted, match => RedactPathValue(match.Value));
        return redacted;
    }

    private static string RedactSecretAssignment(Match match) {
        string quote = match.Groups["quote"].Success ? match.Groups["quote"].Value : string.Empty;
        return match.Groups["name"].Value + match.Groups["separator"].Value + quote + "[redacted]" + quote;
    }

    private static string RedactIpv6Candidate(Match match) {
        string candidate = match.Groups["bracketed"].Success
            ? match.Groups["bracketed"].Value
            : match.Groups["bare"].Value;

        return IPAddress.TryParse(candidate, out IPAddress? address) &&
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? "redacted"
            : match.Value;
    }

    private static bool LooksLikePath(string value) {
        return Path.IsPathRooted(value) || value.Contains('\\') || value.Contains('/');
    }

    private static bool TryRedactSecretOption(string token, out string redactedToken, out bool consumesFollowingValue) {
        consumesFollowingValue = false;
        redactedToken = string.Empty;

        int equalsIndex = token.IndexOf('=');
        if (equalsIndex > 0) {
            string optionName = token[..equalsIndex];
            if (SecretOptions.Contains(optionName)) {
                redactedToken = optionName + "=[redacted]";
                return true;
            }
        }

        if (SecretOptions.Contains(token)) {
            redactedToken = token;
            consumesFollowingValue = true;
            return true;
        }

        return false;
    }

    private static string RedactPathValue(string value) {
        string trimmed = value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (LooksLikeBareHomeDirectoryPath(trimmed) || IsBareRootPath(trimmed))
            return "[local-path]";

        string leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? "[local-path]" : leaf;
    }

    private static bool IsBareRootPath(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
            return false;

        string root = Path.GetPathRoot(trimmed) ?? string.Empty;
        if (root.Length == 0)
            return false;

        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeBareHomeDirectoryPath(string value) {
        return WindowsBareHomePathRegex.IsMatch(value) || UnixBareHomePathRegex.IsMatch(value);
    }
}

internal sealed class CommandLogInterceptor : ICommandInterceptor {
    private static readonly AsyncLocal<CommandLogInvocation?> Current = new();
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = false
    };

    public void Intercept(CommandContext context, CommandSettings settings) {
        string commandName = string.IsNullOrWhiteSpace(context.Name) ? "unknown" : context.Name;
        string commandLine = CommandLogRedactor.RedactCommandLine(context.Arguments);
        Current.Value = new CommandLogInvocation {
            StartedUtc = DateTimeOffset.UtcNow,
            CommandName = commandName,
            CommandLine = commandLine
        };
    }

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result) {
        CommandLogInvocation? invocation = Current.Value;
        if (invocation == null)
            return;

        try {
            Append(new CommandLogEntry {
                TimestampUtc = invocation.StartedUtc,
                CommandName = invocation.CommandName,
                CommandLine = invocation.CommandLine,
                Succeeded = result == 0,
                ExitCode = result,
                DurationMs = Math.Max(0, (long)(DateTimeOffset.UtcNow - invocation.StartedUtc).TotalMilliseconds)
            });
        }
        finally {
            Current.Value = null;
        }
    }

    public static void RecordFailure(Exception exception) {
        CommandLogInvocation? invocation = Current.Value;
        if (invocation == null)
            return;

        try {
            Append(new CommandLogEntry {
                TimestampUtc = invocation.StartedUtc,
                CommandName = invocation.CommandName,
                CommandLine = invocation.CommandLine,
                Succeeded = false,
                ExitCode = null,
                DurationMs = Math.Max(0, (long)(DateTimeOffset.UtcNow - invocation.StartedUtc).TotalMilliseconds),
                Error = CommandLogRedactor.RedactFreeText($"{exception.GetType().Name}: {exception.Message}")
            });
        }
        finally {
            Current.Value = null;
        }
    }

    private static void Append(CommandLogEntry entry) {
        try {
            string json = JsonSerializer.Serialize(entry, JsonOptions);
            using Mutex mutex = new(false, GetMutexName());
            bool acquired = false;
            try {
                try {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
                }
                catch (AbandonedMutexException) {
                    acquired = true;
                }

                if (!acquired)
                    return;

                Directory.CreateDirectory(CommandLogPaths.LogDirectory);
                using FileStream stream = new(
                    CommandLogPaths.LogPath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.Read);
                stream.Seek(0, SeekOrigin.End);
                using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
                writer.WriteLine(json);
                writer.Flush();
                stream.Flush(true);
            }
            finally {
                if (acquired)
                    mutex.ReleaseMutex();
            }
        }
        catch {
            // Logging must never break command execution.
        }
    }

    private static string GetMutexName() {
        string path = Path.GetFullPath(CommandLogPaths.LogPath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return "XeCLI.CommandLog." + Convert.ToHexString(hash);
    }
}

internal static class CommandLogStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public static CommandLogHealthSnapshot GetHealthSnapshot(Func<string, IEnumerable<string>>? readLines = null) {
        string path = CommandLogPaths.LogPath;
        if (!File.Exists(path))
            return new CommandLogHealthSnapshot(false, true, 0, 0, null);

        Func<string, IEnumerable<string>> lineReader = readLines ?? (static filePath => File.ReadLines(filePath, Encoding.UTF8));
        int entries = 0;
        int malformed = 0;
        DateTimeOffset? newest = null;

        try {
            foreach (string line in lineReader(path)) {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try {
                    CommandLogEntry? entry = JsonSerializer.Deserialize<CommandLogEntry>(line, JsonOptions);
                    if (entry == null) {
                        malformed++;
                        continue;
                    }

                    entries++;
                    if (newest == null || entry.TimestampUtc > newest)
                        newest = entry.TimestampUtc;
                }
                catch {
                    malformed++;
                }
            }

            return new CommandLogHealthSnapshot(true, true, entries, malformed, newest);
        }
        catch {
            return new CommandLogHealthSnapshot(true, false, entries, malformed, newest);
        }
    }

    public static IReadOnlyList<CommandLogEntry> ReadAll() {
        string path = CommandLogPaths.LogPath;
        if (!File.Exists(path))
            return [];

        List<CommandLogEntry> entries = [];
        try {
            foreach (string line in File.ReadLines(path, Encoding.UTF8)) {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try {
                    CommandLogEntry? entry = JsonSerializer.Deserialize<CommandLogEntry>(line, JsonOptions);
                    if (entry != null)
                        entries.Add(entry);
                }
                catch {
                    // Skip malformed lines and continue reading the rest of the append-only log.
                }
            }
        }
        catch {
            return entries;
        }

        return entries;
    }
}
