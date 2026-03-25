using System;
using System.Collections.Generic;
using System.IO;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarInstallPlanner
{
	private const int OwnershipRegionStart = 572;

	private const int OwnershipRegionEnd = 812;

	private const int OwnershipEntryCount = 15;

	private const int OwnershipEntryStride = 16;

	public static AvatarInstallPlan PrepareInstallPlan(AvatarInstallRequest request)
	{
		if (request.Item == null)
		{
			throw new ArgumentNullException("request");
		}
		if (string.IsNullOrWhiteSpace(request.DeviceRoot))
		{
			throw new ArgumentException("Device root is required.", "request");
		}
		string text = ResolveWorkingDirectory(request.WorkingDirectory);
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(path3: request.Item.RelativeStorePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar), path1: text, path2: request.Item.TitleId.ToString("X8"));
		string directoryName = Path.GetDirectoryName(text2);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		PatchPackageFile(request.Item.SourcePath, text2, request.Ownership);
		string text3 = $"/{request.DeviceRoot.Trim().TrimEnd(':')}/Content/0000000000000000/{request.Item.TitleId:X8}";
		string text4 = request.Item.RelativeStorePath.Replace('\\', '/').TrimStart('/');
		string text5 = text3 + "/" + text4;
		int num = text5.LastIndexOf('/');
		string remoteDirectory = ((num > 0) ? text5.Substring(0, num) : text3);
		IReadOnlyList<ulong> ownershipTable = NormalizeOwnershipTable(request.Ownership);
		return new AvatarInstallPlan(request.Item, text2, remoteDirectory, text5, request.Ownership.PrimaryXuid, ownershipTable);
	}

	public static void PatchPackageFile(string sourcePath, string destinationPath, AvatarOwnershipPatch ownership)
	{
		byte[] array = File.ReadAllBytes(sourcePath);
		PatchOwnershipInPlace(array, ownership);
		File.WriteAllBytes(destinationPath, array);
	}

	public static void PatchOwnershipInPlace(byte[] packageBytes, AvatarOwnershipPatch ownership)
	{
		if (packageBytes.Length < 812)
		{
			throw new InvalidDataException("Avatar package is too small to contain an ownership table.");
		}
		byte[] array = BuildOwnershipTable(ownership);
		Buffer.BlockCopy(array, 0, packageBytes, 572, array.Length);
	}

	public static IReadOnlyList<ulong> NormalizeOwnershipTable(AvatarOwnershipPatch ownership)
	{
		List<ulong> list = new List<ulong>(15) { ownership.PrimaryXuid };
		if (ownership.AdditionalXuids != null)
		{
			foreach (ulong additionalXuid in ownership.AdditionalXuids)
			{
				if (additionalXuid != 0L && !list.Contains(additionalXuid))
				{
					list.Add(additionalXuid);
					if (list.Count >= 15)
					{
						break;
					}
				}
			}
		}
		return list;
	}

	private static byte[] BuildOwnershipTable(AvatarOwnershipPatch ownership)
	{
		byte[] array = new byte[240];
		IReadOnlyList<ulong> readOnlyList = NormalizeOwnershipTable(ownership);
		for (int i = 0; i < readOnlyList.Count && i < 15; i++)
		{
			byte[] bytes = BitConverter.GetBytes(readOnlyList[i]);
			Buffer.BlockCopy(bytes, 0, array, i * 16, bytes.Length);
		}
		return array;
	}

	private static string ResolveWorkingDirectory(string? workingDirectory)
	{
		if (!string.IsNullOrWhiteSpace(workingDirectory))
		{
			return Path.GetFullPath(workingDirectory);
		}
		return Path.Combine(CliPaths.ConfigDirectory, "avatar-work");
	}
}
