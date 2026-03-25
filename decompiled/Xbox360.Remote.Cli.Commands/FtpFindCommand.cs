using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FtpFindCommand : AsyncCommand<FtpFindCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Root path to search (default: /).")]
		public string? Path { get; init; }

		[CommandOption("--name <PATTERN>")]
		[Description("File or folder name pattern (supports * and ? wildcards).")]
		public string? Name { get; init; }

		[CommandOption("--regex")]
		[Description("Interpret --name as a regular expression.")]
		public bool Regex { get; init; }

		[CommandOption("--depth <N>")]
		[Description("Maximum recursion depth (default: 6).")]
		public int? Depth { get; init; }

		[CommandOption("--max <N>")]
		[Description("Maximum results to return (default: 200).")]
		public int? Max { get; init; }
	}

	private sealed class FtpFindResult
	{
		public string Path { get; init; } = string.Empty;

		public string Name { get; init; } = string.Empty;

		public FtpObjectType Type { get; init; }

		public long Size { get; init; }

		public DateTime Modified { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Name))
		{
			AnsiConsole.MarkupLine("[red]--name is required.[/]");
			return 1;
		}
		int maxDepth = settings.Depth ?? 6;
		if (maxDepth < 0)
		{
			maxDepth = 0;
		}
		int maxResults = settings.Max ?? 200;
		if (maxResults <= 0)
		{
			maxResults = 200;
		}
		Regex matcher = (settings.Regex ? new Regex(settings.Name, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) : BuildWildcardRegex(settings.Name));
		return await FtpHelpers.WithClientAsync(settings, async delegate(AsyncFtpClient client)
		{
			string startPath = FtpHelpers.NormalizePath(settings.Path ?? "/");
			Queue<(string Path, int Depth)> pending = new Queue<(string, int)>();
			HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<FtpFindResult> matches = new List<FtpFindResult>();
			pending.Enqueue((startPath, 0));
			while (pending.Count > 0 && matches.Count < maxResults)
			{
				var (current, depth) = pending.Dequeue();
				if (visited.Add(current))
				{
					FtpListItem[] array;
					bool flag;
					try
					{
						(array, flag) = await FtpHelpers.GetListingWithFallbackAsync(client, current);
					}
					catch
					{
						continue;
					}
					if (!flag || string.Equals(current, "/", StringComparison.Ordinal))
					{
						FtpListItem[] array2 = array;
						foreach (FtpListItem ftpListItem in array2)
						{
							if (!string.IsNullOrWhiteSpace(ftpListItem.Name) && !(ftpListItem.Name == ".") && !(ftpListItem.Name == ".."))
							{
								string text = (string.IsNullOrWhiteSpace(ftpListItem.FullName) ? CombinePath(current, ftpListItem.Name) : ftpListItem.FullName);
								if (matcher.IsMatch(ftpListItem.Name))
								{
									matches.Add(new FtpFindResult
									{
										Path = text,
										Name = ftpListItem.Name,
										Type = ftpListItem.Type,
										Size = ftpListItem.Size,
										Modified = ftpListItem.Modified
									});
									if (matches.Count >= maxResults)
									{
										break;
									}
								}
								if (ftpListItem.Type == FtpObjectType.Directory && depth < maxDepth)
								{
									pending.Enqueue((text, depth + 1));
								}
							}
						}
					}
				}
			}
			if (settings.Json)
			{
				CliOutput.EmitJson(matches.Select((FtpFindResult ftpFindResult) => new
				{
					Path = ftpFindResult.Path,
					Name = ftpFindResult.Name,
					Type = ftpFindResult.Type.ToString(),
					Size = ftpFindResult.Size,
					Modified = ftpFindResult.Modified
				}));
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]FTP Find[/] [grey]" + Markup.Escape(startPath) + "[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[green]Path[/]"));
			table.AddColumn(new TableColumn("[grey]Type[/]"));
			table.AddColumn(new TableColumn("[cyan]Size[/]"));
			table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
			foreach (FtpFindResult item in matches)
			{
				table.AddRow("[green]" + Markup.Escape(item.Path) + "[/]", $"[grey]{item.Type}[/]", (item.Type == FtpObjectType.File) ? ("[cyan]" + FtpHelpers.FormatBytes(item.Size) + "[/]") : "[grey]-[/]", (item.Modified != DateTime.MinValue) ? CliOutput.FormatTimestamp(item.Modified) : "[grey]unknown[/]");
			}
			AnsiConsole.Write(table);
			return 0;
		}, CancellationToken.None);
	}

	private static Regex BuildWildcardRegex(string pattern)
	{
		string text = Regex.Escape(pattern);
		text = text.Replace("\\*", ".*").Replace("\\?", ".");
		return new Regex("^" + text + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	private static string CombinePath(string basePath, string name)
	{
		if (string.Equals(basePath, "/", StringComparison.Ordinal))
		{
			return "/" + name;
		}
		return basePath.TrimEnd('/') + "/" + name;
	}
}
