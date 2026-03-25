using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

internal static class AvatarInstallFlow
{
	public static async Task<int> RunAsync(AvatarCommandHelpers.AvatarResolvedPaths paths, AvatarInstallCommand.Settings settings, IReadOnlyList<AvatarItemRecord> selectedItems, CancellationToken cancellationToken)
	{
		if (selectedItems.Count == 0)
		{
			OperationFeedback.WriteWarning("Avatar install", "No avatar items were selected.");
			return 0;
		}
		(string, int, string, string, int) tuple = await FtpHelpers.ResolveAsync(AvatarCommandHelpers.CreateFtpSettings(settings), cancellationToken);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeout = tuple.Item5;
		(string, int, int) tuple2 = await AvatarCommandHelpers.ResolveXbdmTargetAsync(settings, cancellationToken);
		string xbdmIp = tuple2.Item1;
		int xbdmPort = tuple2.Item2;
		int xbdmTimeout = tuple2.Item3;
		AvatarCommandHelpers.AvatarResolvedOwnership ownership = await AvatarCommandHelpers.ResolveOwnershipAsync(settings, xbdmIp, cancellationToken);
		string device = (string.IsNullOrWhiteSpace(settings.Device) ? "Hdd1" : settings.Device.Trim());
		List<AvatarInstallPlan> plans = (await AvatarCommandHelpers.MaterializeInstallItemsAsync(selectedItems, paths, cancellationToken)).Select((AvatarItemRecord item2) => AvatarInstallPlanner.PrepareInstallPlan(new AvatarInstallRequest(item2, device, new AvatarOwnershipPatch(ownership.Xuid), settings.WorkingDirectory))).ToList();
		if (settings.Json || settings.DryRun)
		{
			CliOutput.EmitJson(new
			{
				Mode = (paths.RemoteMode ? "remote" : "local"),
				CurrentUser = ownership.Gamertag,
				XuidText = ownership.XuidText,
				SignInState = ownership.SignInState,
				Device = device,
				LibraryRoot = paths.EffectiveLibraryRoot,
				CachePath = paths.EffectiveCachePath,
				ManifestUrl = paths.EffectiveManifestUrl,
				ContentBaseUrl = paths.EffectiveContentBaseUrl,
				Items = plans.Select((AvatarInstallPlan avatarInstallPlan2) => new
				{
					TitleId = avatarInstallPlan2.Item.TitleId,
					TitleName = avatarInstallPlan2.Item.TitleName,
					DisplayName = AvatarCommandHelpers.ResolveItemDisplayName(avatarInstallPlan2.Item),
					ContentId = avatarInstallPlan2.Item.ContentId,
					Layout = AvatarCommandHelpers.DescribeLayout(avatarInstallPlan2.Item),
					DownloadUrl = avatarInstallPlan2.Item.DownloadUrl,
					PatchedLocalPath = avatarInstallPlan2.PatchedLocalPath,
					RemoteDirectory = avatarInstallPlan2.RemoteDirectory,
					RemoteFilePath = avatarInstallPlan2.RemoteFilePath
				})
			});
			return 0;
		}
		AnsiConsole.MarkupLine($"[grey]Current user:[/] [springgreen3_1]{Markup.Escape(ownership.Gamertag ?? "unknown")}[/] [grey]|[/] [grey]XUID:[/] [gold1]{Markup.Escape(ownership.XuidText)}[/] [grey]|[/] [grey]State:[/] [deepskyblue1]{Markup.Escape(ownership.SignInState)}[/]");
		bool ftpAvailable = false;
		try
		{
			await using AsyncFtpClient ftpClient = new AsyncFtpClient(ip, user, pass, port);
			ftpClient.Config.ConnectTimeout = timeout;
			ftpClient.Config.ReadTimeout = timeout;
			ftpClient.Config.DataConnectionConnectTimeout = timeout;
			ftpClient.Config.DataConnectionReadTimeout = timeout;
			await ftpClient.Connect(cancellationToken);
			ftpAvailable = true;
		}
		catch
		{
			ftpAvailable = false;
		}
		XbdmClient xbdmClient = ((!ftpAvailable) ? (await XbdmClient.ConnectAsync(new XbdmConnectionOptions
		{
			Host = xbdmIp,
			Port = xbdmPort,
			TimeoutMs = xbdmTimeout
		}, cancellationToken)) : null);
		XbdmClient xbdmClient2 = xbdmClient;
		try
		{
			List<AvatarInstallPlan> uploadPlan = new List<AvatarInstallPlan>();
			int skipped = 0;
			foreach (AvatarInstallPlan plan in plans)
			{
				bool flag = false;
				if (!settings.Overwrite)
				{
					bool flag2 = ((!ftpAvailable) ? (await AvatarCommandHelpers.RemoteFileExistsViaXbdmAsync(xbdmClient2, plan.RemoteFilePath, cancellationToken)) : (await SaveHelpers.RemoteFileExistsAsync(ip, port, user, pass, timeout, plan.RemoteFilePath)));
					flag = flag2;
				}
				if (flag)
				{
					skipped++;
				}
				else
				{
					uploadPlan.Add(plan);
				}
			}
			if (uploadPlan.Count == 0)
			{
				OperationFeedback.WriteWarning("Avatar install skipped", "All target files already exist on the console.");
				return 0;
			}
			await CliOutput.RunBatchProgressAsync($"Avatar install {uploadPlan.Count} item(s)", uploadPlan.Select((AvatarInstallPlan avatarInstallPlan2) => new CliOutput.TransferBatchItem(AvatarCommandHelpers.DescribeItem(avatarInstallPlan2.Item), new FileInfo(avatarInstallPlan2.PatchedLocalPath).Length)).ToList(), async delegate(TransferBatchScope batch)
			{
				if (ftpAvailable)
				{
					foreach (AvatarInstallPlan item2 in uploadPlan)
					{
						long length = new FileInfo(item2.PatchedLocalPath).Length;
						batch.StartFile(AvatarCommandHelpers.DescribeItem(item2.Item), length);
						Progress<long> progress = new Progress<long>(delegate(long value)
						{
							batch.ReportFileProgress(value);
						});
						await SaveHelpers.UploadFileAsync(ip, port, user, pass, timeout, item2.PatchedLocalPath, item2.RemoteFilePath, progress);
						batch.CompleteFile();
					}
				}
				else
				{
					foreach (AvatarInstallPlan plan2 in uploadPlan)
					{
						long length2 = new FileInfo(plan2.PatchedLocalPath).Length;
						batch.StartFile(AvatarCommandHelpers.DescribeItem(plan2.Item), length2);
						await AvatarCommandHelpers.EnsureRemoteDirectoryViaXbdmAsync(xbdmClient2, plan2.RemoteDirectory, cancellationToken);
						await using FileStream stream = new FileStream(plan2.PatchedLocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
						Progress<long> progress2 = new Progress<long>(delegate(long value)
						{
							batch.ReportFileProgress(value);
						});
						await xbdmClient2.UploadFileAsync(AvatarCommandHelpers.ToXbdmPath(plan2.RemoteFilePath), stream, stream.Length, progress2, cancellationToken);
						batch.CompleteFile();
					}
				}
			});
			List<(AvatarInstallPlan Plan, string? RemoteSize)> verified = new List<(AvatarInstallPlan, string)>(uploadPlan.Count);
			foreach (AvatarInstallPlan plan in uploadPlan)
			{
				string text = ((!ftpAvailable) ? (await AvatarCommandHelpers.TryGetRemoteFileSizeTextViaXbdmAsync(xbdmClient2, plan.RemoteFilePath, cancellationToken)) : (await AvatarCommandHelpers.TryGetRemoteFileSizeTextAsync(settings, plan.RemoteFilePath, cancellationToken)));
				string item = text;
				verified.Add((plan, item));
			}
			string remoteFilePath = verified[0].Plan.RemoteFilePath;
			long bytes = verified.Sum<(AvatarInstallPlan, string)>(((AvatarInstallPlan Plan, string RemoteSize) entry) => new FileInfo(entry.Plan.PatchedLocalPath).Length);
			OperationFeedback.WriteSuccess("Avatar install complete", $"[green]{verified.Count}[/] item(s)  [silver]{FtpHelpers.FormatBytes(bytes)}[/] -> [cyan]{Markup.Escape(remoteFilePath)}[/] [grey]via {(ftpAvailable ? "FTP" : "XBDM")}[/]{((verified.Count > 1) ? " [grey](and additional files)[/]" : string.Empty)}");
			if (skipped > 0)
			{
				OperationFeedback.WriteWarning("Avatar install skipped existing files", skipped.ToString(CultureInfo.InvariantCulture));
			}
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[green]Item[/]"));
			table.AddColumn(new TableColumn("[gold1]Content ID[/]"));
			table.AddColumn(new TableColumn("[cyan]Remote[/]"));
			table.AddColumn(new TableColumn("[grey]Size[/]"));
			foreach (var (avatarInstallPlan, text2) in verified.Take(25))
			{
				table.AddRow("[green]" + Markup.Escape(AvatarCommandHelpers.ResolveItemDisplayName(avatarInstallPlan.Item)) + "[/]", "[gold1]" + Markup.Escape(avatarInstallPlan.Item.ContentId) + "[/]", "[cyan]" + Markup.Escape(avatarInstallPlan.RemoteFilePath) + "[/]", (text2 != null) ? ("[grey]" + text2 + "[/]") : "[grey]unverified[/]");
			}
			AnsiConsole.Write(table);
			return 0;
		}
		finally
		{
			if (xbdmClient2 != null)
			{
				((IDisposable)xbdmClient2).Dispose();
			}
		}
	}
}
