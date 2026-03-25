using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ProfilesCommand : AsyncCommand<ProfilesCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--no-ftp")]
        [LocalizedDescription("Skip FTP profile scan.")]
        public bool NoFtp { get; init; }

        [CommandOption("--no-f3")]
        [LocalizedDescription("Skip F3 profile lookup (HTTP 9999).")]
        public bool NoF3 { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        return await CliHelpers.WithClientAsync((ip, port, timeout), settings, async client => {
            IReadOnlyList<XbdmUserInfo> users = await client.GetUserListAsync(CancellationToken.None);
            ProfileHelpers.XamUserInfo? xamUser = null;
            if (users.Count == 0) {
                try {
                    xamUser = await ProfileHelpers.TryGetSignedInXamUserAsync(ip, port, timeout, CancellationToken.None);
                    if (xamUser != null) {
                        users = new[] {
                            new XbdmUserInfo {
                                Gamertag = xamUser.Gamertag,
                                Xuid = xamUser.Xuid != null && ulong.TryParse(xamUser.Xuid.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsedXuid)
                                    ? parsedXuid
                                    : null,
                                SignInState = xamUser.SignInState,
                                RawLine = $"xam slot={xamUser.Slot}"
                            }
                        };
                    }
                }
                catch {
                    // ignored
                }
            }

            List<string>? ftpProfiles = null;
            if (!settings.NoFtp) {
                try {
                    ftpProfiles = await ProfileHelpers.TryGetFtpProfilesAsync(ip);
                }
                catch {
                    ftpProfiles = null;
                }
            }

            List<ProfileHelpers.F3ProfileInfo>? f3Profiles = null;
            if (!settings.NoF3) {
                try {
                    f3Profiles = await ProfileHelpers.TryGetF3ProfilesAsync(ip);
                }
                catch {
                    f3Profiles = null;
                }
            }

            XbdmUserInfo? signedIn = users.FirstOrDefault(u =>
                    u.SignInState.HasValue && u.SignInState.Value > 0 && !string.IsNullOrWhiteSpace(u.Gamertag))
                ?? users.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Gamertag));
            string? signedInUser = signedIn?.Gamertag;
            string? signedInXuid = signedIn?.Xuid.HasValue == true ? $"0x{signedIn.Xuid.Value:X16}" : null;

            ProfileHelpers.F3ProfileInfo? f3SignedIn = f3Profiles?.FirstOrDefault(p => p.SignedIn == 1 && !string.IsNullOrWhiteSpace(p.Gamertag));
            if (string.IsNullOrWhiteSpace(signedInUser) && f3SignedIn != null) {
                signedInUser = f3SignedIn.Gamertag;
                signedInXuid ??= f3SignedIn.Xuid;
            }
            if (string.IsNullOrWhiteSpace(signedInUser) && xamUser != null) {
                signedInUser = xamUser.Gamertag;
                signedInXuid ??= xamUser.Xuid;
            }

            Dictionary<string, List<string>> ftpGrouped = ftpProfiles != null
                ? ProfileHelpers.GroupFtpProfiles(ftpProfiles)
                : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Target = new { ip, port },
                    SignedIn = signedInUser,
                    SignedInXuid = signedInXuid,
                    Users = users,
                    FtpProfiles = ftpGrouped.Select(kvp => new { ProfileId = kvp.Key, Devices = kvp.Value }),
                    F3Profiles = f3Profiles
                });
                return 0;
            }

            const string Unknown = "[grey]unknown[/]";
            static string FormatValue(string? value, string color, string? fallback = null) {
                if (string.IsNullOrWhiteSpace(value))
                    return fallback ?? Unknown;
                return $"[{color}]{Markup.Escape(value)}[/]";
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Profiles[/]").RuleStyle("grey"));
            Table summary = CliOutput.CreateTable();
            summary.AddColumn(new TableColumn("[grey]Field[/]"));
            summary.AddColumn(new TableColumn("[white]Value[/]"));
            summary.AddRow("[grey]Signed In[/]", FormatValue(signedInUser, "green", "[grey]none[/]"));
            summary.AddRow("[grey]Signed In XUID[/]", FormatValue(signedInXuid, "gold1", "[grey]none[/]"));
            summary.AddRow("[grey]XBDM Users[/]", users.Count.ToString(CultureInfo.InvariantCulture));
            summary.AddRow("[grey]FTP Profiles[/]", ftpGrouped.Count.ToString(CultureInfo.InvariantCulture));
            summary.AddRow("[grey]F3 Profiles[/]", (f3Profiles?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            AnsiConsole.Write(summary);

            if (users.Count > 0) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]XBDM Users[/]").RuleStyle("grey"));
                Table userTable = CliOutput.CreateTable();
                userTable.AddColumn(new TableColumn("[green]Gamertag[/]"));
                userTable.AddColumn(new TableColumn("[gold1]XUID[/]"));
                userTable.AddColumn(new TableColumn("[cyan]State[/]"));
                userTable.AddColumn(new TableColumn("[grey]Raw[/]"));
                foreach (XbdmUserInfo user in users) {
                    string stateText = user.SignInState.HasValue ? user.SignInState.Value.ToString(CultureInfo.InvariantCulture) : "unknown";
                    userTable.AddRow(
                        FormatValue(user.Gamertag, "green"),
                        user.Xuid.HasValue ? $"[gold1]0x{user.Xuid.Value:X16}[/]" : Unknown,
                        $"[cyan]{stateText}[/]",
                        Markup.Escape(user.RawLine));
                }
                AnsiConsole.Write(userTable);
            }

            if (ftpGrouped.Count > 0) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]FTP Profiles[/]").RuleStyle("grey"));
                Table profileTable = CliOutput.CreateTable();
                profileTable.AddColumn(new TableColumn("[cyan]Profile ID[/]"));
                profileTable.AddColumn(new TableColumn("[green]Devices[/]"));
                foreach (KeyValuePair<string, List<string>> entry in ftpGrouped.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)) {
                    string devices = entry.Value.Count > 0 ? string.Join(", ", entry.Value) : "unknown";
                    profileTable.AddRow($"[cyan]{Markup.Escape(entry.Key)}[/]", $"[green]{Markup.Escape(devices)}[/]");
                }
                AnsiConsole.Write(profileTable);
            }

            if (f3Profiles != null && f3Profiles.Count > 0) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]F3 Profiles[/]").RuleStyle("grey"));
                Table profileTable = CliOutput.CreateTable();
                profileTable.AddColumn(new TableColumn("[green]Gamertag[/]"));
                profileTable.AddColumn(new TableColumn("[gold1]XUID[/]"));
                profileTable.AddColumn(new TableColumn("[grey]Signed In[/]"));
                profileTable.AddColumn(new TableColumn("[cyan]Gamerscore[/]"));
                foreach (ProfileHelpers.F3ProfileInfo profile in f3Profiles) {
                    profileTable.AddRow(
                        FormatValue(profile.Gamertag, "green"),
                        string.IsNullOrWhiteSpace(profile.Xuid) ? Unknown : $"[gold1]{Markup.Escape(profile.Xuid)}[/]",
                        profile.SignedIn == 1 ? "[green]yes[/]" : "[grey]no[/]",
                        profile.Gamerscore.ToString(CultureInfo.InvariantCulture));
                }
                AnsiConsole.Write(profileTable);
            }

            return 0;
        }, CancellationToken.None);
    }
}

