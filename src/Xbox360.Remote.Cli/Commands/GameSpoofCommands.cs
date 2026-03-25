using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

internal static class GameSpoofHelpers {
    private const uint BlackOps2TitleId = 0x415608C3;
    private const uint BlackOps2UnicodeNameAddress = 0x81AA2DDC;
    private const uint BlackOps2StubPrimaryAddress = 0x81B69F80;
    private const uint BlackOps2StubSecondaryAddress = 0x816DD040;
    private const uint BlackOps2ScratchBase = 0x81A76A88;
    private const uint BlackOps2HookEntryAddress = 0x82596D80;
    private const uint BlackOps2HookSecondaryAddress = 0x8293D694;
    private const uint BlackOps2HookOptionalAddress = 0x8241DB10;
    private const uint BlackOps2ExportOrdinalKrnl = 315U;
    private const uint BlackOps2ExportOrdinalXamPrimary = 1128U;
    private const uint BlackOps2ExportOrdinalXamSecondary = 1080U;
    private const uint BlackOps2ScratchObject5Address = BlackOps2ScratchBase + 256U;
    private const uint BlackOps2ScratchObject6Address = BlackOps2ScratchBase + 512U;
    private const uint BlackOps2ScratchObject7Address = BlackOps2ScratchBase + 768U;
    private const uint BlackOps2ScratchHookAddress = BlackOps2ScratchBase + 1024U;
    private const uint BlackOps2ScratchStateAddress = BlackOps2ScratchBase + 1280U;
    private const uint BlackOps2AccountBlockSearchStart = 0x83700000;
    private const uint BlackOps2AccountBlockSearchEnd = 0x84000000;
    private const int BlackOps2AccountBlockChunkSize = 0x1000;
    private const int BlackOps2AccountBlockXuidOffset = 0x14;
    private const int BlackOps2AccountBlockGamertagOffset = 0x1C;
    private const int BlackOps2AccountBlockGamertagFieldLength = 0x20;
    private const int BlackOps2AccountBlockXuidFieldLength = 8;
    private const int BlackOps2HookStubLength = 16;
    private const int BlackOps2TrampolineLength = 32;
    private const int BlackOps2SnapshotLength = 16;
    private const int BlackOps2PrimaryTrampolineOriginalOffset = 12;
    private const int BlackOps2ScratchClearLength = 0x600;
    private static readonly byte[] BlackOps2RefreshPatchBytes = { 0x60, 0x00, 0x00, 0x00 };
    private static readonly byte[] BlackOps2AccountObject5Bytes = {
        144, 97, 0, 20, 144, 129, 0, 28, 144, 161, 0, 36, 61, 96, 222, 173,
        97, 107, 190, 239, 145, 97, 255, 240, 129, 97, 255, 240, 129, 107, 0, 16,
        233, 107, 0, 0, 129, 65, 0, 36, 249, 106, 0, 0, 56, 96, 0, 0,
        78, 128, 0, 32, 0, 0, 0, 0
    };
    private static readonly byte[] BlackOps2AccountObject6Bytes = {
        125, 136, 2, 166, 145, 129, 255, 248, 148, 33, 255, 160, 248, 97, 0, 112,
        248, 129, 0, 120, 61, 96, 222, 173, 97, 107, 190, 239, 145, 97, 0, 84,
        129, 97, 0, 84, 129, 107, 0, 12, 145, 97, 0, 80, 232, 129, 0, 120,
        232, 97, 0, 112, 129, 97, 0, 80, 125, 105, 3, 166, 78, 128, 4, 33,
        129, 97, 0, 84, 129, 107, 0, 16, 232, 107, 0, 0, 56, 33, 0, 96,
        129, 129, 255, 248, 125, 136, 3, 166, 78, 128, 0, 32, 0, 0, 0, 0
    };
    private static readonly byte[] BlackOps2PrimaryStubBytes = {
        0x7D, 0x88, 0x02, 0xA6, 0x91, 0x81, 0xFF, 0xF8, 0x94, 0x21, 0xFF, 0xA0, 0x3D, 0x60, 0x82, 0x59,
        0x61, 0x6B, 0xB6, 0xA0, 0x7C, 0x0C, 0x58, 0x00, 0x40, 0x82, 0x00, 0x30, 0x3D, 0x80, 0x81, 0xB6,
        0x61, 0x8C, 0x9E, 0x94, 0x39, 0x40, 0x00, 0x20, 0x7D, 0x49, 0x03, 0xA6, 0x39, 0x40, 0x00, 0x00,
        0x7C, 0xCA, 0x60, 0xAE, 0x7C, 0xCA, 0x21, 0xAE, 0x39, 0x4A, 0x00, 0x01, 0x42, 0x00, 0xFF, 0xF4,
        0x38, 0x60, 0x00, 0x00, 0x48, 0x00, 0x00, 0x08, 0x48, 0x00, 0x00, 0x15, 0x38, 0x21, 0x00, 0x60,
        0x81, 0x81, 0xFF, 0xF8, 0x7D, 0x88, 0x03, 0xA6, 0x4E, 0x80, 0x00, 0x20, 0x3D, 0x80, 0x81, 0x6D,
        0x61, 0x8C, 0xD0, 0x50, 0x7D, 0x89, 0x03, 0xA6, 0x7D, 0x88, 0x02, 0xA6, 0x4B, 0xBC, 0x33, 0xD9,
        0x94, 0x21, 0xFF, 0x50, 0x3D, 0x60, 0x81, 0xAA, 0x4E, 0x80, 0x04, 0x20
    };
    private static readonly byte[] BlackOps2SecondaryStubBytes = {
        0x3D, 0x60, 0x81, 0xB6, 0x61, 0x6B, 0x9F, 0x80, 0x7D, 0x69, 0x03, 0xA6, 0x4E, 0x80, 0x04, 0x20
    };
    private static readonly byte[] BlackOps2ExpectedPrimaryHookBytes = {
        0x7D, 0x88, 0x02, 0xA6, 0x91, 0x81, 0xFF, 0xF8, 0xFB, 0xC1, 0xFF, 0xE8, 0xFB, 0xE1, 0xFF, 0xF0
    };

    internal sealed record RemoteSpoofProfile(
        int ClientCount,
        uint PlayerBaseAddress,
        uint PlayerStride,
        uint DisplayNameOffset,
        uint? MirrorNameOffset = null,
        int StartSlotIndex = 0,
        uint? XuidBinaryOffset = null,
        uint? XuidTextOffset = null);

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
        NewProfile(0x41560855, "bo1", "Call of Duty: Black Ops", 0x841987D4, 0x24, 0x2C, remote: new RemoteSpoofProfile(17, 0x84195FDC, 10792, 10232, XuidBinaryOffset: 10268, XuidTextOffset: 10276)),
        NewProfile(0x415608CB, "mw3", "Call of Duty: Modern Warfare 3", 0x839691AC, 0x24, 0x2C, remote: new RemoteSpoofProfile(17, 0x83965E20, 14720, 13196, 13332, XuidBinaryOffset: 13232, XuidTextOffset: 13240)),
        NewProfile(0x415608C3, "bo2", "Call of Duty: Black Ops II", 0x841E1B30, 0x20, 0x28, 0x81B69E94, remote: new RemoteSpoofProfile(11, 0x841DC690, 22520, 21676, 21812, 1, XuidBinaryOffset: 21708, XuidTextOffset: 21716)),
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

