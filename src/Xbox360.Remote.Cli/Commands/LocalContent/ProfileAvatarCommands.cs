using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.LocalProfiles;

namespace Xbox360.Remote.Cli.Commands;

internal static class ProfileAvatarColorCommandHelpers
{
	public static string FormatArgb(uint value) => $"0x{value:X8}";

	public static bool TryParseArgb(string? text, out uint value)
	{
		value = 0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string candidate = text.Trim();
		if (candidate.StartsWith("#", StringComparison.Ordinal))
		{
			candidate = candidate[1..];
		}

		if (!LocalContentHelpers.TryParseUlong(candidate, out ulong raw) || raw > uint.MaxValue)
		{
			return false;
		}

		value = (uint)raw;
		return true;
	}
}

public sealed class ProfileAvatarColorsGetCommand : Command<ProfileAvatarColorsGetCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasAvatarColors)
			{
				return LocalContentHelpers.Fail("Profile package does not contain the avatar color setting.");
			}

			ProfileAvatarColors colors = profile.ReadAvatarColors();
			bool pendingSync = profile.IsSettingPendingSync(ProfileAvatarColors.AvatarInfo1SettingId);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					SettingId = LocalContentHelpers.FormatUInt64(ProfileAvatarColors.AvatarInfo1SettingId),
					Skin = ProfileAvatarColorCommandHelpers.FormatArgb(colors.SkinArgb),
					Hair = ProfileAvatarColorCommandHelpers.FormatArgb(colors.HairArgb),
					Lip = ProfileAvatarColorCommandHelpers.FormatArgb(colors.LipArgb),
					Eye = ProfileAvatarColorCommandHelpers.FormatArgb(colors.EyeArgb),
					Eyebrow = ProfileAvatarColorCommandHelpers.FormatArgb(colors.EyebrowArgb),
					Eyeshadow = ProfileAvatarColorCommandHelpers.FormatArgb(colors.EyeshadowArgb),
					FacialHair = ProfileAvatarColorCommandHelpers.FormatArgb(colors.FacialHairArgb),
					FacePaintPrimary = ProfileAvatarColorCommandHelpers.FormatArgb(colors.FacePaintPrimaryArgb),
					FacePaintSecondary = ProfileAvatarColorCommandHelpers.FormatArgb(colors.FacePaintSecondaryArgb),
					pendingSync
				});
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Avatar Colors[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Field[/]"));
			table.AddColumn(new TableColumn("[white]ARGB[/]"));
			foreach ((string name, uint argb) in colors.Enumerate())
			{
				table.AddRow("[grey]" + name + "[/]", "[white]" + ProfileAvatarColorCommandHelpers.FormatArgb(argb) + "[/]");
			}

			table.AddRow("[grey]Pending Sync[/]", pendingSync ? "[yellow]yes[/]" : "[grey]no[/]");
			AnsiConsole.Write(table);
			return 0;
		});
	}
}

