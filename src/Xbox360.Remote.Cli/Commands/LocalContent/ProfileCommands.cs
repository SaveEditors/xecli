using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Xbox360.Remote.Cli.LocalProfiles;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

internal static class ProfileCommandHelpers
{
	public static int ExecuteWithProfile(string inputPath, Func<string, ProfilePackage, int> action)
	{
		if (!LocalContentHelpers.TryResolveExistingFile(inputPath, out string fullPath, out string error))
		{
			return LocalContentHelpers.Fail(error);
		}

		ProfilePackage? profile = null;
		try
		{
			profile = new ProfilePackage(fullPath);
			return action(fullPath, profile);
		}
		catch (Exception ex)
		{
			return LocalContentHelpers.Fail(ex.Message);
		}
		finally
		{
			profile?.Dispose();
		}
	}

	public static bool TryParseTitleId(string? text, out uint titleId)
	{
		titleId = 0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string candidate = text.Trim();
		if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			candidate = candidate[2..];
		}

		return uint.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out titleId);
	}

	public static string FormatPasscode(ProfilePasscodeButton[] buttons)
	{
		if (buttons.Length == 0 || buttons.All(static button => button == ProfilePasscodeButton.None))
		{
			return "none";
		}

		return string.Join(", ", buttons.Select(static button => button.ToString()));
	}

	public static string FormatSettingValue(ProfileSettingInfo setting)
	{
		return setting.Type switch
		{
			ProfileSettingType.Binary => FormatBinaryPreview(setting.RawBytes),
			ProfileSettingType.Unicode => TruncateText(setting.Value?.ToString() ?? string.Empty, 96),
			ProfileSettingType.DateTime => setting.Value is DateTime value ? value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture) : string.Empty,
			_ => TruncateText(setting.Value?.ToString() ?? string.Empty, 96)
		};
	}

	public static object? GetSettingJsonValue(ProfileSettingInfo setting)
	{
		return setting.Type switch
		{
			ProfileSettingType.Binary => setting.RawBytes == null ? null : LocalContentHelpers.Hex(setting.RawBytes),
			ProfileSettingType.DateTime => setting.Value is DateTime value ? value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : null,
			_ => setting.Value
		};
	}

	private static string FormatBinaryPreview(byte[]? data)
	{
		if (data == null || data.Length == 0)
		{
			return string.Empty;
		}

		if (data.Length <= 32)
		{
			return LocalContentHelpers.Hex(data);
		}

		byte[] preview = data.Take(32).ToArray();
		return LocalContentHelpers.Hex(preview) + "... (" + data.Length.ToString(CultureInfo.InvariantCulture) + " bytes)";
	}

	private static string TruncateText(string value, int maxLength)
	{
		if (value.Length <= maxLength)
		{
			return value;
		}

		return value[..maxLength] + "...";
	}
}

