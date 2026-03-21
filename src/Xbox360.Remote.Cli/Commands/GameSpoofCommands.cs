using System.ComponentModel;
using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

internal static class GameSpoofHelpers {
    internal sealed record RemoteSpoofProfile(
        int ClientCount,
        uint PlayerBaseAddress,
        uint PlayerStride,
        uint DisplayNameOffset,
        uint? MirrorNameOffset = null);

    internal sealed record SupportedGameSpoofProfile(
        uint TitleId,
        string Slug,
        string Name,
        uint NameAddress,
        uint XuidBinaryAddress,
        uint XuidTextAddress,
        uint? SecondaryNameAddress = null,
        uint? PatchAddress = null,
        byte[]? PatchBytes = null,
        RemoteSpoofProfile? Remote = null);

    private static readonly SupportedGameSpoofProfile[] Profiles = {
        NewProfile(0x41560855, "bo1", "Call of Duty: Black Ops", 0x841987D4, 0x24, 0x2C, remote: new RemoteSpoofProfile(17, 0x84195FDC, 10792, 10232)),
        NewProfile(0x415608CB, "mw3", "Call of Duty: Modern Warfare 3", 0x839691AC, 0x24, 0x2C, remote: new RemoteSpoofProfile(17, 0x83965E20, 14720, 13196, 13332)),
        NewProfile(0x415608C3, "bo2", "Call of Duty: Black Ops II", 0x841E1B30, 0x20, 0x28, 0x81AA2C8C, 0x825DE218, new byte[] { 0x48, 0x00, 0x00, 0x00 }, new RemoteSpoofProfile(12, 0x841DC690, 22520, 21676, 21812)),
        NewProfile(0x415607E6, "cod4", "Call of Duty 4: Modern Warfare", 0x84C24BBC),
        NewProfile(0x4156081C, "waw", "Call of Duty: World at War", 0x852336B5, 0x23, 0x2B),
        NewProfile(0x41560817, "mw2", "Call of Duty: Modern Warfare 2", 0x838BA824),
        NewProfile(0x415608FC, "ghosts", "Call of Duty: Ghosts", 0x83F0A35C),
        NewProfile(0x41560914, "aw", "Call of Duty: Advanced Warfare", 0x843DECB4)
    };

    internal static IEnumerable<SupportedGameSpoofProfile> GetProfiles() => Profiles;

    internal static SupportedGameSpoofProfile? TryGet(uint titleId) {
        return Profiles.FirstOrDefault(profile => profile.TitleId == titleId);
    }

    internal static async Task<uint> GetCurrentTitleIdAsync(XbdmClient client, CancellationToken cancellationToken) {
        Jrpc2Client jrpc = new Jrpc2Client(client);
        return await jrpc.GetTitleIdAsync(cancellationToken);
    }

    internal static async Task<GameSpoofState> ReadStateAsync(XbdmClient client, SupportedGameSpoofProfile profile, CancellationToken cancellationToken) {
        byte[] nameBytes = await client.ReadMemoryBytesReliableAsync(profile.NameAddress, 0x20, cancellationToken);
        byte[] xuidBytes = await client.ReadMemoryBytesReliableAsync(profile.XuidBinaryAddress, 8, cancellationToken);
        byte[] xuidTextBytes = await client.ReadMemoryBytesReliableAsync(profile.XuidTextAddress, 0x20, cancellationToken);
        byte[]? secondaryBytes = profile.SecondaryNameAddress.HasValue
            ? await client.ReadMemoryBytesReliableAsync(profile.SecondaryNameAddress.Value, 0x20, cancellationToken)
            : null;

        return new GameSpoofState(
            ReadAsciiZ(nameBytes),
            ToCanonicalXuidHex(xuidBytes),
            Convert.ToHexString(xuidBytes),
            ReadAsciiZ(xuidTextBytes),
            secondaryBytes == null ? null : ReadAsciiLoose(secondaryBytes));
    }

