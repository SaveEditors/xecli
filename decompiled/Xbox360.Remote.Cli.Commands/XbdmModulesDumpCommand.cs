using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmModulesDumpCommand : AsyncCommand<XbdmModulesDumpCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--name <MODULE>")]
		[LocalizedDescription("Module name, e.g. default.xex or xam.xex.")]
		public string? Name { get; init; }

		[CommandOption("--out <FILE>")]
		[LocalizedDescription("Output file path.")]
		public string? Output { get; init; }

		[CommandOption("--all")]
		[LocalizedDescription("Dump all modules to a directory.")]
		public bool All { get; init; }

		[CommandOption("--dir <DIR>")]
		[LocalizedDescription("Output directory for --all.")]
		public string? Directory { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (settings.All)
		{
			if (string.IsNullOrWhiteSpace(settings.Directory))
			{
				AnsiConsole.MarkupLine("[red]--dir is required with --all.[/]");
				return 1;
			}
		}
		else if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Output))
		{
			AnsiConsole.MarkupLine("[red]--name and --out are required.[/]");
			return 1;
		}
		var (ip, port, timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		IReadOnlyList<XbdmModuleInfo> readOnlyList;
		using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
		{
			Host = ip,
			Port = port,
			TimeoutMs = timeout
		}, CancellationToken.None))
		{
			readOnlyList = await client.GetModulesAsync(includeSections: false, CancellationToken.None);
		}
		if (settings.All)
		{
			Directory.CreateDirectory(settings.Directory);
			foreach (XbdmModuleInfo module in readOnlyList)
			{
				string path = SanitizeFileName(module.Name);
				string outputPath = Path.Combine(settings.Directory, path);
				await CliHelpers.WithClientAsync((Ip: ip, Port: port, TimeoutMs: timeout), settings, async delegate(XbdmClient xbdmClient)
				{
					FileStream stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
					try
					{
						await CliOutput.RunWithProgressAsync("Dumping " + module.Name, module.ModuleSize, (IProgress<long> progress) => xbdmClient.ReadMemoryAsync(module.BaseAddress, module.ModuleSize, stream, progress, CancellationToken.None));
						return 0;
					}
					finally
					{
						if (stream != null)
						{
							((IDisposable)stream).Dispose();
						}
					}
				}, CancellationToken.None);
				AnsiConsole.MarkupLine("[green]Dumped[/] " + module.Name + " -> " + outputPath);
			}
			return 0;
		}
		XbdmModuleInfo moduleSingle = readOnlyList.FirstOrDefault((XbdmModuleInfo m) => string.Equals(m.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
		if (moduleSingle == null)
		{
			AnsiConsole.MarkupLine("[red]Module not found:[/] " + settings.Name);
			return 1;
		}
		await CliHelpers.WithClientAsync((Ip: ip, Port: port, TimeoutMs: timeout), settings, async delegate(XbdmClient xbdmClient)
		{
			FileStream streamSingle = new FileStream(settings.Output, FileMode.Create, FileAccess.Write, FileShare.None);
			try
			{
				await CliOutput.RunWithProgressAsync("Dumping " + moduleSingle.Name, moduleSingle.ModuleSize, (IProgress<long> progress) => xbdmClient.ReadMemoryAsync(moduleSingle.BaseAddress, moduleSingle.ModuleSize, streamSingle, progress, CancellationToken.None));
				return 0;
			}
			finally
			{
				if (streamSingle != null)
				{
					((IDisposable)streamSingle).Dispose();
				}
			}
		}, CancellationToken.None);
		AnsiConsole.MarkupLine("[green]Dumped[/] " + moduleSingle.Name + " to " + settings.Output);
		return 0;
	}

	private static string SanitizeFileName(string name)
	{
		string text = name;
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		return text;
	}
}
