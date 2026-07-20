using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class MemorySessionStoreData {
    public int SchemaVersion { get; set; } = MemorySessionStore.CurrentSchemaVersion;

    public List<MemorySessionRecord> Sessions { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}

internal sealed record MemorySessionRecord {
    public string Name { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public List<MemorySessionCaptureRecord> Captures { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}

internal sealed record MemorySessionCaptureRecord {
    public string Kind { get; set; } = "capture";

    public string Label { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}

internal sealed record MemorySessionLoadResult(MemorySessionStoreData Data, string? Issue);

internal sealed record MemorySessionCaptureSpec(string Kind, string Label, string Path);

internal sealed record MemorySessionPathResolution(string DisplayPath, string FullPath);

internal static class MemorySessionStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public const int CurrentSchemaVersion = 1;

    public static string StorePath => Path.Combine(CliPaths.ConfigDirectory, "memory-sessions.json");

    public static MemorySessionLoadResult LoadRaw() {
        if (!File.Exists(StorePath))
            return new MemorySessionLoadResult(new MemorySessionStoreData(), null);

        try {
            MemorySessionStoreData? data = JsonSerializer.Deserialize<MemorySessionStoreData>(File.ReadAllText(StorePath), JsonOptions);
            return new MemorySessionLoadResult(Normalize(data ?? new MemorySessionStoreData()), null);
        }
        catch (JsonException ex) {
            return new MemorySessionLoadResult(
                new MemorySessionStoreData(),
                $"Stored memory-sessions.json is malformed: {ex.Message}");
        }
        catch (IOException) {
            return new MemorySessionLoadResult(
                new MemorySessionStoreData(),
                "Stored memory sessions could not be read.");
        }
        catch (UnauthorizedAccessException) {
            return new MemorySessionLoadResult(
                new MemorySessionStoreData(),
                "Stored memory sessions could not be read.");
        }
    }

    public static MemorySessionStoreData Load() {
        return LoadRaw().Data;
    }

    public static void Save(MemorySessionStoreData data) {
        MemorySessionStoreData normalized = Normalize(data);
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        AtomicFileWriter.WriteAllText(StorePath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public static bool TryValidateName(string? name, out string normalizedName, out string error) {
        normalizedName = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(name)) {
            error = "Session name is required.";
            return false;
        }

        string candidate = name.Trim();
        if (candidate.Any(char.IsControl)) {
            error = "Session name cannot contain control characters.";
            return false;
        }

        normalizedName = candidate;
        return true;
    }

    public static bool TryAddSession(MemorySessionStoreData data, MemorySessionRecord session, out string error) {
        error = string.Empty;

        MemorySessionStoreData normalized = Normalize(data);
        if (normalized.Sessions.Any(existing =>
            string.Equals(existing.Name, session.Name, StringComparison.OrdinalIgnoreCase))) {
            error = "Session name already exists.";
            return false;
        }

        normalized.Sessions.Add(session);
        data.SchemaVersion = normalized.SchemaVersion;
        data.Sessions = normalized.Sessions;
        data.ExtraData = normalized.ExtraData;
        return true;
    }

    public static bool TryRemoveSession(MemorySessionStoreData data, string name, out MemorySessionRecord? removedSession, out string error) {
        removedSession = null;
        error = string.Empty;

        MemorySessionStoreData normalized = Normalize(data);
        if (!TryValidateName(name, out string trimmed, out error))
            return false;

        int index = normalized.Sessions.FindIndex(session =>
            string.Equals(session.Name, trimmed, StringComparison.Ordinal));
        if (index < 0) {
            error = "Session not found.";
            return false;
        }

        removedSession = normalized.Sessions[index];
        normalized.Sessions.RemoveAt(index);
        data.SchemaVersion = normalized.SchemaVersion;
        data.Sessions = normalized.Sessions;
        data.ExtraData = normalized.ExtraData;
        return true;
    }

    public static bool TryFindSession(MemorySessionStoreData data, string name, out MemorySessionRecord? session, out string error) {
        session = null;
        error = string.Empty;

        MemorySessionStoreData normalized = Normalize(data);
        if (!TryValidateName(name, out string trimmed, out error))
            return false;

        session = normalized.Sessions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, trimmed, StringComparison.Ordinal));
        if (session == null) {
            error = "Session not found.";
            return false;
        }

        return true;
    }

    public static bool TryResolveCaptureReference(string? reference, out MemorySessionPathResolution resolution, out string error) {
        resolution = new MemorySessionPathResolution(string.Empty, string.Empty);
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(reference)) {
            error = "File path is required.";
            return false;
        }

        string trimmed = reference.Trim();
        if (!trimmed.StartsWith("@", StringComparison.Ordinal)) {
            try {
                string resolvedPath = Path.GetFullPath(trimmed);
                resolution = new MemorySessionPathResolution(XexInfoCommand.GetDisplayFileName(trimmed) ?? trimmed, resolvedPath);
                return true;
            }
            catch {
                error = "File path is invalid.";
                return false;
            }
        }

        string spec = trimmed[1..];
        int separator = spec.IndexOf(':');
        if (separator <= 0 || separator >= spec.Length - 1) {
            error = "Use @<session>:<label> to reference a stored capture.";
            return false;
        }

        string sessionName = spec[..separator].Trim();
        string label = spec[(separator + 1)..].Trim();
        if (!TryValidateName(sessionName, out _, out error))
            return false;
        if (!TryValidateName(label, out _, out error))
            return false;

        MemorySessionLoadResult load = LoadRaw();
        if (!string.IsNullOrWhiteSpace(load.Issue)) {
            error = load.Issue!;
            return false;
        }

        MemorySessionRecord? session = load.Data.Sessions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, sessionName, StringComparison.OrdinalIgnoreCase));
        if (session == null) {
            error = $"Session not found: {sessionName}";
            return false;
        }

        MemorySessionCaptureRecord? capture = session.Captures.FirstOrDefault(candidate =>
            string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase));
        if (capture == null) {
            error = $"Session capture not found: @{sessionName}:{label}";
            return false;
        }

        string captureFullPath;
        try {
            captureFullPath = Path.GetFullPath(capture.Path);
        }
        catch {
            error = $"Stored capture path is invalid for @{sessionName}:{label}.";
            return false;
        }

        if (!File.Exists(captureFullPath)) {
            error = $"Stored capture path not found for @{sessionName}:{label}.";
            return false;
        }

        resolution = new MemorySessionPathResolution($"@{sessionName}:{label}", captureFullPath);
        return true;
    }

    public static MemorySessionRecord CreateSession(string name, string? note, IEnumerable<MemorySessionCaptureSpec> captures, DateTimeOffset timestampUtc) {
        MemorySessionRecord session = new() {
            Name = name,
            Note = string.IsNullOrWhiteSpace(note) ? null : note,
            CreatedUtc = timestampUtc,
            UpdatedUtc = timestampUtc,
            Captures = captures.Select(capture => new MemorySessionCaptureRecord {
                Kind = NormalizeKind(capture.Kind),
                Label = capture.Label,
                Path = capture.Path,
                CreatedUtc = timestampUtc
            }).ToList()
        };

        return session;
    }

    public static bool TryParseCaptureSpec(string? value, out MemorySessionCaptureSpec capture, out string error) {
        capture = new MemorySessionCaptureSpec(string.Empty, string.Empty, string.Empty);
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value)) {
            error = "Capture spec is required.";
            return false;
        }

        string trimmed = value.Trim();
        int equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex <= 0 || equalsIndex >= trimmed.Length - 1) {
            error = "Use <label>=<path> or <kind>:<label>=<path> for each capture.";
            return false;
        }

        string left = trimmed[..equalsIndex].Trim();
        string pathPart = trimmed[(equalsIndex + 1)..].Trim();
        string kind = "capture";
        string label = left;
        int kindIndex = left.IndexOf(':');
        if (kindIndex > 0 && kindIndex < left.Length - 1) {
            kind = left[..kindIndex];
            label = left[(kindIndex + 1)..];
        }

        if (!TryValidateName(label, out string normalizedLabel, out error))
            return false;

        if (string.IsNullOrWhiteSpace(kind))
            kind = "capture";

        string captureFullPath;
        try {
            captureFullPath = Path.GetFullPath(pathPart);
        }
        catch {
            error = "Capture path is invalid.";
            return false;
        }

        if (!File.Exists(captureFullPath)) {
            error = $"Capture file not found: {XexInfoCommand.GetDisplayFileName(pathPart)}";
            return false;
        }

        capture = new MemorySessionCaptureSpec(NormalizeKind(kind), normalizedLabel, captureFullPath);
        return true;
    }

    public static object ToJsonSession(MemorySessionRecord session) {
        return new {
            session.Name,
            session.Note,
            session.CreatedUtc,
            session.UpdatedUtc,
            Captures = session.Captures.Select(capture => new {
                capture.Kind,
                capture.Label,
                capture.Path,
                capture.CreatedUtc
            }).ToArray()
        };
    }

    private static MemorySessionStoreData Normalize(MemorySessionStoreData? data) {
        data ??= new MemorySessionStoreData();
        data.SchemaVersion = data.SchemaVersion <= 0 ? CurrentSchemaVersion : data.SchemaVersion;
        data.Sessions ??= [];

        Dictionary<string, MemorySessionRecord> sessions = new(StringComparer.OrdinalIgnoreCase);
        foreach (MemorySessionRecord? session in data.Sessions) {
            if (session == null)
                continue;

            if (!TryValidateName(session.Name, out string normalizedName, out _))
                continue;

            MemorySessionRecord normalized = session with {
                Name = normalizedName,
                Note = string.IsNullOrWhiteSpace(session.Note) ? null : session.Note.Trim(),
                CreatedUtc = session.CreatedUtc == default ? DateTimeOffset.MinValue : session.CreatedUtc,
                UpdatedUtc = session.UpdatedUtc == default ? DateTimeOffset.MinValue : session.UpdatedUtc,
                Captures = NormalizeCaptures(session.Captures)
            };

            if (!sessions.TryGetValue(normalizedName, out MemorySessionRecord? existing) || normalized.UpdatedUtc > existing.UpdatedUtc)
                sessions[normalizedName] = normalized;
        }

        data.Sessions = sessions.Values
            .OrderByDescending(session => session.UpdatedUtc)
            .ThenBy(session => session.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return data;
    }

    private static List<MemorySessionCaptureRecord> NormalizeCaptures(List<MemorySessionCaptureRecord>? captures) {
        captures ??= [];
        Dictionary<string, MemorySessionCaptureRecord> normalized = new(StringComparer.OrdinalIgnoreCase);

        foreach (MemorySessionCaptureRecord? capture in captures) {
            if (capture == null)
                continue;

            if (!TryValidateName(capture.Label, out string label, out _))
                continue;

            string kind = NormalizeKind(capture.Kind);
            string path = string.IsNullOrWhiteSpace(capture.Path) ? string.Empty : capture.Path.Trim();
            if (string.IsNullOrWhiteSpace(path))
                continue;

            MemorySessionCaptureRecord normalizedCapture = capture with {
                Kind = kind,
                Label = label,
                Path = path,
                CreatedUtc = capture.CreatedUtc == default ? DateTimeOffset.MinValue : capture.CreatedUtc
            };

            normalized[label] = normalizedCapture;
        }

        return normalized.Values
            .OrderBy(capture => capture.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeKind(string? kind) {
        string candidate = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(candidate) ? "capture" : candidate;
    }
}

public sealed class MemorySessionCreateCommand : Command<MemorySessionCreateCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--name <NAME>")]
        [LocalizedDescription("Session name.")]
        public string? Name { get; init; }

        [CommandOption("--note <TEXT>")]
        [LocalizedDescription("Optional note for the session.")]
        public string? Note { get; init; }

        [CommandOption("--capture <SPEC>")]
        [LocalizedDescription("Capture spec in the form <label>=<path> or <kind>:<label>=<path>. Repeat for each stored capture.")]
        public string[] Captures { get; init; } = Array.Empty<string>();

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!MemorySessionStore.TryValidateName(settings.Name, out string name, out string error))
            return WriteError(settings.Json, "Memory session validation failed", error, "MEM_SESSION_VALIDATION_FAILED");

        if (settings.Captures.Length == 0)
            return WriteError(settings.Json, "Memory session validation failed", "Provide at least one --capture <SPEC>.", "MEM_SESSION_VALIDATION_FAILED");

        List<MemorySessionCaptureSpec> captures = [];
        HashSet<string> labels = new(StringComparer.OrdinalIgnoreCase);
        foreach (string captureText in settings.Captures) {
            if (!MemorySessionStore.TryParseCaptureSpec(captureText, out MemorySessionCaptureSpec capture, out string captureError))
                return WriteError(settings.Json, "Memory session validation failed", captureError, "MEM_SESSION_VALIDATION_FAILED");

            if (!labels.Add(capture.Label))
                return WriteError(settings.Json, "Memory session validation failed", $"Capture label already exists: {capture.Label}", "MEM_SESSION_VALIDATION_FAILED");

            captures.Add(capture);
        }

        MemorySessionStoreData data = MemorySessionStore.Load();
        MemorySessionRecord session = MemorySessionStore.CreateSession(name, settings.Note, captures, DateTimeOffset.UtcNow);
        if (!MemorySessionStore.TryAddSession(data, session, out string addError))
            return WriteError(settings.Json, "Memory session validation failed", addError, "MEM_SESSION_ALREADY_EXISTS");

        MemorySessionStore.Save(data);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Session = MemorySessionStore.ToJsonSession(session),
                SessionCount = data.Sessions.Count
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Created memory session[/] {Markup.Escape(session.Name)} [grey]({session.Captures.Count} capture(s))[/]");
        return 0;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the session arguments and try again."
            }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        return 1;
    }
}

