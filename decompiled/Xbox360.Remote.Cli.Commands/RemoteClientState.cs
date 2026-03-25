namespace Xbox360.Remote.Cli.Commands;

internal sealed record RemoteClientState(int Slot, uint NameAddress, uint? MirrorAddress, string Name, string? MirrorName, uint? XuidAddress = null, string? Xuid = null);
