using System.Text.Json;
using System.Text.Json.Serialization;
using FluentFTP;
using System.Linq;

namespace Xbox360.Remote.Cli.Commands;

internal static class ProfileHelpers {
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
            string[] roots = {
                "Hdd1", "HddX", "Usb0", "Usb1", "UsbMu", "Mu", "IntMu", "MmcMu"
            };

            foreach (string root in roots) {
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
                    if (IsHex16(item.Name)) {
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
