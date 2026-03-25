using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FtpListCommand : AsyncCommand<FtpListCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--path <PATH>")]
		[LocalizedDescription("Remote directory path (default: /).")]
		public string? Path { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		return await FtpHelpers.WithClientAsync(settings, async delegate(AsyncFtpClient client)
		{
			string path = FtpHelpers.NormalizePath(settings.Path ?? "/");
			var (array, flag) = await FtpHelpers.GetListingWithFallbackAsync(client, path);
			if (settings.Json)
			{
				CliOutput.EmitJson(array.Select((FtpListItem i) => new
				{
					FullName = i.FullName,
					Name = i.Name,
					Type = i.Type.ToString(),
					Size = i.Size,
					Modified = i.Modified
				}));
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]FTP List[/] [grey]" + Markup.Escape(path) + "[/]").RuleStyle("grey"));
			if (flag)
			{
				AnsiConsole.MarkupLine("[yellow]Warning:[/] Server returned a root listing for this path. This FTP server may not support listing subfolders.");
			}
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[green]Name[/]"));
			table.AddColumn(new TableColumn("[grey]Type[/]"));
			table.AddColumn(new TableColumn("[cyan]Size[/]"));
			table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
			FtpListItem[] array2 = array;
			foreach (FtpListItem ftpListItem in array2)
			{
				table.AddRow("[green]" + Markup.Escape(ftpListItem.Name) + "[/]", $"[grey]{ftpListItem.Type}[/]", (ftpListItem.Type == FtpObjectType.File) ? ("[cyan]" + FtpHelpers.FormatBytes(ftpListItem.Size) + "[/]") : "[grey]-[/]", (ftpListItem.Modified != DateTime.MinValue) ? CliOutput.FormatTimestamp(ftpListItem.Modified) : "[grey]unknown[/]");
			}
			AnsiConsole.Write(table);
			return 0;
		}, CancellationToken.None);
	}
}
