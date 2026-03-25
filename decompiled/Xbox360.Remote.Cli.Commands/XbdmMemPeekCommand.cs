using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmMemPeekCommand : AsyncCommand<XbdmMemPeekCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }

		[CommandOption("--type <TYPE>")]
		[Description("u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|ascii plus byte/int/float/string aliases")]
		public string? Type { get; init; }

		[CommandOption("--len <N>")]
		[Description("Length for ascii reads (default 32).")]
		public int? Length { get; init; }

		[CommandOption("--le")]
		[Description("Interpret as little-endian (default is big-endian).")]
		public bool LittleEndian { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseUInt32(settings.Address, out var address) || string.IsNullOrWhiteSpace(settings.Type))
		{
			AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --type <type>");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			string type = MemoryValueCodec.NormalizeType(settings.Type);
			int num = ((type == "ascii") ? Math.Max(1, settings.Length ?? 32) : MemoryValueCodec.GetSize(type));
			if (num <= 0)
			{
				AnsiConsole.MarkupLine("[red]Unknown type.[/]");
				return 1;
			}
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
			AnsiConsole.WriteLine(MemoryValueCodec.ParseValue(type, await client.ReadMemoryBytesAsync(address, num, cts.Token), settings.LittleEndian).ToString() ?? "unknown");
			return 0;
		}, CancellationToken.None);
	}
}
