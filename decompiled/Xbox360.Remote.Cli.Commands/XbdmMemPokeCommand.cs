using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmMemPokeCommand : AsyncCommand<XbdmMemPokeCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }

		[CommandOption("--type <TYPE>")]
		[LocalizedDescription("u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|ascii|hex plus byte/int/float/string/bytes aliases")]
		public string? Type { get; init; }

		[CommandOption("--value <VALUE>")]
		public string? Value { get; init; }

		[CommandOption("--le")]
		[LocalizedDescription("Write little-endian (default is big-endian).")]
		public bool LittleEndian { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseUInt32(settings.Address, out var address) || string.IsNullOrWhiteSpace(settings.Type) || string.IsNullOrWhiteSpace(settings.Value))
		{
			AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --type <type> --value <value>");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			byte[] array = MemoryValueCodec.BuildBytes(settings.Type, settings.Value, settings.LittleEndian);
			await client.WriteMemoryAsync(address, array, CancellationToken.None);
			AnsiConsole.MarkupLine("[green]Wrote memory.[/]");
			return 0;
		}, CancellationToken.None);
	}
}
