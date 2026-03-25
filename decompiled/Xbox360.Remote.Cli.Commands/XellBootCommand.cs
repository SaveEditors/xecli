using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XellBootCommand : AsyncCommand<XellCommandSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, XellCommandSettings settings)
	{
		try
		{
			(string, int, int) tuple = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
			XellSessionResult xellSessionResult = await XellWorkflowHelpers.EnsureXellSessionAsync(tuple, settings, "Launch XeLL Reloaded so XeCLI can use the XeLL HTTP services.", CancellationToken.None);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					XellIp = xellSessionResult.Endpoint.Ip,
					AlreadyInXell = xellSessionResult.InitialMode == XellConsoleMode.Xell
				});
				return 0;
			}
			OperationFeedback.WriteSuccess("XeLL ready", "[grey]XeLL IP:[/] [cyan]" + Markup.Escape(xellSessionResult.Endpoint.Ip) + "[/]");
			return 0;
		}
		catch (System.Exception ex)
		{
			OperationFeedback.WriteFailure("XeLL boot", ex.Message);
			XellWorkflowHelpers.WriteManualLaunchGuidance("rgh xell boot");
			return 1;
		}
	}
}
