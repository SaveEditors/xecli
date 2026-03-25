using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GamertagSpoofShowCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			uint num = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
			GameSpoofHelpers.SupportedGameSpoofProfile profile = GameSpoofHelpers.TryGet(num);
			if (profile == null)
			{
				OperationFeedback.WriteWarning("Current title not supported for local spoofing", $"0x{num:X8}");
				return 1;
			}
			GameSpoofState gameSpoofState = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Name = profile.Name,
					TitleId = $"0x{profile.TitleId:X8}",
					Gamertag = gameSpoofState.Gamertag,
					Address = $"0x{profile.NameAddress:X8}"
				});
				return 0;
			}
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[bold white]Field[/]"));
			table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
			table.AddRow("[white]Game[/]", "[springgreen3_1]" + Markup.Escape(profile.Name) + "[/]");
			table.AddRow("[white]Gamertag[/]", "[gold1]" + Markup.Escape(gameSpoofState.Gamertag) + "[/]");
			table.AddRow("[white]Address[/]", $"[mediumpurple3_1]0x{profile.NameAddress:X8}[/]");
			AnsiConsole.Write(table);
			return 0;
		}, CancellationToken.None);
	}
}
