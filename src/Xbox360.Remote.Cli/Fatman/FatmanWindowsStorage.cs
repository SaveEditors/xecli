using System.Management;
using System.Runtime.Versioning;

namespace Xbox360.Remote.Cli.Fatman;

[SupportedOSPlatform("windows")]
internal sealed record FatmanPhysicalDiskInfo(
    int Number,
    string DevicePath,
    string FriendlyName,
    ulong? SizeBytes,
    string? InterfaceType,
    string? MediaType,
    string? SerialNumber,
    IReadOnlyList<string> MountedVolumes);

internal sealed record FatmanSourceInfo(
    string SourcePath,
    string DisplayName,
    long Length,
    bool IsPhysicalDisk,
    FatmanPhysicalDiskInfo? Disk);

[SupportedOSPlatform("windows")]
internal static class FatmanWindowsStorage
{
    public static IReadOnlyList<FatmanPhysicalDiskInfo> EnumeratePhysicalDisks()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<FatmanPhysicalDiskInfo>();

        List<FatmanPhysicalDiskInfo> results = new();
        using ManagementObjectSearcher searcher = new(
            "SELECT Index, DeviceID, Model, Caption, Size, InterfaceType, MediaType, SerialNumber FROM Win32_DiskDrive");
        foreach (ManagementObject disk in searcher.Get().Cast<ManagementObject>())
        {
            int number = TryGetInt32(disk, "Index") ?? -1;
            string devicePath = TryGetString(disk, "DeviceID") ?? $@"\\.\PhysicalDrive{number}";
            string friendlyName = TryGetString(disk, "Model")
                ?? TryGetString(disk, "Caption")
                ?? $"PhysicalDrive{number}";
            ulong? sizeBytes = TryGetUInt64(disk, "Size");
            string? interfaceType = TryGetString(disk, "InterfaceType");
            string? mediaType = TryGetString(disk, "MediaType");
            string? serialNumber = TryGetString(disk, "SerialNumber");
            IReadOnlyList<string> volumes = QueryMountedVolumes(devicePath);

            results.Add(new FatmanPhysicalDiskInfo(
                number,
                devicePath,
                friendlyName.Trim(),
                sizeBytes,
                string.IsNullOrWhiteSpace(interfaceType) ? null : interfaceType.Trim(),
                string.IsNullOrWhiteSpace(mediaType) ? null : mediaType.Trim(),
                string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim(),
                volumes));
        }

        return results.OrderBy(disk => disk.Number).ToArray();
    }

    public static FatmanSourceInfo ResolveSource(string? imagePath, string? diskSelector, bool requireWritable)
    {
        bool hasImage = !string.IsNullOrWhiteSpace(imagePath);
        bool hasDisk = !string.IsNullOrWhiteSpace(diskSelector);
        if (hasImage == hasDisk)
            throw new InvalidOperationException("Provide exactly one source: --image <FILE> or --disk <NUMBER|PATH>.");

        if (hasImage)
        {
            string fullPath = Path.GetFullPath(imagePath!);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Image file not found.", fullPath);

            return new FatmanSourceInfo(
                fullPath,
                fullPath,
                new FileInfo(fullPath).Length,
                false,
                null);
        }

        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("--disk is currently supported on Windows only.");
        if (!InstallHelpers.IsAdministrator())
            throw new InvalidOperationException("Opening a physical disk requires an elevated terminal. Re-run XeCLI as Administrator.");

        IReadOnlyList<FatmanPhysicalDiskInfo> disks = EnumeratePhysicalDisks();
        FatmanPhysicalDiskInfo disk = ResolveDisk(disks, diskSelector!);
        if (!disk.SizeBytes.HasValue || disk.SizeBytes.Value > long.MaxValue)
            throw new InvalidOperationException($"Could not determine the size of physical disk {disk.Number}.");

        return new FatmanSourceInfo(
            disk.DevicePath,
            $"{disk.FriendlyName} ({disk.DevicePath})",
            (long)disk.SizeBytes.Value,
            true,
            disk);
    }

    public static FatmanPhysicalDiskInfo ResolveDisk(IReadOnlyList<FatmanPhysicalDiskInfo> disks, string selector)
    {
        if (int.TryParse(selector, out int number))
        {
            return disks.FirstOrDefault(d => d.Number == number)
                ?? throw new InvalidOperationException($"Physical disk {number} was not found.");
        }

        return disks.FirstOrDefault(d =>
                d.DevicePath.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                d.FriendlyName.Equals(selector, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Physical disk '{selector}' was not found.");
    }

    private static IReadOnlyList<string> QueryMountedVolumes(string devicePath)
    {
        List<string> mounted = new();
        try
        {
            string escapedDevicePath = EscapeManagementValue(devicePath);
            using ManagementObjectSearcher partitionSearcher = new(
                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{escapedDevicePath}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition");

            foreach (ManagementObject partition in partitionSearcher.Get().Cast<ManagementObject>())
            {
                string? partitionId = TryGetString(partition, "DeviceID");
                if (string.IsNullOrWhiteSpace(partitionId))
                    continue;

                string escapedPartitionId = EscapeManagementValue(partitionId);
                using ManagementObjectSearcher logicalSearcher = new(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{escapedPartitionId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");

                foreach (ManagementObject logical in logicalSearcher.Get().Cast<ManagementObject>())
                {
                    string? name = TryGetString(logical, "Name");
                    if (!string.IsNullOrWhiteSpace(name) &&
                        !mounted.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        mounted.Add(name);
                    }
                }
            }
        }
        catch (ManagementException)
        {
            return Array.Empty<string>();
        }

        return mounted.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string EscapeManagementValue(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static string? TryGetString(ManagementBaseObject obj, string propertyName)
        => obj[propertyName]?.ToString();

    private static int? TryGetInt32(ManagementBaseObject obj, string propertyName)
    {
        object? value = obj[propertyName];
        if (value is null)
            return null;
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ulong? TryGetUInt64(ManagementBaseObject obj, string propertyName)
    {
        object? value = obj[propertyName];
        if (value is null)
            return null;
        return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
