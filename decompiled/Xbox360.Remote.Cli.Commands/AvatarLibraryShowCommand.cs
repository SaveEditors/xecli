using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class AvatarLibraryShowCommand : Command<AvatarLibraryShowCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--json")]
		[Description("Emit JSON output.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		CliConfig cliConfig = CliConfig.Load();
		AvatarCommandHelpers.AvatarResolvedPaths avatarResolvedPaths = AvatarCommandHelpers.ResolvePaths(cliConfig.AvatarLibraryRoot, cliConfig.AvatarCachePath, forceRemote: false, cliConfig.AvatarManifestUrl, cliConfig.AvatarTitleMapUrl, cliConfig.AvatarContentBaseUrl, cliConfig.AvatarDownloadCachePath);
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				Mode = (avatarResolvedPaths.RemoteMode ? "remote" : "local"),
				ConfiguredLibraryRoot = avatarResolvedPaths.ConfiguredLibraryRoot,
				EffectiveLibraryRoot = avatarResolvedPaths.EffectiveLibraryRoot,
				ConfiguredCachePath = avatarResolvedPaths.ConfiguredCachePath,
				EffectiveCachePath = avatarResolvedPaths.EffectiveCachePath,
				ConfiguredManifestUrl = avatarResolvedPaths.ConfiguredManifestUrl,
				EffectiveManifestUrl = avatarResolvedPaths.EffectiveManifestUrl,
				ConfiguredTitleMapUrl = avatarResolvedPaths.ConfiguredTitleMapUrl,
				EffectiveTitleMapUrl = avatarResolvedPaths.EffectiveTitleMapUrl,
				ConfiguredContentBaseUrl = avatarResolvedPaths.ConfiguredContentBaseUrl,
				EffectiveContentBaseUrl = avatarResolvedPaths.EffectiveContentBaseUrl,
				ConfiguredDownloadCachePath = avatarResolvedPaths.ConfiguredDownloadCachePath,
				EffectiveDownloadCachePath = avatarResolvedPaths.EffectiveDownloadCachePath
			});
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[bold white]Field[/]"));
		table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
		table.AddRow("[white]Mode[/]", avatarResolvedPaths.RemoteMode ? "[gold1]remote[/]" : "[springgreen3_1]local[/]");
		table.AddRow("[white]Configured Library[/]", string.IsNullOrWhiteSpace(avatarResolvedPaths.ConfiguredLibraryRoot) ? "[grey70]auto[/]" : ("[green]" + Markup.Escape(avatarResolvedPaths.ConfiguredLibraryRoot) + "[/]"));
		table.AddRow("[white]Effective Library[/]", (avatarResolvedPaths.RemoteMode || string.IsNullOrWhiteSpace(avatarResolvedPaths.EffectiveLibraryRoot)) ? "[grey70]n/a[/]" : ("[springgreen3_1]" + Markup.Escape(avatarResolvedPaths.EffectiveLibraryRoot) + "[/]"));
		table.AddRow("[white]Configured Cache[/]", string.IsNullOrWhiteSpace(avatarResolvedPaths.ConfiguredCachePath) ? "[grey70]default[/]" : ("[green]" + Markup.Escape(avatarResolvedPaths.ConfiguredCachePath) + "[/]"));
		table.AddRow("[white]Effective Cache[/]", "[cyan]" + Markup.Escape(avatarResolvedPaths.EffectiveCachePath) + "[/]");
		table.AddRow("[white]Manifest URL[/]", "[deepskyblue1]" + Markup.Escape(avatarResolvedPaths.EffectiveManifestUrl) + "[/]");
		table.AddRow("[white]Title Map URL[/]", "[deepskyblue1]" + Markup.Escape(avatarResolvedPaths.EffectiveTitleMapUrl) + "[/]");
		table.AddRow("[white]Content Base URL[/]", "[deepskyblue1]" + Markup.Escape(avatarResolvedPaths.EffectiveContentBaseUrl) + "[/]");
		table.AddRow("[white]Download Cache[/]", "[cyan]" + Markup.Escape(avatarResolvedPaths.EffectiveDownloadCachePath) + "[/]");
		AnsiConsole.Write(table);
		return 0;
	}
}
