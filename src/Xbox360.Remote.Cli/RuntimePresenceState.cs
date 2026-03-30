using System;

namespace Xbox360.Remote.Cli;

internal sealed class RuntimePresenceSnapshot
{
    public bool Connected { get; init; }

    public string? DebugName { get; init; }

    public string? ExecutionState { get; init; }

    public string? Gamertag { get; init; }

    public string? SignInStateText { get; init; }

    public uint? TitleId { get; init; }

    public string? TitleName { get; init; }

    public string? RunningXex { get; init; }

    public string? Ip { get; init; }

    public int? Port { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

internal static class RuntimePresenceState
{
    private static readonly object SyncRoot = new();

    private static RuntimePresenceSnapshot current = new();

    public static RuntimePresenceSnapshot Current
    {
        get
        {
            lock (SyncRoot)
            {
                return current;
            }
        }
    }

    public static void Update(RuntimePresenceSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        lock (SyncRoot)
        {
            current = snapshot;
        }
    }
}
