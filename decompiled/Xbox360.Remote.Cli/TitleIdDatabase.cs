using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Xbox360.Remote.Cli;

internal sealed class TitleIdDatabase
{
	private static readonly Lazy<TitleIdDatabase> LazyInstance = new Lazy<TitleIdDatabase>(Load);

	private readonly Dictionary<uint, List<TitleIdEntry>> byTitleId;

	public static TitleIdDatabase Instance => LazyInstance.Value;

	private TitleIdDatabase(Dictionary<uint, List<TitleIdEntry>> byTitleId)
	{
		this.byTitleId = byTitleId;
	}

	public bool TryResolve(uint titleId, uint? mediaId, out TitleIdEntry? entry)
	{
		entry = null;
		if (!byTitleId.TryGetValue(titleId, out List<TitleIdEntry> value) || value.Count == 0)
		{
			return false;
		}
		if (mediaId.HasValue)
		{
			entry = value.FirstOrDefault((TitleIdEntry e) => e.MediaId == mediaId.Value) ?? value.FirstOrDefault();
			return entry != null;
		}
		entry = value[0];
		return true;
	}

	public IReadOnlyList<TitleIdEntry> FindAll(uint titleId)
	{
		if (!byTitleId.TryGetValue(titleId, out List<TitleIdEntry> value))
		{
			return Array.Empty<TitleIdEntry>();
		}
		return value;
	}

	private static TitleIdDatabase Load()
	{
		Dictionary<uint, List<TitleIdEntry>> data = new Dictionary<uint, List<TitleIdEntry>>();
		foreach (string sourcePath in GetSourcePaths())
		{
			if (!File.Exists(sourcePath))
			{
				continue;
			}
			try
			{
				if (sourcePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
				{
					LoadCsv(sourcePath, data);
				}
				else if (sourcePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
				{
					LoadTildeList(sourcePath, data);
				}
			}
			catch
			{
			}
		}
		return new TitleIdDatabase(data);
	}

	private static IEnumerable<string> GetSourcePaths()
	{
		string baseDir = AppContext.BaseDirectory;
		yield return Path.Combine(baseDir, "Assets", "xbox360_gamelist.csv");
		yield return Path.Combine(baseDir, "Assets", "xbox360_titleids.txt");
		yield return Path.Combine(CliPaths.ConfigDirectory, "titleids.local.csv");
	}

	private static void LoadCsv(string path, Dictionary<uint, List<TitleIdEntry>> data)
	{
		using StreamReader streamReader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		bool flag = false;
		while (!streamReader.EndOfStream)
		{
			string text = streamReader.ReadLine();
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			List<string> list = ParseCsvLine(text);
			if (list.Count < 2)
			{
				continue;
			}
			if (!flag)
			{
				flag = true;
				if (list[0].Contains("Game", StringComparison.OrdinalIgnoreCase) && list.Any((string f) => f.Contains("Title", StringComparison.OrdinalIgnoreCase)))
				{
					continue;
				}
			}
			string text2 = list.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
			string text3 = list.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
			string serial = list.ElementAtOrDefault(2)?.Trim();
			string type = list.ElementAtOrDefault(3)?.Trim();
			string region = list.ElementAtOrDefault(4)?.Trim();
			string xexCrc = list.ElementAtOrDefault(5)?.Trim();
			string text4 = list.ElementAtOrDefault(6)?.Trim();
			string wave = list.ElementAtOrDefault(7)?.Trim();
			if (TryParseHex(text3, out var value))
			{
				uint value2;
				uint? mediaId = (TryParseHex(text4, out value2) ? new uint?(value2) : ((uint?)null));
				if (string.IsNullOrWhiteSpace(text2))
				{
					text2 = $"Title {value:X8}";
				}
				TitleIdEntry item = new TitleIdEntry(value, mediaId, text2, serial, type, region, xexCrc, wave);
				if (!data.TryGetValue(value, out List<TitleIdEntry> value3))
				{
					value3 = (data[value] = new List<TitleIdEntry>());
				}
				value3.Add(item);
			}
		}
	}

	private static void LoadTildeList(string path, Dictionary<uint, List<TitleIdEntry>> data)
	{
		foreach (string item2 in File.ReadLines(path))
		{
			string text = item2.Trim();
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			int num = text.IndexOf('~');
			if (num <= 0 || num == text.Length - 1)
			{
				continue;
			}
			string text2 = text.Substring(0, num).Trim();
			string name = text.Substring(num + 1).Trim();
			if (TryParseHex(text2, out var value))
			{
				if (string.IsNullOrWhiteSpace(name))
				{
					name = $"Title {value:X8}";
				}
				TitleIdEntry item = new TitleIdEntry(value, null, name, null, null, null, null, null);
				if (!data.TryGetValue(value, out List<TitleIdEntry> value2))
				{
					value2 = (data[value] = new List<TitleIdEntry>());
				}
				if (!value2.Any((TitleIdEntry e) => !e.MediaId.HasValue && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
				{
					value2.Add(item);
				}
			}
		}
	}

	private static bool TryParseHex(string? text, out uint value)
	{
		value = 0u;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		if (text2.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text2 = text2.Substring(2);
		}
		return uint.TryParse(text2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
	}

	private static List<string> ParseCsvLine(string line)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		for (int i = 0; i < line.Length; i++)
		{
			char c = line[i];
			if (flag)
			{
				if (c == '"')
				{
					if (i + 1 < line.Length && line[i + 1] == '"')
					{
						stringBuilder.Append('"');
						i++;
					}
					else
					{
						flag = false;
					}
				}
				else
				{
					stringBuilder.Append(c);
				}
				continue;
			}
			switch (c)
			{
			case '"':
				flag = true;
				break;
			case ',':
				list.Add(stringBuilder.ToString());
				stringBuilder.Clear();
				break;
			default:
				stringBuilder.Append(c);
				break;
			}
		}
		list.Add(stringBuilder.ToString());
		return list;
	}
}
