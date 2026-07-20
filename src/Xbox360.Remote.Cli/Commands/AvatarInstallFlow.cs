using System.Globalization;
using FluentFTP;
using Spectre.Console;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

internal static class AvatarInstallFlow {
    private const string AvatarUploadFailureMessage = "Avatar install failed. Check available storage and try again.";

    public static async Task<int> RunAsync(
        AvatarCommandHelpers.AvatarResolvedPaths paths,
        AvatarInstallCommand.Settings settings,
        IReadOnlyList<AvatarItemRecord> selectedItems,
        CancellationToken cancellationToken) {
        if (selectedItems.Count == 0) {
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "avatar install",
                    Status = "no-op",
                    Uploaded = 0,
                    Skipped = 0,
                    Message = "No avatar items were selected."
                });
                return 0;
            }

            OperationFeedback.WriteWarning("Avatar install", "No avatar items were selected.");
            return 0;
        }

        string device = string.IsNullOrWhiteSpace(settings.Device) ? "Hdd1" : settings.Device.Trim();

        if (settings.DryRun) {
            AvatarCommandHelpers.AvatarInstallProfilePreview profile = AvatarCommandHelpers.DescribeProfileIntent(settings);
            AvatarCacheStatus cacheStatus = AvatarCommandHelpers.DescribeCacheStatus(paths);
            AvatarInstallPreflight preflight = BuildPreflight(paths, device, profile, cacheStatus, selectedItems);

            if (settings.Json)
                CliOutput.EmitJson(preflight);
            else
                WritePreflightSummary(preflight);

            return 0;
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(AvatarCommandHelpers.CreateFtpSettings(settings), cancellationToken);
        (string xbdmIp, int xbdmPort, int xbdmTimeout) = await AvatarCommandHelpers.ResolveXbdmTargetAsync(settings, cancellationToken);
        AvatarCommandHelpers.AvatarResolvedOwnership ownership = await AvatarCommandHelpers.ResolveOwnershipAsync(settings, xbdmIp, cancellationToken);

        IReadOnlyList<AvatarItemRecord> materializedItems = await AvatarCommandHelpers.MaterializeInstallItemsAsync(
            selectedItems,
            paths,
            cancellationToken,
            showProgress: !settings.Json);
        List<AvatarInstallPlan> plans = materializedItems
            .Select(item => AvatarInstallPlanner.PrepareInstallPlan(
                new AvatarInstallRequest(
                    item,
                    device,
                    new AvatarOwnershipPatch(ownership.Xuid),
                    settings.WorkingDirectory)))
            .ToList();

        if (!settings.Json) {
            AnsiConsole.MarkupLine(
                $"[grey]Current user:[/] [springgreen3_1]{Markup.Escape(ownership.Gamertag ?? "unknown")}[/] [grey]|[/] [grey]XUID:[/] [gold1]{Markup.Escape(ownership.XuidText)}[/] [grey]|[/] [grey]State:[/] [deepskyblue1]{Markup.Escape(ownership.SignInState)}[/]");
        }

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

        await using XbdmClient? xbdmClient = ftpAvailable
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
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "avatar install",
                    Status = "no-op",
                    Mode = paths.RemoteMode ? "remote" : "local",
                    Transport = ftpAvailable ? "FTP" : "XBDM",
                    Uploaded = 0,
                    Skipped = skipped,
                    Message = "All target files already exist on the console."
                });
                return 0;
            }

            OperationFeedback.WriteWarning("Avatar install skipped", "All target files already exist on the console.");
            return 0;
        }

        async Task UploadAsync(TransferBatchScope? batch) {
            try {
                if (ftpAvailable) {
                    foreach (AvatarInstallPlan plan in uploadPlan) {
                        long size = new FileInfo(plan.PatchedLocalPath).Length;
                        batch?.StartFile(AvatarCommandHelpers.DescribeItem(plan.Item), size);
                        Progress<long>? progress = batch is null
                            ? null
                            : new Progress<long>(value => batch.ReportFileProgress(value));
                        await SaveHelpers.UploadFileAsync(ip, port, user, pass, timeout, plan.PatchedLocalPath, plan.RemoteFilePath, progress);
                        batch?.CompleteFile();
                    }
                }
                else {
                    foreach (AvatarInstallPlan plan in uploadPlan) {
                        long size = new FileInfo(plan.PatchedLocalPath).Length;
                        batch?.StartFile(AvatarCommandHelpers.DescribeItem(plan.Item), size);
                        await AvatarCommandHelpers.EnsureRemoteDirectoryViaXbdmAsync(xbdmClient!, plan.RemoteDirectory, cancellationToken);
                        await using FileStream stream = new FileStream(plan.PatchedLocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        Progress<long>? progress = batch is null
                            ? null
                            : new Progress<long>(value => batch.ReportFileProgress(value));
                        await xbdmClient!.UploadFileAsync(AvatarCommandHelpers.ToXbdmPath(plan.RemoteFilePath), stream, stream.Length, progress, cancellationToken);
                        batch?.CompleteFile();
                    }
                }
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (TimeoutException) {
                throw;
            }
            catch (Exception ex) {
                throw new InvalidOperationException(AvatarUploadFailureMessage, ex);
            }
        }

        if (settings.Json) {
            await UploadAsync(null);
        }
        else {
            await CliOutput.RunBatchProgressAsync(
                $"Avatar install {uploadPlan.Count} item(s)",
                uploadPlan.Select(plan => new CliOutput.TransferBatchItem(AvatarCommandHelpers.DescribeItem(plan.Item), new FileInfo(plan.PatchedLocalPath).Length)).ToList(),
                UploadAsync);
        }

        List<(AvatarInstallPlan Plan, string? RemoteSize)> verified = new List<(AvatarInstallPlan, string?)>(uploadPlan.Count);
        foreach (AvatarInstallPlan plan in uploadPlan) {
            string? remoteSize = ftpAvailable
                ? await AvatarCommandHelpers.TryGetRemoteFileSizeTextAsync(settings, plan.RemoteFilePath, cancellationToken)
                : await AvatarCommandHelpers.TryGetRemoteFileSizeTextViaXbdmAsync(xbdmClient!, plan.RemoteFilePath, cancellationToken);
            verified.Add((plan, remoteSize));
        }

        string firstRemote = verified[0].Plan.RemoteFilePath;
        long uploadedBytes = verified.Sum(entry => new FileInfo(entry.Plan.PatchedLocalPath).Length);
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Operation = "avatar install",
                Status = "completed",
                Mode = paths.RemoteMode ? "remote" : "local",
                Transport = ftpAvailable ? "FTP" : "XBDM",
                CurrentUser = ownership.Gamertag,
                ownership.XuidText,
                ownership.SignInState,
                Device = device,
                Uploaded = verified.Count,
                UploadedBytes = uploadedBytes,
                Skipped = skipped,
                Items = verified.Select(entry => new {
                    entry.Plan.Item.TitleId,
                    entry.Plan.Item.TitleName,
                    DisplayName = AvatarCommandHelpers.ResolveItemDisplayName(entry.Plan.Item),
                    entry.Plan.Item.ContentId,
                    Layout = AvatarCommandHelpers.DescribeLayout(entry.Plan.Item),
                    entry.Plan.RemoteFilePath,
                    LocalBytes = new FileInfo(entry.Plan.PatchedLocalPath).Length,
                    RemoteSize = entry.RemoteSize
                })
            });
            return 0;
        }

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

    private static AvatarInstallPreflight BuildPreflight(
        AvatarCommandHelpers.AvatarResolvedPaths paths,
        string device,
        AvatarCommandHelpers.AvatarInstallProfilePreview profile,
        AvatarCacheStatus cacheStatus,
        IReadOnlyList<AvatarItemRecord> items) {
        return new AvatarInstallPreflight(
            paths.RemoteMode ? "remote" : "local",
            device,
            profile,
            cacheStatus,
            items.Select(item => new AvatarInstallPreflightItem(
                item.TitleId,
                item.TitleName,
                AvatarCommandHelpers.DescribeLayout(item),
                item.ContentId,
                AvatarCommandHelpers.ResolveItemDisplayName(item),
                item.Publisher,
                item.SizeBytes)).ToArray());
    }

    private static void WritePreflightSummary(AvatarInstallPreflight preflight) {
        AnsiConsole.Write(new Rule("[bold deepskyblue1]Avatar install preflight[/]").RuleStyle("grey"));

        Table summary = CliOutput.CreateTable();
        summary.AddColumn(new TableColumn("[bold white]Field[/]"));
        summary.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
        summary.AddRow("[white]Mode[/]", preflight.Mode == "remote" ? "[gold1]remote[/]" : "[springgreen3_1]local[/]");
        summary.AddRow("[white]Device[/]", $"[cyan]{Markup.Escape(preflight.Device)}[/]");
        summary.AddRow("[white]Profile[/]", FormatProfileIntent(preflight.Profile));
        summary.AddRow("[white]Cache[/]", FormatCacheStatus(preflight.Cache));
        summary.AddRow("[white]Items[/]", $"[cyan]{preflight.Items.Count.ToString(CultureInfo.InvariantCulture)}[/]");
        AnsiConsole.Write(summary);

        Table items = CliOutput.CreateTable();
        items.AddColumn(new TableColumn("[cyan]Title ID[/]"));
        items.AddColumn(new TableColumn("[green]Title[/]"));
        items.AddColumn(new TableColumn("[deepskyblue1]Category[/]"));
        items.AddColumn(new TableColumn("[gold1]Content ID[/]"));
        items.AddColumn(new TableColumn("[grey]Item[/]"));
        foreach (AvatarInstallPreflightItem item in preflight.Items) {
            items.AddRow(
                $"[cyan]0x{item.TitleId:X8}[/]",
                $"[green]{Markup.Escape(item.TitleName)}[/]",
                $"[deepskyblue1]{Markup.Escape(item.Category)}[/]",
                $"[gold1]{Markup.Escape(item.ContentId)}[/]",
                $"[grey]{Markup.Escape(item.DisplayName)}[/]");
        }
        AnsiConsole.Write(items);
    }

    private static string FormatProfileIntent(AvatarCommandHelpers.AvatarInstallProfilePreview profile) {
        string head = profile.Mode == "explicit" ? "[springgreen3_1]explicit[/]" : "[gold1]current-user[/]";
        string detail = $"[grey]{Markup.Escape(profile.Detail)}[/]";
        if (!string.IsNullOrWhiteSpace(profile.Gamertag))
            detail += $"\n[grey]Gamertag:[/] [springgreen3_1]{Markup.Escape(profile.Gamertag)}[/]";
        if (!string.IsNullOrWhiteSpace(profile.XuidText))
            detail += $"\n[grey]XUID:[/] [gold1]{Markup.Escape(profile.XuidText)}[/]";
        if (profile.RequiresConsoleProbe)
            detail += "\n[grey]Console probe:[/] [yellow]deferred[/]";
        return $"{head}\n{detail}";
    }

    private static string FormatCacheStatus(AvatarCacheStatus cache) {
        string state = cache.State.ToLowerInvariant() switch {
            "ready" => "[springgreen3_1]ready[/]",
            "stale" => "[gold1]stale[/]",
            "missing" => "[grey70]missing[/]",
            "corrupt" => "[red]corrupt[/]",
            _ => $"[white]{Markup.Escape(cache.State)}[/]"
        };
        return $"{state}\n[grey]{Markup.Escape(cache.Detail)}[/]\n[grey]Path:[/] [cyan]{Markup.Escape(cache.CachePath)}[/]";
    }

    private sealed record AvatarInstallPreflight(
        string Mode,
        string Device,
        AvatarCommandHelpers.AvatarInstallProfilePreview Profile,
        AvatarCacheStatus Cache,
        IReadOnlyList<AvatarInstallPreflightItem> Items);

    private sealed record AvatarInstallPreflightItem(
        uint TitleId,
        string TitleName,
        string Category,
        string ContentId,
        string DisplayName,
        string? Publisher,
        long SizeBytes);
}
