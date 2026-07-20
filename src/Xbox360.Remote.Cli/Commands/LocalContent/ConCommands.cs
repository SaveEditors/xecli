using System.ComponentModel;
using System.Globalization;
using System.IO;
using NoDev.Common;
using NoDev.XContent;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

internal static class ConCommandHelpers
{
	public static int ExecuteWithPackage(string inputPath, Func<string, XContentPackage, int> action)
	{
		if (!LocalContentHelpers.TryResolveExistingFile(inputPath, out string fullPath, out string error))
		{
			return LocalContentHelpers.Fail(error);
		}

		XContentPackage? package = null;
		try
		{
			package = new XContentPackage(fullPath);
		}
		catch (Exception)
		{
			return LocalContentHelpers.Fail("Unable to open CON package.");
		}

		try
		{
			return action(fullPath, package);
		}
		catch (Exception)
		{
			return LocalContentHelpers.Fail("Unable to extract CON package.");
		}
		finally
		{
			package?.Close();
		}
	}

	public static bool EnsureStfsPackage(XContentPackage package)
	{
		if (package.Header.Metadata.VolumeType == XContentVolumeType.STFS)
		{
			return true;
		}

		LocalContentHelpers.Fail("Only STFS packages are currently supported for save operations.");
		return false;
	}
}

public sealed class ConInfoCommand : Command<ConInfoCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the CON/LIVE/PIRS package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ConCommandHelpers.ExecuteWithPackage(settings.PackagePath, (fullPath, package) =>
		{
			XContentMetadata metadata = package.Header.Metadata;
			TitleIdDatabase.Instance.TryResolve(
				metadata.ExecutionId.TitleID,
				metadata.ExecutionId.MediaID == 0 ? null : metadata.ExecutionId.MediaID,
				out TitleIdEntry? entry);
			string resolvedTitleName = entry?.Name ?? LocalContentHelpers.FormatUInt32(metadata.ExecutionId.TitleID);

			string magicName = package.GetMagicFileName() ?? string.Empty;
			string fatxPath = package.FormatFATXDevicePath();
			string? verification = package.Header.SignatureType == XContentSignatureType.Console
				? (package.SignatureValidated ? "valid" : "invalid")
				: null;

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Path = fullPath,
					SignatureType = package.Header.SignatureType.ToString(),
					VolumeType = metadata.VolumeType.ToString(),
					ContentType = metadata.ContentType.ToString(),
					DisplayName = metadata.DisplayName,
					Description = metadata.Description,
					Publisher = metadata.Publisher,
					TitleName = metadata.TitleName,
					ResolvedTitleName = resolvedTitleName,
					TitleId = LocalContentHelpers.FormatUInt32(metadata.ExecutionId.TitleID),
					MediaId = LocalContentHelpers.FormatUInt32(metadata.ExecutionId.MediaID),
					ContentSize = metadata.ContentSize,
					ContentSizeDisplay = Formatting.GetSizeFromBytes(metadata.ContentSize),
					Creator = LocalContentHelpers.FormatUInt64(metadata.Creator),
					OnlineCreator = LocalContentHelpers.FormatUInt64(metadata.OnlineCreator),
					ConsoleId = LocalContentHelpers.FormatBytes(metadata.ConsoleID),
					DeviceId = LocalContentHelpers.FormatBytes(metadata.DeviceID),
					MagicName = magicName,
					FatxPath = fatxPath,
					IsReadOnly = package.IsReadOnly,
					SignatureVerified = verification
				});
				return 0;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Content Package[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Field[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			table.AddRow("[grey]Path[/]", "[cyan]" + Markup.Escape(fullPath) + "[/]");
			table.AddRow("[grey]Signature[/]", "[green]" + package.Header.SignatureType + "[/]");
			table.AddRow("[grey]Volume[/]", "[green]" + metadata.VolumeType + "[/]");
			table.AddRow("[grey]Content Type[/]", "[green]" + metadata.ContentType + "[/]");
			table.AddRow("[grey]Display Name[/]", LocalContentHelpers.MarkupValue(metadata.DisplayName, "green"));
			table.AddRow("[grey]Description[/]", LocalContentHelpers.MarkupValue(metadata.Description));
			table.AddRow("[grey]Publisher[/]", LocalContentHelpers.MarkupValue(metadata.Publisher));
			table.AddRow("[grey]Title Name[/]", LocalContentHelpers.MarkupValue(metadata.TitleName, "green"));
			table.AddRow("[grey]Resolved Title[/]", "[green]" + Markup.Escape(resolvedTitleName) + "[/]");
			table.AddRow("[grey]Title ID[/]", "[cyan]" + LocalContentHelpers.FormatUInt32(metadata.ExecutionId.TitleID) + "[/]");
			table.AddRow("[grey]Media ID[/]", "[cyan]" + LocalContentHelpers.FormatUInt32(metadata.ExecutionId.MediaID) + "[/]");
			table.AddRow("[grey]Content Size[/]", "[cyan]" + Markup.Escape(Formatting.GetSizeFromBytes(metadata.ContentSize)) + $" ({metadata.ContentSize.ToString(CultureInfo.InvariantCulture)})[/]");
			table.AddRow("[grey]Creator[/]", "[cyan]" + LocalContentHelpers.FormatUInt64(metadata.Creator) + "[/]");
			table.AddRow("[grey]Online Creator[/]", "[cyan]" + LocalContentHelpers.FormatUInt64(metadata.OnlineCreator) + "[/]");
			table.AddRow("[grey]Console ID[/]", string.IsNullOrWhiteSpace(LocalContentHelpers.FormatBytes(metadata.ConsoleID)) ? "[grey]empty[/]" : "[cyan]" + LocalContentHelpers.FormatBytes(metadata.ConsoleID) + "[/]");
			table.AddRow("[grey]Device ID[/]", string.IsNullOrWhiteSpace(LocalContentHelpers.FormatBytes(metadata.DeviceID)) ? "[grey]empty[/]" : "[cyan]" + LocalContentHelpers.FormatBytes(metadata.DeviceID) + "[/]");
			table.AddRow("[grey]Magic Name[/]", LocalContentHelpers.MarkupValue(magicName, "cyan"));
			table.AddRow("[grey]FATX Path[/]", "[cyan]" + Markup.Escape(fatxPath) + "[/]");
			table.AddRow("[grey]Read Only[/]", package.IsReadOnly ? "[yellow]yes[/]" : "[green]no[/]");
			table.AddRow("[grey]Signature Verify[/]", verification != null ? (verification == "valid" ? "[green]valid[/]" : "[red]invalid[/]") : "[grey]not supported[/]");
			AnsiConsole.Write(table);
			return 0;
		});
	}
}