    internal static string GetIdentityCacheKey(string targetKey, uint titleId) {
        return $"{NormalizeTargetKey(targetKey)}|0x{titleId:X8}";
    }

    internal static void StoreOriginalIdentityIfMissing(string targetKey, uint titleId, GameSpoofState state) {
        CliConfig cfg = CliConfig.Load();
        cfg.SpoofIdentityCache ??= new Dictionary<string, CliConfig.SpoofIdentityCacheInfo>(StringComparer.OrdinalIgnoreCase);
        string key = GetIdentityCacheKey(targetKey, titleId);
        if (cfg.SpoofIdentityCache.ContainsKey(key))
            return;

        cfg.SpoofIdentityCache[key] = new CliConfig.SpoofIdentityCacheInfo {
            TargetKey = NormalizeTargetKey(targetKey),
            TitleId = titleId,
            Gamertag = state.Gamertag,
            Xuid = state.CanonicalXuidHex,
            CapturedUtc = DateTimeOffset.UtcNow
        };
        cfg.Save();
    }

    internal static CliConfig.SpoofIdentityCacheInfo? TryGetCachedIdentity(string targetKey, uint titleId) {
        CliConfig cfg = CliConfig.Load();
        if (cfg.SpoofIdentityCache == null || cfg.SpoofIdentityCache.Count == 0)
            return null;

        string key = GetIdentityCacheKey(targetKey, titleId);
        if (!cfg.SpoofIdentityCache.TryGetValue(key, out CliConfig.SpoofIdentityCacheInfo? cached))
            return null;

        return cached;
    }

