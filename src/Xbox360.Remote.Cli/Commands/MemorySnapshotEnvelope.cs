using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xbox360.Remote.Cli.Commands;

internal sealed record MemorySnapshotEnvelope<TPayload> {
    public int SchemaVersion { get; init; } = 1;

    public string Kind { get; init; } = string.Empty;

    public DateTimeOffset CapturedUtc { get; init; }

    public string? Target { get; init; }

    public string? Query { get; init; }

    public object? Summary { get; init; }

    public TPayload? Payload { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; init; }
}
