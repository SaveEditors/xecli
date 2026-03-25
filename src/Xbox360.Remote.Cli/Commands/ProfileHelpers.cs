using System.Text.Json;
using System.Text.Json.Serialization;
using FluentFTP;
using System.Linq;
using System.Globalization;

namespace Xbox360.Remote.Cli.Commands;

internal static class ProfileHelpers {
    private static readonly string[] ProfileRoots = {
        "Hdd1", "Usb0", "Usb1", "Usb2", "Mu", "IntMu", "MmcMu"
    };
    private const uint XamUserGetNameOrdinal = 526;
    private const uint XamUserGetSigninStateOrdinal = 528;
    private const uint XamUserGetSigninInfoOrdinal = 551;

    internal sealed class XamUserInfo {
        [JsonPropertyName("slot")]
        public int Slot { get; set; }

        [JsonPropertyName("gamertag")]
        public string? Gamertag { get; set; }

        [JsonPropertyName("xuid")]
        public string? Xuid { get; set; }

        [JsonPropertyName("signinstate")]
        public uint SignInState { get; set; }
    }

    internal sealed class F3ProfileInfo {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("gamertag")]
        public string? Gamertag { get; set; }

        [JsonPropertyName("gamerscore")]
        public int Gamerscore { get; set; }

        [JsonPropertyName("signedin")]
        public int SignedIn { get; set; }

