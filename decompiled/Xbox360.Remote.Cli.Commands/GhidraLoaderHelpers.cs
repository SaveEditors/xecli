using System;
using System.IO;
using System.Linq;

namespace Xbox360.Remote.Cli.Commands;

internal static class GhidraLoaderHelpers
{
	public static bool IsXexFile(string path)
	{
		if (path.EndsWith(".xex", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			Span<byte> buffer = stackalloc byte[4];
			if (fileStream.Read(buffer) != 4)
			{
				return false;
			}
			return buffer[0] == 88 && buffer[1] == 69 && buffer[2] == 88 && buffer[3] == 50;
		}
		catch
		{
			return false;
		}
	}

	public static bool HasXexLoader(string? ghidraHome)
	{
		if (string.IsNullOrWhiteSpace(ghidraHome))
		{
			return CheckUserExtensions();
		}
		if (File.Exists(Path.Combine(ghidraHome, "Ghidra", "Extensions", "XEXLoaderWV", "lib", "XEXLoaderWV.jar")))
		{
			return true;
		}
		string path = Path.Combine(ghidraHome, "Ghidra", "Extensions");
		if (!Directory.Exists(path))
		{
			return CheckUserExtensions();
		}
		if (Directory.EnumerateFiles(path, "XEXLoaderWV.jar", SearchOption.AllDirectories).Any())
		{
			return true;
		}
		return CheckUserExtensions();
	}

	private static bool CheckUserExtensions()
	{
		try
		{
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			if (string.IsNullOrWhiteSpace(folderPath))
			{
				return false;
			}
			string path = Path.Combine(folderPath, "ghidra");
			if (!Directory.Exists(path))
			{
				return false;
			}
			return Directory.EnumerateFiles(path, "XEXLoaderWV.jar", SearchOption.AllDirectories).Any();
		}
		catch
		{
			return false;
		}
	}
}
