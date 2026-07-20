namespace Xbox360.Remote.Cli;

internal static class CliHttpUserAgent {
    public static string Value { get; } = BuildValue();

    public static void Apply(HttpClient client) {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Value);
    }

    private static string BuildValue() {
        Version? version = typeof(CliHttpUserAgent).Assembly.GetName().Version;
        return $"XeCLI/{version?.ToString(3) ?? "unknown"}";
    }
}
