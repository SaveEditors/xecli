using System.Globalization;
using System.Text;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class NotifyLogoDefinition {
    public NotifyLogoDefinition(int id, string key, string label, string? notes = null, params string[] aliases) {
        Id = id;
        Key = key;
        Label = label;
        Notes = notes;
        Aliases = aliases ?? Array.Empty<string>();
    }

    public int Id { get; }
    public string Key { get; }
    public string Label { get; }
    public string? Notes { get; }
    public IReadOnlyList<string> Aliases { get; }
}

internal static class NotifyCatalog {
    private static readonly NotifyLogoDefinition[] Definitions = [
        new(0, "xbox-logo", "Xbox logo", "Default Xbox sphere/logo."),
        new(1, "new-message-logo", "New message logo"),
        new(2, "friend-request-logo", "Friend request logo"),
        new(3, "new-message", "New message"),
        new(4, "flashing-xbox-logo", "Flashing Xbox logo"),
        new(5, "gamertag-sent-you-a-message", "Gamertag sent you a message", null, "message"),
        new(6, "gamertag-signed-out", "Gamertag signed out", null, "signed-out"),
        new(7, "gamertag-signed-in", "Gamertag signed in", null, "signed-in"),
        new(8, "gamertag-signed-into-xbox-live", "Gamertag signed into Xbox Live", null, "signed-into-live"),
        new(9, "gamertag-signed-in-offline", "Gamertag signed in offline", null, "offline-signin"),
        new(10, "gamertag-wants-to-chat", "Gamertag wants to chat", null, "chat-request"),
        new(11, "disconnected-from-xbox-live", "Disconnected from Xbox Live", null, "xbox-live-disconnected"),
        new(12, "download", "Download"),
        new(13, "flashing-music-symbol", "Flashing music symbol", null, "music"),
        new(14, "flashing-happy-face", "Flashing happy face", "Common success/smiley icon.", "happy-face", "smiley", "success"),
        new(15, "flashing-frowning-face", "Flashing frowning face", null, "frowning-face", "sad"),
        new(16, "flashing-double-sided-hammer", "Flashing double-sided hammer", null, "hammer", "tools"),
        new(17, "gamertag-wants-to-chat-2", "Gamertag wants to chat 2"),
        new(18, "please-reinsert-memory-unit", "Please reinsert memory unit", null, "memory-unit"),
        new(19, "please-reconnect-controller", "Please reconnect controller", null, "controller"),
        new(20, "gamertag-has-joined-chat", "Gamertag has joined chat", null, "joined-chat"),
        new(21, "gamertag-has-left-chat", "Gamertag has left chat", null, "left-chat"),
        new(22, "game-invite-sent", "Game invite sent", null, "invite-sent"),
        new(23, "flash-logo", "Flash logo", null, "flash"),
        new(24, "page-sent-to", "Page sent to", null, "page"),
        new(25, "four-2", "Reserved / unknown 25"),
        new(26, "four-3", "Reserved / unknown 26"),
        new(27, "achievement-unlocked", "Achievement unlocked", null, "achievement"),
        new(28, "four-9", "Reserved / unknown 28"),
        new(29, "gamertag-wants-to-talk-in-video-kinect", "Gamertag wants to talk in video Kinect", null, "video-kinect"),
        new(30, "video-chat-invite-sent", "Video chat invite sent", null, "video-chat"),
        new(31, "ready-to-play", "Ready to play"),
        new(32, "cant-download-x", "Cannot download X", null, "cant-download"),
        new(33, "download-stopped-for-x", "Download stopped for X", null, "download-stopped"),
        new(34, "flashing-xbox-console", "Flashing Xbox console", null, "console"),
        new(35, "x-sent-you-a-game-message", "X sent you a game message", null, "game-message"),
        new(36, "device-full", "Device full", null, "storage-full"),
        new(37, "four-7", "Reserved / unknown 37"),
        new(38, "flashing-chat-icon", "Flashing chat icon", null, "chat-icon"),
        new(39, "achievements-unlocked", "Achievements unlocked", null, "achievements"),
        new(40, "x-has-sent-you-a-nudge", "X has sent you a nudge", null, "nudge"),
        new(41, "messenger-disconnected", "Messenger disconnected", null, "messenger"),
        new(42, "blank", "Blank", "Displays no icon."),
        new(43, "cant-sign-in-messenger", "Cannot sign in messenger", null, "messenger-signin-failed"),
        new(44, "missed-messenger-conversation", "Missed messenger conversation"),
        new(45, "family-timer-x-time-remaining", "Family timer X time remaining", null, "family-timer"),
        new(46, "disconnected-xbox-live-11-minutes-remaining", "Disconnected Xbox Live 11 minutes remaining", null, "11-minutes-remaining"),
        new(47, "kinect-health-effects", "Kinect health effects", null, "kinect"),
        new(48, "four-5", "Reserved / unknown 48"),
        new(49, "gamertag-wants-you-to-join-an-xbox-live-party", "Gamertag wants you to join an Xbox Live party", null, "join-party"),
        new(50, "party-invite-sent", "Party invite sent", null, "party-invite"),
        new(51, "game-invite-sent-to-xbox-live-party", "Game invite sent to Xbox Live party", null, "game-invite-party"),
        new(52, "kicked-from-xbox-live-party", "Kicked from Xbox Live party", null, "kicked-party"),
        new(53, "nulled", "Nulled", "Known public enum entry; exact visual meaning is unclear."),
        new(54, "disconnected-xbox-live-party", "Disconnected Xbox Live party", null, "party-disconnected"),
        new(55, "downloaded", "Downloaded"),
        new(56, "cant-connect-xbl-party", "Cannot connect Xbox Live party", null, "cant-connect-party"),
        new(57, "gamertag-has-joined-xbl-party", "Gamertag has joined Xbox Live party", null, "joined-party"),
        new(58, "gamertag-has-left-xbl-party", "Gamertag has left Xbox Live party", null, "left-party"),
        new(59, "gamer-picture-unlocked", "Gamer picture unlocked", null, "gamer-picture"),
        new(60, "avatar-award-unlocked", "Avatar award unlocked", null, "avatar", "avatar-award"),
        new(61, "joined-xbl-party", "Joined Xbox Live party", null, "joined-xbox-live-party"),
        new(62, "please-reinsert-usb-storage-device", "Please reinsert USB storage device", null, "usb-storage"),
        new(63, "player-muted", "Player muted", null, "muted"),
        new(64, "player-unmuted", "Player unmuted", null, "unmuted"),
        new(65, "flashing-chat-symbol", "Flashing chat symbol", null, "chat-symbol"),
        new(76, "updating", "Updating", null, "update")
    ];

