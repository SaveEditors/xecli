namespace Xbox360.Remote.Cli.Commands;

internal static class SpoofNotifyHelpers
{
	internal static string? GetLogoOverride(string? iconName, string? logoValue)
	{
		if (!string.IsNullOrWhiteSpace(iconName) || !string.IsNullOrWhiteSpace(logoValue))
		{
			return logoValue;
		}
		return "14";
	}

	internal static string BuildSpoofingIdentityMessage(string title, string gamertag, string xuid)
	{
		return $"XeCLI ({title}) - Spoofing {gamertag} | {xuid}";
	}

	internal static string BuildRemoteSpoofMessage(string title, string text)
	{
		return $"XeCLI ({title}) - Spoofing {text} | remote";
	}

	internal static string BuildResetIdentityMessage(string title, string gamertag, string xuid)
	{
		return $"XeCLI ({title}) - Resetting {gamertag} | {xuid}";
	}
}
