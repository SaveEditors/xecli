using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class SignInStateCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			ProfileHelpers.XamUserInfo xamUserInfo = await HardwareHelpers.TryGetSignedInUserAsync(client, CancellationToken.None);
			bool flag = xamUserInfo != null && xamUserInfo.SignInState != 0;
			string text = HardwareHelpers.DescribeSignInState(xamUserInfo?.SignInState);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					SignedIn = flag,
					SignInState = text,
					Slot = xamUserInfo?.Slot,
					Gamertag = xamUserInfo?.Gamertag,
					Xuid = xamUserInfo?.Xuid
				});
				return 0;
			}
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[bold white]Field[/]"));
			table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
			table.AddRow("[white]Signed In[/]", flag ? "[springgreen3_1]Yes[/]" : "[red1]No[/]");
			table.AddRow("[white]State[/]", "[gold1]" + Markup.Escape(text) + "[/]");
			table.AddRow("[white]Gamertag[/]", flag ? ("[springgreen3_1]" + Markup.Escape(xamUserInfo?.Gamertag ?? "unknown") + "[/]") : "[grey70]none[/]");
			table.AddRow("[white]XUID[/]", flag ? ("[gold1]" + Markup.Escape(xamUserInfo?.Xuid ?? "unknown") + "[/]") : "[grey70]none[/]");
			table.AddRow("[white]Slot[/]", flag ? ("[deepskyblue1]" + xamUserInfo.Slot.ToString(CultureInfo.InvariantCulture) + "[/]") : "[grey70]-[/]");
			AnsiConsole.Write(table);
			return 0;
		}, CancellationToken.None);
	}
}
