using System.Security.Cryptography;

namespace Xbox360.Remote.Cli;

internal static class FileHashHelpers {
    public static string ComputeSha256(string path) {
        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ComputeSha256(stream);
    }

    public static string ComputeSha256(Stream stream) {
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
