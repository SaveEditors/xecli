using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XellInfoCommand : AsyncCommand<XellCommandSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, XellCommandSettings settings)
	{
		try
		{
			(string, int, int) tuple = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
			XellSessionResult xellSessionResult = await XellWorkflowHelpers.EnsureXellSessionAsync(tuple, settings, "Launch XeLL Reloaded and inspect the available XeLL HTTP services.", CancellationToken.None);
			XellHttpEndpoint endpoint = xellSessionResult.Endpoint;
			string? text = await XellHelpers.TryReadCpuKeyAsync(endpoint, CancellationToken.None);
			string? text2 = await XellHelpers.TryReadStartupLogAsync(endpoint, CancellationToken.None);
			string? text3 = await XellHelpers.TryReadFuseTextAsync(endpoint, CancellationToken.None);
			XellCustomStatus? xellCustomStatus = await XellHelpers.TryReadCustomStatusAsync(endpoint, CancellationToken.None);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Ip = endpoint.Ip,
					RawFlashPath = endpoint.RawFlashPath,
					KeyVaultPath = endpoint.KeyVaultPath,
					RawKeyVaultPath = endpoint.RawKeyVaultPath,
					CpuKeyPath = endpoint.CpuKeyPath,
					StartupLogPath = endpoint.StartupLogPath,
					RebootPath = endpoint.RebootPath,
					CustomStatusPath = xellCustomStatus?.Path,
					CustomStatusState = xellCustomStatus?.State,
					CustomStatusHeartbeatTicks = xellCustomStatus?.HeartbeatTicks,
					CpuKey = text,
					HasStartupLog = !string.IsNullOrWhiteSpace(text2),
					HasFuseDump = !string.IsNullOrWhiteSpace(text3),
					HasCustomStatus = xellCustomStatus != null
				});
				return 0;
			}
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[white]Field[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			table.AddRow("[white]XeLL IP[/]", "[springgreen3_1]" + Markup.Escape(endpoint.Ip) + "[/]");
			table.AddRow("[white]Flash Dump[/]", "[cyan]" + Markup.Escape(endpoint.RawFlashPath) + "[/]");
			table.AddRow("[white]Key Vault[/]", endpoint.KeyVaultPath == null ? "[grey]not exposed[/]" : "[cyan]" + Markup.Escape(endpoint.KeyVaultPath) + "[/]");
			table.AddRow("[white]Raw Key Vault[/]", endpoint.RawKeyVaultPath == null ? "[grey]not exposed[/]" : "[cyan]" + Markup.Escape(endpoint.RawKeyVaultPath) + "[/]");
			table.AddRow("[white]CPU Key[/]", string.IsNullOrWhiteSpace(text) ? "[grey]not detected[/]" : "[gold1]" + Markup.Escape(text) + "[/]");
			table.AddRow("[white]CPU Key Source[/]", endpoint.CpuKeyPath == null ? "[grey]not exposed[/]" : "[cyan]" + Markup.Escape(endpoint.CpuKeyPath) + "[/]");
			table.AddRow("[white]Startup Log[/]", endpoint.StartupLogPath == null ? "[grey]not exposed[/]" : "[cyan]" + Markup.Escape(endpoint.StartupLogPath) + "[/]");
			table.AddRow("[white]Fuse Dump[/]", string.IsNullOrWhiteSpace(text3) ? "[grey]not captured[/]" : "[springgreen3_1]available[/]");
			table.AddRow("[white]Reboot[/]", endpoint.RebootPath == null ? "[grey]not exposed[/]" : "[cyan]" + Markup.Escape(endpoint.RebootPath) + "[/]");
			table.AddRow("[white]XeCLI Status[/]", xellCustomStatus == null ? "[grey]not exposed[/]" : "[cyan]" + Markup.Escape(xellCustomStatus.Path) + "[/]");
			if (xellCustomStatus != null)
			{
				table.AddRow("[white]XeCLI State[/]", "[springgreen3_1]" + Markup.Escape(xellCustomStatus.State) + "[/]");
				table.AddRow("[white]Heartbeat[/]", xellCustomStatus.HeartbeatTicks.HasValue ? "[white]" + xellCustomStatus.HeartbeatTicks.Value + "[/]" : "[grey]not reported[/]");
			}
			AnsiConsole.Write(table);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				OperationFeedback.WriteSuccess("XeLL startup log", "[grey]Startup log is available through[/] [cyan]" + Markup.Escape(endpoint.StartupLogPath ?? "/LOG") + "[/]");
			}
			if (xellCustomStatus != null && !string.IsNullOrWhiteSpace(xellCustomStatus.Title))
			{
				AnsiConsole.MarkupLine("[grey]XeCLI payload:[/] [white]" + Markup.Escape(xellCustomStatus.Title) + "[/]");
				AnsiConsole.MarkupLine("[grey]State:[/] [springgreen3_1]" + Markup.Escape(xellCustomStatus.State) + "[/] [grey]|[/] [grey]Heartbeat:[/] [white]" + Markup.Escape(xellCustomStatus.HeartbeatTicks?.ToString() ?? "n/a") + "[/]");
			}
			return 0;
		}
		catch (System.Exception ex)
		{
			OperationFeedback.WriteFailure("XeLL info", ex.Message);
			XellWorkflowHelpers.WriteManualLaunchGuidance("rgh xell info");
			return 1;
		}
	}
}
