using System;
using System.Globalization;
using System.IO;
using System.Text;
using NoDev.Common;
using NoDev.Common.IO;
using NoDev.Xbox360;

namespace Xbox360.Remote.Cli.LocalProfiles;

internal enum ProfileMembershipTier
{
	Invalid = 0,
	Silver = 3,
	Gold = 6
}

internal enum ProfilePasscodeButton : byte
{
	None = 0x00,
	DpadUp = 0x01,
	DpadDown = 0x02,
	DpadLeft = 0x03,
	DpadRight = 0x04,
	GamepadX = 0x05,
	GamepadY = 0x06,
	GamepadLeftTrigger = 0x09,
	GamepadRightTrigger = 0x0A,
	GamepadLeftShoulder = 0x0B,
	GamepadRightShoulder = 0x0C
}

internal enum ProfileSettingType : byte
{
	Context = 0x00,
	Int32 = 0x01,
	Int64 = 0x02,
	Double = 0x03,
	Unicode = 0x04,
	Float = 0x05,
	Binary = 0x06,
	DateTime = 0x07,
	Null = 0xFF
}

internal sealed class ProfileAccountInfo
{
	private const uint MembershipTypeMask = 0x01F00000;
	private const uint CountryMask = 0x0000FF00;
	private const uint LanguageMask = 0x3E000000;
	private const uint PasswordProtectedMask = 0x10000000;
	private const uint XboxLiveEnabledMask = 0x20000000;
	private const uint RecoveringMask = 0x40000000;

	private uint _liveFlags;
	private uint _reserved;
	private uint _cachedUserFlags;
	private byte[] _passcode = new byte[4];
	private byte[] _onlineKey = new byte[16];
	private string _userPassportMembername = string.Empty;
	private string _userPassportPassword = string.Empty;
	private string _ownerPassportMembername = string.Empty;

	public bool DeveloperAccount { get; private set; }

	public string Gamertag { get; private set; } = string.Empty;

	public ulong OnlineXuid { get; private set; }

	public uint OnlineServiceNetworkId { get; private set; }

	public string OnlineDomain { get; private set; } = string.Empty;

	public string OnlineKerberosRealm { get; private set; } = string.Empty;

	public bool Recovering => (_liveFlags & RecoveringMask) != 0;

	public bool XboxLiveEnabled => (_liveFlags & XboxLiveEnabledMask) != 0;

	public bool PasswordProtected => (_liveFlags & PasswordProtectedMask) != 0;

	public ProfileMembershipTier MembershipTier => (ProfileMembershipTier)(_cachedUserFlags & MembershipTypeMask);

	public byte CountryCode => (byte)((_cachedUserFlags & CountryMask) >> 8);

	public byte LanguageCode => (byte)((_cachedUserFlags & LanguageMask) >> 25);

	public ProfilePasscodeButton[] Passcode =>
		Array.ConvertAll(_passcode, static value => (ProfilePasscodeButton)value);

	public static ProfileAccountInfo Parse(byte[] accountBuffer)
	{
		byte[]? unobfuscated = XeCrypt.XeKeysUnObfuscate(1, accountBuffer, false);
		bool developerAccount = false;
		if (unobfuscated == null)
		{
			unobfuscated = XeCrypt.XeKeysUnObfuscate(1, accountBuffer, true);
			if (unobfuscated == null)
			{
				throw new InvalidDataException("Account file is corrupted.");
			}

			developerAccount = true;
		}

		var io = new EndianIO(unobfuscated, EndianType.Big);
		try
		{
			return new ProfileAccountInfo
			{
				DeveloperAccount = developerAccount,
				_liveFlags = io.ReadUInt32(),
				_reserved = io.ReadUInt32(),
				Gamertag = io.ReadUnicodeString(16).RemoveNullBytes(),
				OnlineXuid = io.ReadUInt64(),
				_cachedUserFlags = io.ReadUInt32(),
				OnlineServiceNetworkId = io.ReadUInt32(),
				_passcode = io.ReadByteArray(4),
				OnlineDomain = io.ReadAsciiString(20).RemoveNullBytes(),
				OnlineKerberosRealm = io.ReadAsciiString(24).RemoveNullBytes(),
				_onlineKey = io.ReadByteArray(16),
				_userPassportMembername = io.ReadAsciiString(114).RemoveNullBytes(),
				_userPassportPassword = io.ReadAsciiString(32).RemoveNullBytes(),
				_ownerPassportMembername = io.ReadAsciiString(114).RemoveNullBytes()
			};
		}
		finally
		{
			io.Close();
		}
	}

