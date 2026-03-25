using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarBrowseCommand : AsyncCommand<AvatarBrowseCommand.Settings>
{
	public class Settings : AvatarInstallCommand.Settings
	{
		[CommandOption("--search <TEXT>")]
		[LocalizedDescription("Initial item search text.")]
		public string? Search { get; init; }

		[CommandOption("--game <TEXT>")]
		[LocalizedDescription("Initial game/title filter text.")]
		public string? Game { get; init; }

		[CommandOption("--publisher <TEXT>")]
		[LocalizedDescription("Initial publisher filter.")]
		public string? Publisher { get; init; }

		[CommandOption("--tag <TEXT>")]
		[LocalizedDescription("Initial derived tag filter.")]
		public string? Tag { get; init; }

		[CommandOption("--limit <N>")]
		[LocalizedDescription("Maximum titles/items to preload (default: 100, 0 = no limit).")]
		public int? Limit { get; init; }

		[CommandOption("--no-cache")]
		[LocalizedDescription("Force a fresh library scan instead of using the cache.")]
		public bool NoCache { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!OperatingSystem.IsWindows())
		{
			AnsiConsole.MarkupLine("[red]The avatar browser is only available on Windows.[/]");
			return 1;
		}
		AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(settings.LibraryRoot, settings.CachePath, settings.Remote, settings.ManifestUrl, settings.TitleMapUrl, settings.ContentBaseUrl, settings.DownloadCachePath);
		AvatarLibraryIndex index = await AvatarCommandHelpers.LoadIndexAsync(paths, !settings.NoCache, CancellationToken.None);
		AvatarCommandHelpers.AvatarResolvedOwnership ownership;
		try
		{
			string item = (await AvatarCommandHelpers.ResolveXbdmTargetAsync(settings, CancellationToken.None)).Item1;
			ownership = await AvatarCommandHelpers.ResolveOwnershipAsync(settings, item, CancellationToken.None);
		}
		catch
		{
			ownership = null;
		}
		IReadOnlyList<AvatarItemRecord> initialSelection = ResolveInitialSelection(index, settings);
		using AvatarBrowserForm form = new AvatarBrowserForm(index, paths, settings, ownership, initialSelection);
		if (form.ShowDialog() != DialogResult.OK)
		{
			return 0;
		}
		IReadOnlyList<AvatarItemRecord> selectedItems = form.SelectedItems;
		if (selectedItems.Count == 0)
		{
			OperationFeedback.WriteWarning("Avatar browser", "No avatar items were selected.");
			return 0;
		}
		return await AvatarInstallFlow.RunAsync(paths, settings, selectedItems, CancellationToken.None);
	}

	private static IReadOnlyList<AvatarItemRecord> ResolveInitialSelection(AvatarLibraryIndex index, Settings settings)
	{
		List<AvatarItemRecord> list = new List<AvatarItemRecord>();
		uint titleId3;
		if (!string.IsNullOrWhiteSpace(settings.ContentId))
		{
			uint? titleId = null;
			if (!string.IsNullOrWhiteSpace(settings.TitleId) && SaveHelpers.TryParseTitleId(settings.TitleId, out var titleId2))
			{
				titleId = titleId2;
			}
			AvatarItemRecord avatarItemRecord = AvatarLibraryService.FindByContentId(index, settings.ContentId, titleId);
			if (avatarItemRecord != null)
			{
				list.Add(avatarItemRecord);
			}
		}
		else if (settings.All && !string.IsNullOrWhiteSpace(settings.TitleId) && SaveHelpers.TryParseTitleId(settings.TitleId, out titleId3))
		{
			list.AddRange(AvatarLibraryService.SearchItems(index, new AvatarItemQuery(titleId3, null, null, null, int.MaxValue)));
		}
		return list;
	}
}
