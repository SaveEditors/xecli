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
    public bool PathPromptHandled { get; set; }
    public PendingModuleOperationInfo? PendingModuleOperation { get; set; }

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
