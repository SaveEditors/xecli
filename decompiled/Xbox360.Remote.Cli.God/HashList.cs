using System;
using System.IO;
using System.Security.Cryptography;

namespace Xbox360.Remote.Cli.God;

internal sealed class HashList
{
	private readonly byte[] buffer = new byte[4096];

	private int length;

	public ReadOnlySpan<byte> Bytes => buffer;

	public static HashList Read(Stream stream)
	{
		HashList hashList = new HashList();
		stream.ReadExactly(hashList.buffer);
		for (int i = 0; i < hashList.buffer.Length; i += 20)
		{
			bool flag = true;
			for (int j = 0; j < 20; j++)
			{
				if (hashList.buffer[i + j] != 0)
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				hashList.length = i;
				return hashList;
			}
		}
		hashList.length = hashList.buffer.Length;
		return hashList;
	}

	public void AddHash(ReadOnlySpan<byte> hash)
	{
		if (hash.Length != 20)
		{
			throw new ArgumentException("Hash must be 20 bytes.", "hash");
		}
		hash.CopyTo(buffer.AsSpan(length, 20));
		length += 20;
	}

	public void AddBlockHash(ReadOnlySpan<byte> block)
	{
		byte[] array = SHA1.HashData(block);
		AddHash(array);
	}

	public byte[] Digest()
	{
		return SHA1.HashData(buffer);
	}

	public void Write(Stream stream)
	{
		stream.Write(buffer, 0, buffer.Length);
	}
}
