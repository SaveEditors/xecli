using System.ComponentModel;
using System.Globalization;
using System.Linq;
using NoDev.Common;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.LocalProfiles;

namespace Xbox360.Remote.Cli.Commands;

internal static class ProfileMutationCommandHelpers
{
	public static bool TryParseAchievementId(string? text, out uint achievementId)
	{
		achievementId = 0;
		if (!LocalContentHelpers.TryParseUlong(text ?? string.Empty, out ulong value) || value > uint.MaxValue)
		{
			return false;
		}

		achievementId = (uint)value;
		return true;
	}

	public static bool TryParseSettingType(string? text, out ProfileSettingType type)
	{
		type = default;
		return !string.IsNullOrWhiteSpace(text) &&
			Enum.TryParse(text.Trim(), ignoreCase: true, out type);
	}

	public static bool TryParseOptionalUInt32(string? text, out uint? value)
	{
		value = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}

		if (!LocalContentHelpers.TryParseUlong(text, out ulong raw) || raw > uint.MaxValue)
		{
			return false;
		}

		value = (uint)raw;
		return true;
	}

	public static bool TryParseOptionalNonNegativeInt(string? text, out int? value)
	{
		value = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}

		if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
		{
			return false;
		}

		value = parsed;
		return true;
	}

	public static bool TryParseTimestamp(string? text, out DateTime? value)
	{
		value = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}

		DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
		if (DateTime.TryParse(text, CultureInfo.InvariantCulture, styles, out DateTime parsed) ||
			DateTime.TryParse(text, CultureInfo.CurrentCulture, styles, out parsed))
		{
			value = parsed.ToUniversalTime();
			return true;
		}

		return false;
	}

	public static bool TryParseSettingValue(string text, ProfileSettingType type, out object? value, out string? error)
	{
		value = null;
		error = null;
		switch (type)
		{
			case ProfileSettingType.Context:
			case ProfileSettingType.Int32:
				if (!TryParseInt32(text, out int int32Value))
				{
					error = "Invalid Int32 value.";
					return false;
				}

				value = int32Value;
				return true;
			case ProfileSettingType.Int64:
				if (!TryParseInt64(text, out long int64Value))
				{
					error = "Invalid Int64 value.";
					return false;
				}

				value = int64Value;
				return true;
			case ProfileSettingType.Double:
				if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double doubleValue))
				{
					error = "Invalid Double value.";
					return false;
				}

				value = doubleValue;
				return true;
			case ProfileSettingType.Unicode:
				value = text;
				return true;
			case ProfileSettingType.Float:
				if (!float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float floatValue))
				{
					error = "Invalid Float value.";
					return false;
				}

				value = floatValue;
				return true;
			case ProfileSettingType.Binary:
				if (!TryParseHexBytes(text, out byte[] bytes, out error))
				{
					return false;
				}

				value = bytes;
				return true;
			case ProfileSettingType.DateTime:
				if (!TryParseTimestamp(text, out DateTime? parsedTime))
				{
					error = "Invalid DateTime value. Use an ISO-8601 timestamp.";
					return false;
				}

				value = parsedTime;
				return true;
			case ProfileSettingType.Null:
				value = null;
				return true;
			default:
				error = "Unsupported setting type.";
				return false;
		}
	}

	private static bool TryParseInt32(string text, out int value)
	{
		string candidate = text.Trim();
		if (int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
		{
			return true;
		}

		if (!TryNormalizeHex(candidate, out string hex))
		{
			return false;
		}

		if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint raw))
		{
			return false;
		}

		value = unchecked((int)raw);
		return true;
	}

	private static bool TryParseInt64(string text, out long value)
	{
		string candidate = text.Trim();
		if (long.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
		{
			return true;
		}

		if (!TryNormalizeHex(candidate, out string hex))
		{
			return false;
		}

		if (!ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong raw))
		{
			return false;
		}

		value = unchecked((long)raw);
		return true;
	}

	private static bool TryParseHexBytes(string text, out byte[] data, out string? error)
	{
		data = Array.Empty<byte>();
		error = null;
		if (!TryNormalizeHex(text, out string hex))
		{
			error = "Binary values must be a hex string.";
			return false;
		}

		if ((hex.Length & 1) != 0)
		{
			error = "Binary hex strings must contain an even number of characters.";
			return false;
		}

		data = hex.Length == 0 ? Array.Empty<byte>() : Formatting.HexStringToByteArray(hex);
		return true;
	}

	private static bool TryNormalizeHex(string text, out string normalized)
	{
		normalized = new string(text
			.Where(static ch => !char.IsWhiteSpace(ch) && ch != '-')
			.ToArray());
		if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized[2..];
		}

		return normalized.All(static ch => Uri.IsHexDigit(ch));
	}
}

