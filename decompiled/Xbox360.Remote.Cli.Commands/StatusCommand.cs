using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Color = Spectre.Console.Color;

namespace Xbox360.Remote.Cli.Commands;

public sealed class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
	private const int LargeBannerMinimumWidth = 220;

	private const int FourCardLayoutMinimumWidth = 230;

	private static readonly string[] StatusBannerBitmapLines = new string[12]
	{
		"11100000000001110000000000000000001111110000001110000000000111111111",
		"11110000000011110000000000000000011111111000001110000000000111111111",
		"01111000000111100000000000000000111100000000001100000000000000111000",
		"00111100001111000011111110000001110000000000001100000000000000111000",
		"00011110011110000111111111000001110000000000001100000000000000111000",
		"00001111111100001110000011100001100000000000001100000000000000111000",
		"00001111111100001111111111000001100000000000001100000000000000111000",
		"00011110011110001110000000000001110000000000001100000000000000111000",
		"00111100001111001111111111000000111100000000001111111110000000111000",
		"01111000000111100111111110000000011111111000001111111110000111111111",
		"11110000000011110000000000000000001111110000000111111110000111111111",
		"11100000000001110000000000000000000000000000000000000000000000000000"
	};

	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--quick")]
		[LocalizedDescription("Skip JRPC2, drive, and user checks for a fast snapshot.")]
		public bool Quick { get; init; }

		[CommandOption("--no-jrpc")]
		[LocalizedDescription("Skip JRPC2 queries (temps, CPU key, dashboard, title id).")]
		public bool NoJrpc { get; init; }

		[CommandOption("--no-drives")]
		[LocalizedDescription("Skip drive and USB size reporting.")]
		public bool NoDrives { get; init; }

		[CommandOption("--no-users")]
		[LocalizedDescription("Skip user list and signed-in detection.")]
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
				UserSnapshot userSnapshot = null;
				if (!skipUsers)
				{
					userSnapshot = await ResolveUserSnapshotAsync(client, ip, port, timeout);
				}
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
				string signedInUser = null;
				string signedInXuid = null;
				uint? signInStateValue = null;
				if (!skipUsers && userSnapshot != null)
				{
					users = userSnapshot.Users;
					signedInUser = userSnapshot.Gamertag;
					signedInXuid = userSnapshot.Xuid;
					signInStateValue = userSnapshot.SignInState;
					if (string.IsNullOrWhiteSpace(signedInUser))
					{
						try
						{
							f3Profiles = await ProfileHelpers.TryGetF3ProfilesAsync(ip);
						}
						catch
						{
						}
						ProfileHelpers.F3ProfileInfo f3ProfileInfo = f3Profiles?.FirstOrDefault((ProfileHelpers.F3ProfileInfo p) => p.SignedIn == 1 && !string.IsNullOrWhiteSpace(p.Gamertag));
						if (f3ProfileInfo != null)
						{
							signedInUser = TrimOrNull(f3ProfileInfo.Gamertag) ?? signedInUser;
							signedInXuid = TrimOrNull(f3ProfileInfo.Xuid) ?? signedInXuid;
						}
					}
				}
				bool? signedIn = ResolveSignedInStatus(signInStateValue, signedInUser);
				string signInStateText = ResolveSignInStateText(signInStateValue, signedInUser);
				string runningXexDisplay = NormalizeRunningXexPath(runningXex);
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
						RunningXex = runningXexDisplay,
						RunningXexRaw = runningXex,
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
						SignedIn = signedIn,
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
					int width = AnsiConsole.Profile.Width;
					string statusIdentity = BuildStatusIdentity(signedInUser, motherboard, info.DebugName, ip, width);
					AnsiConsole.WriteLine();
					WriteStatusBanner();
					WriteStatusIdentity(statusIdentity);
					AnsiConsole.WriteLine();
					Table table = CreateCardTable("Field", "Value", 12);
					AddStatusRow(table, "IP", "[cyan1]" + ip + "[/]");
					AddStatusRow(table, "Port", $"[deepskyblue1]{port}[/]");
					AddStatusRow(table, "Console ID", FormatValue(info.ConsoleId, "gold1"));
					AddStatusRow(table, "Debug Name", FormatValue(info.DebugName, "springgreen3_1"));
					AddStatusRow(table, "Motherboard", FormatValue(motherboard, "deepskyblue1", FormatSkipped(skipJrpc)));
					AddStatusRow(table, "Dashboard", dashVersion.HasValue ? $"[gold1]{dashVersion.Value}[/]" : FormatSkipped(skipJrpc));
					AddStatusRow(table, "DM Version", FormatValue(dmVersion, "cyan1"));
					if (skipJrpc || !IsEffectivelyUnknown(smcVersion))
					{
						AddStatusRow(table, "SMC Version", FormatValue(smcVersion, "deepskyblue1", FormatSkipped(skipJrpc)));
					}
					Table table2 = CreateCardTable("Field", "Value", 14);
					AddStatusRow(table2, "State", FormatExecutionState(info.ExecutionState));
					if (!IsSameValue(info.TitleIp, ip))
					{
						AddStatusRow(table2, "Title IP", FormatValue(info.TitleIp, "cyan1"));
					}
					AddStatusRow(table2, "Process ID", info.ProcessId.HasValue ? $"[mediumpurple3]0x{info.ProcessId.Value:X8}[/]" : "[grey70]unknown[/]");
					AddStatusRow(table2, "Title ID", titleId.HasValue ? $"[deepskyblue1]0x{titleId.Value:X8}[/]" : FormatSkipped(skipJrpc));
					AddStatusRow(table2, "Title Name", FormatValue(titleName, "springgreen3_1", FormatSkipped(skipJrpc)));
					AddStatusRow(table2, "Running XEX", FormatValue(runningXexDisplay, "springgreen3_1"));
					AddStatusRow(table2, "XBDM Flavor", FormatValue(xbdmFlavor, "mediumpurple3"));
					AddStatusRow(table2, "JRPC2", text2);
					AddStatusRow(table2, "CPU Key", FormatValue(cpuKey, "gold1", FormatSkipped(skipJrpc)));
					AddStatusRow(table2, "Signed In", skipUsers ? FormatSkipped(skipped: true) : FormatSignedInStatus(signedIn));
					AddStatusRow(table2, "Sign-In State", skipUsers ? FormatSkipped(skipped: true) : ("[gold1]" + Markup.Escape(TrimOrNull(signInStateText) ?? signInStateText) + "[/]"));
					AddStatusRow(table2, "Gamertag", FormatValue(signedInUser, "springgreen3_1", GetUserValueFallback(skipUsers, signedIn)));
					AddStatusRow(table2, "Signed In XUID", FormatValue(signedInXuid, "gold1", GetUserValueFallback(skipUsers, signedIn)));
					if (!string.IsNullOrWhiteSpace(pendingModuleText))
					{
						AddStatusRow(table2, "Pending Module", FormatValue(pendingModuleText, "gold1"));
					}
					Table table3 = CreateCardTable("Sensor", "Reading", 9);
					AddStatusRow(table3, "CPU", FormatTemperatureBar(cpuTemp));
					AddStatusRow(table3, "GPU", FormatTemperatureBar(gpuTemp));
					AddStatusRow(table3, "EDRAM", FormatTemperatureBar(edramTemp));
					AddStatusRow(table3, "Board", FormatTemperatureBar(mbTemp));
					Table table4 = CreateCardTable("Drive", "Usage", 13);
					if (drives != null && drives.Count > 0)
					{
						foreach (DriveGroup item in GroupDrives(drives))
						{
							table4.AddRow(FormatDriveName(item), FormatDriveCardValue(item));
						}
					}
					else
					{
						AddStatusRow(table4, "Storage", FormatSkipped(skipDrives));
					}
					Grid grid = new Grid();
					if (width >= FourCardLayoutMinimumWidth)
					{
						grid.AddColumn();
						grid.AddColumn();
						grid.AddColumn();
						grid.AddColumn();
						grid.AddRow(table, table2, table3, table4);
						AnsiConsole.Write(grid);
					}
					else
					{
						AnsiConsole.Write(table);
						AnsiConsole.WriteLine();
						AnsiConsole.Write(table2);
						AnsiConsole.WriteLine();
						AnsiConsole.Write(table3);
						AnsiConsole.WriteLine();
						AnsiConsole.Write(table4);
					}
					result2 = 0;
				}
			}
			return result2;
		}
		static string FormatValue(string? value, string color, string? fallback = null)
		{
			string text = TrimOrNull(value);
			if (!string.IsNullOrWhiteSpace(text))
			{
				return $"[{color}]{Markup.Escape(text)}[/]";
			}
			return fallback ?? "[grey70]unknown[/]";
		}
	}

	private sealed class UserSnapshot
	{
		public IReadOnlyList<XbdmUserInfo> Users { get; init; } = Array.Empty<XbdmUserInfo>();

		public string? Gamertag { get; init; }

		public string? Xuid { get; init; }

		public uint? SignInState { get; init; }
	}

	private static void AddStatusRow(Table table, string field, string value)
	{
		table.AddRow("[white]" + Markup.Escape(field) + "[/]", value);
	}

	private static void WriteStatusBanner()
	{
		if (AnsiConsole.Profile.Width < LargeBannerMinimumWidth)
		{
			AnsiConsole.Write(new Align(new Markup("[bold deepskyblue1]XeCLI[/]"), Spectre.Console.HorizontalAlignment.Center));
			AnsiConsole.WriteLine();
			return;
		}
		Style style = new Style(Color.DeepSkyBlue1);
		foreach (string statusBannerBitmapLine in StatusBannerBitmapLines)
		{
			string rendered = RenderBannerLine(statusBannerBitmapLine);
			AnsiConsole.Write(new Align(new Text(rendered, style), Spectre.Console.HorizontalAlignment.Center));
			AnsiConsole.WriteLine();
		}
		AnsiConsole.WriteLine();
	}

	private static void WriteStatusIdentity(string statusIdentity)
	{
		string text = "XeCLI Status";
		if (!string.IsNullOrWhiteSpace(statusIdentity))
		{
			text = text + " · " + statusIdentity;
		}
		AnsiConsole.Write(new Align(new Markup("[bold #ff79c6]" + Markup.Escape(text) + "[/]"), Spectre.Console.HorizontalAlignment.Center));
		AnsiConsole.WriteLine();
	}

	private static string FormatSignedInStatus(bool? signedIn)
	{
		if (!signedIn.HasValue)
		{
			return "[grey70]not detected[/]";
		}
		if (!signedIn.Value)
		{
			return "[red1]No[/]";
		}
		return "[springgreen3_1]Yes[/]";
	}

	private static string GetUserValueFallback(bool skipped, bool? signedIn)
	{
		if (skipped)
		{
			return FormatSkipped(skipped: true);
		}
		if (signedIn == false)
		{
			return "[grey70]none[/]";
		}
		return "[grey70]not detected[/]";
	}

	private static string BuildStatusIdentity(string? signedInUser, string? motherboard, string? debugName, string ip, int width)
	{
		List<string> list = new string[4]
		{
			TrimOrNull(signedInUser),
			TrimOrNull(motherboard),
			TrimOrNull(debugName),
			ip
		}.Where((string part) => !string.IsNullOrWhiteSpace(part)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		int num = Math.Max(32, width - 20);
		while (list.Count > 1 && string.Join(" | ", list).Length > num)
		{
			list.RemoveAt(0);
		}
		return string.Join(" | ", list);
	}

	private static Table CreateCardTable(string leftHeader, string rightHeader, int leftWidth)
	{
		Table table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey35).Expand();
		table.AddColumn(new TableColumn("[bold grey70]" + Markup.Escape(leftHeader) + "[/]").Width(leftWidth));
		table.AddColumn(new TableColumn("[bold white]" + Markup.Escape(rightHeader) + "[/]"));
		return table;
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
		string text = TrimOrNull(value);
		if (string.IsNullOrWhiteSpace(text))
		{
			return "[grey70]unknown[/]";
		}
		switch (text.ToLowerInvariant())
		{
		case "start":
		case "running":
			return "[bold springgreen3_1]● RUNNING[/]";
		case "break":
		case "stopped":
		case "stop":
			return "[bold gold1]● STOPPED[/]";
		case "reboot_title":
		case "reboot":
			return "[bold mediumpurple3]● REBOOTING[/]";
		default:
			return "[bold cyan1]● " + Markup.Escape(text.ToUpperInvariant()) + "[/]";
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

	private static string FormatTemperatureBar(uint? value)
	{
		if (!value.HasValue)
		{
			return "[grey70]unknown[/]";
		}
		int num = (int)Math.Clamp(Math.Round((double)value.Value / 12.5), 0.0, 8.0);
		string text = new string('▰', num);
		string text2 = new string('▱', 8 - num);
		string value2 = ((value.Value >= 85) ? "red1" : ((value.Value >= 70) ? "gold1" : "springgreen3_1"));
		return $"[bold white]{value.Value}°C[/]  [{value2}]{text}[/][grey35]{text2}[/]";
	}

	private static string FormatDriveCardValue(DriveGroup drive)
	{
		string text = "[cyan1]" + FormatBytes(drive.FreeBytes) + "[/] [grey58]free of[/] [deepskyblue1]" + FormatBytes(drive.TotalBytes) + "[/]";
		return text + "\n" + FormatDriveUsage(drive.TotalBytes, drive.FreeBytes);
	}

	private static string FormatDriveName(DriveGroup drive)
	{
		string text = Markup.Escape(drive.DisplayName);
		return drive.Family switch
		{
			"internal" => "[springgreen3_1]" + text + "[/]", 
			"memoryunit" => "[mediumpurple3]" + text + "[/]", 
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
		int num2 = (int)Math.Clamp(Math.Round(num / 12.5), 0.0, 8.0);
		string text = new string('▰', num2);
		string text2 = new string('▱', 8 - num2);
		string value = ((num < 60.0) ? "springgreen3_1" : ((!(num < 85.0)) ? "red1" : "gold1"));
		return $"[{value}]{num:0.#}% used[/]  [{value}]{text}[/][grey35]{text2}[/]";
	}

	private static bool IsEffectivelyUnknown(string? value)
	{
		string text = TrimOrNull(value);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return string.Equals(text, "unknown", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsSameValue(string? left, string? right)
	{
		return string.Equals(TrimOrNull(left), TrimOrNull(right), StringComparison.OrdinalIgnoreCase);
	}

	private static bool? ResolveSignedInStatus(uint? signInStateValue, string? signedInUser)
	{
		if (!string.IsNullOrWhiteSpace(TrimOrNull(signedInUser)))
		{
			return true;
		}
		if (signInStateValue.HasValue)
		{
			return signInStateValue.Value != 0;
		}
		return null;
	}

	private static string ResolveSignInStateText(uint? signInStateValue, string? signedInUser)
	{
		if (signInStateValue.HasValue)
		{
			return HardwareHelpers.DescribeSignInState(signInStateValue.Value);
		}
		if (!string.IsNullOrWhiteSpace(TrimOrNull(signedInUser)))
		{
			return "Signed in";
		}
		return "not detected";
	}

	private static string? NormalizeRunningXexPath(string? value)
	{
		string text = TrimOrNull(value);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string text2 = text.Replace('/', '\\').TrimEnd('\\');
		int num = text2.LastIndexOf('\\');
		if (num >= 0 && num < text2.Length - 1)
		{
			return text2.Substring(num + 1);
		}
		return text2;
	}

	private static async Task<UserSnapshot> ResolveUserSnapshotAsync(XbdmClient client, string ip, int port, int timeout)
	{
		bool allowXamProbe = string.Equals(Environment.GetEnvironmentVariable("XECLI_ALLOW_XAM_PROBE"), "1", StringComparison.OrdinalIgnoreCase);
		IReadOnlyList<XbdmUserInfo> users = Array.Empty<XbdmUserInfo>();
		try
		{
			users = await client.GetUserListAsync(CancellationToken.None);
		}
		catch
		{
		}
		ProfileHelpers.XamUserInfo xamUser = null;
		XbdmUserInfo xbdmUserInfo = users.FirstOrDefault((XbdmUserInfo u) => u.SignInState.HasValue && u.SignInState.Value != 0 && !string.IsNullOrWhiteSpace(u.Gamertag)) ?? users.FirstOrDefault((XbdmUserInfo u) => !string.IsNullOrWhiteSpace(u.Gamertag));
		bool needsXamProbe = xbdmUserInfo == null || string.IsNullOrWhiteSpace(xbdmUserInfo.Gamertag) || !xbdmUserInfo.SignInState.HasValue || xbdmUserInfo.SignInState.Value == 0;
		if (allowXamProbe && needsXamProbe)
		{
			try
			{
				xamUser = await ProfileHelpers.TryGetSignedInXamUserAsync(client, CancellationToken.None);
				if (xamUser == null)
				{
					xamUser = await ProfileHelpers.TryGetSignedInXamUserAsync(ip, port, timeout, CancellationToken.None);
				}
				if (xamUser != null && users.Count == 0)
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
		xbdmUserInfo = users.FirstOrDefault((XbdmUserInfo u) => u.SignInState.HasValue && u.SignInState.Value != 0 && !string.IsNullOrWhiteSpace(u.Gamertag)) ?? users.FirstOrDefault((XbdmUserInfo u) => !string.IsNullOrWhiteSpace(u.Gamertag));
		string gamertag = TrimOrNull(xbdmUserInfo?.Gamertag);
		string xuid = (((object)xbdmUserInfo != null && xbdmUserInfo.Xuid.HasValue) ? $"0x{xbdmUserInfo.Xuid.Value:X16}" : null);
		uint? signInState = xbdmUserInfo?.SignInState;
		if (string.IsNullOrWhiteSpace(gamertag) && xamUser != null)
		{
			gamertag = TrimOrNull(xamUser.Gamertag) ?? gamertag;
			xuid = TrimOrNull(xamUser.Xuid) ?? xuid;
			signInState ??= xamUser.SignInState;
		}
		if (allowXamProbe && (string.IsNullOrWhiteSpace(gamertag) || !signInState.HasValue || signInState.Value == 0) && xamUser == null)
		{
			try
			{
				await Task.Delay(80, CancellationToken.None);
				ProfileHelpers.XamUserInfo xamUserInfo2 = await ProfileHelpers.TryGetSignedInXamUserAsync(client, CancellationToken.None);
				if (xamUserInfo2 != null)
				{
					gamertag = TrimOrNull(xamUserInfo2.Gamertag) ?? gamertag;
					xuid = TrimOrNull(xamUserInfo2.Xuid) ?? xuid;
					signInState ??= xamUserInfo2.SignInState;
				}
			}
			catch
			{
			}
		}
		if ((!signInState.HasValue || signInState.Value == 0) && !string.IsNullOrWhiteSpace(gamertag))
		{
			signInState = 1u;
		}
		return new UserSnapshot
		{
			Users = users,
			Gamertag = gamertag,
			Xuid = TrimOrNull(xuid),
			SignInState = signInState
		};
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
		List<DriveGroup> list2 = list.Where(ShouldDisplayDriveGroup).ToList();
		if (list2.Count != 0)
		{
			return list2;
		}
		return list;
	}

	private static string GetDriveSortKey(XbdmDriveEntry drive)
	{
		return GetDriveFamily(drive.Name) switch
		{
			"internal" => "0", 
			"memoryunit" => "1", 
			"usb" => "2", 
			"systemext" => "7", 
			"system" => "8", 
			"update" => "9", 
			_ => "9", 
		};
	}

	private static string GetDriveFamily(string name)
	{
		string text = NormalizeDriveName(name);
		switch (text)
		{
		case "game":
		case "d":
			return "internal";
		case "hdd1":
		case "devkit":
		case "hdd":
			return "internal";
		case "mu":
		case "intmu":
		case "mmcmu":
			return "memoryunit";
		case "sysext":
			return "systemext";
		case "system":
			return "system";
		case "update":
			return "update";
		default:
			if (text.StartsWith("usb", StringComparison.OrdinalIgnoreCase))
			{
				return "usb";
			}
			if (text.StartsWith("mu", StringComparison.OrdinalIgnoreCase) || text.StartsWith("intmu", StringComparison.OrdinalIgnoreCase) || text.StartsWith("mmcmu", StringComparison.OrdinalIgnoreCase))
			{
				return "memoryunit";
			}
			return text;
		}
	}

	private static string GetDriveDisplayName(string name)
	{
		return GetDriveFamily(name) switch
		{
			"internal" => "Internal", 
			"memoryunit" => "Memory Unit", 
			"systemext" => "SysExt:", 
			"system" => "System:", 
			"update" => "Update Cache", 
			"usb" => "USB", 
			_ => name, 
		};
	}

	private static string NormalizeDriveName(string name)
	{
		return name.Trim().TrimEnd(':').ToLowerInvariant();
	}

	private static string RenderBannerLine(string bitmapLine)
	{
		return string.Create(bitmapLine.Length, bitmapLine, delegate(Span<char> destination, string source)
		{
			for (int i = 0; i < source.Length; i++)
			{
				destination[i] = ((source[i] == '1') ? '█' : ' ');
			}
		});
	}

	private static bool ShouldDisplayDriveGroup(DriveGroup drive)
	{
		return drive.Family switch
		{
			"internal" => true, 
			"memoryunit" => true, 
			"usb" => true, 
			_ => false, 
		};
	}

	private static string? TrimOrNull(string? value)
	{
		string text = value?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return null;
	}
}
