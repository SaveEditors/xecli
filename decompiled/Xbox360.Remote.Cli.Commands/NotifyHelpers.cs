using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Xbox360.Remote.Cli.Commands;

internal static class NotifyHelpers
{
	public static bool TryResolvePosition(string? positionValue, out int position, out string? error)
	{
		error = null;
		position = 0;
		if (string.IsNullOrWhiteSpace(positionValue))
		{
			return true;
		}
		switch (positionValue.Trim().ToLowerInvariant())
		{
		case "center":
		case "middle":
		case "hc":
		case "vc":
			position = 0;
			return true;
		case "top-center":
		case "topcenter":
		case "top":
			position = 1;
			return true;
		case "bottom":
		case "bottomcenter":
		case "bottom-center":
			position = 2;
			return true;
		case "centerleft":
		case "center-left":
		case "left":
			position = 4;
			return true;
		case "top-left":
		case "topleft":
			position = 5;
			return true;
		case "bottomleft":
		case "bottom-left":
			position = 6;
			return true;
		case "center-right":
		case "centerright":
		case "right":
			position = 8;
			return true;
		case "top-right":
		case "topright":
			position = 9;
			return true;
		case "bottom-right":
		case "bottomright":
			position = 10;
			return true;
		default:
			if (int.TryParse(positionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out position))
			{
				return true;
			}
			if (NotifyCatalog.TryParseInt(positionValue, out position))
			{
				return true;
			}
			error = "Invalid position. Use top, bottom, center, left, right, top-left, top-right, bottom-left, bottom-right, or a numeric value.";
			return false;
		}
	}

	public static bool TryResolveLogo(string? iconName, string? logoValue, out int logo, out string? error)
	{
		error = null;
		logo = 0;
		if (!string.IsNullOrWhiteSpace(iconName))
		{
			CliConfig cliConfig = CliConfig.Load();
			if (cliConfig.NotifyIcons == null || cliConfig.NotifyIcons.Count == 0)
			{
				error = "No notify icon presets configured. Use `rgh notify-icons add` to add presets or `rgh notify-icons list` to browse built-ins.";
				return false;
			}
			if (!cliConfig.NotifyIcons.TryGetValue(iconName, out logo))
			{
				error = "Unknown icon preset '" + iconName + "'. Use `rgh notify-icons list`.";
				return false;
			}
			return true;
		}
		if (!string.IsNullOrWhiteSpace(logoValue))
		{
			if (NotifyCatalog.TryResolve(logoValue, out NotifyLogoDefinition definition) && definition != null)
			{
				logo = definition.Id;
				return true;
			}
			if (NotifyCatalog.TryParseInt(logoValue, out logo))
			{
				return true;
			}
			error = "Invalid logo value. Use a decimal ID, 0x hex ID, or a built-in name from `rgh notify-icons list`.";
			return false;
		}
		logo = 0;
		return true;
	}

	public static string DescribeLogo(int logo)
	{
		return NotifyCatalog.Describe(logo);
	}

	public static async Task TrySendOperationNotificationAsync(XbdmClient client, bool enabled, string? iconName, string? logoValue, string message, CancellationToken cancellationToken, bool useBottomPosition = false)
	{
		if (!enabled)
		{
			return;
		}
		if (!TryResolveLogo(iconName, logoValue, out int logo, out string _))
		{
			logo = 14;
		}
		try
		{
			Jrpc2Client jrpc = new Jrpc2Client(client);
			if (useBottomPosition)
			{
				try
				{
					await jrpc.SetNotificationPositionAsync(2, cancellationToken);
				}
				catch
				{
				}
			}
			await jrpc.ShowNotificationAsync(logo, message, cancellationToken);
		}
		catch
		{
		}
	}
}