    internal static async Task<GameSpoofApplyResult> ApplyIdentityAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        string gamertag,
        string xuidHex,
        CancellationToken cancellationToken) {
        if (profile.TitleId == BlackOps2TitleId) {
            return await ApplyBlackOps2IdentityAsync(client, profile, gamertag, xuidHex, cancellationToken);
        }

        byte[] gamertagBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
        string canonicalXuid = NormalizeXuid(xuidHex);
        byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
        byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");

        await client.WriteMemoryAsync(profile.NameAddress, new byte[0x40], cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[0x20], cancellationToken);
        await client.WriteMemoryAsync(profile.NameAddress, gamertagBytes, cancellationToken);
        await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);

        if (profile.SecondaryNameAddress.HasValue) {
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[0x40], cancellationToken);
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, gamertagBytes, cancellationToken);
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

    private static async Task<GameSpoofApplyResult> ApplyBlackOps2IdentityAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        string gamertag,
        string xuidHex,
        CancellationToken cancellationToken) {
        byte[] gamertagBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
        byte[] unicodeGamertagBytes = ToWideAsciiBytes(gamertag);
        string canonicalXuid = NormalizeXuid(xuidHex);
        byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
        byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");
        GameSpoofState preState = await ReadStateAsync(client, profile, cancellationToken);

        await client.WriteMemoryAsync(BlackOps2StubPrimaryAddress, BlackOps2PrimaryStubBytes, cancellationToken);
        await client.WriteMemoryAsync(BlackOps2StubSecondaryAddress, BlackOps2SecondaryStubBytes, cancellationToken);
        await StageBlackOps2AccountLayerAsync(client, gamertag, canonicalXuid, cancellationToken);

        await client.WriteMemoryAsync(profile.NameAddress, new byte[0x40], cancellationToken);
        await client.WriteMemoryAsync(profile.NameAddress, gamertagBytes, cancellationToken);

        if (profile.SecondaryNameAddress.HasValue) {
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[0x40], cancellationToken);
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, gamertagBytes, cancellationToken);
        }

        await client.WriteMemoryAsync(BlackOps2UnicodeNameAddress, new byte[Math.Max(0x40, unicodeGamertagBytes.Length)], cancellationToken);
        await client.WriteMemoryAsync(BlackOps2UnicodeNameAddress, unicodeGamertagBytes, cancellationToken);

        await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[0x20], cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);

        if (profile.PatchAddress.HasValue && profile.PatchBytes is { Length: > 0 }) {
            await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
        }

        uint? accountBlockAddress = await TryResolveBlackOps2AccountBlockAsync(
            client, preState.Gamertag, preState.CanonicalXuidHex, cancellationToken);
        if (accountBlockAddress.HasValue) {
            await WriteBlackOps2AccountBlockXuidAsync(client, accountBlockAddress.Value, xuidBinaryBytes, cancellationToken);
            await WriteBlackOps2AccountBlockGamertagAsync(client, accountBlockAddress.Value, gamertagBytes, cancellationToken);
        }

        GameSpoofState verified = await ReadStateAsync(client, profile, cancellationToken);
        bool verifiedMatch =
            string.Equals(verified.Gamertag, gamertag, StringComparison.Ordinal) &&
            string.Equals(verified.CanonicalXuidHex, canonicalXuid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(verified.SecondaryName, gamertag, StringComparison.Ordinal);

        return new GameSpoofApplyResult(verified, verifiedMatch, true);
    }

    internal static async Task<GameSpoofApplyResult> ApplyGamertagAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        string gamertag,
        CancellationToken cancellationToken) {
        if (profile.TitleId == BlackOps2TitleId) {
            GameSpoofState currentState = await ReadStateAsync(client, profile, cancellationToken);
            byte[] gamertagBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
            byte[] unicodeGamertagBytes = ToWideAsciiBytes(gamertag);
            byte[] xuidBinaryBytes = ToStoredXuidBytes(currentState.CanonicalXuidHex);
            byte[] xuidTextBytes = Encoding.ASCII.GetBytes(currentState.CanonicalXuidHex + "\0");
            uint? accountBlockAddress = await TryResolveBlackOps2AccountBlockAsync(
                client,
                currentState.Gamertag,
                currentState.CanonicalXuidHex,
                cancellationToken);

            await client.WriteMemoryAsync(profile.NameAddress, new byte[0x40], cancellationToken);
            await client.WriteMemoryAsync(profile.NameAddress, gamertagBytes, cancellationToken);

            if (profile.SecondaryNameAddress.HasValue) {
                await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[0x40], cancellationToken);
                await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, gamertagBytes, cancellationToken);
            }

            await client.WriteMemoryAsync(BlackOps2UnicodeNameAddress, new byte[Math.Max(0x40, unicodeGamertagBytes.Length)], cancellationToken);
            await client.WriteMemoryAsync(BlackOps2UnicodeNameAddress, unicodeGamertagBytes, cancellationToken);

            await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
            await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[0x20], cancellationToken);
            await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);

            if (accountBlockAddress.HasValue) {
                await WriteBlackOps2AccountBlockGamertagAsync(
                    client,
                    accountBlockAddress.Value,
                    gamertagBytes,
                    cancellationToken);
            }

            if (profile.PatchAddress.HasValue && profile.PatchBytes is { Length: > 0 }) {
                await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
            }

            GameSpoofState verified = await ReadStateAsync(client, profile, cancellationToken);
            bool verifiedMatch =
                string.Equals(verified.Gamertag, gamertag, StringComparison.Ordinal) &&
                string.Equals(verified.SecondaryName, gamertag, StringComparison.Ordinal);
            return new GameSpoofApplyResult(verified, verifiedMatch, true);
        }

        byte[] bytes = Encoding.ASCII.GetBytes(gamertag + "\0");
        await client.WriteMemoryAsync(profile.NameAddress, new byte[0x40], cancellationToken);
        await client.WriteMemoryAsync(profile.NameAddress, bytes, cancellationToken);
        if (profile.SecondaryNameAddress.HasValue) {
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[0x40], cancellationToken);
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, bytes, cancellationToken);
        }

        if (profile.PatchAddress.HasValue && profile.PatchBytes is { Length: > 0 }) {
            await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
        }

        GameSpoofState state = await ReadStateAsync(client, profile, cancellationToken);
        bool match = string.Equals(state.Gamertag, gamertag, StringComparison.Ordinal) &&
                     (!profile.SecondaryNameAddress.HasValue || string.Equals(state.SecondaryName, gamertag, StringComparison.Ordinal));
        return new GameSpoofApplyResult(state, match, profile.PatchAddress.HasValue);
    }

    internal static async Task<GameSpoofApplyResult> ApplyXuidAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        string xuidHex,
        CancellationToken cancellationToken) {
        string canonicalXuid = NormalizeXuid(xuidHex);
        byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
        byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");
        GameSpoofState? bo2PreState = profile.TitleId == BlackOps2TitleId
            ? await ReadStateAsync(client, profile, cancellationToken)
            : null;

        await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[0x20], cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);

        if (profile.TitleId == BlackOps2TitleId) {
            await client.WriteMemoryAsync(BlackOps2StubPrimaryAddress, BlackOps2PrimaryStubBytes, cancellationToken);
            await client.WriteMemoryAsync(BlackOps2StubSecondaryAddress, BlackOps2SecondaryStubBytes, cancellationToken);
            await StageBlackOps2AccountLayerAsync(client, bo2PreState!.Gamertag, canonicalXuid, cancellationToken);
            if (profile.PatchAddress.HasValue && profile.PatchBytes is { Length: > 0 }) {
                await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
            }
            uint? accountBlockAddress = await TryResolveBlackOps2AccountBlockAsync(
                client, bo2PreState.Gamertag, bo2PreState.CanonicalXuidHex, cancellationToken);
            if (accountBlockAddress.HasValue) {
                await WriteBlackOps2AccountBlockXuidAsync(client, accountBlockAddress.Value, xuidBinaryBytes, cancellationToken);
            }
        }

        GameSpoofState state = await ReadStateAsync(client, profile, cancellationToken);
        bool match = string.Equals(state.CanonicalXuidHex, canonicalXuid, StringComparison.OrdinalIgnoreCase);
        return new GameSpoofApplyResult(state, match, profile.PatchAddress.HasValue);
    }

    internal static async Task<IReadOnlyList<RemoteClientState>> ReadRemoteClientsAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        CancellationToken cancellationToken) {
        if (profile.Remote == null)
            throw new InvalidOperationException("The current title does not expose a remote spoof profile.");

        List<RemoteClientState> states = new List<RemoteClientState>(profile.Remote.ClientCount);
        for (int slot = 0; slot < profile.Remote.ClientCount; slot++) {
            int actualSlot = slot + profile.Remote.StartSlotIndex;
            uint primary = profile.Remote.PlayerBaseAddress + (uint) actualSlot * profile.Remote.PlayerStride + profile.Remote.DisplayNameOffset;
            string name;
            try {
                name = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(primary, 0x20, cancellationToken));
            }
            catch {
                name = string.Empty;
            }

            string? mirrorName = null;
            if (profile.Remote.MirrorNameOffset.HasValue) {
                uint mirror = profile.Remote.PlayerBaseAddress + (uint) actualSlot * profile.Remote.PlayerStride + profile.Remote.MirrorNameOffset.Value;
                try {
                    mirrorName = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(mirror, 0x20, cancellationToken));
                }
                catch {
                    mirrorName = null;
                }
            }

            uint? xuidAddress = null;
            string? slotXuid = null;
            if (profile.Remote.XuidBinaryOffset.HasValue) {
                xuidAddress = profile.Remote.PlayerBaseAddress + (uint) actualSlot * profile.Remote.PlayerStride + profile.Remote.XuidBinaryOffset.Value;
                try {
                    slotXuid = ToCanonicalXuidHex(await client.ReadMemoryBytesReliableAsync(xuidAddress.Value, 8, cancellationToken));
                }
                catch {
                    slotXuid = null;
                }
            }

            states.Add(new RemoteClientState(actualSlot + 1, primary, profile.Remote.MirrorNameOffset.HasValue
                ? profile.Remote.PlayerBaseAddress + (uint) actualSlot * profile.Remote.PlayerStride + profile.Remote.MirrorNameOffset.Value
                : null, name, mirrorName, xuidAddress, slotXuid));
        }

        return states;
    }

    internal static async Task<RemoteSpoofApplyResult> ApplyRemoteTextAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        IReadOnlyList<int> slots,
        string text,
        CancellationToken cancellationToken,
        string? xuidHex = null) {
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

            uint? xuidAddress = null;
            string? verifiedXuid = null;
            if (!string.IsNullOrWhiteSpace(xuidHex) && profile.Remote.XuidBinaryOffset.HasValue) {
                string canonicalXuid = NormalizeXuid(xuidHex);
                byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
                xuidAddress = baseAddress + profile.Remote.XuidBinaryOffset.Value;
                await client.WriteMemoryAsync(xuidAddress.Value, xuidBinaryBytes, cancellationToken);
                if (profile.Remote.XuidTextOffset.HasValue) {
                    byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");
                    await client.WriteMemoryAsync(baseAddress + profile.Remote.XuidTextOffset.Value, new byte[0x20], cancellationToken);
                    await client.WriteMemoryAsync(baseAddress + profile.Remote.XuidTextOffset.Value, xuidTextBytes, cancellationToken);
                }
                verifiedXuid = ToCanonicalXuidHex(await client.ReadMemoryBytesReliableAsync(xuidAddress.Value, 8, cancellationToken));
            }

            string verified = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(primary, 0x20, cancellationToken));
            string? verifiedMirror = null;
            if (mirror.HasValue) {
                verifiedMirror = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(mirror.Value, 0x20, cancellationToken));
            }

            applied.Add(new RemoteClientState(slot + 1, primary, mirror, verified, verifiedMirror, xuidAddress, verifiedXuid));
        }

        string? canonicalXuidForVerify = string.IsNullOrWhiteSpace(xuidHex) ? null : NormalizeXuid(xuidHex);
        return new RemoteSpoofApplyResult(applied, applied.All(x =>
            string.Equals(x.Name, text.Replace("{slot}", x.Slot.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), StringComparison.Ordinal) &&
            (canonicalXuidForVerify == null || string.Equals(x.Xuid, canonicalXuidForVerify, StringComparison.OrdinalIgnoreCase))));
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

    internal static bool TryResolveRestoreIdentity(
        ProfileHelpers.XamUserInfo? currentUser,
        CliConfig.SpoofIdentityCacheInfo? cached,
        string? gamertag,
        string? xuid,
        bool useCurrentUser,
        out string resolvedGamertag,
        out string resolvedXuid,
        out string? error) {
        error = null;
        resolvedGamertag = string.Empty;
        resolvedXuid = string.Empty;

        bool explicitValues = !string.IsNullOrWhiteSpace(gamertag) || !string.IsNullOrWhiteSpace(xuid);
        if (explicitValues) {
            if (string.IsNullOrWhiteSpace(gamertag) || string.IsNullOrWhiteSpace(xuid)) {
                error = "Provide both gamertag and XUID, or omit both to use cache/current user.";
                return false;
            }

            resolvedGamertag = gamertag.Trim();
            resolvedXuid = NormalizeXuid(xuid);
            return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
        }

        if (!useCurrentUser && cached != null &&
            !string.IsNullOrWhiteSpace(cached.Gamertag) &&
            !string.IsNullOrWhiteSpace(cached.Xuid)) {
            resolvedGamertag = cached.Gamertag.Trim();
            resolvedXuid = NormalizeXuid(cached.Xuid);
            return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
        }

        if (currentUser != null && !string.IsNullOrWhiteSpace(currentUser.Gamertag) && !string.IsNullOrWhiteSpace(currentUser.Xuid)) {
            resolvedGamertag = currentUser.Gamertag.Trim();
            resolvedXuid = NormalizeXuid(currentUser.Xuid);
            return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
        }

        if (cached != null &&
            !string.IsNullOrWhiteSpace(cached.Gamertag) &&
            !string.IsNullOrWhiteSpace(cached.Xuid)) {
            resolvedGamertag = cached.Gamertag.Trim();
            resolvedXuid = NormalizeXuid(cached.Xuid);
            return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
        }

        error = "No signed-in user or cached spoof identity was available. Provide explicit values or use --current-user when signed in.";
        return false;
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
            slots = Enumerable.Range(profile.Remote.StartSlotIndex, profile.Remote.ClientCount).ToArray();
            return true;
        }

        int zeroBased = slot!.Value - 1;
        int minSlot = profile.Remote.StartSlotIndex + 1;
        int maxSlot = profile.Remote.StartSlotIndex + profile.Remote.ClientCount;
        if (zeroBased < profile.Remote.StartSlotIndex || zeroBased >= profile.Remote.StartSlotIndex + profile.Remote.ClientCount) {
            error = $"Slot must be between {minSlot} and {maxSlot}.";
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

    private static string NormalizeTargetKey(string targetKey) {
        return targetKey.Trim().ToLowerInvariant();
    }

    private static async Task StageBlackOps2AccountLayerAsync(
        XbdmClient client,
        string gamertag,
        string xuidHex,
        CancellationToken cancellationToken) {
        Jrpc2Client jrpc = new Jrpc2Client(client);
        uint krnlResolve = await jrpc.ResolveFunctionAsync("xboxkrnl.exe", BlackOps2ExportOrdinalKrnl, cancellationToken);
        uint xamPrimaryResolve = await jrpc.ResolveFunctionAsync("xam.xex", BlackOps2ExportOrdinalXamPrimary, cancellationToken);
        uint xamSecondaryResolve = await jrpc.ResolveFunctionAsync("xam.xex", BlackOps2ExportOrdinalXamSecondary, cancellationToken);

        byte[] stageHeader = new byte[24];
        WriteUInt32BigEndian(stageHeader, 0, krnlResolve);
        WriteUInt32BigEndian(stageHeader, 4, xamPrimaryResolve);
        WriteUInt32BigEndian(stageHeader, 8, xamSecondaryResolve);
        WriteUInt32BigEndian(stageHeader, 12, BlackOps2ScratchHookAddress);
        WriteUInt32BigEndian(stageHeader, 16, BlackOps2ScratchStateAddress);
        WriteUInt32BigEndian(stageHeader, 20, 0U);
        await client.WriteMemoryAsync(BlackOps2ScratchBase, stageHeader, cancellationToken);

        await client.WriteMemoryAsync(BlackOps2ScratchObject5Address, BlackOps2AccountObject5Bytes, cancellationToken);
        await client.WriteMemoryAsync(BlackOps2ScratchObject6Address, BlackOps2AccountObject6Bytes, cancellationToken);
        await PatchBlackOps2EmbeddedPointerAsync(client, BlackOps2ScratchBase, BlackOps2ScratchObject5Address, 12U, cancellationToken);
        await PatchBlackOps2EmbeddedPointerAsync(client, BlackOps2ScratchBase, BlackOps2ScratchObject6Address, 20U, cancellationToken);

        byte[] xuidBytes = ToStoredXuidBytes(NormalizeXuid(xuidHex));
        await client.WriteMemoryAsync(BlackOps2ScratchStateAddress, xuidBytes, cancellationToken);

        await EnsureBlackOps2TrampolineSnapshotAsync(client, BlackOps2HookEntryAddress, BlackOps2ScratchHookAddress, cancellationToken);
        await EnsureBlackOps2SecondaryHookSnapshotAsync(client, BlackOps2HookSecondaryAddress, BlackOps2ScratchObject7Address, cancellationToken);
        await ApplyBlackOps2BranchStubAsync(client, BlackOps2HookSecondaryAddress, BlackOps2ScratchObject5Address, cancellationToken);
        await ApplyBlackOps2BranchStubAsync(client, BlackOps2HookEntryAddress, BlackOps2ScratchObject6Address, cancellationToken);
    }

    internal static async Task<GameSpoofApplyResult> RestoreIdentityAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        string gamertag,
        string xuidHex,
        CancellationToken cancellationToken) {
        if (profile.TitleId == BlackOps2TitleId) {
            return await RestoreBlackOps2IdentityAsync(client, profile, gamertag, xuidHex, cancellationToken);
        }

        return await ApplyIdentityAsync(client, profile, gamertag, xuidHex, cancellationToken);
    }

    internal static void ClearCachedIdentity(string targetKey, uint titleId) {
        CliConfig cfg = CliConfig.Load();
        if (cfg.SpoofIdentityCache == null || cfg.SpoofIdentityCache.Count == 0)
            return;

        string key = GetIdentityCacheKey(targetKey, titleId);
        if (!cfg.SpoofIdentityCache.Remove(key))
            return;

        cfg.Save();
    }

    private static async Task<GameSpoofApplyResult> RestoreBlackOps2IdentityAsync(
        XbdmClient client,
        SupportedGameSpoofProfile profile,
        string gamertag,
        string xuidHex,
        CancellationToken cancellationToken) {
        byte[] gamertagBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
        byte[] unicodeGamertagBytes = ToWideAsciiBytes(gamertag);
        string canonicalXuid = NormalizeXuid(xuidHex);
        byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
        byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");
        GameSpoofState preState = await ReadStateAsync(client, profile, cancellationToken);

        await RestoreBlackOps2AccountLayerAsync(client, cancellationToken);

        await client.WriteMemoryAsync(profile.NameAddress, new byte[0x40], cancellationToken);
        await client.WriteMemoryAsync(profile.NameAddress, gamertagBytes, cancellationToken);

        if (profile.SecondaryNameAddress.HasValue) {
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[0x40], cancellationToken);
            await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, gamertagBytes, cancellationToken);
        }

        await client.WriteMemoryAsync(BlackOps2UnicodeNameAddress, new byte[Math.Max(0x40, unicodeGamertagBytes.Length)], cancellationToken);
        await client.WriteMemoryAsync(BlackOps2UnicodeNameAddress, unicodeGamertagBytes, cancellationToken);

        await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[0x20], cancellationToken);
        await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);

        if (profile.PatchAddress.HasValue && profile.PatchBytes is { Length: > 0 }) {
            await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
        }

        uint? accountBlockAddress = await TryResolveBlackOps2AccountBlockAsync(
            client, preState.Gamertag, preState.CanonicalXuidHex, cancellationToken);
        if (accountBlockAddress.HasValue) {
            await WriteBlackOps2AccountBlockXuidAsync(client, accountBlockAddress.Value, xuidBinaryBytes, cancellationToken);
            await WriteBlackOps2AccountBlockGamertagAsync(client, accountBlockAddress.Value, gamertagBytes, cancellationToken);
        }

        GameSpoofState verified = await ReadStateAsync(client, profile, cancellationToken);
        bool verifiedMatch =
            string.Equals(verified.Gamertag, gamertag, StringComparison.Ordinal) &&
            string.Equals(verified.CanonicalXuidHex, canonicalXuid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(verified.SecondaryName, gamertag, StringComparison.Ordinal);

        return new GameSpoofApplyResult(verified, verifiedMatch, true);
    }

    private static async Task PatchBlackOps2EmbeddedPointerAsync(
        XbdmClient client,
        uint sourceAddress,
        uint destinationAddress,
        uint offset,
        CancellationToken cancellationToken) {
        uint high = 0x3D600000U | (sourceAddress >> 16);
        uint low = 0x616B0000U | (sourceAddress & 0xFFFFU);
        byte[] patchBytes = new byte[8];
        WriteUInt32BigEndian(patchBytes, 0, high);
        WriteUInt32BigEndian(patchBytes, 4, low);
        await client.WriteMemoryAsync(destinationAddress + offset, patchBytes, cancellationToken);
    }

    private static async Task EnsureBlackOps2TrampolineSnapshotAsync(
        XbdmClient client,
        uint sourceAddress,
        uint destinationAddress,
        CancellationToken cancellationToken) {
        byte[] existingHook = await client.ReadMemoryBytesReliableAsync(sourceAddress, BlackOps2HookStubLength, cancellationToken);
        byte[] expectedStub = BuildBlackOps2BranchStub(BlackOps2ScratchObject6Address);
        if (existingHook.AsSpan().SequenceEqual(expectedStub)) {
            byte[] existingTrampoline = await TryReadBlackOps2MemoryAsync(client, destinationAddress, BlackOps2TrampolineLength, cancellationToken);
            if (!HasAnyNonZeroByte(existingTrampoline))
                throw new InvalidOperationException("BO2 hook entry is already patched, but the primary trampoline snapshot is missing.");

            return;
        }

        if (!LooksLikeExpectedPrimaryHook(existingHook)) {
            throw new InvalidOperationException("BO2 hook entry bytes did not match the expected prologue. Aborting to avoid corrupting the title.");
        }

        byte[] existingTrampolineBytes = await TryReadBlackOps2MemoryAsync(client, destinationAddress, BlackOps2TrampolineLength, cancellationToken);
        if (HasAnyNonZeroByte(existingTrampolineBytes)) {
            ReadOnlySpan<byte> savedOriginal = existingTrampolineBytes.AsSpan(BlackOps2PrimaryTrampolineOriginalOffset, BlackOps2SnapshotLength);
            if (!savedOriginal.SequenceEqual(existingHook)) {
                throw new InvalidOperationException("BO2 primary trampoline snapshot does not match the live hook entry bytes.");
            }

            return;
        }

        byte[] trampoline = BuildBlackOps2TrampolineSnapshot(sourceAddress, existingHook);
        await client.WriteMemoryAsync(destinationAddress, trampoline, cancellationToken);
    }

    private static async Task EnsureBlackOps2SecondaryHookSnapshotAsync(
        XbdmClient client,
        uint sourceAddress,
        uint destinationAddress,
        CancellationToken cancellationToken) {
        byte[] existingHook = await client.ReadMemoryBytesReliableAsync(sourceAddress, BlackOps2HookStubLength, cancellationToken);
        byte[] expectedStub = BuildBlackOps2BranchStub(BlackOps2ScratchObject5Address);
        if (existingHook.AsSpan().SequenceEqual(expectedStub)) {
            byte[] existingSnapshot = await TryReadBlackOps2MemoryAsync(client, destinationAddress, BlackOps2SnapshotLength, cancellationToken);
            if (!HasAnyNonZeroByte(existingSnapshot))
                throw new InvalidOperationException("BO2 secondary hook is already patched, but the original-byte snapshot is missing.");

            return;
        }

        byte[] existingSnapshotBytes = await TryReadBlackOps2MemoryAsync(client, destinationAddress, BlackOps2SnapshotLength, cancellationToken);
        if (HasAnyNonZeroByte(existingSnapshotBytes)) {
            if (!existingSnapshotBytes.AsSpan().SequenceEqual(existingHook)) {
                throw new InvalidOperationException("BO2 secondary hook bytes no longer match the saved original snapshot.");
            }

            return;
        }

        if (!LooksLikeExecutableHook(existingHook)) {
            throw new InvalidOperationException("BO2 secondary hook bytes did not look executable. Aborting to avoid corrupting the title.");
        }

        await client.WriteMemoryAsync(destinationAddress, existingHook, cancellationToken);
    }

    private static async Task ApplyBlackOps2BranchStubAsync(
        XbdmClient client,
        uint sourceAddress,
        uint destinationAddress,
        CancellationToken cancellationToken) {
        byte[] branchStub = BuildBlackOps2BranchStub(destinationAddress);
        await client.WriteMemoryAsync(sourceAddress, branchStub, cancellationToken);
    }

    private static async Task RestoreBlackOps2AccountLayerAsync(
        XbdmClient client,
        CancellationToken cancellationToken) {
        byte[] primaryTrampoline = await TryReadBlackOps2MemoryAsync(client, BlackOps2ScratchHookAddress, BlackOps2TrampolineLength, cancellationToken);
        if (HasAnyNonZeroByte(primaryTrampoline)) {
            byte[] originalPrimaryBytes = primaryTrampoline
                .Skip(BlackOps2PrimaryTrampolineOriginalOffset)
                .Take(BlackOps2SnapshotLength)
                .ToArray();

            if (originalPrimaryBytes.Length == BlackOps2SnapshotLength && HasAnyNonZeroByte(originalPrimaryBytes)) {
                await client.WriteMemoryAsync(BlackOps2HookEntryAddress, originalPrimaryBytes, cancellationToken);
            }
        }

        byte[] secondarySnapshot = await TryReadBlackOps2MemoryAsync(client, BlackOps2ScratchObject7Address, BlackOps2SnapshotLength, cancellationToken);
        if (HasAnyNonZeroByte(secondarySnapshot)) {
            await client.WriteMemoryAsync(BlackOps2HookSecondaryAddress, secondarySnapshot, cancellationToken);
        }

        await client.WriteMemoryAsync(BlackOps2StubPrimaryAddress, new byte[BlackOps2PrimaryStubBytes.Length], cancellationToken);
        await client.WriteMemoryAsync(BlackOps2StubSecondaryAddress, new byte[BlackOps2SecondaryStubBytes.Length], cancellationToken);
        await client.WriteMemoryAsync(BlackOps2ScratchBase, new byte[BlackOps2ScratchClearLength], cancellationToken);
    }

    private static byte[] BuildBlackOps2TrampolineSnapshot(uint sourceAddress, ReadOnlySpan<byte> originalBytes) {
        uint returnAddress = sourceAddress + 16U;
        byte[] trampoline = new byte[32];
        uint high = 0x3D600000U + ((returnAddress >> 16) & 0xFFFFU);
        if ((returnAddress & 0x8000U) != 0U)
            high += 1U;

        uint low = 0x39600000U + (returnAddress & 0xFFFFU);
        WriteUInt32BigEndian(trampoline, 0, high);
        WriteUInt32BigEndian(trampoline, 4, low);
        WriteUInt32BigEndian(trampoline, 8, 0x7D6903A6U);
        originalBytes.Slice(0, 16).CopyTo(trampoline.AsSpan(12));
        WriteUInt32BigEndian(trampoline, 28, 0x4E800420U);
        return trampoline;
    }

    private static byte[] BuildBlackOps2BranchStub(uint destinationAddress) {
        byte[] stub = new byte[16];
        uint high = 0x3D600000U + ((destinationAddress >> 16) & 0xFFFFU);
        if ((destinationAddress & 0x8000U) != 0U)
            high += 1U;

        uint low = 0x39600000U + (destinationAddress & 0xFFFFU);
        WriteUInt32BigEndian(stub, 0, high);
        WriteUInt32BigEndian(stub, 4, low);
        WriteUInt32BigEndian(stub, 8, 0x7D6903A6U);
        WriteUInt32BigEndian(stub, 12, 0x4E800420U);
        return stub;
    }

    private static bool LooksLikeExpectedPrimaryHook(ReadOnlySpan<byte> currentBytes) {
        return currentBytes.Length >= BlackOps2SnapshotLength &&
               currentBytes.Slice(0, BlackOps2SnapshotLength).SequenceEqual(BlackOps2ExpectedPrimaryHookBytes);
    }

    private static bool LooksLikeExecutableHook(ReadOnlySpan<byte> currentBytes) {
        if (currentBytes.Length < BlackOps2SnapshotLength)
            return false;

        if (!HasAnyNonZeroByte(currentBytes))
            return false;

        uint firstOpcode = BinaryPrimitives.ReadUInt32BigEndian(currentBytes.Slice(0, 4));
        return firstOpcode is not 0U and not 0xFFFFFFFFU and not 0x4E800420U;
    }

    private static bool HasAnyNonZeroByte(ReadOnlySpan<byte> data) {
        foreach (byte value in data) {
            if (value != 0)
                return true;
        }

        return false;
    }

    private static async Task<byte[]> TryReadBlackOps2MemoryAsync(
        XbdmClient client,
        uint address,
        int length,
        CancellationToken cancellationToken) {
        try {
            return await client.ReadMemoryBytesReliableAsync(address, length, cancellationToken);
        }
        catch {
            return new byte[length];
        }
    }

    private static async Task<uint?> TryResolveBlackOps2AccountBlockAsync(
        XbdmClient client,
        string gamertag,
        string canonicalXuid,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(gamertag) || string.IsNullOrWhiteSpace(canonicalXuid) || canonicalXuid == new string('0', 16))
            return null;

        byte[] pattern = BuildBlackOps2AccountBlockSignature(gamertag, canonicalXuid);
        int overlap = pattern.Length - 1;
        byte[] carry = Array.Empty<byte>();

        for (uint address = BlackOps2AccountBlockSearchStart; address < BlackOps2AccountBlockSearchEnd; address += BlackOps2AccountBlockChunkSize) {
            int length = (int)Math.Min((ulong)BlackOps2AccountBlockChunkSize, BlackOps2AccountBlockSearchEnd - address);
            byte[] chunk = await TryReadBlackOps2MemoryAsync(client, address, length, cancellationToken);
            if (!HasAnyNonZeroByte(chunk))
                continue;

            byte[] window;
            if (carry.Length == 0) {
                window = chunk;
            }
            else {
                window = new byte[carry.Length + chunk.Length];
                Buffer.BlockCopy(carry, 0, window, 0, carry.Length);
                Buffer.BlockCopy(chunk, 0, window, carry.Length, chunk.Length);
            }

            int index = IndexOfPattern(window, pattern);
            if (index >= 0) {
                long absolute = (long)address - carry.Length + index;
                return (uint)absolute;
            }

            if (overlap > 0) {
                int copyLength = Math.Min(overlap, window.Length);
                carry = new byte[copyLength];
                Buffer.BlockCopy(window, window.Length - copyLength, carry, 0, copyLength);
            }
        }

        return null;
    }

    private static byte[] BuildBlackOps2AccountBlockSignature(string gamertag, string canonicalXuid) {
        byte[] gtBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
        byte[] xuidBytes = Convert.FromHexString(canonicalXuid);
        byte[] pattern = new byte[2 + 18 + xuidBytes.Length + gtBytes.Length];
        pattern[0] = 0xDB;
        pattern[1] = 0x47;
        Buffer.BlockCopy(xuidBytes, 0, pattern, 20, xuidBytes.Length);
        Buffer.BlockCopy(gtBytes, 0, pattern, 28, gtBytes.Length);
        return pattern;
    }

    private static int IndexOfPattern(byte[] buffer, byte[] pattern) {
        if (pattern.Length == 0 || buffer.Length < pattern.Length)
            return -1;

        for (int i = 0; i <= buffer.Length - pattern.Length; i++) {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++) {
                if (buffer[i + j] != pattern[j]) {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }

    private static async Task WriteBlackOps2AccountBlockGamertagAsync(
        XbdmClient client,
        uint blockAddress,
        byte[] gamertagBytes,
        CancellationToken cancellationToken) {
        uint fieldAddress = blockAddress + BlackOps2AccountBlockGamertagOffset;
        await client.WriteMemoryAsync(fieldAddress, new byte[BlackOps2AccountBlockGamertagFieldLength], cancellationToken);
        await client.WriteMemoryAsync(fieldAddress, gamertagBytes, cancellationToken);
    }

    private static async Task WriteBlackOps2AccountBlockXuidAsync(
        XbdmClient client,
        uint blockAddress,
        byte[] xuidBytes,
        CancellationToken cancellationToken) {
        await client.WriteMemoryAsync(blockAddress + (uint)BlackOps2AccountBlockXuidOffset, xuidBytes, cancellationToken);
    }

    private static void WriteUInt32BigEndian(Span<byte> destination, int offset, uint value) {
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, 4), value);
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

    private static byte[] ToWideAsciiBytes(string value) {
        byte[] bytes = new byte[value.Length * 2 + 2];
        int cursor = 1;
        foreach (char ch in value) {
            bytes[cursor] = (byte) ch;
            cursor += 2;
        }

        return bytes;
    }
}

