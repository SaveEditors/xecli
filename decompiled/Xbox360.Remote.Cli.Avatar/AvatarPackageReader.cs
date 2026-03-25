using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarPackageReader
{
	private const int HeaderReadLength = 6144;

	private const int TitleIdOffset = 864;

	private const int DisplayNameRegionStart = 1040;

	private const int DisplayNameRegionLength = 1024;

	private const int PublisherRegionStart = 5648;

	private const int PublisherRegionLength = 128;

	private const int GameNameRegionStart = 5776;

	private const int GameNameRegionLength = 128;

	public static AvatarPackageMetadata ReadMetadata(string filePath)
	{
		using FileStream fileStream = File.OpenRead(filePath);
		byte[] array = new byte[(int)Math.Min(6144L, fileStream.Length)];
		int i;
		int num;
		for (i = 0; i < array.Length; i += num)
		{
			num = fileStream.Read(array, i, array.Length - i);
			if (num == 0)
			{
				break;
			}
		}
		if (i < 4)
		{
			return new AvatarPackageMetadata(AvatarPackageMagic.Unknown, 0u, null, null, null);
		}
		AvatarPackageMagic magic = ParseMagic(array);
		uint titleId = ((i >= 868) ? ReadUInt32BigEndian(array, 864) : 0u);
		string displayName = NormalizeDisplayName(ReadUtf16LeRegion(array, 1040, 1024));
		string publisher = NormalizePublisher(ReadUtf16LeRegion(array, 5648, 128));
		string gameName = NormalizeGameName(ReadUtf16LeRegion(array, 5776, 128));
		return new AvatarPackageMetadata(magic, titleId, displayName, publisher, gameName);
	}

	public static bool TryParseTitleIdFromPath(string path, out uint titleId)
	{
		titleId = 0u;
		for (DirectoryInfo parent = Directory.GetParent(path); parent != null; parent = parent.Parent)
		{
			if (uint.TryParse(parent.Name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out titleId))
			{
				return true;
			}
		}
		return false;
	}

	public static bool IsSupportedPackage(string filePath)
	{
		using FileStream fileStream = File.OpenRead(filePath);
		Span<byte> span = stackalloc byte[4];
		if (fileStream.Read(span) != span.Length)
		{
			return false;
		}
		return ((ReadOnlySpan<byte>)span).SequenceEqual("LIVE"u8) || ((ReadOnlySpan<byte>)span).SequenceEqual("PIRS"u8) || ((ReadOnlySpan<byte>)span).SequenceEqual("CON "u8);
	}

	private static AvatarPackageMagic ParseMagic(ReadOnlySpan<byte> buffer)
	{
		ReadOnlySpan<byte> span = buffer.Slice(0, 4);
		if (span.SequenceEqual("LIVE"u8))
		{
			return AvatarPackageMagic.Live;
		}
		if (span.SequenceEqual("PIRS"u8))
		{
			return AvatarPackageMagic.Pirs;
		}
		if (span.SequenceEqual("CON "u8))
		{
			return AvatarPackageMagic.Con;
		}
		return AvatarPackageMagic.Unknown;
	}

	private static uint ReadUInt32BigEndian(byte[] buffer, int offset)
	{
		return (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]);
	}

	private static string? ReadUtf16LeRegion(byte[] buffer, int start, int length)
	{
		if (start >= buffer.Length)
		{
			return null;
		}
		int num = Math.Min(buffer.Length, start + length);
		int i;
		for (i = start; i + 1 < num && buffer[i] == 0 && buffer[i + 1] == 0; i += 2)
		{
		}
		if (i + 1 >= num)
		{
			return null;
		}
		int j;
		for (j = i; j + 1 < num && (buffer[j] != 0 || buffer[j + 1] != 0); j += 2)
		{
		}
		int num2 = j - i;
		if (num2 <= 0)
		{
			return null;
		}
		string text = Encoding.Unicode.GetString(buffer, i, num2).Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return null;
	}

	private static string? NormalizeDisplayName(string? value)
	{
		return NormalizeCommon(value, stripLeadingNoisePrefix: false);
	}

	private static string? NormalizePublisher(string? value)
	{
		string text = NormalizeCommon(value, stripLeadingNoisePrefix: true);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (text.Equals("Xbox Live", StringComparison.OrdinalIgnoreCase) || text.Equals("XBOX LIVE", StringComparison.OrdinalIgnoreCase))
		{
			return "Xbox LIVE";
		}
		return text;
	}

	private static string? NormalizeGameName(string? value)
	{
		return NormalizeCommon(value, stripLeadingNoisePrefix: true);
	}

	private static string? NormalizeCommon(string? value, bool stripLeadingNoisePrefix)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string input = value.Normalize(NormalizationForm.FormKC).Replace('\0', ' ').Replace('\ufeff', ' ')
			.Trim();
		input = Regex.Replace(input, "\\s+", " ");
		input = RepairCommonMojibake(input);
		if (stripLeadingNoisePrefix)
		{
			while (input.Length >= 2 && (IsNoisePrefix(input[0]) || input[0] > '\u007f') && IsLikelyWordStart(input[1]))
			{
				string text = input;
				input = text.Substring(1, text.Length - 1).TrimStart();
			}
			while (input.Length >= 3 && char.IsUpper(input[0]) && input[0] == input[1] && char.IsUpper(input[1]) && char.IsLetter(input[2]))
			{
				string text = input;
				input = text.Substring(1, text.Length - 1).TrimStart();
			}
		}
		input = Regex.Replace(input, "\\s+", " ").Trim();
		if (!string.IsNullOrWhiteSpace(input))
		{
			return input;
		}
		return null;
	}

	private static string RepairCommonMojibake(string value)
	{
		return value.Replace("蓢¢", "™", StringComparison.Ordinal).Replace("胢\u0099", "'", StringComparison.Ordinal).Replace("蓢¢", "™", StringComparison.Ordinal)
			.Replace("胢\u0099", "'", StringComparison.Ordinal)
			.Replace("Xbox Live", "Xbox LIVE", StringComparison.Ordinal);
	}

	private static bool IsNoisePrefix(char value)
	{
		bool flag;
		switch (value)
		{
		case '+':
		case '-':
		case '.':
		case '_':
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return char.IsLower(value);
		}
		return true;
	}

	private static bool IsLikelyWordStart(char value)
	{
		if (!char.IsUpper(value) && !char.IsDigit(value))
		{
			return value == '#';
		}
		return true;
	}
}
