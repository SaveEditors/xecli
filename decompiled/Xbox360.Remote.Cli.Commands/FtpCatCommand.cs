using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FtpCatCommand : AsyncCommand<FtpCatCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--path <PATH>")]
		public string? Path { get; init; }

		[CommandOption("--max <BYTES>")]
		[LocalizedDescription("Maximum bytes to read (default: 65536).")]
		public string? MaxBytes { get; init; }

		[CommandOption("--encoding <ENC>")]
		[LocalizedDescription("ascii|utf8 (default: utf8).")]
		public string? EncodingName { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Path))
		{
			AnsiConsole.MarkupLine("[red]--path is required.[/]");
			return 1;
		}
		int maxBytes = 65536;
		if (!string.IsNullOrWhiteSpace(settings.MaxBytes) && !int.TryParse(settings.MaxBytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxBytes))
		{
			AnsiConsole.MarkupLine("[red]Invalid --max value.[/]");
			return 1;
		}
		Encoding encoding = ((settings.EncodingName?.Equals("ascii", StringComparison.OrdinalIgnoreCase) ?? false) ? Encoding.ASCII : Encoding.UTF8);
		return await FtpHelpers.WithClientAsync(settings, async delegate(AsyncFtpClient client)
		{
			string path = FtpHelpers.NormalizePath(settings.Path);
			using Stream stream = await client.OpenRead(path, FtpDataType.Binary, 0L);
			byte[] buffer = new byte[Math.Min(maxBytes, 1048576)];
			int totalRead = 0;
			using MemoryStream ms = new MemoryStream();
			int num;
			for (; totalRead < maxBytes; totalRead += num)
			{
				int length = Math.Min(buffer.Length, maxBytes - totalRead);
				num = await stream.ReadAsync(buffer.AsMemory(0, length));
				if (num <= 0)
				{
					break;
				}
				ms.Write(buffer, 0, num);
			}
			AnsiConsole.WriteLine(encoding.GetString(ms.ToArray()));
			return 0;
		}, CancellationToken.None);
	}
}
