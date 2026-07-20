namespace Xbox360.Remote;

public static class NotificationTextPolicy {
    public const int MaxNotificationTextLength = 29;

    public static bool TryPrepareNotificationMessage(string? value, out string normalized, out string error) {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value)) {
            error = "Notification text is required.";
            return false;
        }

        if (value.Length > MaxNotificationTextLength) {
            error = $"Notification text must be at most {MaxNotificationTextLength} characters.";
            return false;
        }

        foreach (char ch in value) {
            if (ch > 0x7F) {
                error = "Notification text must use ASCII characters only.";
                return false;
            }

            if (char.IsControl(ch)) {
                error = "Notification text cannot contain control characters or newlines.";
                return false;
            }
        }

        normalized = value;
        return true;
    }
}
