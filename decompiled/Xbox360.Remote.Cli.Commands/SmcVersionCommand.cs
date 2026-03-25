using System;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class SmcVersionCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 30000));
			string text = await HardwareHelpers.GetSmcVersionAsync(client, cts.Token);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					SmcVersion = text
				});
				return 0;
			}
			OperationFeedback.WriteSuccess("SMC version", "[deepskyblue1]" + Markup.Escape(text) + "[/]");
			return 0;
		}, CancellationToken.None);
	}
}
