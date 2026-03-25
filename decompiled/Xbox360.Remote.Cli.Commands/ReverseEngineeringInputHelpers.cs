using System.Threading.Tasks;

namespace Xbox360.Remote.Cli.Commands;

internal static class ReverseEngineeringInputHelpers
{
	public static Task<string?> ResolveRunningFtpPathAsync()
	{
		return GhidraInputHelpers.ResolveRunningFtpPathAsync();
	}

	public static Task<string?> DownloadViaFtpAsync(string ftpPath)
	{
		return GhidraInputHelpers.DownloadViaFtpAsync(ftpPath);
	}

	public static string? MapDevicePathToFtpPath(string? devicePath)
	{
		return GhidraInputHelpers.MapDevicePathToFtpPath(devicePath);
	}
}
