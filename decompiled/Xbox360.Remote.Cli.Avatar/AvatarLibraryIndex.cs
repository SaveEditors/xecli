using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Xbox360.Remote.Cli.Avatar;

internal sealed record AvatarLibraryIndex(AvatarCollectionFingerprint Fingerprint, DateTimeOffset GeneratedUtc, IReadOnlyList<AvatarItemRecord> Items)
{
	[JsonIgnore]
	public IReadOnlyList<AvatarTitleSummary> Titles => AvatarLibraryQueries.SummarizeTitles(Items);
}
