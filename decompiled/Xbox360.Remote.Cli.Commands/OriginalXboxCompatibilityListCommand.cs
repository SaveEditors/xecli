using System.ComponentModel;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

public sealed class OriginalXboxCompatibilityListCommand : Command<OriginalXboxCompatibilityListCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--json")]
		[Description("Emit machine-readable output.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				Sets = OriginalXboxCompatibilityService.Catalog.Select((OriginalXboxCompatibilityDefinition definition) => new { definition.Id, definition.DisplayName, definition.Description, definition.Notes, definition.PrimaryUrl, definition.MirrorUrl }),
				PartitionFixer = new
				{
					Id = "fixer",
					DisplayName = "HDD Compatibility Partition Fixer",
					Description = "Creates the HddX compatibility partition required on non-standard drives."
				}
			});
			return 0;
		}
		OriginalXboxCompatibilityService.RenderList();
		AnsiConsole.MarkupLine("[grey]Use[/] [springgreen3_1]rgh ogxbox install hacked --usb E:[/] [grey]to stage files locally, or omit[/] [springgreen3_1]--usb[/] [grey]to install directly to the console's HddX partition.[/]");
		return 0;
	}
}
