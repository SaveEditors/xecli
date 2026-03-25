using System;
using System.Collections.Generic;
using System.Text;
using Spectre.Console;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class XbdmNotifyEvent
{
	public required string Raw { get; init; }

	public required string Type { get; init; }

	public required List<string> Commands { get; init; }

	public required Dictionary<string, string> Fields { get; init; }

	public string? GetString(string key)
	{
		if (!Fields.TryGetValue(key, out string value))
		{
			return null;
		}
		return value;
	}

	public string GetHexOrDefault(string key)
	{
		return GetString(key) ?? "n/a";
	}

	public string GetFaultOperationSummary()
	{
		string[] array = new string[4] { "write", "read", "readwrite", "execute" };
		foreach (string text in array)
		{
			if (Fields.TryGetValue(text, out string value))
			{
				return text + "=[white]" + Markup.Escape(value) + "[/]";
			}
		}
		return string.Empty;
	}

	public static XbdmNotifyEvent Parse(string raw)
	{
		List<string> list = Tokenize(raw);
		List<string> list2 = new List<string>();
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in list)
		{
			int num = item.IndexOf('=');
			if (num > 0)
			{
				string key = item.Substring(0, num).Trim();
				string value = item.Substring(num + 1).Trim().Trim('"');
				dictionary[key] = value;
			}
			else
			{
				list2.Add(item);
			}
		}
		return new XbdmNotifyEvent
		{
			Raw = raw,
			Type = ((list2.Count > 0) ? list2[0] : "notify"),
			Commands = list2,
			Fields = dictionary
		};
	}

	private static List<string> Tokenize(string raw)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder(raw.Length);
		bool flag = false;
		foreach (char c in raw)
		{
			if (c == '"')
			{
				flag = !flag;
				stringBuilder.Append(c);
			}
			else if (!flag && char.IsWhiteSpace(c))
			{
				if (stringBuilder.Length > 0)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
				}
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		if (stringBuilder.Length > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}
}
