using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spectre.Console;
using Color = Spectre.Console.Color;

namespace Xbox360.Remote.Cli.Homebrew;

internal static class HostDriveService
{
	public static IReadOnlyList<HostDriveInfo> GetRemovableDrives()
	{
		if (!OperatingSystem.IsWindows())
		{
			return Array.Empty<HostDriveInfo>();
		}
		return (from drive in (from drive in DriveInfo.GetDrives()
				where drive.IsReady && drive.DriveType == DriveType.Removable
				select drive).OrderBy<DriveInfo, string>((DriveInfo drive) => drive.Name, StringComparer.OrdinalIgnoreCase)
			select new HostDriveInfo(NormalizeDriveKey(drive.RootDirectory.FullName), drive.RootDirectory.FullName, BuildDisplayName(drive), SafeGet(() => drive.VolumeLabel), SafeGet(() => drive.DriveFormat), SafeGet(() => drive.TotalSize), SafeGet(() => drive.AvailableFreeSpace), IsRemovable: true)).ToArray();
	}

	public static string ResolveTargetRoot(string? token, bool autoConfirm)
	{
		if (!string.IsNullOrWhiteSpace(token))
		{
			return ResolveExplicitTarget(token.Trim());
		}
		IReadOnlyList<HostDriveInfo> removableDrives = GetRemovableDrives();
		if (removableDrives.Count == 0)
		{
			throw new InvalidOperationException("No removable USB drives were detected. Use --usb <drive-or-path> to target a folder manually.");
		}
		if (removableDrives.Count == 1 && autoConfirm)
		{
			return removableDrives[0].RootPath;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]USB Targets[/]").RuleStyle("grey"));
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[white]#[/]"));
		table.AddColumn(new TableColumn("[springgreen3_1]Drive[/]"));
		table.AddColumn(new TableColumn("[cyan]Label[/]"));
		table.AddColumn(new TableColumn("[deepskyblue1]File System[/]"));
		table.AddColumn(new TableColumn("[gold1]Capacity[/]"));
		table.AddColumn(new TableColumn("[grey]Free[/]"));
		for (int i = 0; i < removableDrives.Count; i++)
		{
			HostDriveInfo hostDriveInfo = removableDrives[i];
			table.AddRow($"[white]{i + 1}[/]", "[springgreen3_1]" + Markup.Escape(hostDriveInfo.Key) + "[/]", string.IsNullOrWhiteSpace(hostDriveInfo.VolumeLabel) ? "[grey]unnamed[/]" : ("[cyan]" + Markup.Escape(hostDriveInfo.VolumeLabel) + "[/]"), string.IsNullOrWhiteSpace(hostDriveInfo.FileSystem) ? "[grey]unknown[/]" : ("[deepskyblue1]" + Markup.Escape(hostDriveInfo.FileSystem) + "[/]"), "[gold1]" + FormatBytes(hostDriveInfo.TotalBytes) + "[/]", "[grey]" + FormatBytes(hostDriveInfo.FreeBytes) + "[/]");
		}
		AnsiConsole.Write(table);
		SelectionPrompt<HostDriveInfo> obj = new SelectionPrompt<HostDriveInfo>().Title("[bold white]Select the USB drive to use[/]");
		Color? foreground = Color.DeepSkyBlue1;
		Decoration? decoration = Decoration.Bold;
		return AnsiConsole.Prompt(obj.HighlightStyle(new Style(foreground, null, decoration)).UseConverter((HostDriveInfo drive) => $"{drive.Key} [{FormatBytes(drive.TotalBytes)}] {(string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "unnamed" : drive.VolumeLabel)}").AddChoices(removableDrives)).RootPath;
	}

	private static string ResolveExplicitTarget(string token)
	{
		IReadOnlyList<HostDriveInfo> removableDrives = GetRemovableDrives();
		if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= 1 && result <= removableDrives.Count)
		{
			return removableDrives[result - 1].RootPath;
		}
		string text = token.Trim();
		bool flag = false;
		string text2 = text;
		if (text2.Length == 1 && char.IsLetter(text2[0]))
		{
			text2 = $"{char.ToUpperInvariant(text2[0])}:\\";
			flag = true;
		}
		else if (text2.Length == 2 && char.IsLetter(text2[0]) && text2[1] == ':')
		{
			text2 = $"{char.ToUpperInvariant(text2[0])}:\\";
			flag = true;
		}
		else if (text2.Length == 3 && char.IsLetter(text2[0]) && text2[1] == ':' && (text2[2] == '\\' || text2[2] == '/'))
		{
			text2 = $"{char.ToUpperInvariant(text2[0])}:\\";
			flag = true;
		}
		string text3 = (Path.IsPathRooted(text2) ? Path.GetFullPath(text2) : Path.GetFullPath(text2));
		if (flag)
		{
			string driveKey = NormalizeDriveKey(text3);
			HostDriveInfo hostDriveInfo = removableDrives.FirstOrDefault((HostDriveInfo drive) => string.Equals(drive.Key, driveKey, StringComparison.OrdinalIgnoreCase));
			if (hostDriveInfo != null)
			{
				return hostDriveInfo.RootPath;
			}
		}
		Directory.CreateDirectory(text3);
		return text3;
	}

	private static string NormalizeDriveKey(string path)
	{
		string text = Path.GetPathRoot(path) ?? path;
		text = text.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (!text.EndsWith(":", StringComparison.Ordinal))
		{
			return text + ":";
		}
		return text;
	}

	private static string BuildDisplayName(DriveInfo drive)
	{
		string text = NormalizeDriveKey(drive.RootDirectory.FullName);
		string value = SafeGet(() => drive.VolumeLabel);
		if (string.IsNullOrWhiteSpace(value))
		{
			return text + " [" + FormatBytes(SafeGet(() => drive.TotalSize)) + "]";
		}
		return $"{text} [{FormatBytes(SafeGet(() => drive.TotalSize))}] {value}";
	}

	private static T SafeGet<T>(Func<T> getter)
	{
		try
		{
			return getter();
		}
		catch
		{
			return default(T);
		}
	}

	private static string FormatBytes(long bytes)
	{
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		double num = bytes;
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		if (num2 == 0)
		{
			return $"{num:0} {array[num2]}";
		}
		return $"{num:0.##} {array[num2]}";
	}
}
