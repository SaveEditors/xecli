using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class RemoteSpoofListCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			uint num = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
			GameSpoofHelpers.SupportedGameSpoofProfile profile = GameSpoofHelpers.TryGet(num);
			if (profile?.Remote == null)
			{
				OperationFeedback.WriteWarning("Current title does not expose remote spoof slots", $"0x{num:X8}");
				return 1;
			}
			IReadOnlyList<RemoteClientState> source = await GameSpoofHelpers.ReadRemoteClientsAsync(client, profile, CancellationToken.None);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Name = profile.Name,
					TitleId = $"0x{profile.TitleId:X8}",
					Slots = source.Select((RemoteClientState x) => new
					{
						Slot = x.Slot,
						NameAddress = $"0x{x.NameAddress:X8}",
						MirrorAddress = (x.MirrorAddress.HasValue ? $"0x{x.MirrorAddress.Value:X8}" : null),
						XuidAddress = (x.XuidAddress.HasValue ? $"0x{x.XuidAddress.Value:X8}" : null),
						Name = x.Name,
						MirrorName = x.MirrorName,
						Xuid = x.Xuid
					})
				});
				return 0;
			}
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[bold white]Slot[/]"));
			table.AddColumn(new TableColumn("[bold green3]Name[/]"));
			table.AddColumn(new TableColumn("[bold deepskyblue1]Address[/]"));
			table.AddColumn(new TableColumn("[bold cyan1]XUID[/]"));
			table.AddColumn(new TableColumn("[bold grey70]Mirror[/]"));
			foreach (RemoteClientState item in source.Where((RemoteClientState x) => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.MirrorName)))
			{
				table.AddRow($"[white]{item.Slot}[/]", "[gold1]" + Markup.Escape(item.Name) + "[/]", $"[mediumpurple3_1]0x{item.NameAddress:X8}[/]", (item.Xuid != null) ? ("[cyan1]" + Markup.Escape(item.Xuid) + "[/]") : "[grey50]-[/]", item.MirrorAddress.HasValue ? $"[grey70]0x{item.MirrorAddress.Value:X8}[/]" : "[grey50]-[/]");
			}
			AnsiConsole.Write(table);
			return 0;
		}, CancellationToken.None);
	}
}