        [JsonPropertyName("xuid")]
        public string? Xuid { get; set; }
    }

    internal static async Task<List<string>> TryGetFtpProfilesAsync(string ip) {
        List<string> results = new List<string>();
        FtpConnectionSettings ftpSettings = new FtpConnectionSettings { Ip = ip };
        await FtpHelpers.WithClientAsync(ftpSettings, async client => {
            foreach (string root in ProfileRoots) {
                string path = $"/{root}/Content";
                FtpListItem[] listing;
                try {
                    (listing, bool rootListing) = await FtpHelpers.GetListingWithFallbackAsync(client, path);
                    if (rootListing)
                        continue;
                }
                catch {
                    continue;
                }

                foreach (FtpListItem item in listing) {
                    if (item.Type != FtpObjectType.Directory)
                        continue;
                    if (IsHex16(item.Name) &&
                        !item.Name.Equals("0000000000000000", StringComparison.OrdinalIgnoreCase)) {
                        results.Add($"{root}:{item.Name}");
                    }
                }
            }

            return 0;
        }, CancellationToken.None);

        return results;
    }

    internal static async Task<List<F3ProfileInfo>> TryGetF3ProfilesAsync(string ip) {
        using HttpClient client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(2);
        string url = $"http://{ip}:9999/getProfileInfo";
        using HttpResponseMessage response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return new List<F3ProfileInfo>();

        string json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
            return new List<F3ProfileInfo>();

        try {
            List<F3ProfileInfo>? profiles = JsonSerializer.Deserialize<List<F3ProfileInfo>>(json, new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });
            return profiles ?? new List<F3ProfileInfo>();
        }
        catch {
            return new List<F3ProfileInfo>();
        }
    }

    internal static Dictionary<string, List<string>> GroupFtpProfiles(IEnumerable<string> entries) {
        Dictionary<string, List<string>> grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in entries) {
            int idx = entry.IndexOf(':');
            if (idx <= 0 || idx == entry.Length - 1)
                continue;
            string device = entry.Substring(0, idx);
            string id = entry.Substring(idx + 1);
            if (!grouped.TryGetValue(id, out List<string>? list)) {
                list = new List<string>();
                grouped[id] = list;
            }
            if (!list.Any(existing => existing.Equals(device, StringComparison.OrdinalIgnoreCase))) {
                list.Add(device);
            }
        }

        return grouped;
    }

    internal static async Task<XamUserInfo?> TryGetSignedInXamUserAsync(string ip, int port, int timeoutMs, CancellationToken cancellationToken) {
        int signedInSlot = -1;
        uint signedInState = 0;

        await WithFreshClientAsync(ip, port, timeoutMs, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            for (int slot = 0; slot < 4; slot++) {
                string response = await jrpc.CallAsync(
                    RpcDataType.Int,
                    null,
                    "xam.xex",
                    (int) XamUserGetSigninStateOrdinal,
                    false,
                    false,
                    new[] { new RpcArgument(RpcArgType.Int, slot) },
                    cancellationToken);

                if (!TryParseRpcUInt32(response, out uint state) || state == 0)
                    continue;

                signedInSlot = slot;
                signedInState = state;
                break;
            }

            return 0;
        }, cancellationToken);

        if (signedInSlot < 0)
            return null;

        uint? scratchBase = await WithFreshClientAsync(ip, port, timeoutMs, client => FindScratchBaseAsync(client, cancellationToken), cancellationToken);
        if (!scratchBase.HasValue)
            return null;

        uint nameBuffer = scratchBase.Value;
        uint infoBuffer = scratchBase.Value + 0x100;

        await WithFreshClientAsync(ip, port, timeoutMs, async client => {
            await ZeroMemoryRawAsync(client, nameBuffer, 0x100, cancellationToken);
            await ZeroMemoryRawAsync(client, infoBuffer, 0x100, cancellationToken);
            return 0;
        }, cancellationToken);

        await WithFreshClientAsync(ip, port, timeoutMs, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            await jrpc.CallAsync(
                RpcDataType.Int,
                null,
                "xam.xex",
                (int) XamUserGetNameOrdinal,
                false,
                false,
                new[] {
                    new RpcArgument(RpcArgType.Int, signedInSlot),
                    new RpcArgument(RpcArgType.UInt, nameBuffer),
                    new RpcArgument(RpcArgType.Int, 256)
                },
                cancellationToken);

            await jrpc.CallAsync(
                RpcDataType.Int,
                null,
                "xam.xex",
                (int) XamUserGetSigninInfoOrdinal,
                false,
                false,
                new[] {
                    new RpcArgument(RpcArgType.Int, signedInSlot),
                    new RpcArgument(RpcArgType.Int, 1),
                    new RpcArgument(RpcArgType.UInt, infoBuffer)
                },
                cancellationToken);

            return 0;
        }, cancellationToken);

        byte[] nameBytes = await WithFreshClientAsync(ip, port, timeoutMs, client => ReadMemoryRawAsync(client, nameBuffer, 64, cancellationToken), cancellationToken);
        byte[] infoBytes = await WithFreshClientAsync(ip, port, timeoutMs, client => ReadMemoryRawAsync(client, infoBuffer, 64, cancellationToken), cancellationToken);

        string? gamertag = ReadAsciiString(nameBytes);
        ulong? xuid = null;
        if (infoBytes.Length >= 8) {
            ulong parsed = BitConverter.ToUInt64(infoBytes, 0);
            if (parsed != 0)
                xuid = parsed;
        }

        if (string.IsNullOrWhiteSpace(gamertag))
            return null;

        return new XamUserInfo {
            Slot = signedInSlot,
            Gamertag = gamertag,
            Xuid = xuid.HasValue ? $"0x{xuid.Value:X16}" : null,
            SignInState = signedInState
        };
    }

    internal static async Task<XamUserInfo?> TryGetSignedInXamUserAsync(XbdmClient client, CancellationToken cancellationToken) {
        Jrpc2Client jrpc = new Jrpc2Client(client);
        int signedInSlot = -1;
        uint signedInState = 0;

        for (int slot = 0; slot < 4; slot++) {
            string response = await jrpc.CallAsync(
                RpcDataType.Int,
                null,
                "xam.xex",
                (int) XamUserGetSigninStateOrdinal,
                false,
                false,
                new[] { new RpcArgument(RpcArgType.Int, slot) },
                cancellationToken);

            if (!TryParseRpcUInt32(response, out uint state) || state == 0)
                continue;

            signedInSlot = slot;
            signedInState = state;
            break;
        }

        if (signedInSlot < 0)
            return null;

        uint? scratchBase = await FindScratchBaseAsync(client, cancellationToken);
        if (!scratchBase.HasValue)
            return null;

        uint nameBuffer = scratchBase.Value;
        uint infoBuffer = scratchBase.Value + 0x100;
        try {
            await ZeroMemoryRawAsync(client, nameBuffer, 0x100, cancellationToken);
            await ZeroMemoryRawAsync(client, infoBuffer, 0x100, cancellationToken);

            await jrpc.CallAsync(
                RpcDataType.Int,
                null,
                "xam.xex",
                (int) XamUserGetNameOrdinal,
                false,
                false,
                new[] {
                    new RpcArgument(RpcArgType.Int, signedInSlot),
                    new RpcArgument(RpcArgType.UInt, nameBuffer),
                    new RpcArgument(RpcArgType.Int, 256)
                },
                cancellationToken);

            await jrpc.CallAsync(
                RpcDataType.Int,
                null,
                "xam.xex",
                (int) XamUserGetSigninInfoOrdinal,
                false,
                false,
                new[] {
                    new RpcArgument(RpcArgType.Int, signedInSlot),
                    new RpcArgument(RpcArgType.Int, 1),
                    new RpcArgument(RpcArgType.UInt, infoBuffer)
                },
                cancellationToken);

            byte[] nameBytes = await ReadMemoryRawAsync(client, nameBuffer, 64, cancellationToken);
            byte[] infoBytes = await ReadMemoryRawAsync(client, infoBuffer, 64, cancellationToken);

            string? gamertag = ReadAsciiString(nameBytes);
            ulong? xuid = null;
            if (infoBytes.Length >= 8) {
                ulong parsed = BitConverter.ToUInt64(infoBytes, 0);
                if (parsed != 0)
                    xuid = parsed;
            }

            if (string.IsNullOrWhiteSpace(gamertag))
                return null;

            return new XamUserInfo {
                Slot = signedInSlot,
                Gamertag = gamertag,
                Xuid = xuid.HasValue ? $"0x{xuid.Value:X16}" : null,
                SignInState = signedInState
            };
        }
        catch {
            return null;
        }
    }

    internal static string? TryGetTitleFallbackName(uint? titleId, string? runningXex, string? resolvedName) {
        if (!string.IsNullOrWhiteSpace(resolvedName) &&
            !resolvedName.Equals("Xbox 360 Dashboard", StringComparison.OrdinalIgnoreCase)) {
            return resolvedName;
        }

        if (string.IsNullOrWhiteSpace(runningXex))
            return resolvedName;

        string fileName = Path.GetFileNameWithoutExtension(runningXex.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
            return resolvedName;

        if (titleId == 0xFFFE07D1 &&
            !fileName.Equals("dash", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("default", StringComparison.OrdinalIgnoreCase)) {
            return fileName;
        }

        return string.IsNullOrWhiteSpace(resolvedName) ? fileName : resolvedName;
    }

    private static async Task<uint?> FindScratchBaseAsync(XbdmClient client, CancellationToken cancellationToken) {
        IReadOnlyList<XbdmMemoryRegion> regions = await client.GetMemoryRegionsAsync(cancellationToken);
        IEnumerable<XbdmMemoryRegion> candidates = regions
            .Where(r => r.Protect == 4 && r.Size >= 0x200)
            .OrderBy(r => Math.Abs((long) r.BaseAddress - 0x30000000L));

        foreach (XbdmMemoryRegion region in candidates) {
            byte[] probe = await ReadMemoryRawAsync(client, region.BaseAddress, 0x100, cancellationToken);
            if (probe.Length < 0x100)
                continue;

            int nonZero = probe.Count(b => b != 0);
            if (nonZero == 0)
                return region.BaseAddress;
        }

        return null;
    }

    private static async Task<T> WithFreshClientAsync<T>(string ip, int port, int timeoutMs, Func<XbdmClient, Task<T>> action, CancellationToken cancellationToken) {
        using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
            Host = ip,
            Port = port,
            TimeoutMs = timeoutMs
        }, cancellationToken);
        return await action(client);
    }

    private static async Task ZeroMemoryRawAsync(XbdmClient client, uint address, int length, CancellationToken cancellationToken) {
        const int ChunkSize = 32;
        byte[] zeros = new byte[ChunkSize];
        string hex = Convert.ToHexString(zeros);
        int offset = 0;
        while (offset < length) {
            int writeLength = Math.Min(ChunkSize, length - offset);
            string chunkHex = writeLength == ChunkSize ? hex : Convert.ToHexString(new byte[writeLength]);
            (XbdmResponse response, _) = await client.SendRawAsync(
                $"setmem addr=0x{address + (uint) offset:X8} data={chunkHex}",
                cancellationToken);
            if (response.StatusCode != 200)
                throw new IOException($"setmem failed: {response.RawMessage}");
            offset += writeLength;
        }
    }

    private static async Task<byte[]> ReadMemoryRawAsync(XbdmClient client, uint address, int length, CancellationToken cancellationToken) {
        (XbdmResponse response, IReadOnlyList<string>? lines) = await client.SendRawAsync(
            $"getmem addr=0x{address:X8} length={length}",
            cancellationToken);

        if (response.StatusCode != 202 || lines == null || lines.Count == 0)
            return Array.Empty<byte>();

        string hex = string.Concat(lines);
        char[] filtered = hex.Where(Uri.IsHexDigit).ToArray();
        if (filtered.Length == 0)
            return Array.Empty<byte>();

        string normalized = new string(filtered);
        int maxChars = Math.Min(normalized.Length, length * 2);
        if ((maxChars & 1) != 0)
            maxChars--;
        if (maxChars <= 0)
            return Array.Empty<byte>();

        return Convert.FromHexString(normalized.Substring(0, maxChars));
    }

    private static string? ReadAsciiString(byte[] data) {
        if (data.Length == 0)
            return null;
        int end = Array.IndexOf(data, (byte) 0);
        if (end < 0)
            end = data.Length;
        if (end == 0)
            return null;
        return System.Text.Encoding.ASCII.GetString(data, 0, end).Trim();
    }

    private static bool TryParseRpcUInt32(string? text, out uint value) {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        if (CliHelpers.TryParseUInt32(text, out value))
            return true;
        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsHex16(string name) {
        if (name.Length != 16)
            return false;
        for (int i = 0; i < name.Length; i++) {
            char ch = name[i];
            bool isHex = (ch >= '0' && ch <= '9') ||
                         (ch >= 'a' && ch <= 'f') ||
                         (ch >= 'A' && ch <= 'F');
            if (!isHex)
                return false;
        }
        return true;
    }
}