	public static string FilterGamertag(string gamertag)
	{
		if (gamertag == null)
		{
			throw new ArgumentNullException(nameof(gamertag));
		}

		string filtered = gamertag;
		for (int index = 0; index < filtered.Length; index++)
		{
			char value = filtered[index];
			if (value == '^')
			{
				if (index == filtered.Length - 1)
				{
					filtered = filtered.Remove(index, 1);
				}
				else
				{
					char next = filtered[index + 1];
					filtered = filtered.Remove(index, next is >= '0' and <= '9' ? 2 : 1);
					index--;
				}
			}
			else if (value < ' ' || value > '~' || value is '>' or '<' or '/')
			{
				filtered = filtered.Remove(index--, 1);
			}
		}

		return filtered;
	}

	public void SetGamertag(string gamertag)
	{
		string filtered = FilterGamertag(gamertag).Trim();
		if (string.IsNullOrWhiteSpace(filtered))
		{
			throw new InvalidOperationException("Gamertag cannot be empty after filtering.");
		}

		if (filtered.Length > 15)
		{
			throw new InvalidOperationException("Gamertag must be 15 characters or fewer.");
		}

		Gamertag = filtered;
	}

	public byte[] ToArray()
	{
		var io = new EndianIO(new MemoryStream(404), EndianType.Big);
		try
		{
			io.Write(_liveFlags);
			io.Write(_reserved);
			io.WriteUnicodeString(Gamertag, 16);
			io.Write(OnlineXuid);
			io.Write(_cachedUserFlags);
			io.Write(OnlineServiceNetworkId);
			io.Write(_passcode);
			io.WriteAsciiString(OnlineDomain, 20);
			io.WriteAsciiString(OnlineKerberosRealm, 24);
			io.Write(_onlineKey);
			io.WriteAsciiString(_userPassportMembername, 114);
			io.WriteAsciiString(_userPassportPassword, 32);
			io.WriteAsciiString(_ownerPassportMembername, 114);
			io.Close();
			return XeCrypt.XeKeysObfuscate(1, io.ToArray(), DeveloperAccount);
		}
		finally
		{
			io.Close();
		}
	}
}

internal sealed class ProfileTitleInfo
{
	public uint TitleId { get; private set; }

	public string TitleName { get; private set; } = string.Empty;

	public int AchievementsPossible { get; private set; }

	public int AchievementsEarned { get; private set; }

	public int CreditPossible { get; private set; }

	public int CreditEarned { get; private set; }

	public ushort ReservedAchievementCount { get; private set; }

	public byte AvatarAwardsEarned { get; private set; }

	public byte AvatarAwardsPossible { get; private set; }

	public byte MaleAvatarAwardsEarned { get; private set; }

	public byte MaleAvatarAwardsPossible { get; private set; }

	public byte FemaleAvatarAwardsEarned { get; private set; }

	public byte FemaleAvatarAwardsPossible { get; private set; }

	public uint ReservedFlags { get; private set; }

	public DateTime? LastLoadedUtc { get; private set; }

	public static ProfileTitleInfo Parse(byte[] data)
	{
		var io = new EndianIO(data, EndianType.Big);
		try
		{
			long lastLoadedFileTime;
			var result = new ProfileTitleInfo
			{
				TitleId = io.ReadUInt32(),
				AchievementsPossible = io.ReadInt32(),
				AchievementsEarned = io.ReadInt32(),
				CreditPossible = io.ReadInt32(),
				CreditEarned = io.ReadInt32(),
				ReservedAchievementCount = io.ReadUInt16(),
				AvatarAwardsEarned = io.ReadByte(),
				AvatarAwardsPossible = io.ReadByte(),
				MaleAvatarAwardsEarned = io.ReadByte(),
				MaleAvatarAwardsPossible = io.ReadByte(),
				FemaleAvatarAwardsEarned = io.ReadByte(),
				FemaleAvatarAwardsPossible = io.ReadByte(),
				ReservedFlags = io.ReadUInt32()
			};

			lastLoadedFileTime = io.ReadInt64();
			result.LastLoadedUtc = lastLoadedFileTime == 0 ? null : DateTime.FromFileTimeUtc(lastLoadedFileTime);
			result.TitleName = io.ReadNullTerminatedUnicodeString();
			return result;
		}
		finally
		{
			io.Close();
		}
	}