public sealed class ProfileTitlesAddCommand : Command<ProfileTitlesAddCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--titleid <TITLEID>")]
		[Description("Title ID in hex, for example 415607E7.")]
		public string? TitleId { get; init; }

		[CommandOption("--name <NAME>")]
		[Description("Optional title name override.")]
		public string? Name { get; init; }

		[CommandOption("--achievements-possible <COUNT>")]
		[Description("Optional possible-achievement count. Defaults to the embedded title GPD when available.")]
		public string? AchievementsPossible { get; init; }

		[CommandOption("--credit-possible <COUNT>")]
		[Description("Optional possible gamerscore total. Defaults to the embedded title GPD when available.")]
		public string? CreditPossible { get; init; }

		[CommandOption("--last-loaded <TIMESTAMP>")]
		[Description("Optional ISO-8601 UTC timestamp to store as the last-loaded time.")]
		public string? LastLoaded { get; init; }

		[CommandOption("--replace")]
		[Description("Replace an existing dashboard title record instead of failing.")]
		public bool Replace { get; init; }

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

		if (titleId == ProfilePackage.DashboardTitleId)
		{
			return LocalContentHelpers.Fail("Use a real game Title ID, not the dashboard GPD ID.");
		}

		if (!ProfileMutationCommandHelpers.TryParseOptionalNonNegativeInt(settings.AchievementsPossible, out int? achievementsPossible))
		{
			return LocalContentHelpers.Fail("Invalid --achievements-possible.");
		}

		if (!ProfileMutationCommandHelpers.TryParseOptionalNonNegativeInt(settings.CreditPossible, out int? creditPossible))
		{
			return LocalContentHelpers.Fail("Invalid --credit-possible.");
		}

		if (!ProfileMutationCommandHelpers.TryParseTimestamp(settings.LastLoaded, out DateTime? lastLoadedUtc))
		{
			return LocalContentHelpers.Fail("Invalid --last-loaded. Use an ISO-8601 timestamp.");
		}

		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasDashboardData)
			{
				return LocalContentHelpers.Fail("Profile package does not contain FFFE07D1.gpd.");
			}

			ProfileTitleInfo? existingTitle = profile.ReadTitles().FirstOrDefault(candidate => candidate.TitleId == titleId);
			if (existingTitle != null && !settings.Replace)
			{
				return LocalContentHelpers.Fail("Title record already exists. Use --replace to overwrite it.");
			}

			List<ProfileAchievementInfo>? titleAchievements = null;
			if (profile.HasTitleDataFile(titleId))
			{
				titleAchievements = profile.ReadAchievements(titleId).ToList();
			}

			int derivedAchievementsPossible = titleAchievements?.Count ?? existingTitle?.AchievementsPossible ?? -1;
			int derivedCreditPossible = titleAchievements?.Sum(static achievement => achievement.Credit) ?? existingTitle?.CreditPossible ?? -1;
			int derivedAchievementsEarned = titleAchievements?.Count(static achievement => achievement.IsUnlocked) ?? existingTitle?.AchievementsEarned ?? 0;
			int derivedCreditEarned = titleAchievements?.Where(static achievement => achievement.IsUnlocked).Sum(static achievement => achievement.Credit) ?? existingTitle?.CreditEarned ?? 0;

			int resolvedAchievementsPossible = achievementsPossible ?? derivedAchievementsPossible;
			int resolvedCreditPossible = creditPossible ?? derivedCreditPossible;
			if (resolvedAchievementsPossible < 0)
			{
				return LocalContentHelpers.Fail("Missing possible achievement count. Supply --achievements-possible or embed the title GPD first.");
			}

			if (resolvedCreditPossible < 0)
			{
				return LocalContentHelpers.Fail("Missing possible gamerscore total. Supply --credit-possible or embed the title GPD first.");
			}

			if (titleAchievements != null && resolvedAchievementsPossible < titleAchievements.Count)
			{
				return LocalContentHelpers.Fail("Possible achievement count cannot be lower than the embedded title GPD achievement count.");
			}

			if (titleAchievements != null && resolvedCreditPossible < titleAchievements.Sum(static achievement => achievement.Credit))
			{
				return LocalContentHelpers.Fail("Possible gamerscore cannot be lower than the embedded title GPD total.");
			}

			string resolvedTitleName = settings.Name?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(resolvedTitleName))
			{
				resolvedTitleName = existingTitle?.TitleName ?? string.Empty;
			}

			if (string.IsNullOrWhiteSpace(resolvedTitleName) &&
				TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? entry) &&
				entry != null &&
				!string.IsNullOrWhiteSpace(entry.Name))
			{
				resolvedTitleName = entry.Name;
			}

			if (string.IsNullOrWhiteSpace(resolvedTitleName))
			{
				resolvedTitleName = LocalContentHelpers.FormatUInt32(titleId);
			}

			DateTime? resolvedLastLoadedUtc = lastLoadedUtc ?? existingTitle?.LastLoadedUtc;
			ProfileTitleInfo title = ProfileTitleInfo.Create(
				titleId,
				resolvedTitleName,
				resolvedAchievementsPossible,
				derivedAchievementsEarned,
				resolvedCreditPossible,
				derivedCreditEarned,
				resolvedLastLoadedUtc);

			ProfileTitleInfo saved = profile.WriteTitle(title, settings.Replace);
			bool hasTitleData = profile.HasTitleDataFile(titleId);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Operation = settings.Replace ? "replace-title" : "add-title",
					TitleId = LocalContentHelpers.FormatUInt32(saved.TitleId),
					saved.TitleName,
					saved.AchievementsEarned,
					saved.AchievementsPossible,
					saved.CreditEarned,
					saved.CreditPossible,
					LastLoadedUtc = saved.LastLoadedUtc?.ToString("O", CultureInfo.InvariantCulture),
					HasTitleData = hasTitleData
				});
				return 0;
			}

			OperationFeedback.WriteSuccess(
				settings.Replace ? "Title record refreshed" : "Title record added",
				"[cyan]" + LocalContentHelpers.FormatUInt32(saved.TitleId) + "[/] [grey]|[/] [green]" + Markup.Escape(saved.TitleName) + "[/] [grey]|[/] [gold1]" +
				saved.CreditEarned + "/" + saved.CreditPossible + " GS[/]");
			if (!hasTitleData)
			{
				OperationFeedback.WriteWarning("No embedded title GPD was found", "Only the dashboard title record was added.");
			}

			return 0;
		});
	}
}

