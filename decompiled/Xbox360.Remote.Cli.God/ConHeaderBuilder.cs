using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Xbox360.Remote.Cli.God;

internal sealed class ConHeaderBuilder
{
	private static readonly Lazy<byte[]> Template = new Lazy<byte[]>(LoadTemplate);

	private readonly byte[] buffer;

	public ConHeaderBuilder()
	{
		buffer = Template.Value.ToArray();
	}

	private static byte[] LoadTemplate()
	{
		return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "empty_live.bin"));
	}

	public ConHeaderBuilder WithBlockCounts(uint blocksAllocated, ushort blocksNotAllocated)
	{
		WriteUInt24BE(914, blocksAllocated);
		Endian.WriteUInt16BE(buffer, 917, blocksNotAllocated);
		return this;
	}

	public ConHeaderBuilder WithContentType(ContentType contentType)
	{
		Endian.WriteUInt32BE(buffer, 836, (uint)contentType);
		return this;
	}

	public ConHeaderBuilder WithDataPartsInfo(uint partCount, ulong partsTotalSize)
	{
		Endian.WriteUInt32LE(buffer, 928, partCount);
		Endian.WriteUInt32BE(buffer, 932, (uint)(partsTotalSize / 256));
		return this;
	}

	public ConHeaderBuilder WithExecutionInfo(TitleExecutionInfo info)
	{
		Endian.WriteUInt32BE(buffer, 852, info.MediaId);
		Endian.WriteUInt32BE(buffer, 864, info.TitleId);
		buffer[868] = info.Platform;
		buffer[869] = info.ExecutableType;
		buffer[870] = info.DiscNumber;
		buffer[871] = info.DiscCount;
		return this;
	}

	public ConHeaderBuilder WithGameTitle(string title)
	{
		WriteUtf16BE(1041, title);
		WriteUtf16BE(5777, title);
		return this;
	}

	public ConHeaderBuilder WithMhtHash(byte[] mhtHash)
	{
		if (mhtHash.Length != 20)
		{
			throw new ArgumentException("MHT hash must be 20 bytes.", "mhtHash");
		}
		Buffer.BlockCopy(mhtHash, 0, buffer, 893, 20);
		return this;
	}

	public byte[] FinalizeHeader()
	{
		buffer[859] = 0;
		buffer[863] = 0;
		buffer[913] = 0;
		byte[] array = SHA1.HashData(buffer.AsSpan(836, 44220));
		Buffer.BlockCopy(array, 0, buffer, 812, array.Length);
		return buffer;
	}

	private void WriteUInt24BE(int offset, uint value)
	{
		buffer[offset] = (byte)((value >> 16) & 0xFF);
		buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
		buffer[offset + 2] = (byte)(value & 0xFF);
	}

	private void WriteUtf16BE(int offset, string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			byte[] bytes = Encoding.BigEndianUnicode.GetBytes(text + "\0");
			Buffer.BlockCopy(bytes, 0, buffer, offset, Math.Min(bytes.Length, buffer.Length - offset));
		}
	}
}