	public void ApplyAchievementDelta(int achievementsDelta, int creditDelta)
	{
		int newAchievements = AchievementsEarned + achievementsDelta;
		int newCredit = CreditEarned + creditDelta;
		if (newAchievements < 0 || newAchievements > AchievementsPossible)
		{
			throw new InvalidOperationException("Achievement totals would fall outside the title record bounds.");
		}

		if (newCredit < 0 || newCredit > CreditPossible)
		{
			throw new InvalidOperationException("Credit totals would fall outside the title record bounds.");
		}

		AchievementsEarned = newAchievements;
		CreditEarned = newCredit;
	}

	public byte[] ToArray()
	{
		var io = new EndianIO(new MemoryStream(41), EndianType.Big);
		try
		{
			io.Write(TitleId);
			io.Write(AchievementsPossible);
			io.Write(AchievementsEarned);
			io.Write(CreditPossible);
			io.Write(CreditEarned);
			io.Write(ReservedAchievementCount);
			io.Write(AvatarAwardsEarned);
			io.Write(AvatarAwardsPossible);
			io.Write(MaleAvatarAwardsEarned);
			io.Write(MaleAvatarAwardsPossible);
			io.Write(FemaleAvatarAwardsEarned);
			io.Write(FemaleAvatarAwardsPossible);
			io.Write(ReservedFlags);
			if (LastLoadedUtc.HasValue)
			{
				io.Write(LastLoadedUtc.Value.ToUniversalTime().ToFileTimeUtc());
			}
			else
			{
				io.Write(new byte[8]);
			}

			io.WriteNullTerminatedUnicodeString(TitleName);
			io.Close();
			return io.ToArray();
		}
		finally
		{
			io.Close();
		}
	}
}

internal sealed class ProfileAchievementInfo
{
	private const int KnownHeaderSize = 0x1C;
	private const uint AchievedOnlineMask = 0x00010000;
	private const uint AchievedMask = 0x00020000;
	private const uint ShowUnachievedMask = 0x00000008;
	private const uint PlatformMask = 0x00700000;
	private const uint Xbox360Platform = 0x00100000;

	public uint Id { get; private set; }

	public uint ImageId { get; private set; }

	public int Credit { get; private set; }

	public uint Flags { get; private set; }

	public bool AchievedOffline => (Flags & AchievedMask) != 0;

	public bool AchievedOnline => (Flags & AchievedOnlineMask) != 0;

	public bool IsUnlocked => AchievedOffline || AchievedOnline;

	public bool ShowUnachieved => (Flags & ShowUnachievedMask) != 0;

	public DateTime? AchievedAtUtc { get; private set; }

	public string Label { get; private set; } = string.Empty;

	public string Description { get; private set; } = string.Empty;

	public string UnachievedDescription { get; private set; } = string.Empty;

	public static ProfileAchievementInfo Parse(byte[] data)
	{
		var io = new EndianIO(data, EndianType.Big);
		try
		{
			int dataSize = io.ReadInt32();
			if (dataSize < KnownHeaderSize)
			{
				throw new InvalidDataException(
					"Invalid achievement record size: " + dataSize.ToString(CultureInfo.InvariantCulture));
			}

			long achievedAt = 0;
			var result = new ProfileAchievementInfo
			{
				Id = io.ReadUInt32(),
				ImageId = io.ReadUInt32(),
				Credit = io.ReadInt32(),
				Flags = io.ReadUInt32()
			};

			achievedAt = io.ReadInt64();
			io.Position += dataSize - KnownHeaderSize;
			result.AchievedAtUtc = achievedAt == 0 ? null : DateTime.FromFileTimeUtc(achievedAt);
			result.Label = io.ReadNullTerminatedUnicodeString();
			result.Description = io.ReadNullTerminatedUnicodeString();
			result.UnachievedDescription = io.ReadNullTerminatedUnicodeString();
			return result;
		}
		finally
		{
			io.Close();
		}
	}

	public void UnlockOffline()
	{
		if (IsUnlocked)
		{
			throw new InvalidOperationException("Achievement is already unlocked.");
		}

		Flags |= AchievedMask;
		Flags &= ~AchievedOnlineMask;
		Flags = (Flags & ~PlatformMask) | Xbox360Platform;
		AchievedAtUtc = null;
	}

