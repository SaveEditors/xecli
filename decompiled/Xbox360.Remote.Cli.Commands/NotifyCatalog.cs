using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Xbox360.Remote.Cli.Commands;

internal static class NotifyCatalog
{
	private static readonly NotifyLogoDefinition[] Definitions = new NotifyLogoDefinition[67]
	{
		new NotifyLogoDefinition(0, "xbox-logo", "Xbox logo", "Default Xbox sphere/logo."),
		new NotifyLogoDefinition(1, "new-message-logo", "New message logo", null),
		new NotifyLogoDefinition(2, "friend-request-logo", "Friend request logo", null),
		new NotifyLogoDefinition(3, "new-message", "New message", null),
		new NotifyLogoDefinition(4, "flashing-xbox-logo", "Flashing Xbox logo", null),
		new NotifyLogoDefinition(5, "gamertag-sent-you-a-message", "Gamertag sent you a message", null, "message"),
		new NotifyLogoDefinition(6, "gamertag-signed-out", "Gamertag signed out", null, "signed-out"),
		new NotifyLogoDefinition(7, "gamertag-signed-in", "Gamertag signed in", null, "signed-in"),
		new NotifyLogoDefinition(8, "gamertag-signed-into-xbox-live", "Gamertag signed into Xbox Live", null, "signed-into-live"),
		new NotifyLogoDefinition(9, "gamertag-signed-in-offline", "Gamertag signed in offline", null, "offline-signin"),
		new NotifyLogoDefinition(10, "gamertag-wants-to-chat", "Gamertag wants to chat", null, "chat-request"),
		new NotifyLogoDefinition(11, "disconnected-from-xbox-live", "Disconnected from Xbox Live", null, "xbox-live-disconnected"),
		new NotifyLogoDefinition(12, "download", "Download", null),
		new NotifyLogoDefinition(13, "flashing-music-symbol", "Flashing music symbol", null, "music"),
		new NotifyLogoDefinition(14, "flashing-happy-face", "Flashing happy face", "Common success/smiley icon.", "happy-face", "smiley", "success"),
		new NotifyLogoDefinition(15, "flashing-frowning-face", "Flashing frowning face", null, "frowning-face", "sad"),
		new NotifyLogoDefinition(16, "flashing-double-sided-hammer", "Flashing double-sided hammer", null, "hammer", "tools"),
		new NotifyLogoDefinition(17, "gamertag-wants-to-chat-2", "Gamertag wants to chat 2", null),
		new NotifyLogoDefinition(18, "please-reinsert-memory-unit", "Please reinsert memory unit", null, "memory-unit"),
		new NotifyLogoDefinition(19, "please-reconnect-controller", "Please reconnect controller", null, "controller"),
		new NotifyLogoDefinition(20, "gamertag-has-joined-chat", "Gamertag has joined chat", null, "joined-chat"),
		new NotifyLogoDefinition(21, "gamertag-has-left-chat", "Gamertag has left chat", null, "left-chat"),
		new NotifyLogoDefinition(22, "game-invite-sent", "Game invite sent", null, "invite-sent"),
		new NotifyLogoDefinition(23, "flash-logo", "Flash logo", null, "flash"),
		new NotifyLogoDefinition(24, "page-sent-to", "Page sent to", null, "page"),
		new NotifyLogoDefinition(25, "four-2", "Reserved / unknown 25", null),
		new NotifyLogoDefinition(26, "four-3", "Reserved / unknown 26", null),
		new NotifyLogoDefinition(27, "achievement-unlocked", "Achievement unlocked", null, "achievement"),
		new NotifyLogoDefinition(28, "four-9", "Reserved / unknown 28", null),
		new NotifyLogoDefinition(29, "gamertag-wants-to-talk-in-video-kinect", "Gamertag wants to talk in video Kinect", null, "video-kinect"),
		new NotifyLogoDefinition(30, "video-chat-invite-sent", "Video chat invite sent", null, "video-chat"),
		new NotifyLogoDefinition(31, "ready-to-play", "Ready to play", null),
		new NotifyLogoDefinition(32, "cant-download-x", "Cannot download X", null, "cant-download"),
		new NotifyLogoDefinition(33, "download-stopped-for-x", "Download stopped for X", null, "download-stopped"),
		new NotifyLogoDefinition(34, "flashing-xbox-console", "Flashing Xbox console", null, "console"),
		new NotifyLogoDefinition(35, "x-sent-you-a-game-message", "X sent you a game message", null, "game-message"),
		new NotifyLogoDefinition(36, "device-full", "Device full", null, "storage-full"),
		new NotifyLogoDefinition(37, "four-7", "Reserved / unknown 37", null),
		new NotifyLogoDefinition(38, "flashing-chat-icon", "Flashing chat icon", null, "chat-icon"),
		new NotifyLogoDefinition(39, "achievements-unlocked", "Achievements unlocked", null, "achievements"),
		new NotifyLogoDefinition(40, "x-has-sent-you-a-nudge", "X has sent you a nudge", null, "nudge"),
		new NotifyLogoDefinition(41, "messenger-disconnected", "Messenger disconnected", null, "messenger"),
		new NotifyLogoDefinition(42, "blank", "Blank", "Displays no icon."),
		new NotifyLogoDefinition(43, "cant-sign-in-messenger", "Cannot sign in messenger", null, "messenger-signin-failed"),
		new NotifyLogoDefinition(44, "missed-messenger-conversation", "Missed messenger conversation", null),
		new NotifyLogoDefinition(45, "family-timer-x-time-remaining", "Family timer X time remaining", null, "family-timer"),
		new NotifyLogoDefinition(46, "disconnected-xbox-live-11-minutes-remaining", "Disconnected Xbox Live 11 minutes remaining", null, "11-minutes-remaining"),
		new NotifyLogoDefinition(47, "kinect-health-effects", "Kinect health effects", null, "kinect"),
		new NotifyLogoDefinition(48, "four-5", "Reserved / unknown 48", null),
		new NotifyLogoDefinition(49, "gamertag-wants-you-to-join-an-xbox-live-party", "Gamertag wants you to join an Xbox Live party", null, "join-party"),
		new NotifyLogoDefinition(50, "party-invite-sent", "Party invite sent", null, "party-invite"),
		new NotifyLogoDefinition(51, "game-invite-sent-to-xbox-live-party", "Game invite sent to Xbox Live party", null, "game-invite-party"),
		new NotifyLogoDefinition(52, "kicked-from-xbox-live-party", "Kicked from Xbox Live party", null, "kicked-party"),
		new NotifyLogoDefinition(53, "nulled", "Nulled", "Known public enum entry; exact visual meaning is unclear."),
		new NotifyLogoDefinition(54, "disconnected-xbox-live-party", "Disconnected Xbox Live party", null, "party-disconnected"),
		new NotifyLogoDefinition(55, "downloaded", "Downloaded", null),
		new NotifyLogoDefinition(56, "cant-connect-xbl-party", "Cannot connect Xbox Live party", null, "cant-connect-party"),
		new NotifyLogoDefinition(57, "gamertag-has-joined-xbl-party", "Gamertag has joined Xbox Live party", null, "joined-party"),
		new NotifyLogoDefinition(58, "gamertag-has-left-xbl-party", "Gamertag has left Xbox Live party", null, "left-party"),
		new NotifyLogoDefinition(59, "gamer-picture-unlocked", "Gamer picture unlocked", null, "gamer-picture"),
		new NotifyLogoDefinition(60, "avatar-award-unlocked", "Avatar award unlocked", null, "avatar", "avatar-award"),
		new NotifyLogoDefinition(61, "joined-xbl-party", "Joined Xbox Live party", null, "joined-xbox-live-party"),
		new NotifyLogoDefinition(62, "please-reinsert-usb-storage-device", "Please reinsert USB storage device", null, "usb-storage"),
		new NotifyLogoDefinition(63, "player-muted", "Player muted", null, "muted"),
		new NotifyLogoDefinition(64, "player-unmuted", "Player unmuted", null, "unmuted"),
		new NotifyLogoDefinition(65, "flashing-chat-symbol", "Flashing chat symbol", null, "chat-symbol"),
		new NotifyLogoDefinition(76, "updating", "Updating", null, "update")
	};

