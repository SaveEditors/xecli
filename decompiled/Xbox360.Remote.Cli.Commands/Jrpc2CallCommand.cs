using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class Jrpc2CallCommand : AsyncCommand<Jrpc2CallCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }

		[CommandOption("--module <MODULE>")]
		public string? Module { get; init; }

		[CommandOption("--ordinal <ORD>")]
		public int? Ordinal { get; init; }

		[CommandOption("--ret <TYPE>")]
		public string? ReturnType { get; init; }

		[CommandOption("--arg <ARG>")]
		public string[] Args { get; init; } = Array.Empty<string>();

		[CommandOption("--vm")]
		public bool Vm { get; init; }

		[CommandOption("--system")]
		public bool SystemThread { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.ReturnType))
		{
			AnsiConsole.MarkupLine("[red]--ret is required (int|uint|float|string|byte|u64|void).[/]");
			return 1;
		}
		if (settings.Address == null && string.IsNullOrWhiteSpace(settings.Module))
		{
			AnsiConsole.MarkupLine("[red]--addr or --module is required.[/]");
			return 1;
		}
		uint address = 0u;
		if (settings.Address != null && !CliHelpers.TryParseUInt32(settings.Address, out address))
		{
			AnsiConsole.MarkupLine("[red]Invalid --addr.[/]");
			return 1;
		}
		RpcDataType rpcDataType;
		switch (settings.ReturnType.ToLowerInvariant())
		{
		case "i32":
		case "u32":
		case "int":
		case "uint":
			rpcDataType = RpcDataType.Int;
			break;
		case "float":
		case "double":
			rpcDataType = RpcDataType.Float;
			break;
		case "string":
			rpcDataType = RpcDataType.String;
			break;
		case "byte":
			rpcDataType = RpcDataType.Byte;
			break;
		case "u64":
		case "i64":
		case "long":
		case "int64":
		case "ulong":
		case "uint64":
			rpcDataType = RpcDataType.Uint64;
			break;
		case "void":
			rpcDataType = RpcDataType.Void;
			break;
		default:
			rpcDataType = RpcDataType.Int;
			break;
		}
		RpcDataType ret = rpcDataType;
		List<RpcArgument> args = new List<RpcArgument>();
		string[] args2 = settings.Args;
		foreach (string text in args2)
		{
			if (!CliHelpers.TryParseRpcArgument(text, out RpcArgument arg))
			{
				AnsiConsole.MarkupLine("[red]Invalid arg:[/] " + text);
				return 1;
			}
			args.Add(arg);
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			AnsiConsole.WriteLine(await new Jrpc2Client(client).CallAsync(address: (settings.Address != null) ? new uint?(address) : ((uint?)null), returnType: ret, module: settings.Module, ordinal: settings.Ordinal, systemThread: settings.SystemThread, vm: settings.Vm, args: args, cancellationToken: CancellationToken.None));
			return 0;
		}, CancellationToken.None);
	}
}
