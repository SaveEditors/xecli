using System.Text.Json;

namespace Xbox360.Remote.Cli;

internal sealed class CliConfig {
    public sealed class PendingModuleOperationInfo {
        public string? Action { get; set; }
        public string? ModuleName { get; set; }
        public string? ModulePath { get; set; }
        public bool SystemThread { get; set; }
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
    public string? AvatarLibraryRoot { get; set; }
    public string? AvatarCachePath { get; set; }
    public string? AvatarManifestUrl { get; set; }
    public string? AvatarTitleMapUrl { get; set; }
    public string? AvatarContentBaseUrl { get; set; }
    public string? AvatarDownloadCachePath { get; set; }
    public bool PathPromptHandled { get; set; }
    public PendingModuleOperationInfo? PendingModuleOperation { get; set; }
    public LedStateInfo? LastLedState { get; set; }
    public FanStateInfo? LastFanState { get; set; }

    public static CliConfig Load() {
        string path = CliPaths.ConfigPath;
        if (!File.Exists(path))
            return new CliConfig { NotifyIcons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) };

        string json = File.ReadAllText(path);
        CliConfig cfg = JsonSerializer.Deserialize<CliConfig>(json) ?? new CliConfig();
        cfg.NotifyIcons = new Dictionary<string, int>(cfg.NotifyIcons ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        cfg.ModuleHandles = new Dictionary<string, string>(cfg.ModuleHandles ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        return cfg;
    }

    public void Save() {
        Directory.CreateDirectory(Path.GetDirectoryName(CliPaths.ConfigPath)!);
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CliPaths.ConfigPath, json);
    }
}
