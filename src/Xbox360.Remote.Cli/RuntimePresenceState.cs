using System;

namespace Xbox360.Remote.Cli;

internal sealed class RuntimePresenceSnapshot
{
    public bool Connected { get; init; }

    public string? DebugName { get; init; }

    public string? ExecutionState { get; init; }

    public string? Motherboard { get; init; }

    public uint? DashboardVersion { get; init; }

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

    public static event Action? Changed;

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

        bool changed;
        lock (SyncRoot)
        {
            changed = !AreEquivalent(current, snapshot);
            current = snapshot;
        }

        if (changed)
            Changed?.Invoke();
    }

    private static bool AreEquivalent(RuntimePresenceSnapshot left, RuntimePresenceSnapshot right)
    {
        return left.Connected == right.Connected
            && string.Equals(left.DebugName, right.DebugName, StringComparison.Ordinal)
            && string.Equals(left.ExecutionState, right.ExecutionState, StringComparison.Ordinal)
            && string.Equals(left.Motherboard, right.Motherboard, StringComparison.Ordinal)
            && left.DashboardVersion == right.DashboardVersion
            && string.Equals(left.Gamertag, right.Gamertag, StringComparison.Ordinal)
            && string.Equals(left.SignInStateText, right.SignInStateText, StringComparison.Ordinal)
            && left.TitleId == right.TitleId
            && string.Equals(left.TitleName, right.TitleName, StringComparison.Ordinal)
            && string.Equals(left.RunningXex, right.RunningXex, StringComparison.Ordinal)
            && string.Equals(left.Ip, right.Ip, StringComparison.Ordinal)
            && left.Port == right.Port;
    }
}
