namespace Xbox360.Remote.Cli;

internal sealed record TitleIdEntry(uint TitleId, uint? MediaId, string Name, string? Serial, string? Type, string? Region, string? XexCrc, string? Wave);
