using System;
using System.Collections.Generic;
using System.Linq;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarLibraryQueries
{
	public static IReadOnlyList<AvatarTitleSummary> SummarizeTitles(IReadOnlyList<AvatarItemRecord> items)
	{
		return (from item in items
			group item by item.TitleId into @group
			select new AvatarTitleSummary(@group.Key, @group.First().TitleName, @group.Count(), @group.Sum((AvatarItemRecord item) => item.SizeBytes), (from item in @group
				select item.Publisher into value
				where !string.IsNullOrWhiteSpace(value)
				select value).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string value) => value, StringComparer.OrdinalIgnoreCase).Cast<string>()
				.ToArray())).OrderBy<AvatarTitleSummary, string>((AvatarTitleSummary summary) => summary.TitleName, StringComparer.OrdinalIgnoreCase).ThenBy((AvatarTitleSummary summary) => summary.TitleId).ToArray();
	}

	public static IReadOnlyList<AvatarItemRecord> Filter(IReadOnlyList<AvatarItemRecord> items, AvatarItemQuery query)
	{
		IEnumerable<AvatarItemRecord> source = items;
		if (query.TitleId.HasValue)
		{
			source = source.Where((AvatarItemRecord item) => item.TitleId == query.TitleId.Value);
		}
		if (!string.IsNullOrWhiteSpace(query.Search))
		{
			string search = query.Search.Trim();
			source = source.Where((AvatarItemRecord item) => AvatarLibraryService.ResolveItemDisplayName(item.ContentId, item.DisplayName, item.GameName, item.TitleName).Contains(search, StringComparison.OrdinalIgnoreCase) || item.TitleName.Contains(search, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(item.GameName) && item.GameName.Contains(search, StringComparison.OrdinalIgnoreCase)) || item.ContentId.Contains(search, StringComparison.OrdinalIgnoreCase));
		}
		if (!string.IsNullOrWhiteSpace(query.Publisher))
		{
			string publisher = query.Publisher.Trim();
			source = source.Where((AvatarItemRecord item) => string.Equals(item.Publisher, publisher, StringComparison.OrdinalIgnoreCase));
		}
		if (!string.IsNullOrWhiteSpace(query.Tag))
		{
			string tag = query.Tag.Trim();
			source = source.Where((AvatarItemRecord item) => item.Tags.Any((string existing) => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)));
		}
		source = source.OrderBy<AvatarItemRecord, string>((AvatarItemRecord item) => item.TitleName, StringComparer.OrdinalIgnoreCase).ThenBy<AvatarItemRecord, string>((AvatarItemRecord item) => AvatarLibraryService.ResolveItemDisplayName(item.ContentId, item.DisplayName, item.GameName, item.TitleName), StringComparer.OrdinalIgnoreCase).ThenBy<AvatarItemRecord, string>((AvatarItemRecord item) => item.ContentId, StringComparer.OrdinalIgnoreCase);
		if (query.Limit.HasValue && query.Limit.Value > 0)
		{
			source = source.Take(query.Limit.Value);
		}
		return source.ToArray();
	}
}