public sealed class ProfileInfoCommand : Command<ProfileInfoCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[Description("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (fullPath, profile) =>
		{
			ProfileAccountInfo? account = profile.HasAccount ? profile.ReadAccount() : null;
			var titles = profile.HasDashboardData ? profile.ReadTitles() : Array.Empty<ProfileTitleInfo>();
			int gamerscore = titles.Sum(static title => title.CreditEarned);
			var files = profile.Entries
				.Where(static entry => !entry.IsDirectory)
				.Select(static entry => new
				{
					entry.FullPath,
					entry.Size,
					entry.Contiguous,
					entry.FirstBlockNumber,
					entry.AllocationBlocks
				})
				.ToList();

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Path = fullPath,
					SignatureType = profile.SignatureType.ToString(),
					ProfileId = LocalContentHelpers.FormatUInt64(profile.ProfileId),
					DisplayName = profile.PackageDisplayName,
					Description = profile.PackageDescription,
					HasAccount = profile.HasAccount,
					HasDashboardData = profile.HasDashboardData,
					HasPec = profile.HasPec,
					TitleCount = titles.Count,
					Gamerscore = gamerscore,
					Account = account == null
						? null
						: new
						{
							account.Gamertag,
							OnlineXuid = LocalContentHelpers.FormatUInt64(account.OnlineXuid),
							account.XboxLiveEnabled,
							account.PasswordProtected,
							account.DeveloperAccount,
							account.MembershipTier
						},
					TitleDataFiles = profile.TitleDataFiles.Select(static id => LocalContentHelpers.FormatUInt32(id)),
					Files = files
				});
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Profile Package[/]").RuleStyle("grey"));
			Table summary = CliOutput.CreateTable();
			summary.AddColumn(new TableColumn("[grey]Field[/]"));
			summary.AddColumn(new TableColumn("[white]Value[/]"));
			summary.AddRow("[grey]Path[/]", Markup.Escape(fullPath));
			summary.AddRow("[grey]Profile ID[/]", "[cyan]" + LocalContentHelpers.FormatUInt64(profile.ProfileId) + "[/]");
			summary.AddRow("[grey]Signature[/]", "[gold1]" + profile.SignatureType + "[/]");
			summary.AddRow("[grey]Display Name[/]", LocalContentHelpers.MarkupValue(profile.PackageDisplayName, "green"));
			summary.AddRow("[grey]Gamertag[/]", LocalContentHelpers.MarkupValue(account?.Gamertag, "green"));
			summary.AddRow("[grey]Online XUID[/]", account == null ? "[grey]unknown[/]" : "[gold1]" + LocalContentHelpers.FormatUInt64(account.OnlineXuid) + "[/]");
			summary.AddRow("[grey]Dashboard GPD[/]", profile.HasDashboardData ? "[green]present[/]" : "[red]missing[/]");
			summary.AddRow("[grey]Account[/]", profile.HasAccount ? "[green]present[/]" : "[red]missing[/]");
			summary.AddRow("[grey]PEC[/]", profile.HasPec ? "[green]present[/]" : "[grey]missing[/]");
			summary.AddRow("[grey]Titles[/]", "[cyan]" + titles.Count.ToString(CultureInfo.InvariantCulture) + "[/]");
			summary.AddRow("[grey]Gamerscore[/]", "[cyan]" + gamerscore.ToString(CultureInfo.InvariantCulture) + "[/]");
			summary.AddRow("[grey]Files[/]", "[cyan]" + files.Count.ToString(CultureInfo.InvariantCulture) + "[/]");
			AnsiConsole.Write(summary);

			if (files.Count > 0)
			{
				AnsiConsole.Write(new Rule("[bold deepskyblue1]Files[/]").RuleStyle("grey"));
				Table fileTable = CliOutput.CreateTable();
				fileTable.AddColumn(new TableColumn("[green]Path[/]"));
				fileTable.AddColumn(new TableColumn("[cyan]Size[/]"));
				fileTable.AddColumn(new TableColumn("[grey]Blocks[/]"));
				foreach (var file in files.OrderBy(static file => file.FullPath, StringComparer.OrdinalIgnoreCase))
				{
					fileTable.AddRow(
						"[green]" + Markup.Escape(file.FullPath) + "[/]",
						"[cyan]" + FtpHelpers.FormatBytes(file.Size) + "[/]",
						"[grey]" + file.AllocationBlocks.ToString(CultureInfo.InvariantCulture) + "[/]");
				}

				AnsiConsole.Write(fileTable);
			}

			return 0;
		});
	}
}

