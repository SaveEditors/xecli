using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmModuleLoadCommand : AsyncCommand<XbdmModuleLoadCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--path <PATH>")]
		[Description("Remote XEX path to load, e.g. Hdd:\\XDRPC.xex.")]
		public string? Path { get; init; }

		[CommandOption("--flags <N>")]
		[Description("Kernel load flags (default 8).")]
		public int? Flags { get; init; }

		[CommandOption("--system")]
		[Description("Run the load RPC on a system thread instead of the default title thread.")]
		public bool SystemThread { get; init; }

		[CommandOption("--reboot-expected")]
		[Description("Treat a console disconnect/reboot as an expected part of the load and persist pending verification.")]
		public bool RebootExpected { get; init; }

		[CommandOption("--dry-run")]
		[Description("Show the resolved call without writing anything.")]
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
		if (string.IsNullOrWhiteSpace(settings.Path))
		{
			AnsiConsole.MarkupLine("[red]--path is required.[/]");
			return 1;
		}
		string modulePath = settings.Path.Trim();
		int flags = settings.Flags ?? 8;
		string moduleName = Path.GetFileName(modulePath.Replace('\\', Path.DirectorySeparatorChar));
		if (settings.DryRun)
		{
			AnsiConsole.MarkupLine($"[grey]Would load[/] [cyan]{Markup.Escape(modulePath)}[/] [grey]with flags[/] [cyan]{flags}[/]");
			return 0;
		}
		var (ip, port, timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		return await CliHelpers.WithClientOnceAsync((Ip: ip, Port: port, TimeoutMs: timeout), settings, async delegate(XbdmClient client)
		{
			Jrpc2Client jrpc = new Jrpc2Client(client);
			uint status = 0u;
			Exception rpcError = null;
			try
			{
				status = ModuleCommandHelpers.ParseRpcUInt32(await jrpc.CallAsync(RpcDataType.Int, null, "xboxkrnl.exe", 409, settings.SystemThread, vm: false, new RpcArgument[4]
				{
					new RpcArgument(RpcArgType.Bytes, ModuleCommandHelpers.CreateNullTerminatedAscii(modulePath)),
					new RpcArgument(RpcArgType.Int, flags),
					new RpcArgument(RpcArgType.Int, 0),
					new RpcArgument(RpcArgType.Int, 0)
				}, CancellationToken.None));
			}
			catch (Exception ex) when (ModuleCommandHelpers.IsAmbiguousRpcCompletion(ex))
			{
				rpcError = ex;
			}
			if (rpcError != null && settings.RebootExpected)
			{
				ModuleCommandHelpers.StorePendingLoad(moduleName, modulePath, settings.SystemThread);
				OperationFeedback.WriteWarning("Module load pending verification", "[yellow]" + Markup.Escape(moduleName) + "[/] [grey]triggered a disconnect/reboot-expected path. Reboot the console if needed, then run[/] [cyan]rgh modules pending[/] [grey]or[/] [cyan]rgh status[/] [grey]to verify.[/]");
				return 0;
			}
			XbdmModuleInfo loaded = await ModuleCommandHelpers.SafeWaitForModuleStateAsync(client, moduleName, shouldExist: true, CancellationToken.None);
			if (loaded == null && rpcError != null)
			{
				loaded = await ModuleCommandHelpers.WaitForModuleStateWithReconnectAsync(ip, port, timeout, moduleName, shouldExist: true, CancellationToken.None);
			}
			if (status != 0)
			{
				OperationFeedback.WriteFailure("Load failed", $"NTSTATUS=0x{status:X8}");
				return 1;
			}
			if (loaded == null && rpcError != null)
			{
				OperationFeedback.WriteFailure("Load failed", rpcError.Message);
				return 1;
			}
			if (loaded != null)
			{
				XbdmModuleInfo xbdmModuleInfo = await ModuleCommandHelpers.ConfirmModulePresenceFreshAsync(ip, port, timeout, moduleName, CancellationToken.None);
				if (xbdmModuleInfo == null)
				{
					OperationFeedback.WriteFailure("Load failed", "module did not remain loaded after the initial success response");
					return 1;
				}
				loaded = xbdmModuleInfo;
			}
			uint num = 0u;
			if (rpcError == null)
			{
				num = await ModuleCommandHelpers.ResolveModuleHandleAsync(client, jrpc, moduleName, CancellationToken.None);
			}
			else if (loaded != null)
			{
				num = await ModuleCommandHelpers.TryResolveHandleAfterReconnectAsync(ip, port, timeout, moduleName, CancellationToken.None);
			}
			if (num != 0)
			{
				ModuleCommandHelpers.StoreHandle(moduleName, num);
			}
			ModuleCommandHelpers.ClearPendingIfMatch("load", moduleName);
			if (loaded != null)
			{
				string value = ((num != 0) ? $" [grey]handle[/] [gold1]0x{num:X8}[/]" : string.Empty);
				string value2 = ((rpcError != null) ? " [grey](verified after ambiguous RPC completion)[/]" : string.Empty);
				OperationFeedback.WriteSuccess("Module loaded", $"[green]{Markup.Escape(loaded.Name)}[/] [grey]at[/] [cyan]0x{loaded.BaseAddress:X8}[/]{value}{value2}");
			}
			else
			{
				string text = ((num != 0) ? $" [grey]handle[/] [gold1]0x{num:X8}[/]" : string.Empty);
				OperationFeedback.WriteWarning("Module load returned success", "[yellow]" + Markup.Escape(moduleName) + "[/] [grey]was not visible in the module list yet.[/]" + text);
			}
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, settings.NotifyLogo, "Success :)", CancellationToken.None);
			return 0;
		}, CancellationToken.None);
	}
}
