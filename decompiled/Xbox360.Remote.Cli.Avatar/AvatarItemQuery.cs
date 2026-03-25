namespace Xbox360.Remote.Cli.Avatar;

internal sealed record AvatarItemQuery(uint? TitleId = null, string? Search = null, string? Publisher = null, string? Tag = null, int? Limit = null);