public sealed class ProfileAvatarColorsSetCommand : Command<ProfileAvatarColorsSetCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the profile package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--skin <ARGB>")]
		[LocalizedDescription("ARGB color for the avatar skin.")]
		public string? Skin { get; init; }

		[CommandOption("--hair <ARGB>")]
		[LocalizedDescription("ARGB color for the avatar hair.")]
		public string? Hair { get; init; }

		[CommandOption("--lip <ARGB>")]
		[LocalizedDescription("ARGB color for the avatar lips.")]
		public string? Lip { get; init; }

		[CommandOption("--eye <ARGB>")]
		[LocalizedDescription("ARGB color for the avatar eyes.")]
		public string? Eye { get; init; }

		[CommandOption("--eyebrow <ARGB>")]
		[LocalizedDescription("ARGB color for the avatar eyebrows.")]
		public string? Eyebrow { get; init; }

		[CommandOption("--eyeshadow <ARGB>")]
		[LocalizedDescription("ARGB color for the avatar eye shadow.")]
		public string? Eyeshadow { get; init; }

		[CommandOption("--facial-hair <ARGB>")]
		[LocalizedDescription("ARGB color for the avatar facial hair.")]
		public string? FacialHair { get; init; }

		[CommandOption("--face-paint <ARGB>")]
		[LocalizedDescription("ARGB color for the primary face paint slot.")]
		public string? FacePaintPrimary { get; init; }

		[CommandOption("--face-paint-2 <ARGB>")]
		[LocalizedDescription("ARGB color for the secondary face paint slot.")]
		public string? FacePaintSecondary { get; init; }

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (!TryParseOptionalArgb(settings.Skin, "--skin", out uint? skinArgb, out string? error) ||
			!TryParseOptionalArgb(settings.Hair, "--hair", out uint? hairArgb, out error) ||
			!TryParseOptionalArgb(settings.Lip, "--lip", out uint? lipArgb, out error) ||
			!TryParseOptionalArgb(settings.Eye, "--eye", out uint? eyeArgb, out error) ||
			!TryParseOptionalArgb(settings.Eyebrow, "--eyebrow", out uint? eyebrowArgb, out error) ||
			!TryParseOptionalArgb(settings.Eyeshadow, "--eyeshadow", out uint? eyeshadowArgb, out error) ||
			!TryParseOptionalArgb(settings.FacialHair, "--facial-hair", out uint? facialHairArgb, out error) ||
			!TryParseOptionalArgb(settings.FacePaintPrimary, "--face-paint", out uint? facePaintPrimaryArgb, out error) ||
			!TryParseOptionalArgb(settings.FacePaintSecondary, "--face-paint-2", out uint? facePaintSecondaryArgb, out error))
		{
			return LocalContentHelpers.Fail(error ?? "Invalid color value.");
		}

		if (skinArgb == null &&
			hairArgb == null &&
			lipArgb == null &&
			eyeArgb == null &&
			eyebrowArgb == null &&
			eyeshadowArgb == null &&
			facialHairArgb == null &&
			facePaintPrimaryArgb == null &&
			facePaintSecondaryArgb == null)
		{
			return LocalContentHelpers.Fail("Specify at least one avatar color to update.");
		}

		return ProfileCommandHelpers.ExecuteWithProfile(settings.PackagePath, (_, profile) =>
		{
			if (!profile.HasAvatarColors)
			{
				return LocalContentHelpers.Fail("Profile package does not contain the avatar color setting.");
			}

			ProfileAvatarColors updated = profile.ReadAvatarColors().WithOverrides(
				skinArgb,
				hairArgb,
				lipArgb,
				eyeArgb,
				eyebrowArgb,
				eyeshadowArgb,
				facialHairArgb,
				facePaintPrimaryArgb,
				facePaintSecondaryArgb);
			ProfileAvatarColors saved = profile.WriteAvatarColors(updated);
			bool pendingSync = profile.IsSettingPendingSync(ProfileAvatarColors.AvatarInfo1SettingId);

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Operation = "set-avatar-colors",
					SettingId = LocalContentHelpers.FormatUInt64(ProfileAvatarColors.AvatarInfo1SettingId),
					Skin = ProfileAvatarColorCommandHelpers.FormatArgb(saved.SkinArgb),
					Hair = ProfileAvatarColorCommandHelpers.FormatArgb(saved.HairArgb),
					Lip = ProfileAvatarColorCommandHelpers.FormatArgb(saved.LipArgb),
					Eye = ProfileAvatarColorCommandHelpers.FormatArgb(saved.EyeArgb),
					Eyebrow = ProfileAvatarColorCommandHelpers.FormatArgb(saved.EyebrowArgb),
					Eyeshadow = ProfileAvatarColorCommandHelpers.FormatArgb(saved.EyeshadowArgb),
					FacialHair = ProfileAvatarColorCommandHelpers.FormatArgb(saved.FacialHairArgb),
					FacePaintPrimary = ProfileAvatarColorCommandHelpers.FormatArgb(saved.FacePaintPrimaryArgb),
					FacePaintSecondary = ProfileAvatarColorCommandHelpers.FormatArgb(saved.FacePaintSecondaryArgb),
					pendingSync
				});
				return 0;
			}

			OperationFeedback.WriteSuccess(
				"Avatar colors updated",
				"[grey]Skin:[/] [white]" + ProfileAvatarColorCommandHelpers.FormatArgb(saved.SkinArgb) + "[/] [grey]|[/] [grey]Hair:[/] [white]" + ProfileAvatarColorCommandHelpers.FormatArgb(saved.HairArgb) + "[/]");
			if (pendingSync)
			{
				OperationFeedback.WriteWarning("Avatar color setting is pending sync", LocalContentHelpers.FormatUInt64(ProfileAvatarColors.AvatarInfo1SettingId));
			}

			return 0;
		});
	}

	private static bool TryParseOptionalArgb(string? text, string optionName, out uint? value, out string? error)
	{
		value = null;
		error = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}

		if (!ProfileAvatarColorCommandHelpers.TryParseArgb(text, out uint parsed))
		{
			error = "Invalid " + optionName + " color. Use ARGB hex like 0xFFAA7744.";
			return false;
		}

		value = parsed;
		return true;
	}
}