    internal static async Task<GameSpoofApplyResult> ApplyIdentityAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        string gamertag,
        string xuidHex,
        CancellationToken cancellationToken) {
        byte[] gamertagBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
        string canonicalXuid = NormalizeXuid(xuidHex);
        byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
        byte[] xuidTextBytes = Encoding.ASCII.GetBytes(Convert.ToHexString(xuidBinaryBytes) + "\0");

        await client.WriteMemoryAsync(profile.NameAddress, new byte[0x40], cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[0x20], cancellationToken);
        await client.WriteMemoryAsync(profile.NameAddress, gamertagBytes, cancellationToken);
        await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);

        if (profile.SecondaryNameAddress.HasValue) {
            byte[] reversed = Encoding.ASCII.GetBytes(gamertag + "\0").Reverse().ToArray();
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[0x40], cancellationToken);
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, reversed, cancellationToken);
        }

        if (profile.PatchAddress.HasValue && profile.PatchBytes is { Length: > 0 }) {
            await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
        }

        GameSpoofState verified = await ReadStateAsync(client, profile, cancellationToken);
        return new GameSpoofApplyResult(
            verified,
            string.Equals(verified.Gamertag, gamertag, StringComparison.Ordinal) &&
            string.Equals(verified.CanonicalXuidHex, canonicalXuid, StringComparison.OrdinalIgnoreCase),
            profile.PatchAddress.HasValue);
    }

    internal static async Task<IReadOnlyList<RemoteClientState>> ReadRemoteClientsAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        CancellationToken cancellationToken) {
        if (profile.Remote == null)
            throw new InvalidOperationException("The current title does not expose a remote spoof profile.");

        List<RemoteClientState> states = new List<RemoteClientState>(profile.Remote.ClientCount);
        for (int slot = 0; slot < profile.Remote.ClientCount; slot++) {
            uint primary = profile.Remote.PlayerBaseAddress + (uint) slot * profile.Remote.PlayerStride + profile.Remote.DisplayNameOffset;
            string name;
            try {
                name = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(primary, 0x20, cancellationToken));
            }
            catch {
                name = string.Empty;
            }

            string? mirrorName = null;
            if (profile.Remote.MirrorNameOffset.HasValue) {
                uint mirror = profile.Remote.PlayerBaseAddress + (uint) slot * profile.Remote.PlayerStride + profile.Remote.MirrorNameOffset.Value;
                try {
                    mirrorName = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(mirror, 0x20, cancellationToken));
                }
                catch {
                    mirrorName = null;
                }
            }

            states.Add(new RemoteClientState(slot + 1, primary, profile.Remote.MirrorNameOffset.HasValue
                ? profile.Remote.PlayerBaseAddress + (uint) slot * profile.Remote.PlayerStride + profile.Remote.MirrorNameOffset.Value
                : null, name, mirrorName));
        }

        return states;
    }

    internal static async Task<RemoteSpoofApplyResult> ApplyRemoteTextAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        IReadOnlyList<int> slots,
        string text,
        CancellationToken cancellationToken) {
        if (profile.Remote == null)
            throw new InvalidOperationException("The current title does not expose a remote spoof profile.");

        List<RemoteClientState> applied = new List<RemoteClientState>(slots.Count);
        foreach (int slot in slots) {
            uint baseAddress = profile.Remote.PlayerBaseAddress + (uint) slot * profile.Remote.PlayerStride;
            string resolvedText = text.Replace("{slot}", (slot + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            byte[] bytes = Encoding.ASCII.GetBytes(resolvedText + "\0");

            uint primary = baseAddress + profile.Remote.DisplayNameOffset;
            await client.WriteMemoryAsync(primary, new byte[0x20], cancellationToken);
            await client.WriteMemoryAsync(primary, bytes, cancellationToken);

            uint? mirror = null;
            if (profile.Remote.MirrorNameOffset.HasValue) {
                mirror = baseAddress + profile.Remote.MirrorNameOffset.Value;
                await client.WriteMemoryAsync(mirror.Value, new byte[0x20], cancellationToken);
                await client.WriteMemoryAsync(mirror.Value, bytes, cancellationToken);
            }

            string verified = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(primary, 0x20, cancellationToken));
            string? verifiedMirror = null;
            if (mirror.HasValue) {
                verifiedMirror = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(mirror.Value, 0x20, cancellationToken));
            }

            applied.Add(new RemoteClientState(slot + 1, primary, mirror, verified, verifiedMirror));
        }

        return new RemoteSpoofApplyResult(applied, applied.All(x => string.Equals(x.Name, text.Replace("{slot}", x.Slot.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), StringComparison.Ordinal)));
    }

    internal static bool TryResolveIdentity(
        ProfileHelpers.XamUserInfo? currentUser,
        string? gamertag,
        string? xuid,
        bool useCurrentUser,
        out string resolvedGamertag,
        out string resolvedXuid,
        out string? error) {
        error = null;
        resolvedGamertag = string.Empty;
        resolvedXuid = string.Empty;

        if (useCurrentUser || (string.IsNullOrWhiteSpace(gamertag) && string.IsNullOrWhiteSpace(xuid))) {
            if (currentUser == null || string.IsNullOrWhiteSpace(currentUser.Gamertag) || string.IsNullOrWhiteSpace(currentUser.Xuid)) {
                error = "No signed-in user was available. Provide explicit values or use --current-user when signed in.";
                return false;
            }

            resolvedGamertag = currentUser.Gamertag.Trim();
            resolvedXuid = NormalizeXuid(currentUser.Xuid);
            return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
        }

        if (string.IsNullOrWhiteSpace(gamertag) || string.IsNullOrWhiteSpace(xuid)) {
            error = "Provide both gamertag and XUID, or use --current-user.";
            return false;
        }

        resolvedGamertag = gamertag.Trim();
        resolvedXuid = NormalizeXuid(xuid);
        return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
    }

    internal static bool TryResolveGamertag(
        ProfileHelpers.XamUserInfo? currentUser,
        string? value,
        bool useCurrentUser,
        out string gamertag,
        out string? error) {
        gamertag = string.Empty;
        error = null;

        if (useCurrentUser || string.IsNullOrWhiteSpace(value)) {
            if (currentUser == null || string.IsNullOrWhiteSpace(currentUser.Gamertag)) {
                error = "No signed-in user was available. Provide --value explicitly or use --current-user when signed in.";
                return false;
            }

            gamertag = currentUser.Gamertag.Trim();
        }
        else {
            gamertag = value.Trim();
        }

        if (gamertag.Length is < 1 or > 15) {
            error = "Gamertag must be between 1 and 15 characters.";
            return false;
        }

        if (!char.IsLetter(gamertag[0])) {
            error = "Gamertag must start with a letter.";
            return false;
        }

        if (gamertag.Any(ch => ch < 32 || ch > 126)) {
            error = "Gamertag must be ASCII for this spoof path.";
            return false;
        }

        return true;
    }

    internal static bool TryResolveXuid(ProfileHelpers.XamUserInfo? currentUser, string? value, bool useCurrentUser, out string xuid, out string? error) {
        error = null;
        xuid = string.Empty;

        if (useCurrentUser || string.IsNullOrWhiteSpace(value)) {
            if (currentUser == null || string.IsNullOrWhiteSpace(currentUser.Xuid)) {
                error = "No signed-in user was available. Provide --value explicitly or use --current-user when signed in.";
                return false;
            }

            xuid = NormalizeXuid(currentUser.Xuid);
        }
        else {
            xuid = NormalizeXuid(value);
        }

        if (xuid.Length != 16 || !xuid.All(Uri.IsHexDigit)) {
            error = "XUID must be exactly 16 hex characters.";
            return false;
        }

        return true;
    }

    internal static bool TryResolveSlots(SupportedGameSpoofProfile profile, int? slot, bool all, out IReadOnlyList<int> slots, out string? error) {
        error = null;
        slots = Array.Empty<int>();

        if (profile.Remote == null) {
            error = "The current title does not expose a remote spoof profile.";
            return false;
        }

        if (all == (slot.HasValue)) {
            error = "Provide either --slot or --all.";
            return false;
        }

        if (all) {
            slots = Enumerable.Range(0, profile.Remote.ClientCount).ToArray();
            return true;
        }

        int zeroBased = slot!.Value - 1;
        if (zeroBased < 0 || zeroBased >= profile.Remote.ClientCount) {
            error = $"Slot must be between 1 and {profile.Remote.ClientCount}.";
            return false;
        }

        slots = new[] { zeroBased };
        return true;
    }

    private static SupportedGameSpoofProfile NewProfile(
        uint titleId,
        string slug,
        string name,
        uint nameAddress,
        uint xuidOffset = 0x24,
        uint xuidTextOffset = 0x2C,
        uint? secondaryNameAddress = null,
        uint? patchAddress = null,
        byte[]? patchBytes = null,
        RemoteSpoofProfile? remote = null) {
        return new SupportedGameSpoofProfile(
            titleId,
            slug,
            name,
            nameAddress,
            nameAddress + xuidOffset,
            nameAddress + xuidTextOffset,
            secondaryNameAddress,
            patchAddress,
            patchBytes,
            remote);
    }

    private static bool ValidateIdentity(string gamertag, string xuid, out string? error) {
        error = null;
        if (gamertag.Length is < 1 or > 15) {
            error = "Gamertag must be between 1 and 15 characters.";
            return false;
        }

        if (!char.IsLetter(gamertag[0])) {
            error = "Gamertag must start with a letter.";
            return false;
        }

        if (gamertag.Any(ch => ch < 32 || ch > 126)) {
            error = "Gamertag must be ASCII for this spoof path.";
            return false;
        }

        if (xuid.Length != 16 || !xuid.All(Uri.IsHexDigit)) {
            error = "XUID must be exactly 16 hex characters.";
            return false;
        }

        return true;
    }

    private static string NormalizeXuid(string value) {
        string normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(2);
        return normalized.ToUpperInvariant();
    }

    private static byte[] ToStoredXuidBytes(string canonicalXuid) {
        byte[] bytes = Convert.FromHexString(canonicalXuid);
        Array.Reverse(bytes);
        return bytes;
    }

    private static string ToCanonicalXuidHex(byte[] storedBytes) {
        byte[] clone = storedBytes.ToArray();
        Array.Reverse(clone);
        return Convert.ToHexString(clone);
    }

    private static string ReadAsciiZ(byte[] data) {
        int end = Array.IndexOf(data, (byte) 0);
        if (end < 0)
            end = data.Length;
        return end == 0 ? string.Empty : Encoding.ASCII.GetString(data, 0, end).Trim();
    }

    private static string ReadAsciiLoose(byte[] data) {
        return Encoding.ASCII.GetString(data).Trim('\0').Trim();
    }
}

