using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GamertagSpoofSetCommand : AsyncCommand<GamertagSpoofSetCommand.Settings>
{
	public sealed class Settings : SpoofIdentitySettingsBase
	{
		[CommandOption("--value <TEXT>")]
		[LocalizedDescription("Gamertag to write into the running supported title.")]
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
			if (!GameSpoofHelpers.TryResolveGamertag(await HardwareHelpers.TryGetSignedInUserAsync(client, CancellationToken.None), settings.Value, settings.CurrentUser, out string gamertag, out string error))
			{
				AnsiConsole.MarkupLine("[red]" + Markup.Escape(error ?? "Invalid gamertag.") + "[/]");
				return 1;
			}
			GameSpoofState state = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
			GameSpoofHelpers.StoreOriginalIdentityIfMissing(targetKey, titleId, state);
			GameSpoofApplyResult result = await GameSpoofHelpers.ApplyGamertagAsync(client, profile, gamertag, CancellationToken.None);
			if (result.VerifiedMatch)
			{
				OperationFeedback.WriteSuccess("Gamertag spoof applied", "[gold1]" + Markup.Escape(result.Verified.Gamertag) + "[/]");
			}
			else
			{
				OperationFeedback.WriteWarning("Gamertag spoof wrote memory but verification did not fully match", Markup.Escape(profile.Name));
			}
			string message = SpoofNotifyHelpers.BuildSpoofingIdentityMessage(profile.Name, result.Verified.Gamertag, result.Verified.CanonicalXuidHex);
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), message, CancellationToken.None, useBottomPosition: true);
			return (!result.VerifiedMatch) ? 1 : 0;
		}, CancellationToken.None);
	}
}