public sealed class MemorySessionListCommand : Command<MemorySessionListCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        MemorySessionLoadResult load = MemorySessionStore.LoadRaw();
        if (!string.IsNullOrWhiteSpace(load.Issue))
            return WriteError(settings.Json, "Memory session store failed", load.Issue!, "MEM_SESSION_STORE_MALFORMED");

        IReadOnlyList<MemorySessionRecord> sessions = load.Data.Sessions;
        if (settings.Json) {
            CliOutput.EmitJson(sessions.Select(session => new {
                session.Name,
                session.Note,
                session.CreatedUtc,
                session.UpdatedUtc,
                CaptureCount = session.Captures.Count
            }));
            return 0;
        }

        if (sessions.Count == 0) {
            AnsiConsole.MarkupLine("[grey]No memory sessions saved.[/]");
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Memory Sessions[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn("Name");
        table.AddColumn("Captures");
        table.AddColumn("Updated");
        table.AddColumn("Note");

        foreach (MemorySessionRecord session in sessions) {
            table.AddRow(
                $"[white]{Markup.Escape(session.Name)}[/]",
                $"[cyan]{session.Captures.Count.ToString(CultureInfo.InvariantCulture)}[/]",
                CliOutput.FormatTimestamp(session.UpdatedUtc.UtcDateTime),
                string.IsNullOrWhiteSpace(session.Note) ? "[grey]n/a[/]" : $"[white]{Markup.Escape(session.Note)}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Fix or remove the sessions file, then try again."
            }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        return 1;
    }
}

public sealed class MemorySessionShowCommand : Command<MemorySessionShowCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--name <NAME>")]
        [LocalizedDescription("Session name.")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        MemorySessionLoadResult load = MemorySessionStore.LoadRaw();
        if (!string.IsNullOrWhiteSpace(load.Issue))
            return WriteError(settings.Json, "Memory session store failed", load.Issue!, "MEM_SESSION_STORE_MALFORMED");

        if (!MemorySessionStore.TryValidateName(settings.Name, out string name, out string error))
            return WriteError(settings.Json, "Memory session validation failed", error, "MEM_SESSION_VALIDATION_FAILED");

        if (!MemorySessionStore.TryFindSession(load.Data, name, out MemorySessionRecord? session, out string findError))
            return WriteError(settings.Json, "Memory session show failed", findError, "MEM_SESSION_NOT_FOUND");

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Session = MemorySessionStore.ToJsonSession(session!)
            });
            return 0;
        }

        AnsiConsole.Write(new Rule($"[bold deepskyblue1]Memory Session[/] [white]{Markup.Escape(session!.Name)}[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn("Kind");
        table.AddColumn("Label");
        table.AddColumn("File");
        table.AddColumn("Path");

        foreach (MemorySessionCaptureRecord capture in session.Captures) {
            table.AddRow(
                $"[green]{Markup.Escape(capture.Kind)}[/]",
                $"[white]{Markup.Escape(capture.Label)}[/]",
                $"[cyan]{Markup.Escape(XexInfoCommand.GetDisplayFileName(capture.Path) ?? capture.Path)}[/]",
                $"[grey]{Markup.Escape(capture.Path)}[/]");
        }

        AnsiConsole.Write(table);
        if (!string.IsNullOrWhiteSpace(session.Note)) {
            AnsiConsole.MarkupLine($"[grey]Note:[/] {Markup.Escape(session.Note)}");
        }

        AnsiConsole.MarkupLine($"[grey]Use:[/] [cyan]rgh mem diff --left @{Markup.Escape(session.Name)}:<label> --right @{Markup.Escape(session.Name)}:<label>[/]");
        return 0;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the session name and try again."
            }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        return 1;
    }
}

public sealed class MemorySessionRemoveCommand : Command<MemorySessionRemoveCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--name <NAME>")]
        [LocalizedDescription("Session name.")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        MemorySessionLoadResult load = MemorySessionStore.LoadRaw();
        if (!string.IsNullOrWhiteSpace(load.Issue))
            return WriteError(settings.Json, "Memory session store failed", load.Issue!, "MEM_SESSION_STORE_MALFORMED");

        if (!MemorySessionStore.TryValidateName(settings.Name, out string name, out string error))
            return WriteError(settings.Json, "Memory session validation failed", error, "MEM_SESSION_VALIDATION_FAILED");

        if (!MemorySessionStore.TryRemoveSession(load.Data, name, out MemorySessionRecord? removed, out string removeError))
            return WriteError(settings.Json, "Memory session remove failed", removeError, "MEM_SESSION_NOT_FOUND");

        MemorySessionStore.Save(load.Data);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Removed = removed!.Name
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Removed memory session[/] {Markup.Escape(removed!.Name)}");
        return 0;
    }

    private static int WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the session name and try again."
            }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        return 1;
    }
}