internal sealed record GameSpoofState(string Gamertag, string CanonicalXuidHex, string StoredXuidHex, string StoredXuidText, string? SecondaryName);
internal sealed record GameSpoofApplyResult(GameSpoofState Verified, bool VerifiedMatch, bool PatchApplied);
internal sealed record RemoteClientState(int Slot, uint NameAddress, uint? MirrorAddress, string Name, string? MirrorName, uint? XuidAddress = null, string? Xuid = null);
internal sealed record RemoteSpoofApplyResult(IReadOnlyList<RemoteClientState> Applied, bool VerifiedMatch);

internal static class SpoofNotifyHelpers {
    internal static string? GetLogoOverride(string? iconName, string? logoValue) {
        return string.IsNullOrWhiteSpace(iconName) && string.IsNullOrWhiteSpace(logoValue) ? "14" : logoValue;
    }

    internal static string BuildSpoofingIdentityMessage(string title, string gamertag, string xuid) {
        return $"XeCLI ({title}) - Spoofing {gamertag} | {xuid}";
    }

    internal static string BuildRemoteSpoofMessage(string title, string text) {
        return $"XeCLI ({title}) - Spoofing {text} | remote";
    }

    internal static string BuildResetIdentityMessage(string title, string gamertag, string xuid) {
        return $"XeCLI ({title}) - Resetting {gamertag} | {xuid}";
    }
}

