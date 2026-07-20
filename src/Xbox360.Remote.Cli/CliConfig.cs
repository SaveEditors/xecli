using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Xbox360.Remote.Cli;

internal sealed class CliConfig {
    public sealed class PendingModuleOperationInfo {
        public string? Action { get; set; }
        public string? ModuleName { get; set; }
        public string? ModulePath { get; set; }
        public bool? SystemThread { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
    }

    public sealed class LedStateInfo {
        public string? Preset { get; set; }
        public string? TopLeft { get; set; }
        public string? TopRight { get; set; }
        public string? BottomLeft { get; set; }
        public string? BottomRight { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }

    public sealed class FanStateInfo {
        public int SpeedPercent { get; set; }
        public string? Channel { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }

    public sealed class TerminalWindowPlacementInfo {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? WindowState { get; set; }
        public string? ScreenDeviceName { get; set; }
    }

    public string? DefaultIp { get; set; }
    public int? DefaultPort { get; set; }
    public int? DefaultFtpPort { get; set; }
    public string? DefaultFtpUser { get; set; }
    public string? DefaultFtpPassword { get; set; }
    public Dictionary<string, int>? NotifyIcons { get; set; }
    public Dictionary<string, string>? ModuleHandles { get; set; }
    public string? GhidraPath { get; set; }
    public string? GhidraJavaPath { get; set; }
    public string? GhidraProjectsPath { get; set; }
    public string? IdaPath { get; set; }
    public string? IdaPythonPath { get; set; }
    public string? IdaUserPath { get; set; }
    public string? IdaPreferredBackend { get; set; }
    public string? XtafCliPath { get; set; }
    public string? AvatarLibraryRoot { get; set; }
    public string? AvatarCachePath { get; set; }
    public string? AvatarManifestUrl { get; set; }
    public string? AvatarTitleMapUrl { get; set; }
    public string? AvatarContentBaseUrl { get; set; }
    public string? AvatarDownloadCachePath { get; set; }
    public string? UiLanguage { get; set; }
    public string? TerminalTheme { get; set; }
    public bool? TerminalAutoConnect { get; set; }
    public bool? TerminalTelemetryEnabled { get; set; }
    public bool? DiscordRichPresenceEnabled { get; set; }
    public bool? RememberTerminalWindowPlacement { get; set; }
    public int? ConnectionTimeoutMs { get; set; }
    public bool? AutoReconnectEnabled { get; set; }
    public int? ReconnectAttempts { get; set; }
    public int? ReconnectDelaySeconds { get; set; }
    public string? DefaultLocalDirectory { get; set; }
    public string? ScreenshotDirectory { get; set; }
    public string? ScreenshotAfterCapture { get; set; }
    public string? CommandLogDirectory { get; set; }
    public string? TitleDatabasePath { get; set; }
    public string? FtpConflictBehavior { get; set; }
    public string? DiscordClientId { get; set; }
    public TerminalWindowPlacementInfo? TerminalWindowPlacement { get; set; }
    public bool PathPromptHandled { get; set; }
    public PendingModuleOperationInfo? PendingModuleOperation { get; set; }
    public LedStateInfo? LastLedState { get; set; }
    public FanStateInfo? LastFanState { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }

    public static CliConfig Load() {
        string path = CliPaths.ConfigPath;
        if (!File.Exists(path))
            return CreateDefault();

        string json = File.ReadAllText(path);
        CliConfig cfg = JsonSerializer.Deserialize<CliConfig>(json) ?? new CliConfig();
        NormalizeCollections(cfg);
        return cfg;
    }

    internal static bool TryLoad(out CliConfig config) {
        try {
            config = Load();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
            config = CreateDefault();
            return false;
        }
    }

    public void Save() {
        Directory.CreateDirectory(Path.GetDirectoryName(CliPaths.ConfigPath)!);
        JsonSerializerOptions options = new() { WriteIndented = true };
        string json = JsonSerializer.Serialize(this, options);
        VerifyRoundTrip(json, options);
        AtomicFileWriter.WriteAllText(CliPaths.ConfigPath, json);
    }

    private static CliConfig CreateDefault() {
        CliConfig config = new CliConfig();
        NormalizeCollections(config);
        return config;
    }

    private static void NormalizeCollections(CliConfig config) {
        config.NotifyIcons = new Dictionary<string, int>(config.NotifyIcons ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        config.ModuleHandles = new Dictionary<string, string>(config.ModuleHandles ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
    }

    private static void VerifyRoundTrip(string json, JsonSerializerOptions options) {
        CliConfig? roundTripped = JsonSerializer.Deserialize<CliConfig>(json, options);
        if (roundTripped == null)
            throw new JsonException("Serialized CLI config could not be round-tripped.");

        string roundTrippedJson = JsonSerializer.Serialize(roundTripped, options);
        JsonNode? originalNode = JsonNode.Parse(json);
        JsonNode? roundTrippedNode = JsonNode.Parse(roundTrippedJson);
        if (originalNode == null || roundTrippedNode == null || !JsonNode.DeepEquals(originalNode, roundTrippedNode))
            throw new JsonException("Serialized CLI config did not round-trip structurally.");
    }
}
