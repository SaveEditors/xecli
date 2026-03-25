using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmModuleUnloadCommand : AsyncCommand<XbdmModuleUnloadCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--name <MODULE>")]
		[LocalizedDescription("Loaded module name, e.g. XDRPC.xex.")]
		public string? Name { get; init; }

		[CommandOption("--handle <HANDLE>")]
		[LocalizedDescription("Explicit module handle as hex or decimal.")]
		public string? Handle { get; init; }

		[CommandOption("--skip-mark")]
		[LocalizedDescription("Do not set the sysdll unload marker at handle+0x40 before unloading.")]
		public bool SkipMark { get; init; }

		[CommandOption("--dry-run")]
		[LocalizedDescription("Show the resolved unload target without writing anything.")]
		public bool DryRun { get; init; }

		[CommandOption("--force")]
		[LocalizedDescription("Required. Module unload can wedge the console if the target rejects live unload.")]
		public bool Force { get; init; }

		[CommandOption("--notify")]
		[LocalizedDescription("Send a default success notification to the console.")]
		public bool Notify { get; init; }

		[CommandOption("--notify-icon <NAME>")]
		[LocalizedDescription("Notification icon preset name.")]
		public string? NotifyIcon { get; init; }

		[CommandOption("--notify-logo <ID>")]
		[LocalizedDescription("Notification logo id (decimal or 0x hex).")]
		public string? NotifyLogo { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Name) && string.IsNullOrWhiteSpace(settings.Handle))
		{
			AnsiConsole.MarkupLine("[red]--name or --handle is required.[/]");
			return 1;
		}
		if (!settings.DryRun && !settings.Force)
		{
			AnsiConsole.MarkupLine("[red]--force is required for live unloads.[/]");
			return 1;
		}
		var (ip, port, timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
		return await CliHelpers.WithClientOnceAsync((Ip: ip, Port: port, TimeoutMs: timeout), settings, async delegate(XbdmClient client)
		{
			Jrpc2Client jrpc = new Jrpc2Client(client);
			string moduleName = settings.Name;
			uint handle;
			if (!string.IsNullOrWhiteSpace(settings.Handle))
			{
				if (!CliHelpers.TryParseUInt32(settings.Handle, out handle))
				{
					AnsiConsole.MarkupLine("[red]Invalid --handle.[/]");
					return 1;
				}
			}
			else
			{
				handle = await ModuleCommandHelpers.ResolveModuleHandleAsync(client, jrpc, settings.Name, CancellationToken.None);
				if (handle == 0)
				{
					OperationFeedback.WriteFailure("Module handle not found", Markup.Escape(settings.Name));
					return 1;
				}
			}
			if (settings.DryRun)
			{
				AnsiConsole.MarkupLine($"[grey]Would unload module handle[/] [cyan]0x{handle:X8}[/]");
				return 0;
			}
			if (!settings.SkipMark)
			{
				await client.WriteMemoryAsync(handle + 64, new byte[2] { 0, 1 }, CancellationToken.None);
			}
			uint status = 0u;
			Exception rpcError = null;
			try
			{
				status = await ModuleCommandHelpers.UnloadModuleAsync(jrpc, handle, CancellationToken.None);
			}
			catch (Exception ex) when (ModuleCommandHelpers.IsAmbiguousRpcCompletion(ex))
			{
				rpcError = ex;
			}
			if (status != 0)
			{
				OperationFeedback.WriteFailure("Unload failed", $"NTSTATUS=0x{status:X8}");
				return 1;
			}
			if (!string.IsNullOrWhiteSpace(moduleName))
			{
				XbdmModuleInfo xbdmModuleInfo = await ModuleCommandHelpers.SafeWaitForModuleStateAsync(client, moduleName, shouldExist: false, CancellationToken.None);
				if (xbdmModuleInfo != null && rpcError != null)
				{
					xbdmModuleInfo = await ModuleCommandHelpers.WaitForModuleStateWithReconnectAsync(ip, port, timeout, moduleName, shouldExist: false, CancellationToken.None);
				}
				if (xbdmModuleInfo != null)
				{
					string detail = rpcError?.Message ?? "module is still present in the module list";
					OperationFeedback.WriteFailure("Unload failed", detail);
					return 1;
				}
			}
			else if (rpcError != null)
			{
				OperationFeedback.WriteFailure("Unload failed", rpcError.Message);
				return 1;
			}
			if (!string.IsNullOrWhiteSpace(moduleName))
			{
				ModuleCommandHelpers.RemoveHandle(moduleName);
			}
			string value = ((rpcError != null) ? "[grey](verified after ambiguous RPC completion)[/]" : string.Empty);
			OperationFeedback.WriteSuccess("Module unloaded", $"[cyan]0x{handle:X8}[/] {value}".TrimEnd());
			await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, settings.NotifyLogo, "Success :)", CancellationToken.None);
			return 0;
		}, CancellationToken.None);
	}
}
