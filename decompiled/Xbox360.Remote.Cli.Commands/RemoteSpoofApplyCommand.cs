using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class RemoteSpoofApplyCommand : AsyncCommand<RemoteSpoofApplyCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--slot <N>")]
		[Description("1-based client slot to overwrite.")]
		public int? Slot { get; init; }

		[CommandOption("--all")]
		[Description("Overwrite every supported slot.")]
		public bool All { get; init; }

		[CommandOption("--text <TEXT>")]
		[Description("Replacement name text. Use {slot} to inject the 1-based slot number.")]
		public string? Text { get; init; }

		[CommandOption("--xuid <HEX>")]
		[Description("16-character XUID to write into each target slot's identity field alongside the name.")]
		public string? Xuid { get; init; }

		[CommandOption("--notify")]
		[Description("Send a default success notification to the console.")]
		public bool Notify { get; init; }

		[CommandOption("--notify-icon <NAME>")]
		[Description("Notification icon preset name.")]
		public string? NotifyIcon { get; init; }

		[CommandOption("--notify-logo <ID>")]
		[Description("Notification logo id (decimal or 0x hex).")]
		public string? NotifyLogo { get; init; }
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
			if (string.IsNullOrWhiteSpace(settings.Text))
			{
				AnsiConsole.MarkupLine("[red]Provide --text for the remote spoof payload.[/]");
				return 1;
			}
			uint titleId = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
			GameSpoofHelpers.SupportedGameSpoofProfile profile = GameSpoofHelpers.TryGet(titleId);
			if (profile?.Remote == null)
			{
				OperationFeedback.WriteWarning("Current title does not expose remote spoof slots", $"0x{titleId:X8}");
				return 1;
			}
			if (!GameSpoofHelpers.TryResolveSlots(profile, settings.Slot, settings.All, out IReadOnlyList<int> slots, out string error))
			{
				AnsiConsole.MarkupLine("[red]" + Markup.Escape(error ?? "Invalid remote slot selection.") + "[/]");
				return 1;
			}
			string remoteXuid = null;
			if (!string.IsNullOrWhiteSpace(settings.Xuid))
			{
				if (!GameSpoofHelpers.TryResolveXuid(null, settings.Xuid, useCurrentUser: false, out string xuid, out string error2))
				{
					AnsiConsole.MarkupLine("[red]" + Markup.Escape(error2 ?? "Invalid XUID.") + "[/]");
					return 1;
				}
				GameSpoofHelpers.RemoteSpoofProfile? remote = profile.Remote;
				if ((object)remote == null || !remote.XuidBinaryOffset.HasValue)
				{
					AnsiConsole.MarkupLine("[yellow]--xuid ignored: current title does not have a mapped remote XUID field.[/]");
				}
				else
				{
					remoteXuid = xuid;
				}
			}
			GameSpoofState state = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
			GameSpoofHelpers.StoreOriginalIdentityIfMissing(targetKey, titleId, state);
			RemoteSpoofApplyResult result = await GameSpoofHelpers.ApplyRemoteTextAsync(client, profile, slots, settings.Text.Trim(), CancellationToken.None, remoteXuid);
			if (result.VerifiedMatch)
			{
				OperationFeedback.WriteSuccess("Remote spoof applied", $"[springgreen3_1]{Markup.Escape(profile.Name)}[/] [grey]=>[/] [gold1]{Markup.Escape(settings.Text)}[/]");
			}
			else
			{
				OperationFeedback.WriteWarning("Remote spoof wrote memory but verification did not fully match", Markup.Escape(profile.Name));
			}
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[bold white]Slot[/]"));
			table.AddColumn(new TableColumn("[bold green3]Verified[/]"));
			table.AddColumn(new TableColumn("[bold deepskyblue1]Address[/]"));
			table.AddColumn(new TableColumn("[bold cyan1]XUID[/]"));
			foreach (RemoteClientState item4 in result.Applied)
			{
				table.AddRow($"[white]{item4.Slot}[/]", "[gold1]" + Markup.Escape(item4.Name) + "[/]", $"[mediumpurple3_1]0x{item4.NameAddress:X8}[/]", (item4.Xuid != null) ? ("[cyan1]" + Markup.Escape(item4.Xuid) + "[/]") : "[grey50]-[/]");
			}
			AnsiConsole.Write(table);
			string message = SpoofNotifyHelpers.BuildRemoteSpoofMessage(profile.Name, result.Applied.FirstOrDefault()?.Name ?? settings.Text.Trim());
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), message, CancellationToken.None, useBottomPosition: true);
			return (!result.VerifiedMatch) ? 1 : 0;
		}, CancellationToken.None);
	}
}
