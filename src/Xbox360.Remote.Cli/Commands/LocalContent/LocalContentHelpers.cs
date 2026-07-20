using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NoDev.Common;
using NoDev.Xdbf;
using NoDev.Xdbf.Records;
using Spectre.Console;

using XdbfNamespace = NoDev.Xdbf.Namespace;

namespace Xbox360.Remote.Cli.Commands;

internal static class LocalContentHelpers
{
	public const int HexPreviewLimit = 4096;

	public static int Fail(string message)
	{
		AnsiConsole.MarkupLine("[red]" + Markup.Escape(message) + "[/]");
		return 1;
	}

	public static bool TryResolveExistingFile(string input, out string fullPath, out string error)
	{
		fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input));
		if (File.Exists(fullPath))
		{
			error = string.Empty;
			return true;
		}

		error = "File not found: " + fullPath;
		return false;
	}

	public static string Hex(byte[] data) => Formatting.ByteArrayToHexString(data, upperCase: true);

	public static string FormatUInt32(uint value) => $"0x{value:X8}";

	public static string FormatUInt64(ulong value) => $"0x{value:X16}";

	public static string FormatBytes(byte[] data) =>
		data.Length == 0 ? string.Empty : Formatting.ByteArrayToHexString(data, upperCase: true);

	public static string MarkupValue(string? value, string color = "white") =>
		string.IsNullOrWhiteSpace(value)
			? "[grey]empty[/]"
			: "[" + color + "]" + Markup.Escape(value) + "[/]";

	public static bool TryParseUlong(string text, out ulong value)
	{
		string candidate = text.Trim();
		if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return ulong.TryParse(candidate[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
		}

		if (candidate.Any(static c => char.IsLetter(c)))
		{
			return ulong.TryParse(candidate, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
		}

		if (candidate.Length > 1 && candidate[0] == '0')
		{
			return ulong.TryParse(candidate, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
		}

		if (ulong.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
		{
			return true;
		}

		return ulong.TryParse(candidate, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
	}

	public static bool TryParseNamespace(string? text, out XdbfNamespace value)
	{
		if (!string.IsNullOrWhiteSpace(text) &&
			Enum.TryParse(text, ignoreCase: true, out value) &&
			Enum.IsDefined(typeof(XdbfNamespace), value))
		{
			return true;
		}

		if (!string.IsNullOrWhiteSpace(text) &&
			TryParseUlong(text, out ulong raw) &&
			raw <= ushort.MaxValue &&
			Enum.IsDefined(typeof(XdbfNamespace), (ushort)raw))
		{
			value = (XdbfNamespace)(ushort)raw;
			return true;
		}

		value = default;
		return false;
	}

	public static bool TryParseOrigin(string? text, out DataFileOrigin value)
	{
		if (string.Equals(text, "pec", StringComparison.OrdinalIgnoreCase))
		{
			value = DataFileOrigin.PEC;
			return true;
		}

		if (string.IsNullOrWhiteSpace(text) ||
			string.Equals(text, "profile", StringComparison.OrdinalIgnoreCase))
		{
			value = DataFileOrigin.Profile;
			return true;
		}

		if (TryParseUlong(text, out ulong raw) &&
			raw <= int.MaxValue &&
			Enum.IsDefined(typeof(DataFileOrigin), (int)raw))
		{
			value = (DataFileOrigin)(int)raw;
			return true;
		}

		value = default;
		return false;
	}

	public static IReadOnlyList<DataFileRecord> GetRecords(DataFile dataFile, XdbfNamespace? filter)
	{
		IEnumerable<DataFileRecord> records = filter.HasValue
			? dataFile.GetRecords(filter.Value)
			: Enum.GetValues<XdbfNamespace>().SelectMany(dataFile.GetRecords);

		return records
			.OrderBy(static record => record.Namespace)
			.ThenBy(static record => record.ID)
			.ToList();
	}

	public static void RenderHexPreview(byte[] data, int maxBytes = HexPreviewLimit)
	{
		int rendered = Math.Min(data.Length, maxBytes);
		for (int offset = 0; offset < rendered; offset += 16)
		{
			int count = Math.Min(16, rendered - offset);
			var hex = new StringBuilder(16 * 3);
			var ascii = new StringBuilder(16);
			for (int index = 0; index < 16; index++)
			{
				if (index < count)
				{
					byte value = data[offset + index];
					hex.Append(value.ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
					ascii.Append(value is >= 32 and <= 126 ? (char)value : '.');
				}
				else
				{
					hex.Append("   ");
				}
			}

			AnsiConsole.MarkupLine(
				$"[white]0x{offset:X8}[/] [deepskyblue1]{hex.ToString().TrimEnd()}[/] [springgreen3_1]{Markup.Escape(ascii.ToString())}[/]");
		}

		if (data.Length > rendered)
		{
			AnsiConsole.MarkupLine($"[yellow]Output truncated to {rendered} bytes. Use --out to export the full record.[/]");
		}
	}
}