	public void UnlockOnline(DateTime achievedAtUtc)
	{
		if (IsUnlocked)
		{
			throw new InvalidOperationException("Achievement is already unlocked.");
		}

		Flags |= AchievedOnlineMask;
		Flags &= ~AchievedMask;
		Flags = (Flags & ~PlatformMask) | Xbox360Platform;
		AchievedAtUtc = achievedAtUtc.ToUniversalTime();
	}

	public void Lock()
	{
		if (!IsUnlocked)
		{
			throw new InvalidOperationException("Achievement is already locked.");
		}

		Flags &= ~AchievedMask;
		Flags &= ~AchievedOnlineMask;
		AchievedAtUtc = null;
	}

	public byte[] ToArray()
	{
		var io = new EndianIO(new MemoryStream(KnownHeaderSize), EndianType.Big);
		try
		{
			io.Write(KnownHeaderSize);
			io.Write(Id);
			io.Write(ImageId);
			io.Write(Credit);
			io.Write(Flags);
			io.Write(AchievedAtUtc?.ToUniversalTime().ToFileTimeUtc() ?? 0L);
			io.WriteNullTerminatedUnicodeString(Label);
			io.WriteNullTerminatedUnicodeString(Description);
			io.WriteNullTerminatedUnicodeString(UnachievedDescription);
			io.Close();
			return io.ToArray();
		}
		finally
		{
			io.Close();
		}
	}
}

internal sealed class ProfileSettingInfo
{
	public ulong Id { get; private set; }

	public ProfileSettingType Type { get; private set; }

	public uint Unknown1 { get; private set; }

	public byte[] Unknown2 { get; private set; } = Array.Empty<byte>();

	public object? Value { get; private set; }

	public byte[]? RawBytes { get; private set; }

	public uint? BinaryFlags { get; private set; }

	public static ProfileSettingInfo Parse(byte[] data)
	{
		var io = new EndianIO(data, EndianType.Big);
		try
		{
			ulong settingId = io.ReadUInt32();
			uint unknown1 = io.ReadUInt32();
			ProfileSettingType type = (ProfileSettingType)io.ReadByte();
			byte[] unknown2 = io.ReadByteArray(7);

			object? value = null;
			byte[]? rawBytes = null;
			uint? binaryFlags = null;

			switch (type)
			{
				case ProfileSettingType.Context:
				case ProfileSettingType.Int32:
					value = io.ReadInt32();
					rawBytes = io.ReadByteArray(4);
					break;
				case ProfileSettingType.Int64:
					value = io.ReadInt64();
					break;
				case ProfileSettingType.Double:
					value = io.ReadDouble();
					break;
				case ProfileSettingType.Unicode:
				case ProfileSettingType.Binary:
				{
					int length = io.ReadInt32();
					binaryFlags = io.ReadUInt32();
					rawBytes = io.ReadByteArray(length);
					value = type == ProfileSettingType.Unicode
						? Encoding.BigEndianUnicode.GetString(rawBytes).TrimEnd('\0')
						: (object)rawBytes;
					break;
				}
				case ProfileSettingType.Float:
					value = io.ReadSingle();
					rawBytes = io.ReadByteArray(4);
					break;
				case ProfileSettingType.DateTime:
				{
					long ticks = io.ReadInt64();
					value = ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
					break;
				}
				default:
					rawBytes = io.ReadByteArray(8);
					value = rawBytes;
					break;
			}

			return new ProfileSettingInfo
			{
				Id = settingId,
				Type = type,
				Unknown1 = unknown1,
				Unknown2 = unknown2,
				Value = value,
				RawBytes = rawBytes,
				BinaryFlags = binaryFlags
			};
		}
		finally
		{
			io.Close();
		}
	}

	public static ProfileSettingInfo Create(ulong id, ProfileSettingType type, object? value, uint? binaryFlags = null)
	{
		var setting = new ProfileSettingInfo
		{
			Id = id,
			Type = type,
			Unknown2 = new byte[7]
		};
		setting.ApplyValue(value, binaryFlags, preserveExistingPayload: false);
		return setting;
	}

	public ProfileSettingInfo WithValue(object? value, uint? binaryFlags = null)
	{
		var setting = new ProfileSettingInfo
		{
			Id = Id,
			Type = Type,
			Unknown1 = Unknown1,
			Unknown2 = (byte[])Unknown2.Clone(),
			RawBytes = RawBytes == null ? null : (byte[])RawBytes.Clone(),
			BinaryFlags = BinaryFlags
		};
		setting.ApplyValue(value, binaryFlags, preserveExistingPayload: true);
		return setting;
	}