	private static readonly Dictionary<int, NotifyLogoDefinition> ById = Definitions.ToDictionary((NotifyLogoDefinition x) => x.Id);

	private static readonly Dictionary<string, NotifyLogoDefinition> ByName = BuildNameMap();

	public static IReadOnlyList<NotifyLogoDefinition> All => Definitions;

	public static bool TryResolve(string value, out NotifyLogoDefinition? definition)
	{
		definition = null;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		if (TryParseInt(value, out var number) && ById.TryGetValue(number, out definition))
		{
			return true;
		}
		return ByName.TryGetValue(NormalizeToken(value), out definition);
	}

	public static bool TryGet(int id, out NotifyLogoDefinition? definition)
	{
		return ById.TryGetValue(id, out definition);
	}

	public static string Describe(int id)
	{
		if (TryGet(id, out NotifyLogoDefinition definition) && definition != null)
		{
			return $"{id} ({definition.Label})";
		}
		return id.ToString(CultureInfo.InvariantCulture);
	}

	public static bool TryParseInt(string value, out int number)
	{
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			if (int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number))
			{
				return number >= 0;
			}
			return false;
		}
		if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
		{
			return number >= 0;
		}
		return false;
	}

	private static Dictionary<string, NotifyLogoDefinition> BuildNameMap()
	{
		Dictionary<string, NotifyLogoDefinition> dictionary = new Dictionary<string, NotifyLogoDefinition>(StringComparer.OrdinalIgnoreCase);
		NotifyLogoDefinition[] definitions = Definitions;
		foreach (NotifyLogoDefinition notifyLogoDefinition in definitions)
		{
			dictionary[NormalizeToken(notifyLogoDefinition.Key)] = notifyLogoDefinition;
			dictionary[NormalizeToken(notifyLogoDefinition.Label)] = notifyLogoDefinition;
			foreach (string alias in notifyLogoDefinition.Aliases)
			{
				dictionary[NormalizeToken(alias)] = notifyLogoDefinition;
			}
		}
		return dictionary;
	}

	private static string NormalizeToken(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		bool flag = false;
		string text = value.Trim().ToLowerInvariant();
		foreach (char c in text)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(c);
				flag = false;
				continue;
			}
			bool flag2;
			switch (c)
			{
			case ' ':
			case '-':
			case '.':
			case '/':
			case '\\':
			case '_':
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			if (flag2 && !flag)
			{
				stringBuilder.Append('-');
				flag = true;
			}
		}
		return stringBuilder.ToString().Trim('-');
	}
}
