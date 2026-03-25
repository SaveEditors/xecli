using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ContentDeleteCommand : AsyncCommand<ContentDeleteCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--titleid <TITLEID>")]
		[LocalizedDescription("Title ID to remove.")]
		public string? TitleId { get; init; }

		[CommandOption("--device <ROOTS>")]
		[LocalizedDescription("Comma-separated content roots, for example Hdd1 or Hdd1,Usb0.")]
		public string? Devices { get; init; }

		[CommandOption("--yes")]
		[LocalizedDescription("Delete without interactive confirmation.")]
		public bool Yes { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!SaveHelpers.TryParseTitleId(settings.TitleId, out var titleId))
		{
			AnsiConsole.MarkupLine("[red]--titleid is required.[/]");
			return 1;
		}
		(string, int, string, string, int) tuple = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeout = tuple.Item5;
		List<ContentHelpers.ContentLocation> matches = await ContentHelpers.FindLocationsAsync(ip, port, user, pass, timeout, settings.Devices, titleId);
		if (matches.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No matching content folders found.[/]");
			return 0;
		}
		if (!settings.Yes && !Console.IsInputRedirected)
		{
			AnsiConsole.MarkupLine("[yellow]The following paths will be removed:[/]");
			foreach (ContentHelpers.ContentLocation item in matches)
			{
				AnsiConsole.MarkupLine("[grey]" + Markup.Escape(item.Path) + "[/]");
			}
			if (!AnsiConsole.Confirm("Delete these content folders?", defaultValue: false))
			{
				AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
				return 0;
			}
		}
		foreach (ContentHelpers.ContentLocation item2 in matches)
		{
			await ContentHelpers.DeletePathAsync(ip, port, user, pass, timeout, item2.Path);
		}
		AnsiConsole.MarkupLine($"[green]Deleted[/] {matches.Count} content folder(s).");
		return 0;
	}
}
