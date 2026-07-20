using System.Text;

namespace Xbox360.Remote.Cli.Commands;

internal static class CommandLineTokenizer {
    public static IReadOnlyList<string> Split(string commandLine) {
        List<string> tokens = [];
        StringBuilder current = new();
        char? quote = null;
        bool escaping = false;

        foreach (char ch in commandLine) {
            if (escaping) {
                current.Append(ch);
                escaping = false;
                continue;
            }

            if (quote != null) {
                current.Append(ch);
                if (ch == '\\') {
                    escaping = true;
                    continue;
                }

                if (ch == quote)
                    quote = null;

                continue;
            }

            if (char.IsWhiteSpace(ch)) {
                FlushToken(tokens, current);
                continue;
            }

            if (ch == '"' || ch == '\'') {
                quote = ch;
                current.Append(ch);
                continue;
            }

            current.Append(ch);
        }

        FlushToken(tokens, current);
        return tokens;
    }

    public static IReadOnlyList<string> StripCliPrefix(IReadOnlyList<string> tokens) {
        if (tokens.Count == 0)
            return tokens;

        if (!IsCliPrefix(tokens[0]))
            return tokens;

        if (tokens.Count == 1)
            return [];

        List<string> stripped = new(tokens.Count - 1);
        for (int i = 1; i < tokens.Count; i++)
            stripped.Add(tokens[i]);

        return stripped;
    }

    private static bool IsCliPrefix(string token) {
        return token.Equals("rgh", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("rgh.exe", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("xecli", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("xecli.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static void FlushToken(List<string> tokens, StringBuilder current) {
        if (current.Length == 0)
            return;

        tokens.Add(current.ToString());
        current.Clear();
    }
}