internal sealed record GameSpoofState(string Gamertag, string CanonicalXuidHex, string StoredXuidHex, string StoredXuidText, string? SecondaryName);
internal sealed record GameSpoofApplyResult(GameSpoofState Verified, bool VerifiedMatch, bool PatchApplied);
internal sealed record RemoteClientState(int Slot, uint NameAddress, uint? MirrorAddress, string Name, string? MirrorName);
internal sealed record RemoteSpoofApplyResult(IReadOnlyList<RemoteClientState> Applied, bool VerifiedMatch);

internal static class SpoofNotifyHelpers {
    internal static string? GetLogoOverride(string? iconName, string? logoValue) {
        return string.IsNullOrWhiteSpace(iconName) && string.IsNullOrWhiteSpace(logoValue) ? "14" : logoValue;
    }
}

public abstract class SpoofIdentitySettingsBase : ConnectionSettings {
    [CommandOption("--current-user")]
    [Description("Use the currently signed-in user value.")]
    public bool CurrentUser { get; init; }

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

public sealed class GamertagSpoofShowCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            uint titleId = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
            GameSpoofHelpers.SupportedGameSpoofProfile? profile = GameSpoofHelpers.TryGet(titleId);
            if (profile == null) {
                OperationFeedback.WriteWarning("Current title not supported for local spoofing", $"0x{titleId:X8}");
                return 1;
            }