public abstract class SpoofIdentitySettingsBase : ConnectionSettings {
    [CommandOption("--current-user")]
    [LocalizedDescription("Use the currently signed-in user value.")]
    public bool CurrentUser { get; init; }

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
        [LocalizedDescription("Gamertag to write into the running supported title.")]
        public string? Value { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        string targetKey = $"{ip}:{port}";
        return await CliHelpers.WithClientOnceAsync((ip, port, timeout), settings, async client => {
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
            GameSpoofHelpers.StoreOriginalIdentityIfMissing(targetKey, titleId, currentState);
            GameSpoofApplyResult result = await GameSpoofHelpers.ApplyGamertagAsync(client, profile, gamertag, CancellationToken.None);

            if (result.VerifiedMatch) {
                OperationFeedback.WriteSuccess("Gamertag spoof applied", $"[gold1]{Markup.Escape(result.Verified.Gamertag)}[/]");
            }
            else {
                OperationFeedback.WriteWarning("Gamertag spoof wrote memory but verification did not fully match", Markup.Escape(profile.Name));
            }

            string notifyMessage = SpoofNotifyHelpers.BuildSpoofingIdentityMessage(profile.Name, result.Verified.Gamertag, result.Verified.CanonicalXuidHex);
            await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), notifyMessage, CancellationToken.None, useBottomPosition: true);
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
        [LocalizedDescription("16-character XUID to write into the running supported title.")]
        public string? Value { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        string targetKey = $"{ip}:{port}";
        return await CliHelpers.WithClientOnceAsync((ip, port, timeout), settings, async client => {
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

            if (profile.TitleId == 0x415608C3) {
                AnsiConsole.MarkupLine("[red]BO2 XUID spoof is disabled: use `rgh spoof gt` for local-name spoofing and `rgh spoof remote` for lobby-slot spoofing.[/]");
                return 1;
            }

            GameSpoofState currentState = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
            GameSpoofHelpers.StoreOriginalIdentityIfMissing(targetKey, titleId, currentState);
            GameSpoofApplyResult result = await GameSpoofHelpers.ApplyIdentityAsync(client, profile, currentState.Gamertag, xuid, CancellationToken.None);

            if (result.VerifiedMatch) {
                OperationFeedback.WriteSuccess("XUID spoof applied", $"[cyan1]{Markup.Escape(result.Verified.CanonicalXuidHex)}[/]");
            }
            else {
                OperationFeedback.WriteWarning("XUID spoof wrote memory but verification did not fully match", Markup.Escape(profile.Name));
            }

            string notifyMessage = SpoofNotifyHelpers.BuildSpoofingIdentityMessage(profile.Name, result.Verified.Gamertag, result.Verified.CanonicalXuidHex);
            await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), notifyMessage, CancellationToken.None, useBottomPosition: true);
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
                        XuidAddress = x.XuidAddress.HasValue ? $"0x{x.XuidAddress.Value:X8}" : null,
                        x.Name,
                        x.MirrorName,
                        x.Xuid
                    })
                });
                return 0;
            }

            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold white]Slot[/]"));
            table.AddColumn(new TableColumn("[bold green3]Name[/]"));
            table.AddColumn(new TableColumn("[bold deepskyblue1]Address[/]"));
            table.AddColumn(new TableColumn("[bold cyan1]XUID[/]"));
            table.AddColumn(new TableColumn("[bold grey70]Mirror[/]"));
            foreach (RemoteClientState slot in slots.Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.MirrorName))) {
                table.AddRow(
                    $"[white]{slot.Slot}[/]",
                    $"[gold1]{Markup.Escape(slot.Name)}[/]",
                    $"[mediumpurple3_1]0x{slot.NameAddress:X8}[/]",
                    slot.Xuid != null ? $"[cyan1]{Markup.Escape(slot.Xuid)}[/]" : "[grey50]-[/]",
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
        [LocalizedDescription("1-based client slot to overwrite.")]
        public int? Slot { get; init; }

        [CommandOption("--all")]
        [LocalizedDescription("Overwrite every supported slot.")]
        public bool All { get; init; }

        [CommandOption("--text <TEXT>")]
        [LocalizedDescription("Replacement name text. Use {slot} to inject the 1-based slot number.")]
        public string? Text { get; init; }

        [CommandOption("--xuid <HEX>")]
        [LocalizedDescription("16-character XUID to write into each target slot's identity field alongside the name.")]
        public string? Xuid { get; init; }

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

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        string targetKey = $"{ip}:{port}";
        return await CliHelpers.WithClientOnceAsync((ip, port, timeout), settings, async client => {
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

            string? remoteXuid = null;
            if (!string.IsNullOrWhiteSpace(settings.Xuid)) {
                if (!GameSpoofHelpers.TryResolveXuid(null, settings.Xuid, false, out string rxuid, out string? rxuidErr)) {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(rxuidErr ?? "Invalid XUID.")}[/]");
                    return 1;
                }
                if (profile.Remote?.XuidBinaryOffset == null) {
                    AnsiConsole.MarkupLine("[yellow]--xuid ignored: current title does not have a mapped remote XUID field.[/]");
                }
                else {
                    remoteXuid = rxuid;
                }
            }

            GameSpoofState currentState = await GameSpoofHelpers.ReadStateAsync(client, profile, CancellationToken.None);
            GameSpoofHelpers.StoreOriginalIdentityIfMissing(targetKey, titleId, currentState);
            RemoteSpoofApplyResult result = await GameSpoofHelpers.ApplyRemoteTextAsync(client, profile, slots, settings.Text.Trim(), CancellationToken.None, remoteXuid);
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
            table.AddColumn(new TableColumn("[bold cyan1]XUID[/]"));
            foreach (RemoteClientState applied in result.Applied) {
                table.AddRow(
                    $"[white]{applied.Slot}[/]",
                    $"[gold1]{Markup.Escape(applied.Name)}[/]",
                    $"[mediumpurple3_1]0x{applied.NameAddress:X8}[/]",
                    applied.Xuid != null ? $"[cyan1]{Markup.Escape(applied.Xuid)}[/]" : "[grey50]-[/]");
            }

            AnsiConsole.Write(table);
            string notifyMessage = SpoofNotifyHelpers.BuildRemoteSpoofMessage(profile.Name, result.Applied.FirstOrDefault()?.Name ?? settings.Text.Trim());
            await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), notifyMessage, CancellationToken.None, useBottomPosition: true);
            return result.VerifiedMatch ? 0 : 1;
        }, CancellationToken.None);
    }
}

