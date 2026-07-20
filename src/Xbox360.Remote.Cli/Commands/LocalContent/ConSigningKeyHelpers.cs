using NoDev.XContent;
using NoDev.Xbox360;

namespace Xbox360.Remote.Cli.Commands;

internal static class ConSigningKeyHelpers
{
	public static int Execute(XContentSignatureType signatureType, string? keyVaultPath, Func<int> action)
	{
		if (signatureType != XContentSignatureType.Console)
		{
			return action();
		}

		if (string.IsNullOrWhiteSpace(keyVaultPath))
		{
			return LocalContentHelpers.Fail(KeyStorage.MissingSigningMaterialMessage);
		}

		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(keyVaultPath));
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return LocalContentHelpers.Fail("Invalid --keyvault file path.");
		}

		if (!File.Exists(fullPath))
		{
			return LocalContentHelpers.Fail("Keyvault file was not found. Provide --keyvault <FILE> with your own Xbox 360 keyvault.");
		}

		long length;
		try
		{
			length = new FileInfo(fullPath).Length;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return LocalContentHelpers.Fail("Keyvault file could not be read.");
		}

		if (!KeyVault.IsSupportedLength(length))
		{
			return LocalContentHelpers.Fail("Keyvault size must be 0x4000 or 0x3FF0 bytes.");
		}

		KeyVault keyVault;
		try
		{
			keyVault = new KeyVault(fullPath);
		}
		catch (Exception)
		{
			return LocalContentHelpers.Fail("Keyvault could not be parsed. Supply a valid decrypted Xbox 360 keyvault.");
		}

		using (keyVault)
		using (KeyStorage.Load(keyVault))
		{
			return action();
		}
	}
}