    private static readonly Dictionary<int, NotifyLogoDefinition> ById = Definitions.ToDictionary(x => x.Id);
    private static readonly Dictionary<string, NotifyLogoDefinition> ByName = BuildNameMap();

    public static IReadOnlyList<NotifyLogoDefinition> All => Definitions;

    public static bool TryResolve(string value, out NotifyLogoDefinition? definition) {
        definition = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (TryParseInt(value, out int id) && ById.TryGetValue(id, out definition))
            return true;

        return ByName.TryGetValue(NormalizeToken(value), out definition);
    }

    public static bool TryGet(int id, out NotifyLogoDefinition? definition) {
        return ById.TryGetValue(id, out definition);
    }

    public static string Describe(int id) {
        if (TryGet(id, out NotifyLogoDefinition? definition) && definition != null)
            return $"{id} ({definition.Label})";
        return id.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryParseInt(string value, out int number) {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number) && number >= 0;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 0;
    }

    private static Dictionary<string, NotifyLogoDefinition> BuildNameMap() {
        Dictionary<string, NotifyLogoDefinition> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (NotifyLogoDefinition definition in Definitions) {
            map[NormalizeToken(definition.Key)] = definition;
            map[NormalizeToken(definition.Label)] = definition;
            foreach (string alias in definition.Aliases) {
                map[NormalizeToken(alias)] = definition;
            }
        }

        return map;
    }

    private static string NormalizeToken(string value) {
        StringBuilder builder = new(value.Length);
        bool lastDash = false;
        foreach (char ch in value.Trim().ToLowerInvariant()) {
            if (char.IsLetterOrDigit(ch)) {
                builder.Append(ch);
                lastDash = false;
                continue;
            }

            if (ch is ' ' or '_' or '-' or '/' or '\\' or '.') {
                if (!lastDash) {
                    builder.Append('-');
                    lastDash = true;
                }
            }
        }

        return builder.ToString().Trim('-');
    }
}