public sealed class ConVerifyCommand : Command<ConVerifyCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the CON/LIVE/PIRS package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ConCommandHelpers.ExecuteWithPackage(settings.PackagePath, (fullPath, package) =>
		{
			bool supported = package.Header.SignatureType == XContentSignatureType.Console;
			bool valid = supported && package.SignatureValidated;

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Path = fullPath,
					SignatureType = package.Header.SignatureType.ToString(),
					Supported = supported,
					Valid = supported ? valid : (bool?)null
				});
				return valid || !supported ? 0 : 1;
			}

			AnsiConsole.Write(new Rule("[bold deepskyblue1]Package Verification[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Field[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			table.AddRow("[grey]Path[/]", "[cyan]" + Markup.Escape(fullPath) + "[/]");
			table.AddRow("[grey]Signature[/]", "[green]" + package.Header.SignatureType + "[/]");
			table.AddRow("[grey]Supported[/]", supported ? "[green]yes[/]" : "[yellow]no[/]");
			table.AddRow("[grey]Result[/]", !supported ? "[grey]only CON signature verification is implemented[/]" : valid ? "[green]valid[/]" : "[red]invalid[/]");
			AnsiConsole.Write(table);
			return valid || !supported ? 0 : 1;
		});
	}
}

public sealed class ConRehashCommand : Command<ConRehashCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the STFS package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--keyvault <FILE>")]
		[LocalizedDescription("Your decrypted Xbox 360 keyvault, required when rehashing a CON package.")]
		public string? KeyVaultPath { get; init; }

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ConCommandHelpers.ExecuteWithPackage(settings.PackagePath, (fullPath, package) =>
		{
			if (!ConCommandHelpers.EnsureStfsPackage(package))
			{
				return 1;
			}

			int signingResult = ConSigningKeyHelpers.Execute(package.Header.SignatureType, settings.KeyVaultPath, () =>
			{
				package.Save();
				return 0;
			});
			if (signingResult != 0)
			{
				return signingResult;
			}

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Path = fullPath,
					Operation = "rehash",
					SignatureType = package.Header.SignatureType.ToString(),
					VolumeType = package.Header.Metadata.VolumeType.ToString(),
					Resigned = package.Header.SignatureType == XContentSignatureType.Console
				});
				return 0;
			}

			AnsiConsole.MarkupLine("[green]Package header saved.[/]");
			if (package.Header.SignatureType == XContentSignatureType.Console)
			{
				AnsiConsole.MarkupLine("[grey]CON signature refreshed as part of the save.[/]");
			}
			return 0;
		});
	}
}

