using System;
using System.IO;

namespace Xbox360.Remote.Cli;

internal static class CliPaths
{
	private const string RootOverrideEnvVar = "XECLI_HOME";

	private static string? RootOverride
	{
		get
		{
			string environmentVariable = Environment.GetEnvironmentVariable(RootOverrideEnvVar);
			if (string.IsNullOrWhiteSpace(environmentVariable))
			{
				return null;
			}
			return Path.GetFullPath(environmentVariable);
		}
	}

	private static string DefaultConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XeCLI");

	private static string DefaultCacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XeCLI", "cache");

	public static string ConfigDirectory => RootOverride ?? DefaultConfigDirectory;

	public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

	public static string CachePath => (RootOverride != null) ? Path.Combine(RootOverride, "cache") : DefaultCacheDirectory;
}
