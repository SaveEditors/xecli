using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;

namespace Xbox360.Remote.Cli.Commands;

internal static class PluginHelpers
{
	internal sealed class PluginConfig
	{
		public required string Path { get; init; }

		public required List<string> Lines { get; init; }

		public required Dictionary<int, string> Slots { get; init; }

		public void SetSlot(int slot, string value)
		{
			Slots[slot] = value;
		}
	}

	public static async Task<PluginConfig> LoadAsync(string ip, int port, string user, string pass, int timeoutMs, string? iniPath)
	{
		string path = NormalizeIniPath(iniPath);
		List<string> lines = (await DownloadTextAsync(ip, port, user, pass, timeoutMs, path)).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
		Dictionary<int, string> slots = ParseSlots(lines);
		return new PluginConfig
		{
			Path = path,
			Lines = lines,
			Slots = slots
		};
	}

	public static async Task SaveAsync(string ip, int port, string user, string pass, int timeoutMs, PluginConfig config, bool backup, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<string> values = UpdatePluginLines(config.Lines, config.Slots);
		string s = string.Join("\r\n", values);
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		if (backup)
		{
			string remotePath = config.Path + ".bak";
			await FtpHelpers.UploadBytesVerifiedAsync(ip, port, user, pass, timeoutMs, Encoding.UTF8.GetBytes(string.Join("\r\n", config.Lines)), remotePath, ensureRemoteDirectory: true, null, cancellationToken);
		}
		await FtpHelpers.UploadBytesVerifiedAsync(ip, port, user, pass, timeoutMs, bytes, config.Path, ensureRemoteDirectory: true, null, cancellationToken);
	}

	private static Dictionary<int, string> ParseSlots(List<string> lines)
	{
		Dictionary<int, string> dictionary = new Dictionary<int, string>();
		bool flag = false;
		foreach (string line in lines)
		{
			string text = line.Trim();
			if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
			{
				flag = text.Equals("[Plugins]", StringComparison.OrdinalIgnoreCase);
			}
			else if (flag && !text.StartsWith(';') && text.Contains('=', StringComparison.Ordinal))
			{
				int num = text.IndexOf('=');
				string text2 = text.Substring(0, num).Trim();
				string value = text.Substring(num + 1).Trim();
				if (text2.StartsWith("plugin", StringComparison.OrdinalIgnoreCase) && int.TryParse(text2.Substring("plugin".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
				{
					dictionary[result] = value;
				}
			}
		}
		for (int i = 1; i <= Math.Max(5, dictionary.Keys.DefaultIfEmpty(0).Max()); i++)
		{
			dictionary.TryAdd(i, string.Empty);
		}
		return dictionary;
	}

	private static List<string> UpdatePluginLines(List<string> sourceLines, Dictionary<int, string> slots)
	{
		List<string> list = new List<string>(sourceLines);
		bool flag = false;
		bool flag2 = false;
		HashSet<int> hashSet = new HashSet<int>();
		int key;
		string value;
		for (int i = 0; i < list.Count; i++)
		{
			string text = list[i].Trim();
			if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
			{
				if (flag && hashSet.Count < slots.Count)
				{
					foreach (KeyValuePair<int, string> item in slots.OrderBy<KeyValuePair<int, string>, int>((KeyValuePair<int, string> s) => s.Key))
					{
						item.Deconstruct(out key, out value);
						int num = key;
						string value2 = value;
						if (!hashSet.Contains(num))
						{
							list.Insert(i++, $"plugin{num} = {value2}");
						}
					}
				}
				flag = text.Equals("[Plugins]", StringComparison.OrdinalIgnoreCase);
				flag2 = flag2 || flag;
			}
			else if (flag && !text.StartsWith(';') && text.Contains('=', StringComparison.Ordinal))
			{
				string text2 = text[..text.IndexOf('=')].Trim();
				if (text2.StartsWith("plugin", StringComparison.OrdinalIgnoreCase) && int.TryParse(text2.Substring("plugin".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && slots.TryGetValue(result, out string value3))
				{
					list[i] = $"plugin{result} = {value3}";
					hashSet.Add(result);
				}
			}
		}
		if (!flag2)
		{
			if (list.Count > 0)
			{
				if (!string.IsNullOrWhiteSpace(list[list.Count - 1]))
				{
					list.Add(string.Empty);
				}
			}
			list.Add("[Plugins]");
			{
				foreach (KeyValuePair<int, string> item2 in slots.OrderBy<KeyValuePair<int, string>, int>((KeyValuePair<int, string> s) => s.Key))
				{
					item2.Deconstruct(out key, out value);
					int value4 = key;
					string value5 = value;
					list.Add($"plugin{value4} = {value5}");
				}
				return list;
			}
		}
		if (flag && hashSet.Count < slots.Count)
		{
			foreach (KeyValuePair<int, string> item3 in slots.OrderBy<KeyValuePair<int, string>, int>((KeyValuePair<int, string> s) => s.Key))
			{
				item3.Deconstruct(out key, out value);
				int num2 = key;
				string value6 = value;
				if (!hashSet.Contains(num2))
				{
					list.Add($"plugin{num2} = {value6}");
				}
			}
		}
		return list;
	}

	private static async Task<string> DownloadTextAsync(string ip, int port, string user, string pass, int timeoutMs, string path)
	{
		string result;
		await using (AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port))
		{
			client.Config.ConnectTimeout = timeoutMs;
			client.Config.ReadTimeout = timeoutMs;
			client.Config.DataConnectionConnectTimeout = timeoutMs;
			client.Config.DataConnectionReadTimeout = timeoutMs;
			await client.Connect();
			using MemoryStream ms = new MemoryStream();
			await client.DownloadStream(ms, path, 0L, null, default(CancellationToken), 0L);
			result = Encoding.UTF8.GetString(ms.ToArray());
		}
		return result;
	}

	private static string NormalizeIniPath(string? path)
	{
		return FtpHelpers.NormalizePath(string.IsNullOrWhiteSpace(path) ? "/Hdd1/launch.ini" : path);
	}
}