public sealed class ProfileAccountSetGamertagCommand : Command<ProfileAccountSetGamertagCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandArgument(1, "<GAMERTAG>")]
		[Description("Replacement gamertag.")]
		public string Gamertag { get; init; } = string.Empty;

		[CommandOption("--json")]
		[Description("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			profile.SetGamertag(settings.Gamertag);
			ProfileAccountInfo account = profile.ReadAccount();

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Operation = "set-gamertag",
					ProfileId = LocalContentHelpers.FormatUInt64(profile.ProfileId),
					account.Gamertag,
					OnlineXuid = LocalContentHelpers.FormatUInt64(account.OnlineXuid)
				});
				return 0;
			}

			OperationFeedback.WriteSuccess(
				"Profile gamertag updated",
				"[green]" + Markup.Escape(account.Gamertag) + "[/] [grey]|[/] [gold1]" + LocalContentHelpers.FormatUInt64(account.OnlineXuid) + "[/]");
			return 0;
		});
	}
}

public sealed class ProfileSettingsSetCommand : Command<ProfileSettingsSetCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandArgument(1, "<SETTINGID>")]
		[Description("Setting ID in hex, for example 0x10040006.")]
		public string SettingId { get; init; } = string.Empty;

		[CommandArgument(2, "<VALUE>")]
		[Description("New setting value.")]
		public string Value { get; init; } = string.Empty;

		[CommandOption("--type <TYPE>")]
		[Description("Setting type for missing records: int32, int64, double, unicode, float, binary, datetime, or null.")]
		public string? Type { get; init; }

		[CommandOption("--binary-flags <FLAGS>")]
		[Description("Optional binary/unicode flags value.")]
		public string? BinaryFlags { get; init; }

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

		if (!ProfileMutationCommandHelpers.TryParseOptionalUInt32(settings.BinaryFlags, out uint? binaryFlags))
		{
			return LocalContentHelpers.Fail("Invalid --binary-flags.");
		}

		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasDashboardData)
			{
				return LocalContentHelpers.Fail("Profile package does not contain FFFE07D1.gpd.");
			}

			bool exists = profile.HasSetting(settingId);
			ProfileSettingInfo? template = null;
			ProfileSettingType targetType;
			if (exists)
			{
				template = profile.ReadSetting(settingId);
				targetType = template.Type;
				if (!string.IsNullOrWhiteSpace(settings.Type) &&
					(!ProfileMutationCommandHelpers.TryParseSettingType(settings.Type, out ProfileSettingType declaredType) || declaredType != targetType))
				{
					return LocalContentHelpers.Fail("Existing setting type does not match --type.");
				}
			}
			else
			{
				if (!ProfileMutationCommandHelpers.TryParseSettingType(settings.Type, out targetType))
				{
					return LocalContentHelpers.Fail("Missing setting type. Use --type when creating a new record.");
				}
			}

			if (!ProfileMutationCommandHelpers.TryParseSettingValue(settings.Value, targetType, out object? parsedValue, out string? error))
			{
				return LocalContentHelpers.Fail(error ?? "Invalid setting value.");
			}

			ProfileSettingInfo updated = exists
				? template!.WithValue(parsedValue, binaryFlags)
				: ProfileSettingInfo.Create(settingId, targetType, parsedValue, binaryFlags);

			ProfileSettingInfo saved = profile.WriteSetting(settingId, updated);
			bool pendingSync = profile.IsSettingPendingSync(settingId);

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Operation = "set",
					Id = LocalContentHelpers.FormatUInt64(saved.Id),
					Type = saved.Type.ToString(),
					Value = ProfileCommandHelpers.GetSettingJsonValue(saved),
					saved.BinaryFlags,
					PendingSync = pendingSync
				});
				return 0;
			}

			OperationFeedback.WriteSuccess(
				"Profile setting updated",
				"[cyan]" + LocalContentHelpers.FormatUInt64(saved.Id) + "[/] [grey]|[/] [gold1]" + saved.Type + "[/] [grey]|[/] [white]" +
				Markup.Escape(ProfileCommandHelpers.FormatSettingValue(saved)) + "[/]");
			return 0;
		});
	}
}

