using System.Text.Json;
using System.Text.Json.Serialization;
using FluentFTP;
using System.Linq;
using System.Globalization;
using Xbox360.Remote.Cli.LocalProfiles;

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

    internal sealed class ResolvedIdentityInfo {
        [JsonPropertyName("slot")]
        public int? Slot { get; set; }

        [JsonPropertyName("gamertag")]
        public string? Gamertag { get; set; }

        [JsonPropertyName("xuid")]
        public string? Xuid { get; set; }

        [JsonPropertyName("signinstate")]
        public uint? SignInState { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonIgnore]
        public IReadOnlyList<XbdmUserInfo> Users { get; set; } = Array.Empty<XbdmUserInfo>();

        [JsonIgnore]
        public XamUserInfo? XamUser { get; set; }

        [JsonIgnore]
        public F3ProfileInfo? F3Profile { get; set; }

        [JsonPropertyName("signedin")]
        public bool IsSignedIn => !string.IsNullOrWhiteSpace(Gamertag) || (SignInState.HasValue && SignInState.Value > 0);

        [JsonPropertyName("signinstatetext")]
        public string SignInStateText => SignInState.HasValue
            ? HardwareHelpers.DescribeSignInState(SignInState.Value)
            : (IsSignedIn ? "Signed in" : "Not signed in");
    }

    internal sealed class ProfilePackageIdentityInfo {
        [JsonPropertyName("profileid")]
        public string? ProfileId { get; set; }

        [JsonPropertyName("gamertag")]
        public string? Gamertag { get; set; }

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

    internal static async Task<ResolvedIdentityInfo> ResolveSignedInIdentityAsync(
        XbdmClient client,
        string ip,
        int port,
        int timeoutMs,
        CliConfig config,
        bool allowF3,
        bool allowProfilePackage,
        CancellationToken cancellationToken) {
        ResolvedIdentityInfo resolved = new ResolvedIdentityInfo();

        IReadOnlyList<XbdmUserInfo> users = Array.Empty<XbdmUserInfo>();
        try {
            users = await client.GetUserListAsync(cancellationToken);
        }
        catch {
            users = Array.Empty<XbdmUserInfo>();
        }

        resolved.Users = users;
        ApplyXbdmUsers(users, resolved);

        if (allowProfilePackage && NeedsProfilePackageFallback(resolved)) {
            try {
                ProfilePackageIdentityInfo? packageIdentity = await TryResolveProfilePackageIdentityAsync(ip, timeoutMs, config, cancellationToken);
                if (packageIdentity != null) {
                    resolved.Gamertag ??= TrimOrNull(packageIdentity.Gamertag);
                    resolved.Xuid ??= NormalizeXuidText(packageIdentity.Xuid);
                    resolved.SignInState ??= 1;
                    resolved.Source ??= "profile-package";
                }
            }
            catch {
                // ignored
            }
        }

        if (allowF3 && string.IsNullOrWhiteSpace(TrimOrNull(resolved.Gamertag))) {
            try {
                F3ProfileInfo? f3Profile = (await TryGetF3ProfilesAsync(ip))
                    .FirstOrDefault(p => p.SignedIn == 1 && !string.IsNullOrWhiteSpace(p.Gamertag));
                if (f3Profile != null) {
                    resolved.F3Profile = f3Profile;
                    resolved.Gamertag ??= TrimOrNull(f3Profile.Gamertag);
                    resolved.Xuid ??= NormalizeXuidText(f3Profile.Xuid);
                    resolved.SignInState ??= 1;
                    resolved.Source ??= "aurora";
                }
            }
            catch {
                // ignored
            }
        }

        if (NeedsMoreIdentity(resolved)) {
            try {
                MergeXamIdentity(resolved, await TryGetSignedInXamUserAsync(client, cancellationToken), "xam");
            }
            catch {
                // ignored
            }
        }

        if (NeedsMoreIdentity(resolved)) {
            try {
                MergeXamIdentity(resolved, await TryGetSignedInXamUserAsync(ip, port, timeoutMs, cancellationToken), "xam-fresh");
            }
            catch {
                // ignored
            }
        }

        return resolved;
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

        if (string.IsNullOrWhiteSpace(gamertag) && !xuid.HasValue && signedInState == 0)
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

            if (string.IsNullOrWhiteSpace(gamertag) && !xuid.HasValue && signedInState == 0)
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

        string? rawTitleId = titleId.HasValue ? TitleIdDatabase.FormatTitleId(titleId.Value) : null;
        if (string.IsNullOrWhiteSpace(runningXex))
            return string.IsNullOrWhiteSpace(resolvedName) ? rawTitleId : resolvedName;

        string fileName = Path.GetFileNameWithoutExtension(runningXex.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
            return string.IsNullOrWhiteSpace(resolvedName) ? rawTitleId : resolvedName;

        if (titleId == 0xFFFE07D1 &&
            !fileName.Equals("dash", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("default", StringComparison.OrdinalIgnoreCase)) {
            return fileName;
        }

        if (!string.IsNullOrWhiteSpace(resolvedName))
            return resolvedName;
        if (fileName.Equals("dash", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("default", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("game", StringComparison.OrdinalIgnoreCase)) {
            return rawTitleId;
        }
        return fileName;
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

    private static bool NeedsMoreIdentity(ResolvedIdentityInfo info) {
        return string.IsNullOrWhiteSpace(TrimOrNull(info.Gamertag)) ||
               string.IsNullOrWhiteSpace(TrimOrNull(info.Xuid)) ||
               !info.SignInState.HasValue ||
               info.SignInState.Value == 0;
    }

    private static bool NeedsProfilePackageFallback(ResolvedIdentityInfo info) {
        return string.IsNullOrWhiteSpace(TrimOrNull(info.Gamertag)) ||
               !info.SignInState.HasValue ||
               info.SignInState.Value == 0;
    }

    private static void ApplyXbdmUsers(IReadOnlyList<XbdmUserInfo> users, ResolvedIdentityInfo resolved) {
        XbdmUserInfo? signedInUser = SelectSignedInXbdmUser(users);

        if (signedInUser == null)
            return;

        resolved.Gamertag ??= TrimOrNull(signedInUser.Gamertag);
        resolved.Xuid ??= signedInUser.Xuid.HasValue ? $"0x{signedInUser.Xuid.Value:X16}" : null;
        resolved.SignInState ??= signedInUser.SignInState;
        resolved.Source ??= "xbdm";
    }

    internal static XbdmUserInfo? SelectSignedInXbdmUser(IReadOnlyList<XbdmUserInfo> users) {
        return users.FirstOrDefault(u =>
                u.SignInState.HasValue && u.SignInState.Value > 0 && !string.IsNullOrWhiteSpace(u.Gamertag))
            ?? users.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Gamertag))
            ?? users.FirstOrDefault(u => u.SignInState.HasValue && u.SignInState.Value > 0);
    }

    private static void MergeXamIdentity(ResolvedIdentityInfo resolved, XamUserInfo? xamUser, string source) {
        if (xamUser == null)
            return;

        resolved.XamUser ??= xamUser;
        if (!resolved.Slot.HasValue)
            resolved.Slot = xamUser.Slot;
        resolved.Gamertag ??= TrimOrNull(xamUser.Gamertag);
        resolved.Xuid ??= NormalizeXuidText(xamUser.Xuid);
        resolved.SignInState ??= xamUser.SignInState;
        resolved.Source ??= source;
    }

    internal static async Task<ProfilePackageIdentityInfo?> TryResolveProfilePackageIdentityAsync(
        string ip,
        int timeoutMs,
        CliConfig config,
        CancellationToken cancellationToken) {
        List<(string User, string Pass)> credentials = BuildFtpCredentialCandidates(config);
        if (credentials.Count == 0)
            return null;

        int ftpPort = FtpEndpointHelpers.GetConfiguredFtpPort(config);
        int effectiveTimeout = Math.Clamp(timeoutMs, 1500, 7000);

        foreach ((string user, string pass) in credentials) {
            List<(string ProfileId, string RemotePath, DateTime ModifiedUtc)> candidates = new List<(string, string, DateTime)>();
            try {
                await using AsyncFtpClient ftpClient = FtpHelpers.CreateClient(ip, ftpPort, user, pass, effectiveTimeout);
                await ftpClient.Connect(cancellationToken);

                foreach (string root in ProfileRoots) {
                    string contentPath = $"/{root}/Content";
                    FtpListItem[] profiles;
                    try {
                        (profiles, bool rootListing) = await FtpHelpers.GetListingWithFallbackAsync(ftpClient, contentPath);
                        if (rootListing)
                            continue;
                    }
                    catch {
                        continue;
                    }

                    foreach (FtpListItem profileDir in profiles) {
                        if (profileDir.Type != FtpObjectType.Directory ||
                            !IsHex16(profileDir.Name) ||
                            profileDir.Name.Equals("0000000000000000", StringComparison.OrdinalIgnoreCase)) {
                            continue;
                        }

                        string packageDirectory = $"{contentPath}/{profileDir.Name}/FFFE07D1/00010000";
                        FtpListItem[] packages;
                        try {
                            (packages, _) = await FtpHelpers.GetListingWithFallbackAsync(ftpClient, packageDirectory);
                        }
                        catch {
                            continue;
                        }

                        foreach (FtpListItem package in packages) {
                            if (package.Type != FtpObjectType.File || !string.Equals(package.Name, profileDir.Name, StringComparison.OrdinalIgnoreCase))
                                continue;

                            string remotePath = FtpHelpers.NormalizePath($"{packageDirectory}/{profileDir.Name}");
                            DateTime modifiedUtc = package.Modified == DateTime.MinValue
                                ? DateTime.MinValue
                                : package.Modified.ToUniversalTime();
                            candidates.Add((profileDir.Name, remotePath, modifiedUtc));
                        }
                    }
                }

                foreach ((string profileId, string remotePath, _) in candidates.OrderByDescending(candidate => candidate.ModifiedUtc)) {
                    string tempPath = Path.Combine(Path.GetTempPath(), "xecli-profile-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".bin");
                    try {
                        FtpStatus downloadStatus = await ftpClient.DownloadFile(
                            tempPath,
                            remotePath,
                            FtpLocalExists.Overwrite,
                            FtpVerify.None,
                            null,
                            cancellationToken);
                        if (downloadStatus != FtpStatus.Success)
                            throw new IOException($"FTP profile download did not complete for {remotePath} (status: {downloadStatus}).");

                        using ProfilePackage profilePackage = new ProfilePackage(tempPath);
                        ProfileAccountInfo account = profilePackage.ReadAccount();
                        string? gamertag = TrimOrNull(account.Gamertag);
                        string? xuid = account.OnlineXuid != 0 ? $"0x{account.OnlineXuid:X16}" : null;
                        if (!string.IsNullOrWhiteSpace(gamertag) || !string.IsNullOrWhiteSpace(xuid)) {
                            return new ProfilePackageIdentityInfo {
                                ProfileId = profileId,
                                Gamertag = gamertag,
                                Xuid = xuid
                            };
                        }
                    }
                    catch {
                        // ignored
                    }
                    finally {
                        try {
                            if (File.Exists(tempPath))
                                File.Delete(tempPath);
                        }
                        catch {
                            // ignored
                        }
                    }
                }
            }
            catch {
                // try next credential candidate
            }
        }

        return null;
    }

    private static List<(string User, string Pass)> BuildFtpCredentialCandidates(CliConfig config) {
        List<(string User, string Pass)> candidates = new List<(string User, string Pass)>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? user, string? pass) {
            string? normalizedUser = TrimOrNull(user);
            if (string.IsNullOrWhiteSpace(normalizedUser))
                return;

            string normalizedPass = pass ?? string.Empty;
            string key = normalizedUser + "\n" + normalizedPass;
            if (seen.Add(key))
                candidates.Add((normalizedUser, normalizedPass));
        }

        AddCandidate(config.DefaultFtpUser, config.DefaultFtpPassword);
        AddCandidate("xboxftp", "xboxftp");
        AddCandidate("xbox", "xbox");
        return candidates;
    }

    private static string? NormalizeXuidText(string? value) {
        string? trimmed = TrimOrNull(value);
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed : $"0x{trimmed}";
    }

    private static string? TrimOrNull(string? value) {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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