            GameSpoofState state = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    profile.Name,
                    TitleId = $"0x{profile.TitleId:X8}",
                    Gamertag = state.Gamertag,
                    Address = $"0x{profile.NameAddress:X8}"
                });
                return 0;
            }

            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold white]Field[/]"));
            table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
            table.AddRow("[white]Game[/]", $"[springgreen3_1]{Markup.Escape(profile.Name)}[/]");
            table.AddRow("[white]Gamertag[/]", $"[gold1]{Markup.Escape(state.Gamertag)}[/]");
            table.AddRow("[white]Address[/]", $"[mediumpurple3_1]0x{profile.NameAddress:X8}[/]");
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class GamertagSpoofSetCommand : AsyncCommand<GamertagSpoofSetCommand.Settings> {
    public sealed class Settings : SpoofIdentitySettingsBase {
        [CommandOption("--value <TEXT>")]
        [Description("Gamertag to write into the running supported title.")]
        public string? Value { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            uint titleId = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
            GameSpoofHelpers.SupportedGameSpoofProfile? profile = GameSpoofHelpers.TryGet(titleId);
            if (profile == null) {
                OperationFeedback.WriteWarning("Current title not supported for local spoofing", $"0x{titleId:X8}");
                return 1;
            }

            ProfileHelpers.XamUserInfo? currentUser = await HardwareHelpers.TryGetSignedInUserAsync(client, CancellationToken.None);
            if (!GameSpoofHelpers.TryResolveGamertag(currentUser, settings.Value, settings.CurrentUser, out string gamertag, out string? error)) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid gamertag.")}[/]");
                return 1;
            }

            GameSpoofState currentState = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
            GameSpoofApplyResult result = await GameSpoofHelpers.ApplyIdentityAsync(client, profile, gamertag, currentState.CanonicalXuidHex, CancellationToken.None);

            if (result.VerifiedMatch) {
                OperationFeedback.WriteSuccess("Gamertag spoof applied", $"[gold1]{Markup.Escape(result.Verified.Gamertag)}[/]");
            }
            else {
                OperationFeedback.WriteWarning("Gamertag spoof wrote memory but verification did not fully match", Markup.Escape(profile.Name));
            }

            string notifyMessage = $"XeCLI: Spoofing as {result.Verified.Gamertag}";
            await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), notifyMessage, CancellationToken.None);
            return result.VerifiedMatch ? 0 : 1;
        }, CancellationToken.None);
    }
}

