using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarInstallCommand : AsyncCommand<AvatarInstallCommand.Settings>
{
	public class Settings : AvatarInstallSettingsBase
	{
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(settings.LibraryRoot, settings.CachePath, settings.Remote, settings.ManifestUrl, settings.TitleMapUrl, settings.ContentBaseUrl, settings.DownloadCachePath);
		IReadOnlyList<AvatarItemRecord> selectedItems = AvatarCommandHelpers.SelectInstallItems(await AvatarCommandHelpers.LoadIndexAsync(paths, useCache: true, CancellationToken.None), settings);
		return await AvatarInstallFlow.RunAsync(paths, settings, selectedItems, CancellationToken.None);
	}
}
