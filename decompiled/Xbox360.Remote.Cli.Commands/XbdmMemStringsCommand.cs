using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmMemStringsCommand : AsyncCommand<XbdmMemStringsCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }

		[CommandOption("--size <SIZE>")]
		public string? Size { get; init; }

		[CommandOption("--min <N>")]
		[Description("Minimum string length (default 4).")]
		public int? MinLength { get; init; }

		[CommandOption("--max <N>")]
		[Description("Maximum strings to return (default 200).")]
		public int? MaxCount { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseUInt32(settings.Address, out var address) || !CliHelpers.TryParseUInt32(settings.Size, out var size))
		{
			AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec>");
			return 1;
		}
		int minLength = Math.Max(2, settings.MinLength ?? 4);
		int maxCount = Math.Max(1, settings.MaxCount ?? 200);
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			List<(uint Address, string Text)> results = new List<(uint, string)>();
			byte[] carry = null;
			uint carryStart = 0u;
			ulong remaining = size;
			uint current = address;
			while (remaining != 0 && results.Count < maxCount)
			{
				int read = (int)Math.Min(32768uL, remaining);
				byte[] array = await client.ReadMemoryBytesAsync(current, read, CancellationToken.None);
				int i = 0;
				if (carry != null && carry.Length != 0)
				{
					for (; i < array.Length && IsPrintable(array[i]); i++)
					{
						Array.Resize(ref carry, carry.Length + 1);
						carry[^1] = array[i];
					}
					if (carry.Length >= minLength)
					{
						results.Add((carryStart, Encoding.ASCII.GetString(carry)));
					}
					carry = null;
				}
				int num = -1;
				for (; i < array.Length; i++)
				{
					if (IsPrintable(array[i]))
					{
						if (num == -1)
						{
							num = i;
						}
					}
					else if (num != -1)
					{
						int num2 = i - num;
						if (num2 >= minLength)
						{
							uint item = current + (uint)num;
							results.Add((item, Encoding.ASCII.GetString(array, num, num2)));
							if (results.Count >= maxCount)
							{
								break;
							}
						}
						num = -1;
					}
				}
				if (results.Count >= maxCount)
				{
					break;
				}
				if (num != -1 && num < array.Length)
				{
					int num3 = array.Length - num;
					carry = new byte[num3];
					Buffer.BlockCopy(array, num, carry, 0, num3);
					carryStart = current + (uint)num;
				}
				remaining -= (ulong)read;
				current += (uint)read;
			}
			if (settings.Json)
			{
				CliOutput.EmitJson(Enumerable.Select(results, ((uint Address, string Text) r) => new
				{
					Address = $"0x{r.Address:X8}",
					Text = r.Text
				}).ToList());
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Strings[/]").RuleStyle("grey"));
			foreach (var (value, text) in results)
			{
				AnsiConsole.MarkupLine($"[grey]0x{value:X8}[/] [green]{Markup.Escape(text)}[/]");
			}
			if (results.Count == 0)
			{
				AnsiConsole.MarkupLine("[yellow]No strings found.[/]");
			}
			return 0;
		}, CancellationToken.None);
	}

	private static bool IsPrintable(byte value)
	{
		if (value >= 32)
		{
			return value <= 126;
		}
		return false;
	}
}
