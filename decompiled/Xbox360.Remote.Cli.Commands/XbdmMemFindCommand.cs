using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmMemFindCommand : AsyncCommand<XbdmMemFindCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }

		[CommandOption("--size <SIZE>")]
		public string? Size { get; init; }

		[CommandOption("--chunk <SIZE>")]
		[LocalizedDescription("Read chunk size (hex or dec, default: 0x4000).")]
		public string? ChunkSize { get; init; }

		[CommandOption("--pattern <HEX>")]
		[LocalizedDescription("Hex pattern, e.g. DEADBEEF or 0xDE AD BE EF.")]
		public string? Pattern { get; init; }

		[CommandOption("--ascii <TEXT>")]
		[LocalizedDescription("ASCII text pattern.")]
		public string? Ascii { get; init; }

		[CommandOption("--max <N>")]
		[LocalizedDescription("Maximum matches to return (default 20).")]
		public int? MaxCount { get; init; }

		[CommandOption("--out <FILE>")]
		[LocalizedDescription("Write the hit list to a file (.json for JSON, otherwise text).")]
		public string? Output { get; init; }

		[CommandOption("--freeze")]
		[LocalizedDescription("Continuously rewrite a value to the selected hit(s). Requires --freeze-type and --freeze-value.")]
		public bool Freeze { get; init; }

		[CommandOption("--freeze-type <TYPE>")]
		[LocalizedDescription("Value type for freeze writes: int|uint|float|string|bytes|u32|f32|ascii|hex, etc.")]
		public string? FreezeType { get; init; }

		[CommandOption("--freeze-value <VALUE>")]
		[LocalizedDescription("Value to freeze to the selected hit(s).")]
		public string? FreezeValue { get; init; }

		[CommandOption("--freeze-interval <MS>")]
		[LocalizedDescription("Freeze write interval in milliseconds (default 250).")]
		public int? FreezeIntervalMs { get; init; }

		[CommandOption("--freeze-count <N>")]
		[LocalizedDescription("Number of freeze write passes (default 0 = until Ctrl+C).")]
		public int? FreezeCount { get; init; }

		[CommandOption("--freeze-all")]
		[LocalizedDescription("Freeze all hits instead of just one selected hit.")]
		public bool FreezeAll { get; init; }

		[CommandOption("--hit <N>")]
		[LocalizedDescription("1-based hit index to freeze when --freeze-all is not used (default 1).")]
		public int? HitIndex { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseUInt32(settings.Address, out var address) || !CliHelpers.TryParseUInt32(settings.Size, out var size))
		{
			AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec>");
			return 1;
		}
		if (string.IsNullOrWhiteSpace(settings.Pattern) == string.IsNullOrWhiteSpace(settings.Ascii))
		{
			AnsiConsole.MarkupLine("[red]Provide either --pattern or --ascii.[/]");
			return 1;
		}
		byte[] pattern = ((settings.Pattern != null) ? MemoryValueCodec.ParseHexPattern(settings.Pattern) : Encoding.ASCII.GetBytes(settings.Ascii));
		if (pattern.Length == 0)
		{
			AnsiConsole.MarkupLine("[red]Pattern cannot be empty.[/]");
			return 1;
		}
		if (settings.Freeze && (string.IsNullOrWhiteSpace(settings.FreezeType) || string.IsNullOrWhiteSpace(settings.FreezeValue)))
		{
			AnsiConsole.MarkupLine("[red]--freeze requires --freeze-type and --freeze-value.[/]");
			return 1;
		}
		int maxCount = Math.Max(1, settings.MaxCount ?? 20);
		int chunkSize = 16384;
		if (!string.IsNullOrWhiteSpace(settings.ChunkSize))
		{
			if (!CliHelpers.TryParseUInt32(settings.ChunkSize, out var value))
			{
				AnsiConsole.MarkupLine("[red]Invalid --chunk size.[/]");
				return 1;
			}
			chunkSize = (int)Math.Clamp(value, 512u, 1048576u);
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			List<uint> matches = new List<uint>();
			int overlap = pattern.Length - 1;
			byte[] tail = Array.Empty<byte>();
			if (size <= 4194304)
			{
				byte[] obj = await client.ReadMemoryBytesAsync(address, (int)size, CancellationToken.None);
				int i = 0;
				int num;
				for (ReadOnlySpan<byte> readOnlySpan = obj; i < readOnlySpan.Length; i += num + 1)
				{
					num = readOnlySpan.Slice(i).IndexOf(pattern);
					if (num < 0)
					{
						break;
					}
					matches.Add(address + (uint)(i + num));
					if (matches.Count >= maxCount)
					{
						break;
					}
				}
			}
			else
			{
				ulong remaining = size;
				uint current = address;
				while (remaining != 0 && matches.Count < maxCount)
				{
					int read = (int)Math.Min((ulong)chunkSize, remaining);
					byte[] array = await client.ReadMemoryBytesAsync(current, read, CancellationToken.None);
					byte[] array2 = new byte[tail.Length + array.Length];
					if (tail.Length != 0)
					{
						Buffer.BlockCopy(tail, 0, array2, 0, tail.Length);
					}
					Buffer.BlockCopy(array, 0, array2, tail.Length, array.Length);
					int j = 0;
					int num2;
					for (ReadOnlySpan<byte> readOnlySpan2 = array2; j < readOnlySpan2.Length; j += num2 + 1)
					{
						num2 = readOnlySpan2.Slice(j).IndexOf(pattern);
						if (num2 < 0)
						{
							break;
						}
						uint item = (uint)((int)current - tail.Length + (j + num2));
						matches.Add(item);
						if (matches.Count >= maxCount)
						{
							break;
						}
					}
					if (overlap > 0)
					{
						int num3 = Math.Min(overlap, array2.Length);
						tail = new byte[num3];
						Buffer.BlockCopy(array2, array2.Length - num3, tail, 0, num3);
					}
					else
					{
						tail = Array.Empty<byte>();
					}
					remaining -= (ulong)read;
					current += (uint)read;
				}
			}
			List<string> formattedMatches = matches.Select((uint addr) => $"0x{addr:X8}").ToList();
			if (!string.IsNullOrWhiteSpace(settings.Output))
			{
				string output = settings.Output;
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
				if (!output.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				{
					await File.WriteAllLinesAsync(output, formattedMatches, CancellationToken.None);
				}
				else
				{
					string contents = JsonSerializer.Serialize(formattedMatches, new JsonSerializerOptions
					{
						WriteIndented = true
					});
					await File.WriteAllTextAsync(output, contents, CancellationToken.None);
				}
			}
			if (settings.Json)
			{
				CliOutput.EmitJson(formattedMatches);
			}
			else
			{
				AnsiConsole.Write(new Rule("[bold deepskyblue1]Matches[/]").RuleStyle("grey"));
				foreach (uint item5 in matches)
				{
					AnsiConsole.MarkupLine($"[green]0x{item5:X8}[/]");
				}
				if (matches.Count == 0)
				{
					AnsiConsole.MarkupLine("[yellow]No matches found.[/]");
				}
				else if (!string.IsNullOrWhiteSpace(settings.Output))
				{
					AnsiConsole.MarkupLine("[green]Saved hit list[/] " + Markup.Escape(settings.Output));
				}
			}
			if (!settings.Freeze || matches.Count == 0)
			{
				return 0;
			}
			byte[] freezeBytes = MemoryValueCodec.BuildBytes(settings.FreezeType, settings.FreezeValue, littleEndian: false);
			List<uint> targets = (settings.FreezeAll ? matches : SelectFreezeTarget(matches, settings.HitIndex ?? 1));
			var (item2, item3, item4) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
			return await CliHelpers.WithClientAsync((Ip: item2, Port: item3, TimeoutMs: item4), settings, async delegate(XbdmClient freezeClient)
			{
				int interval = Math.Max(25, settings.FreezeIntervalMs ?? 250);
				int count = Math.Max(0, settings.FreezeCount.GetValueOrDefault());
				CancellationTokenSource freezeCts = new CancellationTokenSource();
				try
				{
					ConsoleCancelEventHandler handler = null;
					if (!Console.IsInputRedirected)
					{
						handler = delegate(object? _, ConsoleCancelEventArgs e)
						{
							e.Cancel = true;
							freezeCts.Cancel();
						};
						Console.CancelKeyPress += handler;
					}
					try
					{
						AnsiConsole.MarkupLine($"[yellow]Freezing[/] {targets.Count} hit(s) every {interval} ms. Press Ctrl+C to stop.");
						int pass = 0;
						while (count == 0 || pass < count)
						{
							freezeCts.Token.ThrowIfCancellationRequested();
							foreach (uint item6 in targets)
							{
								await freezeClient.WriteMemoryAsync(item6, freezeBytes, freezeCts.Token);
							}
							pass++;
							if (count > 0 && pass >= count)
							{
								break;
							}
							await Task.Delay(interval, freezeCts.Token);
						}
						AnsiConsole.MarkupLine("[green]Freeze loop completed.[/]");
						return 0;
					}
					catch (OperationCanceledException)
					{
						AnsiConsole.MarkupLine("[yellow]Freeze loop stopped.[/]");
						return 0;
					}
					finally
					{
						if (handler != null)
						{
							Console.CancelKeyPress -= handler;
						}
					}
				}
				finally
				{
					if (freezeCts != null)
					{
						((IDisposable)freezeCts).Dispose();
					}
				}
			}, CancellationToken.None);
		}, CancellationToken.None);
	}

	private static List<uint> SelectFreezeTarget(List<uint> matches, int hitIndex)
	{
		if (hitIndex <= 0)
		{
			throw new InvalidOperationException("--hit must be 1 or greater.");
		}
		if (hitIndex > matches.Count)
		{
			throw new InvalidOperationException($"Requested hit #{hitIndex}, but only {matches.Count} match(es) were found.");
		}
		return new List<uint> { matches[hitIndex - 1] };
	}
}
