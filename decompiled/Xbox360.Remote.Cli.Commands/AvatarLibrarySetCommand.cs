using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarLibrarySetCommand : Command<AvatarLibrarySetCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--path <DIR>")]
		[Description("Set the default Avatar-Item-Collection root.")]
		public string? Path { get; init; }

		[CommandOption("--cache <PATH>")]
		[Description("Set the avatar index cache file or directory.")]
		public string? CachePath { get; init; }

		[CommandOption("--manifest-url <URL>")]
		[Description("Set the hosted avatar manifest URL.")]
		public string? ManifestUrl { get; init; }

		[CommandOption("--title-map-url <URL>")]
		[Description("Set the hosted avatar title map URL.")]
		public string? TitleMapUrl { get; init; }

		[CommandOption("--content-base-url <URL>")]
		[Description("Set the hosted avatar content base URL.")]
		public string? ContentBaseUrl { get; init; }

		[CommandOption("--download-cache <DIR>")]
		[Description("Set the cache directory for downloaded avatar packages.")]
		public string? DownloadCachePath { get; init; }

		[CommandOption("--clear")]
		[Description("Clear saved avatar library settings.")]
		public bool Clear { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (settings.Clear)
		{
			AvatarCommandHelpers.SaveConfiguredPaths(null, null, null, null, null, null, clear: true);
			OperationFeedback.WriteSuccess("Avatar library settings cleared", "[grey]The default avatar library and cache paths were removed.[/]");
			return 0;
		}
		if (string.IsNullOrWhiteSpace(settings.Path) && string.IsNullOrWhiteSpace(settings.CachePath) && string.IsNullOrWhiteSpace(settings.ManifestUrl) && string.IsNullOrWhiteSpace(settings.TitleMapUrl) && string.IsNullOrWhiteSpace(settings.ContentBaseUrl) && string.IsNullOrWhiteSpace(settings.DownloadCachePath))
		{
			AnsiConsole.MarkupLine("[red]Provide --path, --cache, --manifest-url, --title-map-url, --content-base-url, --download-cache, or --clear.[/]");
			return 1;
		}
		AvatarCommandHelpers.SaveConfiguredPaths(settings.Path, settings.CachePath, settings.ManifestUrl, settings.TitleMapUrl, settings.ContentBaseUrl, settings.DownloadCachePath, clear: false);
		AvatarCommandHelpers.AvatarResolvedPaths avatarResolvedPaths = AvatarCommandHelpers.ResolvePaths(settings.Path, settings.CachePath, forceRemote: false, settings.ManifestUrl, settings.TitleMapUrl, settings.ContentBaseUrl, settings.DownloadCachePath);
		OperationFeedback.WriteSuccess("Avatar library settings updated", $"[grey]Mode:[/] [{(avatarResolvedPaths.RemoteMode ? "gold1" : "springgreen3_1")}]{(avatarResolvedPaths.RemoteMode ? "remote" : "local")}[/]\n[grey]Library:[/] {(string.IsNullOrWhiteSpace(avatarResolvedPaths.EffectiveLibraryRoot) ? "[grey70]n/a[/]" : ("[springgreen3_1]" + Markup.Escape(avatarResolvedPaths.EffectiveLibraryRoot) + "[/]"))}\n[grey]Cache:[/] [cyan]{Markup.Escape(avatarResolvedPaths.EffectiveCachePath)}[/]\n[grey]Manifest:[/] [deepskyblue1]{Markup.Escape(avatarResolvedPaths.EffectiveManifestUrl)}[/]\n[grey]Content Base:[/] [deepskyblue1]{Markup.Escape(avatarResolvedPaths.EffectiveContentBaseUrl)}[/]");
		return 0;
	}
}
