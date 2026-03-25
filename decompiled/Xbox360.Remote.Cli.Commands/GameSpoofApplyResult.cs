namespace Xbox360.Remote.Cli.Commands;

internal sealed record GameSpoofApplyResult(GameSpoofState Verified, bool VerifiedMatch, bool PatchApplied);
