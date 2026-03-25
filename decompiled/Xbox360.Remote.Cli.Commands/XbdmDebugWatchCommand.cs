using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmDebugWatchCommand : AsyncCommand<XbdmDebugWatchCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--duration <SEC>")]
		[LocalizedDescription("How long to wait for notifications before exiting (default: 30).")]
		public int? DurationSeconds { get; init; }

		[CommandOption("--max <N>")]
		[LocalizedDescription("Maximum notifications to print before exiting.")]
		public int? MaxEvents { get; init; }

		[CommandOption("--raw")]
		[LocalizedDescription("Print raw notify lines instead of parsed summaries.")]
		public bool Raw { get; init; }

		[CommandOption("--stopon-fce")]
		[LocalizedDescription("Ask XBDM to stop on first-chance exceptions.")]
		public bool StopOnFce { get; init; }

		[CommandOption("--name <NAME>")]
		[LocalizedDescription("Debugger session name (default: XeCLI).")]
		public string? DebuggerName { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		(string, int, int) tuple = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		int timeout = tuple.Item3;
		int durationSeconds = Math.Max(1, settings.DurationSeconds ?? 30);
		int maxEvents = Math.Max(1, settings.MaxEvents ?? int.MaxValue);
		string debuggerName = (string.IsNullOrWhiteSpace(settings.DebuggerName) ? "XeCLI" : settings.DebuggerName.Trim());
		using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
		CancellationToken token = timeoutCts.Token;
		int result;
		await using (XbdmClient control = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
		{
			Host = ip,
			Port = port,
			TimeoutMs = timeout
		}, token))
		{
			await control.SendCommandAsync($"debugger connect override name=\"{EscapeQuoted(debuggerName)}\" user=\"{EscapeQuoted(Environment.MachineName)}\"", token);
			if (settings.StopOnFce)
			{
				await control.SendCommandAsync("stopon fce", token);
			}
			using TcpClient notifyClient = new TcpClient();
			notifyClient.ReceiveTimeout = timeout;
			notifyClient.SendTimeout = timeout;
			await notifyClient.ConnectAsync(ip, port, token);
			using NetworkStream notifyStream = notifyClient.GetStream();
			using StreamReader reader = new StreamReader(notifyStream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);
			using StreamWriter writer = new StreamWriter(notifyStream, Encoding.ASCII, 4096, leaveOpen: true)
			{
				NewLine = "\r\n",
				AutoFlush = true
			};
			if (string.IsNullOrWhiteSpace(await reader.ReadLineAsync(token)))
			{
				throw new IOException("Notification connection did not return a handshake.");
			}
			await writer.WriteLineAsync("notify reconnectport=1");
			string text = await reader.ReadLineAsync(token);
			if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("205", StringComparison.Ordinal))
			{
				throw new IOException("Notification setup failed: " + (text ?? "(no response)"));
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Debug Watch[/]").RuleStyle("grey"));
			AnsiConsole.MarkupLine($"[grey]Target:[/] [cyan]{ip}:{port}[/]  [grey]Session:[/] [green]{Markup.Escape(debuggerName)}[/]  [grey]Timeout:[/] [cyan]{durationSeconds}s[/]");
			int count = 0;
			try
			{
				while (!token.IsCancellationRequested && count < maxEvents)
				{
					string text2 = await reader.ReadLineAsync(token);
					if (!string.IsNullOrWhiteSpace(text2))
					{
						RenderEvent(XbdmNotifyEvent.Parse(text2), settings.Raw);
						count++;
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
			AnsiConsole.MarkupLine($"[grey]Watch ended after[/] [cyan]{count}[/] [grey]event(s).[/]");
			result = 0;
		}
		return result;
	}

	private static void RenderEvent(XbdmNotifyEvent evt, bool raw)
	{
		string text = $"[green]{DateTime.Now:HH:mm:ss}[/]";
		if (raw)
		{
			AnsiConsole.MarkupLine(text + " [grey]" + Markup.Escape(evt.Raw) + "[/]");
			return;
		}
		switch (evt.Type.ToLowerInvariant())
		{
		case "debugstr":
			AnsiConsole.MarkupLine(text + " [deepskyblue1]debugstr[/] " + Markup.Escape(evt.GetString("string") ?? evt.Raw));
			break;
		case "execution":
		{
			string text2 = ((evt.Commands.Count > 1) ? evt.Commands[1] : evt.Raw);
			AnsiConsole.MarkupLine(text + " [yellow]execution[/] [white]" + Markup.Escape(text2) + "[/]");
			break;
		}
		case "exception":
			AnsiConsole.MarkupLine($"{text} [red]exception[/] code=[white]{Markup.Escape(evt.GetHexOrDefault("code"))}[/] thread=[white]{Markup.Escape(evt.GetHexOrDefault("thread"))}[/] address=[white]{Markup.Escape(evt.GetHexOrDefault("address"))}[/] {Markup.Escape(evt.GetFaultOperationSummary())}");
			break;
		case "break":
			AnsiConsole.MarkupLine($"{text} [gold1]break[/] addr=[white]{Markup.Escape(evt.GetHexOrDefault("addr"))}[/] thread=[white]{Markup.Escape(evt.GetHexOrDefault("thread"))}[/]");
			break;
		case "databreak":
			AnsiConsole.MarkupLine($"{text} [gold1]databreak[/] {Markup.Escape(evt.GetFaultOperationSummary())} pc=[white]{Markup.Escape(evt.GetHexOrDefault("addr"))}[/] thread=[white]{Markup.Escape(evt.GetHexOrDefault("thread"))}[/]");
			break;
		default:
			AnsiConsole.MarkupLine(text + " [grey]" + Markup.Escape(evt.Raw) + "[/]");
			break;
		}
	}

	private static string EscapeQuoted(string text)
	{
		return text.Replace("\"", "\\\"", StringComparison.Ordinal);
	}
}
