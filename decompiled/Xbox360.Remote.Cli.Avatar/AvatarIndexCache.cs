using System.IO;
using System.Text.Json;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarIndexCache
{
	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		WriteIndented = false
	};

	public static string GetDefaultCachePath()
	{
		return GetCachePath(null);
	}

	public static string GetCachePath(string? explicitCachePath)
	{
		if (string.IsNullOrWhiteSpace(explicitCachePath))
		{
			return Path.Combine(CliPaths.ConfigDirectory, "avatar-index.v5.json");
		}
		string fullPath = Path.GetFullPath(explicitCachePath);
		if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
		{
			return Path.Combine(fullPath, "avatar-index.v5.json");
		}
		return fullPath;
	}

	public static AvatarLibraryIndex? TryLoad(string cachePath, AvatarCollectionFingerprint expectedFingerprint)
	{
		try
		{
			if (!File.Exists(cachePath))
			{
				return null;
			}
			AvatarLibraryIndex avatarLibraryIndex = JsonSerializer.Deserialize<AvatarLibraryIndex>(File.ReadAllText(cachePath), SerializerOptions);
			if (avatarLibraryIndex == null)
			{
				return null;
			}
			return object.Equals(avatarLibraryIndex.Fingerprint, expectedFingerprint) ? avatarLibraryIndex : null;
		}
		catch
		{
			return null;
		}
	}

	public static void Save(string cachePath, AvatarLibraryIndex index)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
		string contents = JsonSerializer.Serialize(index, SerializerOptions);
		File.WriteAllText(cachePath, contents);
	}
}
