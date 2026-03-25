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

public sealed class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--quick")]
		[Description("Skip JRPC2, drive, and user checks for a fast snapshot.")]
		public bool Quick { get; init; }

		[CommandOption("--no-jrpc")]
		[Description("Skip JRPC2 queries (temps, CPU key, dashboard, title id).")]
		public bool NoJrpc { get; init; }

		[CommandOption("--no-drives")]
		[Description("Skip drive and USB size reporting.")]
		public bool NoDrives { get; init; }

		[CommandOption("--no-users")]
		[Description("Skip user list and signed-in detection.")]
		public bool NoUsers { get; init; }
	}

	private sealed class DriveGroup
	{
		public string Family { get; }

		public string DisplayName { get; }

		public ulong? TotalBytes { get; }

		public ulong? FreeBytes { get; }

		public List<string> Aliases { get; }

		public DriveGroup(string family, string displayName, string primaryAlias, ulong? totalBytes, ulong? freeBytes)
		{
			Family = family;
			DisplayName = displayName;
			TotalBytes = totalBytes;
			FreeBytes = freeBytes;
			Aliases = new List<string> { primaryAlias };
		}
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		(string, int, int) tuple = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		int timeout = settings.TimeoutMs ?? 2000;
		using (CancellationTokenSource connectCts = new CancellationTokenSource(timeout))
		{
			int result2;
			await using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
			{
				Host = ip,
				Port = port,
				TimeoutMs = timeout
			}, connectCts.Token))
			{
				bool skipJrpc = settings.Quick || settings.NoJrpc;
				bool skipDrives = settings.Quick || settings.NoDrives;
				bool skipUsers = settings.Quick || settings.NoUsers;
				XbdmConsoleInfo info = await client.GetConsoleInfoAsync(CancellationToken.None);
				string dmVersion = null;
				try
				{
					dmVersion = await client.GetDmVersionAsync(CancellationToken.None);
					dmVersion = dmVersion?.Trim();
				}
				catch
				{
				}
				string xbdmFlavor = null;
				try
				{
					xbdmFlavor = (await client.SendCommandAsync("whomadethis", CancellationToken.None)).Message.Trim();
				}
				catch
				{
				}
				string runningXex = null;
				try
				{
					runningXex = await client.GetRunningXexPathAsync(null, CancellationToken.None);
				}
				catch
				{
				}
				bool? jrpcAvailable = null;
				uint? titleId = null;
				uint? dashVersion = null;
				string motherboard = null;
				string smcVersion = null;
				string cpuKey = null;
				uint? cpuTemp = null;
				uint? gpuTemp = null;
				uint? edramTemp = null;
				uint? mbTemp = null;
				IReadOnlyList<XbdmDriveEntry> drives = null;
				IReadOnlyList<XbdmUserInfo> users = null;
				List<ProfileHelpers.F3ProfileInfo> f3Profiles = null;
				if (!skipJrpc)
				{
					try
					{
						Jrpc2Client jrpc = new Jrpc2Client(client);
						titleId = await jrpc.GetTitleIdAsync(CancellationToken.None);
						dashVersion = await jrpc.GetDashboardVersionAsync(CancellationToken.None);
						motherboard = await jrpc.GetMotherboardTypeAsync(CancellationToken.None);
						cpuKey = await jrpc.GetCpuKeyAsync(CancellationToken.None);
						cpuTemp = await jrpc.GetTemperatureAsync(SensorType.CPU, CancellationToken.None);
						gpuTemp = await jrpc.GetTemperatureAsync(SensorType.GPU, CancellationToken.None);
						edramTemp = await jrpc.GetTemperatureAsync(SensorType.EDRAM, CancellationToken.None);
						mbTemp = await jrpc.GetTemperatureAsync(SensorType.MotherBoard, CancellationToken.None);
						jrpcAvailable = true;
					}
					catch
					{
						jrpcAvailable = false;
					}
					if (jrpcAvailable == true)
					{
						try
						{
							smcVersion = await HardwareHelpers.GetSmcVersionAsync(client, CancellationToken.None);
						}
						catch
						{
						}
					}
				}
				if (!skipDrives)
				{
					try
					{
						drives = await client.GetDrivesAsync(includeSize: true, CancellationToken.None);
					}
					catch
					{
					}
				}
				if (!skipUsers)
				{
					try
					{
						users = await client.GetUserListAsync(CancellationToken.None);
					}
					catch
					{
					}
				}
				ProfileHelpers.XamUserInfo xamUser = null;
				if (!skipUsers && (users == null || users.Count == 0))
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
				XbdmUserInfo xbdmUserInfo = users?.FirstOrDefault((XbdmUserInfo u) => u.SignInState.HasValue && u.SignInState.Value != 0 && !string.IsNullOrWhiteSpace(u.Gamertag)) ?? users?.FirstOrDefault((XbdmUserInfo u) => !string.IsNullOrWhiteSpace(u.Gamertag));
				string signedInUser = xbdmUserInfo?.Gamertag;
				string signedInXuid = (((object)xbdmUserInfo != null && xbdmUserInfo.Xuid.HasValue) ? $"0x{xbdmUserInfo.Xuid.Value:X16}" : null);
				uint? signInStateValue = xbdmUserInfo?.SignInState;
				if (string.IsNullOrWhiteSpace(signedInUser) && !skipUsers)
				{
					try
					{
						f3Profiles = await ProfileHelpers.TryGetF3ProfilesAsync(ip);
					}
					catch
					{
					}
				}
				ProfileHelpers.F3ProfileInfo f3ProfileInfo = f3Profiles?.FirstOrDefault((ProfileHelpers.F3ProfileInfo p) => p.SignedIn == 1 && !string.IsNullOrWhiteSpace(p.Gamertag));
				if (string.IsNullOrWhiteSpace(signedInUser) && f3ProfileInfo != null)
				{
					signedInUser = f3ProfileInfo.Gamertag;
					if (signedInXuid == null)
					{
						signedInXuid = f3ProfileInfo.Xuid;
					}
				}
				if (string.IsNullOrWhiteSpace(signedInUser) && xamUser != null)
				{
					signedInUser = xamUser.Gamertag;
					if (signedInXuid == null)
					{
						signedInXuid = xamUser.Xuid;
					}
				}
				uint? num = signInStateValue;
				if (!num.HasValue)
				{
					signInStateValue = xamUser?.SignInState;
				}
				bool isSignedIn = !string.IsNullOrWhiteSpace(signedInUser) || (signInStateValue.HasValue && signInStateValue.Value != 0);
				string signInStateText = (signInStateValue.HasValue ? HardwareHelpers.DescribeSignInState(signInStateValue.Value) : (isSignedIn ? "Signed in" : "Not signed in"));
				string titleName = null;
				if (titleId.HasValue && TitleIdDatabase.Instance.TryResolve(titleId.Value, null, out TitleIdEntry entry))
				{
					titleName = entry?.Name;
				}
				titleName = ProfileHelpers.TryGetTitleFallbackName(titleId, runningXex, titleName);
				CliConfig cfg = CliConfig.Load();
				string pendingModuleText = null;
				if (cfg.PendingModuleOperation != null && !string.IsNullOrWhiteSpace(cfg.PendingModuleOperation.Action) && !string.IsNullOrWhiteSpace(cfg.PendingModuleOperation.ModuleName))
				{
					string action = cfg.PendingModuleOperation.Action;
					string moduleName = cfg.PendingModuleOperation.ModuleName;
					pendingModuleText = action + " " + moduleName;
					bool? satisfied = null;
					try
					{
						bool flag = (await client.GetModulesAsync(includeSections: false, CancellationToken.None)).Any((XbdmModuleInfo m) => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
						satisfied = (string.Equals(action, "load", StringComparison.OrdinalIgnoreCase) ? flag : (!flag));
						if (satisfied.Value)
						{
							cfg.PendingModuleOperation = null;
							cfg.Save();
							pendingModuleText = null;
						}
					}
					catch
					{
					}
					if (pendingModuleText != null)
					{
						string text = ((!satisfied.HasValue) ? (action + " " + moduleName) : ((satisfied != true) ? (action + " " + moduleName + " (pending)") : (action + " " + moduleName + " (verified)")));
						pendingModuleText = text;
					}
				}
				if (settings.Json)
				{
					CliOutput.EmitJson(new
					{
						Target = new { ip, port },
						Info = info,
						DmVersion = dmVersion,
						XbdmFlavor = xbdmFlavor,
						RunningXex = runningXex,
						TitleId = (titleId.HasValue ? $"0x{titleId.Value:X8}" : null),
						TitleName = titleName,
						Dashboard = dashVersion,
						Motherboard = motherboard,
						SmcVersion = smcVersion,
						CpuKey = cpuKey,
						Temps = new { cpuTemp, gpuTemp, edramTemp, mbTemp },
						JrpcAvailable = jrpcAvailable,
						Drives = drives,
						Users = users,
						SignedIn = isSignedIn,
						SignInState = signInStateText,
						Gamertag = signedInUser,
						SignedInXuid = signedInXuid,
						PendingModuleOperation = pendingModuleText
					});
					result2 = 0;
				}
				else
				{
					string text2 = ((!jrpcAvailable.HasValue) ? "[grey70]skipped[/]" : (jrpcAvailable.Value ? "[springgreen3_1]online[/]" : "[red1]unavailable[/]"));
					AnsiConsole.Write(new Rule("[bold deepskyblue1]Status[/]").RuleStyle("silver"));
					Table table = CliOutput.CreateTable();
					table.AddColumn(new TableColumn("[bold white]Field[/]"));
					table.AddColumn(new TableColumn("[bold white]Value[/]"));
					table.AddRow("[white]IP[/]", "[cyan1]" + ip + "[/]");
					table.AddRow("[white]Port[/]", $"[deepskyblue1]{port}[/]");
					table.AddRow("[white]Console ID[/]", FormatValue(info.ConsoleId, "gold1"));
					table.AddRow("[white]Debug Name[/]", FormatValue(info.DebugName, "springgreen3_1"));
					table.AddRow("[white]Execution State[/]", FormatExecutionState(info.ExecutionState));
					table.AddRow("[white]Title IP[/]", FormatValue(info.TitleIp, "cyan1"));
					table.AddRow("[white]Process ID[/]", info.ProcessId.HasValue ? $"[mediumpurple3]0x{info.ProcessId.Value:X8}[/]" : "[grey70]unknown[/]");
					table.AddRow("[white]Title ID[/]", titleId.HasValue ? $"[deepskyblue1]0x{titleId.Value:X8}[/]" : FormatSkipped(skipJrpc));
					table.AddRow("[white]Title Name[/]", FormatValue(titleName, "springgreen3_1", FormatSkipped(skipJrpc)));
					table.AddRow("[white]Running XEX[/]", FormatValue(runningXex, "springgreen3_1"));
					table.AddRow("[white]DM Version[/]", FormatValue(dmVersion, "cyan1"));
					table.AddRow("[white]XBDM Flavor[/]", FormatValue(xbdmFlavor, "mediumpurple3"));
					table.AddRow("[white]JRPC2[/]", text2);
					table.AddRow("[white]Dashboard[/]", dashVersion.HasValue ? $"[gold1]{dashVersion.Value}[/]" : FormatSkipped(skipJrpc));
					table.AddRow("[white]Motherboard[/]", FormatValue(motherboard, "deepskyblue1", FormatSkipped(skipJrpc)));
					table.AddRow("[white]SMC Version[/]", FormatValue(smcVersion, "deepskyblue1", FormatSkipped(skipJrpc)));
					table.AddRow("[white]CPU Key[/]", FormatValue(cpuKey, "gold1", FormatSkipped(skipJrpc)));
					table.AddRow("[white]Signed In[/]", skipUsers ? FormatSkipped(skipped: true) : (isSignedIn ? "[springgreen3_1]Yes[/]" : "[red1]No[/]"));
					table.AddRow("[white]Sign-In State[/]", skipUsers ? FormatSkipped(skipped: true) : ("[gold1]" + Markup.Escape(signInStateText) + "[/]"));
					table.AddRow("[white]Gamertag[/]", FormatValue(signedInUser, "springgreen3_1", skipUsers ? FormatSkipped(skipped: true) : "[grey70]none[/]"));
					table.AddRow("[white]Signed In XUID[/]", FormatValue(signedInXuid, "gold1", skipUsers ? FormatSkipped(skipped: true) : "[grey70]none[/]"));
					if (!string.IsNullOrWhiteSpace(pendingModuleText))
					{
						table.AddRow("[white]Pending Module[/]", FormatValue(pendingModuleText, "gold1"));
					}
					AnsiConsole.Write(table);
					if (cpuTemp.HasValue || gpuTemp.HasValue || edramTemp.HasValue || mbTemp.HasValue)
					{
						AnsiConsole.Write(new Rule("[bold deepskyblue1]Temps (C)[/]").RuleStyle("silver"));
						Table table2 = CliOutput.CreateTable();
						table2.AddColumn(new TableColumn("[bold deepskyblue1]CPU[/]"));
						table2.AddColumn(new TableColumn("[bold deepskyblue1]GPU[/]"));
						table2.AddColumn(new TableColumn("[bold deepskyblue1]EDRAM[/]"));
						table2.AddColumn(new TableColumn("[bold deepskyblue1]Board[/]"));
						table2.AddRow(FormatTemperature(cpuTemp), FormatTemperature(gpuTemp), FormatTemperature(edramTemp), FormatTemperature(mbTemp));
						AnsiConsole.Write(table2);
					}
					if (drives != null && drives.Count > 0)
					{
						AnsiConsole.Write(new Rule("[bold deepskyblue1]Drives[/]").RuleStyle("silver"));
						Table table3 = CliOutput.CreateTable();
						table3.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
						table3.AddColumn(new TableColumn("[bold white]Aliases[/]"));
						table3.AddColumn(new TableColumn("[bold deepskyblue1]Total[/]"));
						table3.AddColumn(new TableColumn("[bold cyan1]Free[/]"));
						table3.AddColumn(new TableColumn("[bold gold1]Used[/]"));
						foreach (DriveGroup item in GroupDrives(drives))
						{
							Markup.Escape(item.DisplayName);
							string text3 = ((item.Aliases.Count > 0) ? ("[silver]" + Markup.Escape(string.Join(", ", item.Aliases)) + "[/]") : "[grey70]-[/]");
							table3.AddRow(FormatDriveName(item), text3, "[deepskyblue1]" + FormatBytes(item.TotalBytes) + "[/]", "[cyan1]" + FormatBytes(item.FreeBytes) + "[/]", FormatDriveUsage(item.TotalBytes, item.FreeBytes));
						}
						AnsiConsole.Write(table3);
						List<XbdmDriveEntry> list = drives.Where((XbdmDriveEntry d) => d.Name.StartsWith("Usb", StringComparison.OrdinalIgnoreCase)).ToList();
						if (list.Count > 0)
						{
							AnsiConsole.Write(new Rule("[bold deepskyblue1]Connected USB[/]").RuleStyle("silver"));
							Table table4 = CliOutput.CreateTable();
							table4.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
							table4.AddColumn(new TableColumn("[bold deepskyblue1]Total[/]"));
							table4.AddColumn(new TableColumn("[bold cyan1]Free[/]"));
							table4.AddColumn(new TableColumn("[bold gold1]Used[/]"));
							foreach (XbdmDriveEntry item2 in list)
							{
								table4.AddRow("[deepskyblue1]" + Markup.Escape(item2.Name) + "[/]", "[deepskyblue1]" + FormatBytes(item2.TotalBytes) + "[/]", "[cyan1]" + FormatBytes(item2.FreeBytes) + "[/]", FormatDriveUsage(item2.TotalBytes, item2.FreeBytes));
							}
							AnsiConsole.Write(table4);
						}
					}
					result2 = 0;
				}
			}
			return result2;
		}
		static string FormatValue(string? value, string color, string? fallback = null)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				return $"[{color}]{Markup.Escape(value)}[/]";
			}
			return fallback ?? "[grey70]unknown[/]";
		}
	}

	private static string FormatBytes(ulong? value)
	{
		if (!value.HasValue)
		{
			return "unknown";
		}
		double num = value.Value;
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		return $"{num:0.##} {array[num2]}";
	}

	private static string FormatSkipped(bool skipped)
	{
		if (!skipped)
		{
			return "[grey70]unknown[/]";
		}
		return "[grey70]skipped[/]";
	}

	private static string FormatExecutionState(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "[grey70]unknown[/]";
		}
		switch (value.Trim().ToLowerInvariant())
		{
		case "start":
		case "running":
			return "[springgreen3_1]" + Markup.Escape(value) + "[/]";
		case "break":
		case "stopped":
		case "stop":
			return "[gold1]" + Markup.Escape(value) + "[/]";
		case "reboot_title":
		case "reboot":
			return "[mediumpurple3]" + Markup.Escape(value) + "[/]";
		default:
			return "[cyan]" + Markup.Escape(value) + "[/]";
		}
	}

	private static string FormatTemperature(uint? value)
	{
		if (!value.HasValue)
		{
			return "[grey70]unknown[/]";
		}
		string text;
		switch (value.Value)
		{
		case 0u:
		case 1u:
		case 2u:
		case 3u:
		case 4u:
		case 5u:
		case 6u:
		case 7u:
		case 8u:
		case 9u:
		case 10u:
		case 11u:
		case 12u:
		case 13u:
		case 14u:
		case 15u:
		case 16u:
		case 17u:
		case 18u:
		case 19u:
		case 20u:
		case 21u:
		case 22u:
		case 23u:
		case 24u:
		case 25u:
		case 26u:
		case 27u:
		case 28u:
		case 29u:
		case 30u:
		case 31u:
		case 32u:
		case 33u:
		case 34u:
		case 35u:
		case 36u:
		case 37u:
		case 38u:
		case 39u:
		case 40u:
		case 41u:
		case 42u:
		case 43u:
		case 44u:
			text = "springgreen3_1";
			break;
		case 45u:
		case 46u:
		case 47u:
		case 48u:
		case 49u:
		case 50u:
		case 51u:
		case 52u:
		case 53u:
		case 54u:
		case 55u:
		case 56u:
		case 57u:
		case 58u:
		case 59u:
			text = "yellow1";
			break;
		case 60u:
		case 61u:
		case 62u:
		case 63u:
		case 64u:
		case 65u:
		case 66u:
		case 67u:
		case 68u:
		case 69u:
			text = "darkorange";
			break;
		default:
			text = "red1";
			break;
		}
		string value2 = text;
		return $"[{value2}]{value.Value}[/]";
	}

	private static string FormatDriveName(DriveGroup drive)
	{
		string text = Markup.Escape(drive.DisplayName);
		return drive.Family switch
		{
			"internal" => "[springgreen3_1]" + text + "[/]", 
			"usb" => "[deepskyblue1]" + text + "[/]", 
			"systemext" => "[gold1]" + text + "[/]", 
			"system" => "[mediumpurple3]" + text + "[/]", 
			_ => "[cyan]" + text + "[/]", 
		};
	}

	private static string FormatDriveUsage(ulong? totalBytes, ulong? freeBytes)
	{
		if (!totalBytes.HasValue || !freeBytes.HasValue || totalBytes.Value == 0L || freeBytes.Value > totalBytes.Value)
		{
			return "[grey70]unknown[/]";
		}
		double num = (double)(totalBytes.Value - freeBytes.Value) / (double)totalBytes.Value * 100.0;
		string text = ((num < 60.0) ? "springgreen3_1" : ((!(num < 85.0)) ? "red1" : "gold1"));
		string value = text;
		return $"[{value}]{num:0.#}%[/]";
	}

	private static IReadOnlyList<DriveGroup> GroupDrives(IReadOnlyList<XbdmDriveEntry> drives)
	{
		List<DriveGroup> list = new List<DriveGroup>();
		foreach (XbdmDriveEntry drive in drives.OrderBy(GetDriveSortKey).ThenBy<XbdmDriveEntry, string>((XbdmDriveEntry d) => d.Name, StringComparer.OrdinalIgnoreCase))
		{
			string family = GetDriveFamily(drive.Name);
			DriveGroup driveGroup = list.FirstOrDefault((DriveGroup g) => g.Family == family && g.TotalBytes == drive.TotalBytes && g.FreeBytes == drive.FreeBytes);
			if (driveGroup == null)
			{
				list.Add(new DriveGroup(family, GetDriveDisplayName(drive.Name), drive.Name, drive.TotalBytes, drive.FreeBytes));
				continue;
			}
			driveGroup.Aliases.Add(drive.Name);
			driveGroup.Aliases.Sort(StringComparer.OrdinalIgnoreCase);
		}
		foreach (DriveGroup group in list)
		{
			group.Aliases.RemoveAll((string alias) => string.Equals(alias, group.DisplayName, StringComparison.OrdinalIgnoreCase));
		}
		return list;
	}

	private static string GetDriveSortKey(XbdmDriveEntry drive)
	{
		return GetDriveFamily(drive.Name) switch
		{
			"internal" => "0", 
			"usb" => "1", 
			"systemext" => "2", 
			"system" => "3", 
			_ => "9", 
		};
	}

	private static string GetDriveFamily(string name)
	{
		switch (NormalizeDriveName(name))
		{
		case "game":
		case "hdd1":
		case "devkit":
		case "hdd":
		case "d":
			return "internal";
		case "sysext":
			return "systemext";
		case "system":
			return "system";
		default:
			if (name.StartsWith("Usb", StringComparison.OrdinalIgnoreCase))
			{
				return "usb";
			}
			return NormalizeDriveName(name);
		}
	}

	private static string GetDriveDisplayName(string name)
	{
		return GetDriveFamily(name) switch
		{
			"internal" => "Internal", 
			"systemext" => "SysExt:", 
			"system" => "System:", 
			_ => name, 
		};
	}

	private static string NormalizeDriveName(string name)
	{
		return name.Trim().TrimEnd(':').ToLowerInvariant();
	}
}