public sealed class ProfileExtractCommand : Command<ProfileExtractCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandArgument(1, "<OUTDIR>")]
		[Description("Destination directory.")]
		public string OutputDirectory { get; init; } = string.Empty;

		[CommandOption("--overwrite")]
		[Description("Overwrite files that already exist.")]
		public bool Overwrite { get; init; }

		[CommandOption("--json")]
		[Description("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			var result = profile.ExtractAll(settings.OutputDirectory, settings.Overwrite);
			string outputDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.OutputDirectory));
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					OutputDirectory = outputDirectory,
					result.Written,
					result.Skipped,
					BytesWritten = result.BytesWritten
				});
				return 0;
			}

			OperationFeedback.WriteSuccess(
				"Profile extract complete",
				$"[green]{result.Written}[/] file(s)  [silver]{FtpHelpers.FormatBytes(result.BytesWritten)}[/] -> [white]{Markup.Escape(outputDirectory)}[/]");
			if (result.Skipped > 0)
			{
				OperationFeedback.WriteWarning("Profile extract skipped existing files", result.Skipped.ToString(CultureInfo.InvariantCulture));
			}

			return 0;
		});
	}
}

public sealed class ProfileAccountShowCommand : Command<ProfileAccountShowCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[Description("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasAccount)
			{
				return LocalContentHelpers.Fail("Profile package does not contain an Account file.");
			}

			ProfileAccountInfo account = profile.ReadAccount();
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					account.Gamertag,
					OnlineXuid = LocalContentHelpers.FormatUInt64(account.OnlineXuid),
					account.OnlineServiceNetworkId,
					account.XboxLiveEnabled,
					account.PasswordProtected,
					account.Recovering,
					account.DeveloperAccount,
					account.MembershipTier,
					account.CountryCode,
					account.LanguageCode,
					account.OnlineDomain,
					account.OnlineKerberosRealm,
					Passcode = account.Passcode.Select(static button => button.ToString())
				});
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Profile Account[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Field[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			table.AddRow("[grey]Gamertag[/]", "[green]" + Markup.Escape(account.Gamertag) + "[/]");
			table.AddRow("[grey]Online XUID[/]", "[gold1]" + LocalContentHelpers.FormatUInt64(account.OnlineXuid) + "[/]");
			table.AddRow("[grey]Membership[/]", "[cyan]" + account.MembershipTier + "[/]");
			table.AddRow("[grey]Live Enabled[/]", account.XboxLiveEnabled ? "[green]yes[/]" : "[grey]no[/]");
			table.AddRow("[grey]Password Protected[/]", account.PasswordProtected ? "[yellow]yes[/]" : "[grey]no[/]");
			table.AddRow("[grey]Developer Account[/]", account.DeveloperAccount ? "[yellow]yes[/]" : "[grey]no[/]");
			table.AddRow("[grey]Country Code[/]", "[cyan]0x" + account.CountryCode.ToString("X2", CultureInfo.InvariantCulture) + "[/]");
			table.AddRow("[grey]Language Code[/]", "[cyan]0x" + account.LanguageCode.ToString("X2", CultureInfo.InvariantCulture) + "[/]");
			table.AddRow("[grey]Passcode[/]", "[white]" + Markup.Escape(ProfileCommandHelpers.FormatPasscode(account.Passcode)) + "[/]");
			AnsiConsole.Write(table);
			return 0;
		});
	}
}