public sealed class XuidSpoofShowCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            uint titleId = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
            GameSpoofHelpers.SupportedGameSpoofProfile? profile = GameSpoofHelpers.TryGet(titleId);
            if (profile == null) {
                OperationFeedback.WriteWarning("Current title not supported for local spoofing", $"0x{titleId:X8}");
                return 1;
            }

            GameSpoofState state = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    profile.Name,
                    TitleId = $"0x{profile.TitleId:X8}",
                    Xuid = state.CanonicalXuidHex,
                    BinaryAddress = $"0x{profile.XuidBinaryAddress:X8}",
                    TextAddress = $"0x{profile.XuidTextAddress:X8}"
                });
                return 0;
            }

            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold white]Field[/]"));
            table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
            table.AddRow("[white]Game[/]", $"[springgreen3_1]{Markup.Escape(profile.Name)}[/]");
            table.AddRow("[white]XUID[/]", $"[cyan1]{Markup.Escape(state.CanonicalXuidHex)}[/]");
            table.AddRow("[white]Stored[/]", $"[grey70]{Markup.Escape(state.StoredXuidHex)}[/]");
            table.AddRow("[white]Binary Addr[/]", $"[mediumpurple3_1]0x{profile.XuidBinaryAddress:X8}[/]");
            table.AddRow("[white]Text Addr[/]", $"[mediumpurple3_1]0x{profile.XuidTextAddress:X8}[/]");
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XuidSpoofSetCommand : AsyncCommand<XuidSpoofSetCommand.Settings> {
    public sealed class Settings : SpoofIdentitySettingsBase {
        [CommandOption("--value <HEX>")]
        [Description("16-character XUID to write into the running supported title.")]
        public string? Value { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            uint titleId = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
            GameSpoofHelpers.SupportedGameSpoofProfile? profile = GameSpoofHelpers.TryGet(titleId);
            if (profile == null) {
                OperationFeedback.WriteWarning("Current title not supported for local spoofing", $"0x{titleId:X8}");
                return 1;
            }

            ProfileHelpers.XamUserInfo? currentUser = await HardwareHelpers.TryGetSignedInUserAsync(client, CancellationToken.None);
            if (!GameSpoofHelpers.TryResolveXuid(currentUser, settings.Value, settings.CurrentUser, out string xuid, out string? error)) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid XUID.")}[/]");
                return 1;
            }

            GameSpoofState currentState = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
            GameSpoofApplyResult result = await GameSpoofHelpers.ApplyIdentityAsync(client, profile, currentState.Gamertag, xuid, CancellationToken.None);

            if (result.VerifiedMatch) {
                OperationFeedback.WriteSuccess("XUID spoof applied", $"[cyan1]{Markup.Escape(result.Verified.CanonicalXuidHex)}[/]");
            }
            else {
                OperationFeedback.WriteWarning("XUID spoof wrote memory but verification did not fully match", Markup.Escape(profile.Name));
            }

            string notifyMessage = $"XeCLI: XUID set to {result.Verified.CanonicalXuidHex}";
            await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), notifyMessage, CancellationToken.None);
            return result.VerifiedMatch ? 0 : 1;
        }, CancellationToken.None);
    }
}