	public byte[] ToArray()
	{
		if (Id > uint.MaxValue)
		{
			throw new InvalidOperationException("Profile setting IDs are limited to 32 bits.");
		}

		var io = new EndianIO(new MemoryStream(20), EndianType.Big);
		try
		{
			io.Write((uint)Id);
			io.Write(Unknown1);
			io.Write((byte)Type);
			io.Write(Unknown2);

			switch (Type)
			{
				case ProfileSettingType.Context:
				case ProfileSettingType.Int32:
					io.Write(Convert.ToInt32(Value, CultureInfo.InvariantCulture));
					io.Write(RawBytes is { Length: 4 } ? RawBytes : new byte[4]);
					break;
				case ProfileSettingType.Int64:
					io.Write(Convert.ToInt64(Value, CultureInfo.InvariantCulture));
					break;
				case ProfileSettingType.Double:
					io.Write(Convert.ToDouble(Value, CultureInfo.InvariantCulture));
					break;
				case ProfileSettingType.Unicode:
				case ProfileSettingType.Binary:
				{
					byte[] payload = RawBytes ?? Array.Empty<byte>();
					io.Write(payload.Length);
					io.Write(BinaryFlags ?? 0u);
					io.Write(payload);
					break;
				}
				case ProfileSettingType.Float:
					io.Write(Convert.ToSingle(Value, CultureInfo.InvariantCulture));
					io.Write(RawBytes is { Length: 4 } ? RawBytes : new byte[4]);
					break;
				case ProfileSettingType.DateTime:
				{
					long ticks = Value is DateTime dateTime ? dateTime.ToUniversalTime().Ticks : 0L;
					io.Write(ticks);
					break;
				}
				default:
					io.Write(RawBytes is { Length: 8 } ? RawBytes : new byte[8]);
					break;
			}

			io.Close();
			return io.ToArray();
		}
		finally
		{
			io.Close();
		}
	}

	private void ApplyValue(object? value, uint? binaryFlags, bool preserveExistingPayload)
	{
		switch (Type)
		{
			case ProfileSettingType.Context:
			case ProfileSettingType.Int32:
				Value = Convert.ToInt32(value, CultureInfo.InvariantCulture);
				RawBytes = preserveExistingPayload && RawBytes is { Length: 4 } ? RawBytes : new byte[4];
				BinaryFlags = null;
				break;
			case ProfileSettingType.Int64:
				Value = Convert.ToInt64(value, CultureInfo.InvariantCulture);
				RawBytes = null;
				BinaryFlags = null;
				break;
			case ProfileSettingType.Double:
				Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
				RawBytes = null;
				BinaryFlags = null;
				break;
			case ProfileSettingType.Unicode:
			{
				string text = value?.ToString() ?? string.Empty;
				bool nullTerminate = preserveExistingPayload && RawBytes is { Length: >= 2 } &&
					RawBytes[^1] == 0 &&
					RawBytes[^2] == 0;
				byte[] payload = Encoding.BigEndianUnicode.GetBytes(nullTerminate ? text + '\0' : text);
				Value = text;
				RawBytes = payload;
				BinaryFlags = binaryFlags ?? BinaryFlags ?? 0u;
				break;
			}
			case ProfileSettingType.Binary:
				RawBytes = value switch
				{
					byte[] data => (byte[])data.Clone(),
					null => Array.Empty<byte>(),
					_ => throw new InvalidOperationException("Binary settings require a byte array value.")
				};
				Value = RawBytes;
				BinaryFlags = binaryFlags ?? BinaryFlags ?? 0u;
				break;
			case ProfileSettingType.Float:
				Value = Convert.ToSingle(value, CultureInfo.InvariantCulture);
				RawBytes = preserveExistingPayload && RawBytes is { Length: 4 } ? RawBytes : new byte[4];
				BinaryFlags = null;
				break;
			case ProfileSettingType.DateTime:
				Value = value switch
				{
					null => null,
					DateTime dateTime => dateTime.ToUniversalTime(),
					_ => throw new InvalidOperationException("DateTime settings require a DateTime value.")
				};
				RawBytes = null;
				BinaryFlags = null;
				break;
			default:
				Value = value;
				RawBytes = preserveExistingPayload && RawBytes is { Length: 8 } ? RawBytes : new byte[8];
				BinaryFlags = null;
				break;
		}
	}
}
