using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ProfilesCommand : AsyncCommand<ProfilesCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--no-ftp")]
		[LocalizedDescription("Skip FTP profile scan.")]
		public bool NoFtp { get; init; }

		[CommandOption("--no-f3")]
		[LocalizedDescription("Skip F3 profile lookup (HTTP 9999).")]
		public bool NoF3 { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		var (ip, port, timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		return await CliHelpers.WithClientAsync((Ip: ip, Port: port, TimeoutMs: timeout), settings, async delegate(XbdmClient client)
		{
			IReadOnlyList<XbdmUserInfo> users = await client.GetUserListAsync(CancellationToken.None);
			ProfileHelpers.XamUserInfo xamUser = null;
			if (users.Count == 0)
			{
				try
				{
					xamUser = await ProfileHelpers.TryGetSignedInXamUserAsync(ip, port, timeout, CancellationToken.None);
					if (xamUser != null)
					{
						users = new XbdmUserInfo[1]
						{
							new XbdmUserInfo
							{
								Gamertag = xamUser.Gamertag,
								Xuid = ((xamUser.Xuid != null && ulong.TryParse(xamUser.Xuid.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result)) ? new ulong?(result) : ((ulong?)null)),
								SignInState = xamUser.SignInState,
								RawLine = $"xam slot={xamUser.Slot}"
							}
						};
					}
				}
				catch
				{
				}
			}
			List<string> ftpProfiles = null;
			if (!settings.NoFtp)
			{
				try
				{
					ftpProfiles = await ProfileHelpers.TryGetFtpProfilesAsync(ip);
				}
				catch
				{
					ftpProfiles = null;
				}
			}
			List<ProfileHelpers.F3ProfileInfo> list = null;
			if (!settings.NoF3)
			{
				try
				{
					list = await ProfileHelpers.TryGetF3ProfilesAsync(ip);
				}
				catch
				{
					list = null;
				}
			}
			XbdmUserInfo xbdmUserInfo = users.FirstOrDefault((XbdmUserInfo u) => u.SignInState.HasValue && u.SignInState.Value != 0 && !string.IsNullOrWhiteSpace(u.Gamertag)) ?? users.FirstOrDefault((XbdmUserInfo u) => !string.IsNullOrWhiteSpace(u.Gamertag));
			string text = xbdmUserInfo?.Gamertag;
			string text2 = (((object)xbdmUserInfo != null && xbdmUserInfo.Xuid.HasValue) ? $"0x{xbdmUserInfo.Xuid.Value:X16}" : null);
			ProfileHelpers.F3ProfileInfo f3ProfileInfo = list?.FirstOrDefault((ProfileHelpers.F3ProfileInfo p) => p.SignedIn == 1 && !string.IsNullOrWhiteSpace(p.Gamertag));
			if (string.IsNullOrWhiteSpace(text) && f3ProfileInfo != null)
			{
				text = f3ProfileInfo.Gamertag;
				if (text2 == null)
				{
					text2 = f3ProfileInfo.Xuid;
				}
			}
			if (string.IsNullOrWhiteSpace(text) && xamUser != null)
			{
				text = xamUser.Gamertag;
				if (text2 == null)
				{
					text2 = xamUser.Xuid;
				}
			}
			Dictionary<string, List<string>> dictionary = ((ftpProfiles != null) ? ProfileHelpers.GroupFtpProfiles(ftpProfiles) : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Target = new { ip, port },
					SignedIn = text,
					SignedInXuid = text2,
					Users = users,
					FtpProfiles = dictionary.Select((KeyValuePair<string, List<string>> kvp) => new
					{
						ProfileId = kvp.Key,
						Devices = kvp.Value
					}),
					F3Profiles = list
				});
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Profiles[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Field[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			table.AddRow("[grey]Signed In[/]", FormatValue(text, "green", "[grey]none[/]"));
			table.AddRow("[grey]Signed In XUID[/]", FormatValue(text2, "gold1", "[grey]none[/]"));
			table.AddRow("[grey]XBDM Users[/]", users.Count.ToString(CultureInfo.InvariantCulture));
			table.AddRow("[grey]FTP Profiles[/]", dictionary.Count.ToString(CultureInfo.InvariantCulture));
			table.AddRow("[grey]F3 Profiles[/]", (list?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
			AnsiConsole.Write(table);
			if (users.Count > 0)
			{
				AnsiConsole.Write(new Rule("[bold deepskyblue1]XBDM Users[/]").RuleStyle("grey"));
				Table table2 = CliOutput.CreateTable();
				table2.AddColumn(new TableColumn("[green]Gamertag[/]"));
				table2.AddColumn(new TableColumn("[gold1]XUID[/]"));
				table2.AddColumn(new TableColumn("[cyan]State[/]"));
				table2.AddColumn(new TableColumn("[grey]Raw[/]"));
				foreach (XbdmUserInfo item in users)
				{
					string text3 = (item.SignInState.HasValue ? item.SignInState.Value.ToString(CultureInfo.InvariantCulture) : "unknown");
					table2.AddRow(FormatValue(item.Gamertag, "green"), item.Xuid.HasValue ? $"[gold1]0x{item.Xuid.Value:X16}[/]" : "[grey]unknown[/]", "[cyan]" + text3 + "[/]", Markup.Escape(item.RawLine));
				}
				AnsiConsole.Write(table2);
			}
			if (dictionary.Count > 0)
			{
				AnsiConsole.Write(new Rule("[bold deepskyblue1]FTP Profiles[/]").RuleStyle("grey"));
				Table table3 = CliOutput.CreateTable();
				table3.AddColumn(new TableColumn("[cyan]Profile ID[/]"));
				table3.AddColumn(new TableColumn("[green]Devices[/]"));
				foreach (KeyValuePair<string, List<string>> item2 in dictionary.OrderBy<KeyValuePair<string, List<string>>, string>((KeyValuePair<string, List<string>> e) => e.Key, StringComparer.OrdinalIgnoreCase))
				{
					string text4 = ((item2.Value.Count > 0) ? string.Join(", ", item2.Value) : "unknown");
					table3.AddRow("[cyan]" + Markup.Escape(item2.Key) + "[/]", "[green]" + Markup.Escape(text4) + "[/]");
				}
				AnsiConsole.Write(table3);
			}
			if (list != null && list.Count > 0)
			{
				AnsiConsole.Write(new Rule("[bold deepskyblue1]F3 Profiles[/]").RuleStyle("grey"));
				Table table4 = CliOutput.CreateTable();
				table4.AddColumn(new TableColumn("[green]Gamertag[/]"));
				table4.AddColumn(new TableColumn("[gold1]XUID[/]"));
				table4.AddColumn(new TableColumn("[grey]Signed In[/]"));
				table4.AddColumn(new TableColumn("[cyan]Gamerscore[/]"));
				foreach (ProfileHelpers.F3ProfileInfo item3 in list)
				{
					table4.AddRow(FormatValue(item3.Gamertag, "green"), string.IsNullOrWhiteSpace(item3.Xuid) ? "[grey]unknown[/]" : ("[gold1]" + Markup.Escape(item3.Xuid) + "[/]"), (item3.SignedIn == 1) ? "[green]yes[/]" : "[grey]no[/]", item3.Gamerscore.ToString(CultureInfo.InvariantCulture));
				}
				AnsiConsole.Write(table4);
			}
			return 0;
		}, CancellationToken.None);
		static string FormatValue(string? value, string color, string? fallback = null)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				return $"[{color}]{Markup.Escape(value)}[/]";
			}
			return fallback ?? "[grey]unknown[/]";
		}
	}
}