public sealed class ProfileTitlesListCommand : Command<ProfileTitlesListCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[Description("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasDashboardData)
			{
				return LocalContentHelpers.Fail("Profile package does not contain FFFE07D1.gpd.");
			}

			var titles = profile.ReadTitles()
				.Select(title =>
				{
					TitleIdDatabase.Instance.TryResolve(title.TitleId, null, out TitleIdEntry? entry);
					string resolvedName = string.IsNullOrWhiteSpace(title.TitleName) ? entry?.Name ?? string.Empty : title.TitleName;
					return new
					{
						title.TitleId,
						title.TitleName,
						ResolvedTitleName = resolvedName,
						title.AchievementsEarned,
						title.AchievementsPossible,
						title.CreditEarned,
						title.CreditPossible,
						title.LastLoadedUtc,
						HasTitleData = profile.HasTitleDataFile(title.TitleId)
					};
				})
				.OrderByDescending(static title => title.LastLoadedUtc ?? DateTime.MinValue)
				.ThenBy(static title => title.TitleId)
				.ToList();

			if (settings.Json)
			{
				CliOutput.EmitJson(titles.Select(static title => new
				{
					TitleId = LocalContentHelpers.FormatUInt32(title.TitleId),
					title.TitleName,
					title.ResolvedTitleName,
					title.AchievementsEarned,
					title.AchievementsPossible,
					title.CreditEarned,
					title.CreditPossible,
					LastLoadedUtc = title.LastLoadedUtc?.ToString("O", CultureInfo.InvariantCulture),
					title.HasTitleData
				}));
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Profile Titles[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[cyan]Title ID[/]"));
			table.AddColumn(new TableColumn("[green]Title[/]"));
			table.AddColumn(new TableColumn("[gold1]GS[/]"));
			table.AddColumn(new TableColumn("[grey]Ach[/]"));
			table.AddColumn(new TableColumn("[grey]Last Loaded[/]"));
			foreach (var title in titles)
			{
				table.AddRow(
					"[cyan]" + LocalContentHelpers.FormatUInt32(title.TitleId) + "[/]",
					"[green]" + Markup.Escape(title.ResolvedTitleName) + "[/]",
					"[gold1]" + title.CreditEarned + "/" + title.CreditPossible + "[/]",
					"[grey]" + title.AchievementsEarned + "/" + title.AchievementsPossible + "[/]",
					title.LastLoadedUtc.HasValue
						? "[white]" + Markup.Escape(title.LastLoadedUtc.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)) + "[/]"
						: "[grey]never[/]");
			}

			AnsiConsole.Write(table);
			return 0;
		});
	}
}

public sealed class ProfileAchievementsListCommand : Command<ProfileAchievementsListCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--titleid <TITLEID>")]
		[Description("Title ID in hex, for example 4D530805.")]
		public string? TitleId { get; init; }

		[CommandOption("--json")]
		[Description("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (!ProfileCommandHelpers.TryParseTitleId(settings.TitleId, out uint titleId))
		{
			return LocalContentHelpers.Fail("Invalid or missing --titleid.");
		}

		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasTitleDataFile(titleId))
			{
				return LocalContentHelpers.Fail("Title GPD not found in the profile package for " + LocalContentHelpers.FormatUInt32(titleId) + ".");
			}

			TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? titleEntry);
			string titleName = titleEntry?.Name ?? LocalContentHelpers.FormatUInt32(titleId);
			var achievements = profile.ReadAchievementsWithSync(titleId)
				.Select(static item => new
				{
					item.Achievement.Id,
					item.Achievement.Label,
					item.Achievement.Description,
					item.Achievement.UnachievedDescription,
					item.Achievement.Credit,
					item.Achievement.AchievedOffline,
					item.Achievement.AchievedOnline,
					item.Achievement.AchievedAtUtc,
					item.PendingSync
				})
				.ToList();

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					TitleId = LocalContentHelpers.FormatUInt32(titleId),
					TitleName = titleName,
					Achievements = achievements.Select(static achievement => new
					{
						Id = LocalContentHelpers.FormatUInt32(achievement.Id),
						achievement.Label,
						achievement.Description,
						achievement.UnachievedDescription,
						achievement.Credit,
						achievement.AchievedOffline,
						achievement.AchievedOnline,
						AchievedAtUtc = achievement.AchievedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
						achievement.PendingSync
					})
				});
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Achievements[/] [grey]" + Markup.Escape(titleName) + "[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[cyan]ID[/]"));
			table.AddColumn(new TableColumn("[green]Label[/]"));
			table.AddColumn(new TableColumn("[gold1]GS[/]"));
			table.AddColumn(new TableColumn("[grey]State[/]"));
			table.AddColumn(new TableColumn("[grey]Sync[/]"));
			foreach (var achievement in achievements.OrderBy(static value => value.Id))
			{
				string state = achievement.AchievedOnline
					? "online"
					: achievement.AchievedOffline
						? "offline"
						: "locked";
				table.AddRow(
					"[cyan]" + LocalContentHelpers.FormatUInt32(achievement.Id) + "[/]",
					"[green]" + Markup.Escape(achievement.Label) + "[/]",
					"[gold1]" + achievement.Credit + "[/]",
					"[white]" + state + "[/]",
					achievement.PendingSync ? "[yellow]pending[/]" : "[grey]clean[/]");
			}

			AnsiConsole.Write(table);
			return 0;
		});
	}
}