public sealed class ProfileAchievementsUnlockCommand : Command<ProfileAchievementsUnlockCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--titleid <TITLEID>")]
		[Description("Title ID in hex, for example 4D530805.")]
		public string? TitleId { get; init; }

		[CommandOption("--achievementid <ACHIEVEMENTID>")]
		[Description("Achievement ID in decimal or hex.")]
		public string? AchievementId { get; init; }

		[CommandOption("--online")]
		[Description("Mark the achievement as unlocked online.")]
		public bool Online { get; init; }

		[CommandOption("--time <TIMESTAMP>")]
		[Description("Optional ISO-8601 UTC timestamp for online unlocks.")]
		public string? Time { get; init; }

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

		if (!ProfileMutationCommandHelpers.TryParseAchievementId(settings.AchievementId, out uint achievementId))
		{
			return LocalContentHelpers.Fail("Invalid or missing --achievementid.");
		}

		if (!ProfileMutationCommandHelpers.TryParseTimestamp(settings.Time, out DateTime? achievedAtUtc))
		{
			return LocalContentHelpers.Fail("Invalid --time. Use an ISO-8601 timestamp.");
		}

		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			ProfileAchievementInfo achievement = profile.UnlockAchievement(titleId, achievementId, settings.Online, achievedAtUtc);
			ProfileTitleInfo? title = profile.ReadTitles().FirstOrDefault(candidate => candidate.TitleId == titleId);
			bool pendingSync = profile.ReadAchievementsWithSync(titleId)
				.First(candidate => candidate.Achievement.Id == achievementId)
				.PendingSync;
			TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? titleEntry);
			string titleName = title?.TitleName;
			if (string.IsNullOrWhiteSpace(titleName))
			{
				titleName = titleEntry?.Name ?? LocalContentHelpers.FormatUInt32(titleId);
			}

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Operation = "unlock",
					TitleId = LocalContentHelpers.FormatUInt32(titleId),
					TitleName = titleName,
					AchievementId = LocalContentHelpers.FormatUInt32(achievement.Id),
					achievement.Label,
					achievement.Credit,
					achievement.AchievedOffline,
					achievement.AchievedOnline,
					AchievedAtUtc = achievement.AchievedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
					PendingSync = pendingSync,
					TitleAchievementsEarned = title?.AchievementsEarned,
					TitleCreditEarned = title?.CreditEarned
				});
				return 0;
			}

			OperationFeedback.WriteSuccess(
				"Achievement unlocked",
				"[green]" + Markup.Escape(achievement.Label) + "[/] [grey]|[/] [gold1]" + achievement.Credit +
				" GS[/] [grey]|[/] [cyan]" + Markup.Escape(titleName) + "[/] [grey]|[/] [white]" +
				(achievement.AchievedOnline ? "online" : "offline") + "[/]");
			return 0;
		});
	}
}

