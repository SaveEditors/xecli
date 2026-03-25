namespace Xbox360.Remote.Cli.Commands;

internal sealed record GameSpoofState(string Gamertag, string CanonicalXuidHex, string StoredXuidHex, string StoredXuidText, string? SecondaryName);
