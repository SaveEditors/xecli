using System.ComponentModel;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public abstract class AvatarLibrarySettings : CommandSettings
{
	[CommandOption("--library <DIR>")]
	[LocalizedDescription("Avatar item library root. Defaults to config or Avatar-Item-Collection.")]
	public string? LibraryRoot { get; init; }

	[CommandOption("--cache <PATH>")]
	[LocalizedDescription("Avatar index cache file or directory.")]
	public string? CachePath { get; init; }

	[CommandOption("--remote")]
	[LocalizedDescription("Use the hosted avatar library instead of the local corpus.")]
	public bool Remote { get; init; }

	[CommandOption("--manifest-url <URL>")]
	[LocalizedDescription("Override the hosted avatar manifest URL.")]
	public string? ManifestUrl { get; init; }

	[CommandOption("--title-map-url <URL>")]
	[LocalizedDescription("Override the hosted avatar title map URL.")]
	public string? TitleMapUrl { get; init; }

	[CommandOption("--content-base-url <URL>")]
	[LocalizedDescription("Override the hosted avatar content base URL.")]
	public string? ContentBaseUrl { get; init; }

	[CommandOption("--download-cache <DIR>")]
	[LocalizedDescription("Directory used to cache downloaded avatar packages.")]
	public string? DownloadCachePath { get; init; }

	[CommandOption("--json")]
	[LocalizedDescription("Emit JSON output.")]
	public bool Json { get; init; }
}