public sealed class ProfileAchievementsLockCommand : Command<ProfileAchievementsLockCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[Description("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--titleid <TITLEID>")]
		[Description("Title ID in hex, for example 4D530805.")]
		public string? TitleId { get; init; }

		[CommandOption("--achievementid <ACHIEVEMENTID>")]
		[Description("Achievement ID in decimal or hex.")]
		public string? AchievementId { get; init; }

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

		if (!ProfileMutationCommandHelpers.TryParseAchievementId(settings.AchievementId, out uint achievementId))
		{
			return LocalContentHelpers.Fail("Invalid or missing --achievementid.");
		}

		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			ProfileAchievementInfo achievement = profile.LockAchievement(titleId, achievementId);
			ProfileTitleInfo? title = profile.ReadTitles().FirstOrDefault(candidate => candidate.TitleId == titleId);
			bool pendingSync = profile.ReadAchievementsWithSync(titleId)
				.First(candidate => candidate.Achievement.Id == achievementId)
				.PendingSync;
			TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? titleEntry);
			string titleName = title?.TitleName;
			if (string.IsNullOrWhiteSpace(titleName))
			{
				titleName = titleEntry?.Name ?? LocalContentHelpers.FormatUInt32(titleId);
			}

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Operation = "lock",
					TitleId = LocalContentHelpers.FormatUInt32(titleId),
					TitleName = titleName,
					AchievementId = LocalContentHelpers.FormatUInt32(achievement.Id),
					achievement.Label,
					achievement.Credit,
					achievement.AchievedOffline,
					achievement.AchievedOnline,
					AchievedAtUtc = achievement.AchievedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
					PendingSync = pendingSync,
					TitleAchievementsEarned = title?.AchievementsEarned,
					TitleCreditEarned = title?.CreditEarned
				});
				return 0;
			}

			OperationFeedback.WriteSuccess(
				"Achievement locked",
				"[green]" + Markup.Escape(achievement.Label) + "[/] [grey]|[/] [cyan]" + Markup.Escape(titleName) +
				"[/] [grey]|[/] [white]locked[/]");
			return 0;
		});
	}
}
