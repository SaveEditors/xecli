using System.Globalization;
using FluentFTP;
using Spectre.Console;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

internal static class AvatarInstallFlow {
    public static async Task<int> RunAsync(
        AvatarCommandHelpers.AvatarResolvedPaths paths,
        AvatarInstallCommand.Settings settings,
        IReadOnlyList<AvatarItemRecord> selectedItems,
        CancellationToken cancellationToken) {
        if (selectedItems.Count == 0) {
            OperationFeedback.WriteWarning("Avatar install", "No avatar items were selected.");
            return 0;
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(AvatarCommandHelpers.CreateFtpSettings(settings), cancellationToken);
        (string xbdmIp, int xbdmPort, int xbdmTimeout) = await AvatarCommandHelpers.ResolveXbdmTargetAsync(settings, cancellationToken);
        AvatarCommandHelpers.AvatarResolvedOwnership ownership = await AvatarCommandHelpers.ResolveOwnershipAsync(settings, xbdmIp, cancellationToken);

        string device = string.IsNullOrWhiteSpace(settings.Device) ? "Hdd1" : settings.Device.Trim();
        IReadOnlyList<AvatarItemRecord> materializedItems = await AvatarCommandHelpers.MaterializeInstallItemsAsync(selectedItems, paths, cancellationToken);
        List<AvatarInstallPlan> plans = materializedItems
            .Select(item => AvatarInstallPlanner.PrepareInstallPlan(
                new AvatarInstallRequest(
                    item,
                    device,
                    new AvatarOwnershipPatch(ownership.Xuid),
                    settings.WorkingDirectory)))
            .ToList();

        if (settings.Json || settings.DryRun) {
            CliOutput.EmitJson(new {
                Mode = paths.RemoteMode ? "remote" : "local",
                CurrentUser = ownership.Gamertag,
                ownership.XuidText,
                ownership.SignInState,
                Device = device,
                LibraryRoot = paths.EffectiveLibraryRoot,
                CachePath = paths.EffectiveCachePath,
                ManifestUrl = paths.EffectiveManifestUrl,
                ContentBaseUrl = paths.EffectiveContentBaseUrl,
                Items = plans.Select(plan => new {
                    plan.Item.TitleId,
                    plan.Item.TitleName,
                    DisplayName = AvatarCommandHelpers.ResolveItemDisplayName(plan.Item),
                    plan.Item.ContentId,
                    Layout = AvatarCommandHelpers.DescribeLayout(plan.Item),
                    plan.Item.DownloadUrl,
                    plan.PatchedLocalPath,
                    plan.RemoteDirectory,
                    plan.RemoteFilePath
                })
            });
            return 0;
        }

        AnsiConsole.MarkupLine(
            $"[grey]Current user:[/] [springgreen3_1]{Markup.Escape(ownership.Gamertag ?? "unknown")}[/] [grey]|[/] [grey]XUID:[/] [gold1]{Markup.Escape(ownership.XuidText)}[/] [grey]|[/] [grey]State:[/] [deepskyblue1]{Markup.Escape(ownership.SignInState)}[/]");

        bool ftpAvailable = false;
        try {
            await using AsyncFtpClient ftpClient = new AsyncFtpClient(ip, user, pass, port);
            ftpClient.Config.ConnectTimeout = timeout;
            ftpClient.Config.ReadTimeout = timeout;
            ftpClient.Config.DataConnectionConnectTimeout = timeout;
            ftpClient.Config.DataConnectionReadTimeout = timeout;
            await ftpClient.Connect(cancellationToken);
            ftpAvailable = true;
        }
        catch {
            ftpAvailable = false;
        }

        using XbdmClient? xbdmClient = ftpAvailable
            ? null
            : await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
                Host = xbdmIp,
                Port = xbdmPort,
                TimeoutMs = xbdmTimeout
            }, cancellationToken);

        List<AvatarInstallPlan> uploadPlan = new List<AvatarInstallPlan>();
        int skipped = 0;
        foreach (AvatarInstallPlan plan in plans) {
            bool exists = false;
            if (!settings.Overwrite) {
                exists = ftpAvailable
                    ? await SaveHelpers.RemoteFileExistsAsync(ip, port, user, pass, timeout, plan.RemoteFilePath)
                    : await AvatarCommandHelpers.RemoteFileExistsViaXbdmAsync(xbdmClient!, plan.RemoteFilePath, cancellationToken);
            }

            if (exists) {
                skipped++;
                continue;
            }

            uploadPlan.Add(plan);
        }