public sealed class SpoofResetCommand : AsyncCommand<SpoofResetCommand.Settings> {
    public sealed class Settings : SpoofIdentitySettingsBase {
        [CommandOption("--gamertag <TEXT>")]
        [LocalizedDescription("Explicit gamertag to restore instead of the signed-in user.")]
        public string? Gamertag { get; init; }

        [CommandOption("--xuid <HEX>")]
        [LocalizedDescription("Explicit XUID to restore instead of the signed-in user.")]
        public string? Xuid { get; init; }

        [CommandOption("--clear-remote")]
        [LocalizedDescription("Clear every supported remote slot after restoring the local identity.")]
        public bool ClearRemote { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        string targetKey = $"{ip}:{port}";
        return await CliHelpers.WithClientOnceAsync((ip, port, timeout), settings, async client => {
            uint titleId = await GameSpoofHelpers.GetCurrentTitleIdAsync(client, CancellationToken.None);
            GameSpoofHelpers.SupportedGameSpoofProfile? profile = GameSpoofHelpers.TryGet(titleId);
            if (profile == null) {
                OperationFeedback.WriteWarning("Current title not supported for spoof reset", $"0x{titleId:X8}");
                return 1;
            }

            ProfileHelpers.XamUserInfo? currentUser = await HardwareHelpers.TryGetSignedInUserAsync(client, CancellationToken.None);
            CliConfig.SpoofIdentityCacheInfo? cached = GameSpoofHelpers.TryGetCachedIdentity(targetKey, titleId);
            if (!GameSpoofHelpers.TryResolveRestoreIdentity(currentUser, cached, settings.Gamertag, settings.Xuid, settings.CurrentUser, out string gamertag, out string xuid, out string? error)) {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Unable to resolve restore identity.")}[/]");
                return 1;
            }

            GameSpoofApplyResult result = await GameSpoofHelpers.RestoreIdentityAsync(client, profile, gamertag, xuid, CancellationToken.None);
            bool remoteCleared = false;
            if (settings.ClearRemote && profile.Remote != null) {
                IReadOnlyList<int> slots = Enumerable.Range(profile.Remote.StartSlotIndex, profile.Remote.ClientCount).ToArray();
                RemoteSpoofApplyResult clearResult = await GameSpoofHelpers.ApplyRemoteTextAsync(client, profile, slots, string.Empty, CancellationToken.None);
                remoteCleared = clearResult.VerifiedMatch;
            }

            if (result.VerifiedMatch)
                GameSpoofHelpers.ClearCachedIdentity(targetKey, titleId);

            if (result.VerifiedMatch) {
                string detail = remoteCleared ? "[grey]Local identity restored and remote slots cleared[/]" : "[grey]Local identity restored[/]";
                OperationFeedback.WriteSuccess("Spoof reset applied", $"[gold1]{Markup.Escape(result.Verified.Gamertag)}[/] [grey]|[/] [cyan1]{Markup.Escape(result.Verified.CanonicalXuidHex)}[/] {detail}");
            }
            else {
                OperationFeedback.WriteWarning("Spoof reset wrote memory but verification did not fully match", Markup.Escape(profile.Name));
            }

            string notifyMessage = SpoofNotifyHelpers.BuildResetIdentityMessage(profile.Name, result.Verified.Gamertag, result.Verified.CanonicalXuidHex);
            await NotifyHelpers.TrySendOperationNotificationAsync(client, settings.Notify, settings.NotifyIcon, SpoofNotifyHelpers.GetLogoOverride(settings.NotifyIcon, settings.NotifyLogo), notifyMessage, CancellationToken.None, useBottomPosition: true);
            return result.VerifiedMatch ? 0 : 1;
        }, CancellationToken.None);
    }
}

