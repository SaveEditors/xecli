using System;
using System.IO;

namespace Xbox360.Remote.Cli.LocalProfiles;

internal sealed partial class ProfilePackage
{
	public bool HasAvatarColors => HasDashboardData && HasSetting(ProfileAvatarColors.AvatarInfo1SettingId);

	public byte[] ReadAccountBytes() => ReadRequiredPackageFile("Account");

	public byte[] ReadDashboardDataBytes() => ReadRequiredPackageFile($"{DashboardTitleId:X8}.gpd");

	public byte[] ReadTitleDataBytes(uint titleId) => ReadRequiredPackageFile($"{titleId:X8}.gpd");

	public byte[] ReadPackageFile(string nameOrPath)
	{
		if (!TryFindFile(nameOrPath, out StfsPackageEntry? entry))
		{
			throw new FileNotFoundException("Profile file not found in package: " + nameOrPath);
		}

		return _reader.ReadFile(entry.FullPath);
	}

	public string ResolvePackageFilePath(string nameOrPath)
	{
		if (!TryFindFile(nameOrPath, out StfsPackageEntry? entry))
		{
			throw new FileNotFoundException("Profile file not found in package: " + nameOrPath);
		}

		return entry.FullPath;
	}

	public ProfileAvatarColors ReadAvatarColors()
	{
		if (!HasDashboardData)
		{
			throw new InvalidOperationException("Profile package does not contain FFFE07D1.gpd.");
		}

		if (!HasSetting(ProfileAvatarColors.AvatarInfo1SettingId))
		{
			throw new InvalidOperationException("Profile package does not contain the avatar color setting.");
		}

		ProfileSettingInfo setting = ReadSetting(ProfileAvatarColors.AvatarInfo1SettingId);
		if (setting.Type != ProfileSettingType.Binary || setting.RawBytes == null)
		{
			throw new InvalidOperationException("Avatar colors are stored in a binary dashboard setting.");
		}

		return ProfileAvatarColors.Parse(setting.RawBytes);
	}

	public ProfileAvatarColors WriteAvatarColors(ProfileAvatarColors colors)
	{
		if (colors == null)
		{
			throw new ArgumentNullException(nameof(colors));
		}

		if (!HasDashboardData)
		{
			throw new InvalidOperationException("Profile package does not contain FFFE07D1.gpd.");
		}

		if (!HasSetting(ProfileAvatarColors.AvatarInfo1SettingId))
		{
			throw new InvalidOperationException("Profile package does not contain the avatar color setting.");
		}

		ProfileSettingInfo setting = ReadSetting(ProfileAvatarColors.AvatarInfo1SettingId);
		if (setting.Type != ProfileSettingType.Binary)
		{
			throw new InvalidOperationException("Avatar colors are stored in a binary dashboard setting.");
		}

		WriteSetting(
			ProfileAvatarColors.AvatarInfo1SettingId,
			setting.WithValue(colors.ToArray(), setting.BinaryFlags));
		return ReadAvatarColors();
	}

	private byte[] ReadRequiredPackageFile(string fileName)
	{
		if (!TryFindFile(fileName, out StfsPackageEntry? entry))
		{
			throw new FileNotFoundException("Profile file not found in package: " + fileName);
		}

		return _reader.ReadFile(entry.FullPath);
	}
}
