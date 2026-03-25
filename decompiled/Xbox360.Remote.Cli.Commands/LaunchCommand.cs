using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class LaunchCommand : AsyncCommand<LaunchCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandArgument(0, "[XEX]")]
		[Description("XEX path to launch, for example Hdd1:\\Aurora\\Aurora.xex.")]
		public string? Xex { get; init; }

		[CommandOption("--xex <PATH>")]
		[Description("XEX path to launch, for example Hdd1:\\Aurora\\Aurora.xex.")]
		public string? XexPath { get; init; }

		[CommandOption("--directory <DIR>")]
		[Description("Working directory passed to XBDM. Defaults to the XEX folder.")]
		public string? Directory { get; init; }

		[CommandOption("--args <TEXT>")]
		[Description("Command-line arguments passed to the XEX.")]
		public string? Arguments { get; init; }

		[CommandOption("--titleid <TITLEID>")]
		[Description("Optional Title ID for display/logging.")]
		public string? TitleId { get; init; }

		[CommandOption("--dry-run")]
		[Description("Show the generated XBDM command without executing it.")]
		public bool DryRun { get; init; }

		[CommandOption("--notify")]
		[Description("Send a default success notification to the console.")]
		public bool Notify { get; init; }

		[CommandOption("--notify-icon <NAME>")]
		[Description("Notification icon preset name.")]
		public string? NotifyIcon { get; init; }

		[CommandOption("--notify-logo <ID>")]
		[Description("Notification logo id (decimal or 0x hex).")]
		public string? NotifyLogo { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		string xexPath = ((!string.IsNullOrWhiteSpace(settings.XexPath)) ? settings.XexPath : settings.Xex);
		if (string.IsNullOrWhiteSpace(xexPath))
		{
			AnsiConsole.MarkupLine("[red]Provide a XEX path with `rgh launch <path>` or `--xex <path>`.[/]");
			return 1;
		}
		string workingDirectory = ((!string.IsNullOrWhiteSpace(settings.Directory)) ? settings.Directory : DeriveDirectory(xexPath));
		string command = BuildMagicBootCommand(xexPath, workingDirectory, settings.Arguments);
		string titleSummary = TryFormatTitleId(settings.TitleId);
		if (settings.DryRun)
		{
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Launch Preview[/]").RuleStyle("grey"));
			AnsiConsole.MarkupLine("[grey]Command:[/] " + Markup.Escape(command));
			if (!string.IsNullOrWhiteSpace(titleSummary))
			{
				AnsiConsole.MarkupLine("[grey]Title:[/] " + Markup.Escape(titleSummary));
			}
			return 0;
		}
		return await CliHelpers.WithClientOnceAsync(settings, async delegate(XbdmClient client)
		{
			await client.SendCommandAsync(command, CancellationToken.None);
			OperationFeedback.WriteSuccess("Launch requested", "[green]" + Markup.Escape(xexPath) + "[/]");
			if (!string.IsNullOrWhiteSpace(titleSummary))
			{
				AnsiConsole.MarkupLine("[grey]Target title:[/] " + Markup.Escape(titleSummary));
			}
			if (!string.IsNullOrWhiteSpace(settings.Arguments))
			{
				AnsiConsole.MarkupLine("[grey]Arguments:[/] " + Markup.Escape(settings.Arguments));
			}
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, settings.NotifyLogo, "Success :)", CancellationToken.None);
			return 0;
		}, CancellationToken.None);
	}

	private static string BuildMagicBootCommand(string xexPath, string workingDirectory, string? arguments)
	{
		List<string> list = new List<string>
		{
			"magicboot",
			"title=\"" + EscapeQuoted(xexPath) + "\""
		};
		if (!string.IsNullOrWhiteSpace(workingDirectory))
		{
			list.Add("directory=\"" + EscapeQuoted(workingDirectory) + "\"");
		}
		if (!string.IsNullOrWhiteSpace(arguments))
		{
			list.Add("cmdline=\"" + EscapeQuoted(arguments) + "\"");
		}
		return string.Join(' ', list);
	}

	private static string EscapeQuoted(string text)
	{
		return text.Replace("\"", "\\\"", StringComparison.Ordinal);
	}

	private static string DeriveDirectory(string xexPath)
	{
		int num = Math.Max(xexPath.LastIndexOf('\\'), xexPath.LastIndexOf('/'));
		if (num <= 0)
		{
			return xexPath;
		}
		return xexPath.Substring(0, num);
	}

	private static string? TryFormatTitleId(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (!TryParseHex(text, out var value))
		{
			return text;
		}
		if (TitleIdDatabase.Instance.TryResolve(value, null, out TitleIdEntry entry) && entry != null)
		{
			return $"{entry.Name} (0x{value:X8})";
		}
		return $"0x{value:X8}";
	}

	private static bool TryParseHex(string text, out uint value)
	{
		string text2 = text.Trim();
		if (text2.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text2 = text2.Substring(2);
		}
		return uint.TryParse(text2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
	}
}
