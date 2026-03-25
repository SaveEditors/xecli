using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XuidSpoofSetCommand : AsyncCommand<XuidSpoofSetCommand.Settings>
{
	public sealed class Settings : SpoofIdentitySettingsBase
	{
		[CommandOption("--value <HEX>")]
		[LocalizedDescription("16-character XUID to write into the running supported title.")]
		public string? Value { get; init; }
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
				OperationFeedback.WriteWarning("Current title not supported for local spoofing", $"0x{titleId:X8}");
				return 1;
			}
			if (!GameSpoofHelpers.TryResolveXuid(await HardwareHelpers.TryGetSignedInUserAsync(client, CancellationToken.None), settings.Value, settings.CurrentUser, out string xuid, out string error))
			{
				AnsiConsole.MarkupLine("[red]" + Markup.Escape(error ?? "Invalid XUID.") + "[/]");
				return 1;
			}
			GameSpoofState gameSpoofState = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
			GameSpoofHelpers.StoreOriginalIdentityIfMissing(targetKey, titleId, gameSpoofState);
			GameSpoofApplyResult result = await GameSpoofHelpers.ApplyIdentityAsync(client, profile, gameSpoofState.Gamertag, xuid, CancellationToken.None);
			if (result.VerifiedMatch)
			{
				OperationFeedback.WriteSuccess("XUID spoof applied", "[cyan1]" + Markup.Escape(result.Verified.CanonicalXuidHex) + "[/]");
			}
			else
			{
				OperationFeedback.WriteWarning("XUID spoof wrote memory but verification did not fully match", Markup.Escape(profile.Name));
			}
			string message = SpoofNotifyHelpers.BuildSpoofingIdentityMessage(profile.Name, result.Verified.Gamertag, result.Verified.CanonicalXuidHex);
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), message, CancellationToken.None, useBottomPosition: true);
			return (!result.VerifiedMatch) ? 1 : 0;
		}, CancellationToken.None);
	}
}
