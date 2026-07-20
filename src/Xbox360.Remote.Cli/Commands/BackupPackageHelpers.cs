using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Xbox360.Remote.Cli.Commands;

internal sealed record BackupArtifact(string SourcePath, string EntryName);

internal static class BackupPackageHelpers
{
	public static string? FindExistingOutputPath(IEnumerable<string> paths, bool overwrite)
	{
		if (overwrite)
		{
			return null;
		}
		foreach (string path in paths)
		{
			if (File.Exists(path) || Directory.Exists(path))
			{
				return path;
			}
		}
		return null;
	}

	public static string? SaveOptionalText(string path, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
		File.WriteAllText(path, value.Trim() + Environment.NewLine);
		return path;
	}

	public static string WriteText(string path, string value)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
		File.WriteAllText(path, value);
		return path;
	}

	public static string CreateZip(string primaryOutputPath, IReadOnlyList<BackupArtifact> artifacts)
	{
		string text = Path.ChangeExtension(primaryOutputPath, ".zip");
		if (File.Exists(text))
		{
			File.Delete(text);
		}
		using (ZipArchive zipArchive = ZipFile.Open(text, ZipArchiveMode.Create))
		{
			for (int i = 0; i < artifacts.Count; i++)
			{
				BackupArtifact backupArtifact = artifacts[i];
				zipArchive.CreateEntryFromFile(backupArtifact.SourcePath, backupArtifact.EntryName, CompressionLevel.Optimal);
			}
		}
		return text;
	}

	public static string WriteSha256Manifest(string manifestPath, IReadOnlyList<BackupArtifact> artifacts)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? Environment.CurrentDirectory);
		using StreamWriter streamWriter = new StreamWriter(manifestPath, append: false);
		for (int i = 0; i < artifacts.Count; i++)
		{
			BackupArtifact backupArtifact = artifacts[i];
			streamWriter.WriteLine(ComputeSha256(backupArtifact.SourcePath) + " *" + backupArtifact.EntryName);
		}
		return manifestPath;
	}

	public static void VerifyCopiedOutput(string referencePath, string outputPath)
	{
		if (!FilesAreIdentical(referencePath, outputPath))
		{
			throw new InvalidOperationException("The final output file did not match the verified staging file.");
		}
	}

	public static void VerifyZipContents(string zipPath, IReadOnlyList<BackupArtifact> artifacts)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(zipPath);
		for (int i = 0; i < artifacts.Count; i++)
		{
			BackupArtifact backupArtifact = artifacts[i];
			ZipArchiveEntry? entry = zipArchive.GetEntry(backupArtifact.EntryName);
			if (entry == null)
			{
				throw new InvalidOperationException("Archive verification failed. Missing entry: " + backupArtifact.EntryName);
			}
			using Stream stream = entry.Open();
			using FileStream fileStream = new FileStream(backupArtifact.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (!StreamsAreIdentical(fileStream, stream))
			{
				throw new InvalidOperationException("Archive verification failed. Entry did not match source file: " + backupArtifact.EntryName);
			}
		}
	}

	public static string ComputeSha256(string path)
	{
		using FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		using SHA256 sHA = SHA256.Create();
		return Convert.ToHexString(sHA.ComputeHash(inputStream));
	}

	public static bool FilesAreIdentical(string leftPath, string rightPath)
	{
		FileInfo fileInfo = new FileInfo(leftPath);
		FileInfo fileInfo2 = new FileInfo(rightPath);
		if (!fileInfo.Exists || !fileInfo2.Exists || fileInfo.Length != fileInfo2.Length)
		{
			return false;
		}
		using FileStream left = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using FileStream right = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		return StreamsAreIdentical(left, right);
	}

	private static bool StreamsAreIdentical(Stream left, Stream right)
	{
		byte[] array = new byte[65536];
		byte[] array2 = new byte[65536];
		while (true)
		{
			int num = ReadChunk(left, array);
			int num2 = ReadChunk(right, array2);
			if (num != num2)
			{
				return false;
			}
			if (num == 0)
			{
				return true;
			}
			if (!array.AsSpan(0, num).SequenceEqual(array2.AsSpan(0, num2)))
			{
				return false;
			}
		}
	}

	private static int ReadChunk(Stream stream, byte[] buffer)
	{
		int num = 0;
		while (num < buffer.Length)
		{
			int num2 = stream.Read(buffer, num, buffer.Length - num);
			if (num2 == 0)
			{
				break;
			}
			num += num2;
		}
		return num;
	}
}