public sealed class RemoteSpoofListCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            uint titleId = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
            GameSpoofHelpers.SupportedGameSpoofProfile? profile = GameSpoofHelpers.TryGet(titleId);
            if (profile?.Remote == null) {
                OperationFeedback.WriteWarning("Current title does not expose remote spoof slots", $"0x{titleId:X8}");
                return 1;
            }

            IReadOnlyList<RemoteClientState> slots = await GameSpoofHelpers.ReadRemoteClientsAsync(client, profile, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    profile.Name,
                    TitleId = $"0x{profile.TitleId:X8}",
                    Slots = slots.Select(x => new {
                        x.Slot,
                        NameAddress = $"0x{x.NameAddress:X8}",
                        MirrorAddress = x.MirrorAddress.HasValue ? $"0x{x.MirrorAddress.Value:X8}" : null,
                        x.Name,
                        x.MirrorName
                    })
                });
                return 0;
            }

            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold white]Slot[/]"));
            table.AddColumn(new TableColumn("[bold green3]Name[/]"));
            table.AddColumn(new TableColumn("[bold deepskyblue1]Address[/]"));
            table.AddColumn(new TableColumn("[bold grey70]Mirror[/]"));
            foreach (RemoteClientState slot in slots.Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.MirrorName))) {
                table.AddRow(
                    $"[white]{slot.Slot}[/]",
                    $"[gold1]{Markup.Escape(slot.Name)}[/]",
                    $"[mediumpurple3_1]0x{slot.NameAddress:X8}[/]",
                    slot.MirrorAddress.HasValue ? $"[grey70]0x{slot.MirrorAddress.Value:X8}[/]" : "[grey50]-[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class RemoteSpoofApplyCommand : AsyncCommand<RemoteSpoofApplyCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--slot <N>")]
        [Description("1-based client slot to overwrite.")]
        public int? Slot { get; init; }

        [CommandOption("--all")]
        [Description("Overwrite every supported slot.")]
        public bool All { get; init; }

        [CommandOption("--text <TEXT>")]
        [Description("Replacement name text. Use {slot} to inject the 1-based slot number.")]
        public string? Text { get; init; }

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

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            if (string.IsNullOrWhiteSpace(settings.Text)) {
                AnsiConsole.MarkupLine("[red]Provide --text for the remote spoof payload.[/]");
                return 1;
            }

            uint titleId = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
            GameSpoofHelpers.SupportedGameSpoofProfile? profile = GameSpoofHelpers.TryGet(titleId);
            if (profile?.Remote == null) {
                OperationFeedback.WriteWarning("Current title does not expose remote spoof slots", $"0x{titleId:X8}");
                return 1;
            }

            if (!GameSpoofHelpers.TryResolveSlots(profile, settings.Slot, settings.All, out IReadOnlyList<int> slots, out string? error)) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid remote slot selection.")}[/]");
                return 1;
            }

            RemoteSpoofApplyResult result = await GameSpoofHelpers.ApplyRemoteTextAsync(client, profile, slots, settings.Text.Trim(), CancellationToken.None);
            if (result.VerifiedMatch) {
                OperationFeedback.WriteSuccess("Remote spoof applied", $"[springgreen3_1]{Markup.Escape(profile.Name)}[/] [grey]=>[/] [gold1]{Markup.Escape(settings.Text)}[/]");
            }
            else {
                OperationFeedback.WriteWarning("Remote spoof wrote memory but verification did not fully match", Markup.Escape(profile.Name));
            }

            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold white]Slot[/]"));
            table.AddColumn(new TableColumn("[bold green3]Verified[/]"));
            table.AddColumn(new TableColumn("[bold deepskyblue1]Address[/]"));
            foreach (RemoteClientState applied in result.Applied) {
                table.AddRow(
                    $"[white]{applied.Slot}[/]",
                    $"[gold1]{Markup.Escape(applied.Name)}[/]",
                    $"[mediumpurple3_1]0x{applied.NameAddress:X8}[/]");
            }

            AnsiConsole.Write(table);
            string notifyMessage = $"XeCLI: Spoofing as {result.Applied.FirstOrDefault()?.Name ?? settings.Text.Trim()}";
            await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), notifyMessage, CancellationToken.None);
            return result.VerifiedMatch ? 0 : 1;
        }, CancellationToken.None);
    }
}
