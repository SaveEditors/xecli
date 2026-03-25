using System.Collections.Generic;

namespace Xbox360.Remote.Cli.Commands;

internal sealed record RemoteSpoofApplyResult(IReadOnlyList<RemoteClientState> Applied, bool VerifiedMatch);
