using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class Jrpc2TempsCommand : AsyncCommand<Jrpc2TempsCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--sensor <SENSOR>")]
		[Description("cpu|gpu|edram|motherboard")]
		public string? Sensor { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			Jrpc2Client jrpc2Client = new Jrpc2Client(client);
			AnsiConsole.WriteLine((await jrpc2Client.GetTemperatureAsync(settings.Sensor?.ToLowerInvariant() switch
			{
				"cpu" => SensorType.CPU, 
				"gpu" => SensorType.GPU, 
				"edram" => SensorType.EDRAM, 
				"motherboard" => SensorType.MotherBoard, 
				_ => SensorType.CPU, 
			}, CancellationToken.None)).ToString(CultureInfo.InvariantCulture));
			return 0;
		}, CancellationToken.None);
	}
}