public sealed class ConResignCommand : Command<ConResignCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the CON package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--keyvault <FILE>")]
		[LocalizedDescription("Your decrypted Xbox 360 keyvault, required to re-sign the CON package.")]
		public string? KeyVaultPath { get; init; }

		[CommandOption("--json")]
		[LocalizedDescription("Output JSON.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (!LocalContentHelpers.TryResolveExistingFile(settings.PackagePath, out string fullPath, out string error))
		{
			return LocalContentHelpers.Fail(error);
		}

		XContentPackage? package = null;
		try
		{
			package = new XContentPackage(fullPath);
		}
		catch (Exception)
		{
			return LocalContentHelpers.Fail("Unable to open CON package.");
		}

		try
		{
			if (!ConCommandHelpers.EnsureStfsPackage(package))
			{
				return 1;
			}

			if (package.Header.SignatureType != XContentSignatureType.Console)
			{
				return LocalContentHelpers.Fail("Only CON packages can be re-signed. Use `rgh con rehash` for LIVE/PIRS STFS packages.");
			}

			int signingResult = ConSigningKeyHelpers.Execute(package.Header.SignatureType, settings.KeyVaultPath, () =>
			{
				package.Save();
				return 0;
			});
			if (signingResult != 0)
			{
				return signingResult;
			}

			package.Close();

			package = new XContentPackage(fullPath);
			bool valid = package.SignatureValidated;

			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Path = fullPath,
					Operation = "resign",
					SignatureType = package.Header.SignatureType.ToString(),
					Valid = valid
				});
				return valid ? 0 : 1;
			}

			AnsiConsole.MarkupLine(valid ? "[green]Package re-signed and verified.[/]" : "[red]Package was saved but the signature did not verify.[/]");
			return valid ? 0 : 1;
		}
		catch (Exception)
		{
			return LocalContentHelpers.Fail("Unable to extract CON package.");
		}
		finally
		{
			package?.Close();
		}
	}
}

public sealed class ConMagicNameCommand : Command<ConMagicNameCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the CON/LIVE/PIRS package.")]
		public string PackagePath { get; init; } = string.Empty;
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ConCommandHelpers.ExecuteWithPackage(settings.PackagePath, (_, package) =>
		{
			string? magicName = package.GetMagicFileName();
			if (string.IsNullOrWhiteSpace(magicName))
			{
				return LocalContentHelpers.Fail("This package type does not have a magic filename mapping.");
			}

			AnsiConsole.WriteLine(magicName);
			return 0;
		});
	}
}

public sealed class ConFatxPathCommand : Command<ConFatxPathCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<PACKAGE>")]
		[LocalizedDescription("Path to the CON/LIVE/PIRS package.")]
		public string PackagePath { get; init; } = string.Empty;

		[CommandOption("--fix-name")]
		[LocalizedDescription("Replace the leaf filename with the package's magic FATX name when available.")]
		public bool FixName { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		return ConCommandHelpers.ExecuteWithPackage(settings.PackagePath, (_, package) =>
		{
			AnsiConsole.WriteLine(package.FormatFATXDevicePath(settings.FixName));
			return 0;
		});
	}
}