        if (uploadPlan.Count == 0) {
            OperationFeedback.WriteWarning("Avatar install skipped", "All target files already exist on the console.");
            return 0;
        }

        await CliOutput.RunBatchProgressAsync(
            $"Avatar install {uploadPlan.Count} item(s)",
            uploadPlan.Select(plan => new CliOutput.TransferBatchItem(AvatarCommandHelpers.DescribeItem(plan.Item), new FileInfo(plan.PatchedLocalPath).Length)).ToList(),
            async batch => {
                if (ftpAvailable) {
                    foreach (AvatarInstallPlan plan in uploadPlan) {
                        long size = new FileInfo(plan.PatchedLocalPath).Length;
                        batch.StartFile(AvatarCommandHelpers.DescribeItem(plan.Item), size);
                        Progress<long> progress = new Progress<long>(value => batch.ReportFileProgress(value));
                        await SaveHelpers.UploadFileAsync(ip, port, user, pass, timeout, plan.PatchedLocalPath, plan.RemoteFilePath, progress);
                        batch.CompleteFile();
                    }
                }
                else {
                    foreach (AvatarInstallPlan plan in uploadPlan) {
                        long size = new FileInfo(plan.PatchedLocalPath).Length;
                        batch.StartFile(AvatarCommandHelpers.DescribeItem(plan.Item), size);
                        await AvatarCommandHelpers.EnsureRemoteDirectoryViaXbdmAsync(xbdmClient!, plan.RemoteDirectory, cancellationToken);
                        await using FileStream stream = new FileStream(plan.PatchedLocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        Progress<long> progress = new Progress<long>(value => batch.ReportFileProgress(value));
                        await xbdmClient!.UploadFileAsync(AvatarCommandHelpers.ToXbdmPath(plan.RemoteFilePath), stream, stream.Length, progress, cancellationToken);
                        batch.CompleteFile();
                    }
                }
            });

        List<(AvatarInstallPlan Plan, string? RemoteSize)> verified = new List<(AvatarInstallPlan, string?)>(uploadPlan.Count);
        foreach (AvatarInstallPlan plan in uploadPlan) {
            string? remoteSize = ftpAvailable
                ? await AvatarCommandHelpers.TryGetRemoteFileSizeTextAsync(settings, plan.RemoteFilePath, cancellationToken)
                : await AvatarCommandHelpers.TryGetRemoteFileSizeTextViaXbdmAsync(xbdmClient!, plan.RemoteFilePath, cancellationToken);
            verified.Add((plan, remoteSize));
        }

        string firstRemote = verified[0].Plan.RemoteFilePath;
        long uploadedBytes = verified.Sum(entry => new FileInfo(entry.Plan.PatchedLocalPath).Length);
        OperationFeedback.WriteSuccess(
            "Avatar install complete",
            $"[green]{verified.Count}[/] item(s)  [silver]{FtpHelpers.FormatBytes(uploadedBytes)}[/] -> [cyan]{Markup.Escape(firstRemote)}[/] [grey]via {(ftpAvailable ? "FTP" : "XBDM")}[/]{(verified.Count > 1 ? " [grey](and additional files)[/]" : string.Empty)}");
        if (skipped > 0)
            OperationFeedback.WriteWarning("Avatar install skipped existing files", skipped.ToString(CultureInfo.InvariantCulture));

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[green]Item[/]"));
        table.AddColumn(new TableColumn("[gold1]Content ID[/]"));
        table.AddColumn(new TableColumn("[cyan]Remote[/]"));
        table.AddColumn(new TableColumn("[grey]Size[/]"));
        foreach ((AvatarInstallPlan plan, string? remoteSize) in verified.Take(25)) {
            table.AddRow(
                $"[green]{Markup.Escape(AvatarCommandHelpers.ResolveItemDisplayName(plan.Item))}[/]",
                $"[gold1]{Markup.Escape(plan.Item.ContentId)}[/]",
                $"[cyan]{Markup.Escape(plan.RemoteFilePath)}[/]",
                remoteSize != null ? $"[grey]{remoteSize}[/]" : "[grey]unverified[/]");
        }
        AnsiConsole.Write(table);
        return 0;
    }
}
