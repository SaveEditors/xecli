using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class SpoofResetCommand : AsyncCommand<SpoofResetCommand.Settings>
{
	public sealed class Settings : SpoofIdentitySettingsBase
	{
		[CommandOption("--gamertag <TEXT>")]
		[LocalizedDescription("Explicit gamertag to restore instead of the signed-in user.")]
		public string? Gamertag { get; init; }

		[CommandOption("--xuid <HEX>")]
		[LocalizedDescription("Explicit XUID to restore instead of the signed-in user.")]
		public string? Xuid { get; init; }

		[CommandOption("--clear-remote")]
		[LocalizedDescription("Clear every supported remote slot after restoring the local identity.")]
		public bool ClearRemote { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		(string, int, int) obj = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		string item = obj.Item1;
		int item2 = obj.Item2;
		int item3 = obj.Item3;
		string targetKey = $"{item}:{item2}";
		return await CliHelpers.WithClientOnceAsync((Ip: item, Port: item2, TimeoutMs: item3), settings, async delegate(XbdmClient client)
		{
			uint titleId = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
			GameSpoofHelpers.SupportedGameSpoofProfile profile = GameSpoofHelpers.TryGet(titleId);
			if (profile == null)
			{
				OperationFeedback.WriteWarning("Current title not supported for spoof reset", $"0x{titleId:X8}");
				return 1;
			}
			ProfileHelpers.XamUserInfo currentUser = await HardwareHelpers.TryGetSignedInUserAsync(client, CancellationToken.None);
			CliConfig.SpoofIdentityCacheInfo cached = GameSpoofHelpers.TryGetCachedIdentity(targetKey, titleId);
			if (!GameSpoofHelpers.TryResolveRestoreIdentity(currentUser, cached, settings.Gamertag, settings.Xuid, settings.CurrentUser, out string resolvedGamertag, out string resolvedXuid, out string error))
			{
				AnsiConsole.MarkupLine("[red]" + Markup.Escape(error ?? "Unable to resolve restore identity.") + "[/]");
				return 1;
			}
			GameSpoofApplyResult result = await GameSpoofHelpers.RestoreIdentityAsync(client, profile, resolvedGamertag, resolvedXuid, CancellationToken.None);
			bool flag = false;
			if (settings.ClearRemote && profile.Remote != null)
			{
				IReadOnlyList<int> slots = Enumerable.Range(profile.Remote.StartSlotIndex, profile.Remote.ClientCount).ToArray();
				flag = (await GameSpoofHelpers.ApplyRemoteTextAsync(client, profile, slots, string.Empty, CancellationToken.None)).VerifiedMatch;
			}
			if (result.VerifiedMatch)
			{
				GameSpoofHelpers.ClearCachedIdentity(targetKey, titleId);
			}
			if (result.VerifiedMatch)
			{
				string value = (flag ? "[grey]Local identity restored and remote slots cleared[/]" : "[grey]Local identity restored[/]");
				OperationFeedback.WriteSuccess("Spoof reset applied", $"[gold1]{Markup.Escape(result.Verified.Gamertag)}[/] [grey]|[/] [cyan1]{Markup.Escape(result.Verified.CanonicalXuidHex)}[/] {value}");
			}
			else
			{
				OperationFeedback.WriteWarning("Spoof reset wrote memory but verification did not fully match", Markup.Escape(profile.Name));
			}
			string message = SpoofNotifyHelpers.BuildResetIdentityMessage(profile.Name, result.Verified.Gamertag, result.Verified.CanonicalXuidHex);
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), message, CancellationToken.None, useBottomPosition: true);
			return (!result.VerifiedMatch) ? 1 : 0;
		}, CancellationToken.None);
	}
}
