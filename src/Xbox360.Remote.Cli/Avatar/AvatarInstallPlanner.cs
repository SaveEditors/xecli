namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarInstallPlanner {
    private const int OwnershipRegionStart = 0x023C;
    private const int OwnershipRegionEnd = 0x032C;
    private const int OwnershipEntryCount = 15;
    private const int OwnershipEntryStride = 16;

    public static AvatarInstallPlan PrepareInstallPlan(AvatarInstallRequest request) {
        if (request.Item == null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.DeviceRoot))
            throw new ArgumentException("Device root is required.", nameof(request));

        string workingRoot = ResolveWorkingDirectory(request.WorkingDirectory);
        Directory.CreateDirectory(workingRoot);

        string relativeStorePath = request.Item.RelativeStorePath
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        string patchedLocalPath = Path.Combine(workingRoot, request.Item.TitleId.ToString("X8"), relativeStorePath);
        string? targetDirectory = Path.GetDirectoryName(patchedLocalPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        PatchPackageFile(request.Item.SourcePath, patchedLocalPath, request.Ownership);

        string deviceRoot = NormalizeDeviceRoot(request.DeviceRoot);
        string remoteRoot = $"/{deviceRoot}/Content/0000000000000000/{request.Item.TitleId:X8}";
        string remoteRelativePath = request.Item.RelativeStorePath.Replace('\\', '/').TrimStart('/');
        string remoteFilePath = $"{remoteRoot}/{remoteRelativePath}";
        int slash = remoteFilePath.LastIndexOf('/');
        string remoteDirectory = slash > 0 ? remoteFilePath[..slash] : remoteRoot;
        IReadOnlyList<ulong> ownershipTable = NormalizeOwnershipTable(request.Ownership);

        return new AvatarInstallPlan(
            request.Item,
            patchedLocalPath,
            remoteDirectory,
            remoteFilePath,
            request.Ownership.PrimaryXuid,
            ownershipTable);
    }

    public static void PatchPackageFile(string sourcePath, string destinationPath, AvatarOwnershipPatch ownership) {
        byte[] packageBytes = File.ReadAllBytes(sourcePath);
        PatchOwnershipInPlace(packageBytes, ownership);
        File.WriteAllBytes(destinationPath, packageBytes);
    }

    public static void PatchOwnershipInPlace(byte[] packageBytes, AvatarOwnershipPatch ownership) {
        if (packageBytes.Length < OwnershipRegionEnd)
            throw new InvalidDataException("Avatar package is too small to contain an ownership table.");

        byte[] table = BuildOwnershipTable(ownership);
        Buffer.BlockCopy(table, 0, packageBytes, OwnershipRegionStart, table.Length);
    }

    public static IReadOnlyList<ulong> NormalizeOwnershipTable(AvatarOwnershipPatch ownership) {
        List<ulong> values = new List<ulong>(OwnershipEntryCount) { ownership.PrimaryXuid };
        if (ownership.AdditionalXuids != null) {
            foreach (ulong additional in ownership.AdditionalXuids) {
                if (additional == 0 || values.Contains(additional))
                    continue;
                values.Add(additional);
                if (values.Count >= OwnershipEntryCount)
                    break;
            }
        }

        return values;
    }

    private static byte[] BuildOwnershipTable(AvatarOwnershipPatch ownership) {
        byte[] table = new byte[OwnershipRegionEnd - OwnershipRegionStart];
        IReadOnlyList<ulong> values = NormalizeOwnershipTable(ownership);
        for (int index = 0; index < values.Count && index < OwnershipEntryCount; index++) {
            byte[] xuidBytes = BitConverter.GetBytes(values[index]);
            Buffer.BlockCopy(xuidBytes, 0, table, index * OwnershipEntryStride, xuidBytes.Length);
        }

        return table;
    }

    private static string ResolveWorkingDirectory(string? workingDirectory) {
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            return Path.GetFullPath(workingDirectory);

        return Path.Combine(CliPaths.ConfigDirectory, "avatar-work");
    }

    private static string NormalizeDeviceRoot(string deviceRoot) {
        string normalized = deviceRoot.Trim().TrimEnd(':').Trim().Trim('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Device root is required.", nameof(deviceRoot));
        if (normalized.Contains('\\') || normalized.Contains('/'))
            throw new ArgumentException("Device root must be a single path segment.", nameof(deviceRoot));

        return normalized;
    }
}