public sealed class ProfileSettingsListCommand : Command<ProfileSettingsListCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[Description("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasDashboardData)
			{
				return LocalContentHelpers.Fail("Profile package does not contain FFFE07D1.gpd.");
			}

			var settingsList = profile.ReadSettings();
			if (settings.Json)
			{
				CliOutput.EmitJson(settingsList.Select(static setting => new
				{
					Id = LocalContentHelpers.FormatUInt64(setting.Id),
					Type = setting.Type.ToString(),
					Value = ProfileCommandHelpers.GetSettingJsonValue(setting),
					RawLength = setting.RawBytes?.Length
				}));
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Profile Settings[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[cyan]Setting[/]"));
			table.AddColumn(new TableColumn("[gold1]Type[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			foreach (ProfileSettingInfo setting in settingsList)
			{
				table.AddRow(
					"[cyan]" + LocalContentHelpers.FormatUInt64(setting.Id) + "[/]",
					"[gold1]" + setting.Type + "[/]",
					"[white]" + Markup.Escape(ProfileCommandHelpers.FormatSettingValue(setting)) + "[/]");
			}

			AnsiConsole.Write(table);
			return 0;
		});
	}
}

public sealed class ProfileSettingsGetCommand : Command<ProfileSettingsGetCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandArgument(1, "<SETTINGID>")]
		[Description("Setting ID in hex, for example 0x10040006.")]
		public string SettingId { get; init; } = string.Empty;

		[CommandOption("--json")]
		[Description("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (!LocalContentHelpers.TryParseUlong(settings.SettingId, out ulong settingId))
		{
			return LocalContentHelpers.Fail("Invalid <SETTINGID>.");
		}

		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasDashboardData)
			{
				return LocalContentHelpers.Fail("Profile package does not contain FFFE07D1.gpd.");
			}

			ProfileSettingInfo setting = profile.ReadSetting(settingId);
			bool pendingSync = profile.IsSettingPendingSync(settingId);

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Id = LocalContentHelpers.FormatUInt64(setting.Id),
					Type = setting.Type.ToString(),
					Value = ProfileCommandHelpers.GetSettingJsonValue(setting),
					setting.BinaryFlags,
					RawLength = setting.RawBytes?.Length,
					PendingSync = pendingSync
				});
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Profile Setting[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Field[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			table.AddRow("[grey]ID[/]", "[cyan]" + LocalContentHelpers.FormatUInt64(setting.Id) + "[/]");
			table.AddRow("[grey]Type[/]", "[gold1]" + setting.Type + "[/]");
			table.AddRow("[grey]Value[/]", "[white]" + Markup.Escape(ProfileCommandHelpers.FormatSettingValue(setting)) + "[/]");
			table.AddRow("[grey]Pending Sync[/]", pendingSync ? "[yellow]yes[/]" : "[grey]no[/]");
			if (setting.BinaryFlags.HasValue)
			{
				table.AddRow("[grey]Binary Flags[/]", "[cyan]0x" + setting.BinaryFlags.Value.ToString("X8", CultureInfo.InvariantCulture) + "[/]");
			}

			AnsiConsole.Write(table);
			return 0;
		});
	}
}
