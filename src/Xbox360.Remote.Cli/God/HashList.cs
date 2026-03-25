using System.Security.Cryptography;

namespace Xbox360.Remote.Cli.God;

internal sealed class HashList {
    private readonly byte[] buffer = new byte[4096];
    private int length;

    public ReadOnlySpan<byte> Bytes => buffer;

    public static HashList Read(Stream stream) {
        HashList list = new HashList();
        stream.ReadExactly(list.buffer);

        for (int i = 0; i < list.buffer.Length; i += 20) {
            bool empty = true;
            for (int j = 0; j < 20; j++) {
                if (list.buffer[i + j] != 0) {
                    empty = false;
                    break;
                }
            }

            if (empty) {
                list.length = i;
                return list;
            }
        }

        list.length = list.buffer.Length;
        return list;
    }

    public void AddHash(ReadOnlySpan<byte> hash) {
        if (hash.Length != 20)
            throw new ArgumentException("Hash must be 20 bytes.", nameof(hash));
        hash.CopyTo(buffer.AsSpan(length, 20));
        length += 20;
    }

    public void AddBlockHash(ReadOnlySpan<byte> block) {
        byte[] digest = SHA1.HashData(block);
        AddHash(digest);
    }

    public byte[] Digest() {
        return SHA1.HashData(buffer);
    }

    public void Write(Stream stream) {
        stream.Write(buffer, 0, buffer.Length);
    }
}
