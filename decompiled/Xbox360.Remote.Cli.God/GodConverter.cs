using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Xbox360.Remote.Cli.God;

internal static class GodConverter
{
	public static GodConversionResult Convert(string sourceIso, string destDir, GodConvertOptions options, Action<int, int>? onPartComplete, CancellationToken cancellationToken = default(CancellationToken))
	{
		using FileStream fileStream = new FileStream(sourceIso, FileMode.Open, FileAccess.Read, FileShare.Read);
		IsoReader iso = IsoReader.Read(fileStream);
		TitleInfo titleInfo = TitleInfo.FromImage(iso);
		ulong num = ((options.TrimMode == TrimMode.FromEnd) ? iso.GetMaxUsedPrefixSize() : ((ulong)fileStream.Length - iso.Volume.RootOffset));
		if (num == 0L)
		{
			throw new InvalidDataException("ISO contains no readable data.");
		}
		ulong num2 = (num + 4096 - 1) / 4096;
		ulong num3 = (num2 + 41412 - 1) / 41412;
		if (num3 == 0L)
		{
			num3 = 1uL;
		}
		GodFileLayout layout = new GodFileLayout(destDir, titleInfo.ExecutionInfo, titleInfo.ContentType);
		EnsureEmptyDir(layout.DataDirPath());
		int partCountInt = checked((int)num3);
		int completed = 0;
		ParallelOptions parallelOptions = new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(1, options.Threads),
			CancellationToken = cancellationToken
		};
		Parallel.ForEach(Enumerable.Range(0, partCountInt), parallelOptions, delegate(int partIndex)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using FileStream fileStream2 = new FileStream(sourceIso, FileMode.Open, FileAccess.Read, FileShare.Read);
			fileStream2.Seek((long)iso.Volume.RootOffset, SeekOrigin.Begin);
			using FileStream partFile = new FileStream(layout.PartFilePath(partIndex), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
			WritePart(fileStream2, partIndex, partFile, cancellationToken);
			int arg = Interlocked.Increment(ref completed);
			onPartComplete?.Invoke(arg, partCountInt);
		});
		cancellationToken.ThrowIfCancellationRequested();
		HashList hashList = ReadPartMht(layout, partCountInt - 1);
		for (int num4 = partCountInt - 2; num4 >= 0; num4--)
		{
			cancellationToken.ThrowIfCancellationRequested();
			HashList hashList2 = ReadPartMht(layout, num4);
			hashList2.AddHash(hashList.Digest());
			WritePartMht(layout, num4, hashList2);
			hashList = hashList2;
		}
		ulong partsTotalSize = (ulong)(new FileInfo(layout.PartFilePath(partCountInt - 1)).Length + (long)(partCountInt - 1) * 4096L * 41616);
		string text = options.GameTitle;
		if (string.IsNullOrWhiteSpace(text) && TitleIdDatabase.Instance.TryResolve(titleInfo.ExecutionInfo.TitleId, null, out TitleIdEntry entry))
		{
			text = entry?.Name;
		}
		cancellationToken.ThrowIfCancellationRequested();
		ConHeaderBuilder conHeaderBuilder = new ConHeaderBuilder().WithExecutionInfo(titleInfo.ExecutionInfo).WithBlockCounts((uint)num2, 0).WithDataPartsInfo((uint)partCountInt, partsTotalSize)
			.WithContentType(titleInfo.ContentType)
			.WithMhtHash(hashList.Digest());
		if (!string.IsNullOrWhiteSpace(text))
		{
			conHeaderBuilder = conHeaderBuilder.WithGameTitle(text);
		}
		byte[] bytes = conHeaderBuilder.FinalizeHeader();
		string text2 = layout.ConHeaderFilePath();
		Directory.CreateDirectory(Path.GetDirectoryName(text2) ?? destDir);
		File.WriteAllBytes(text2, bytes);
		return new GodConversionResult(titleInfo.ExecutionInfo, titleInfo.ContentType, num, num2, num3, layout.DataDirPath(), text2);
	}

	private static void EnsureEmptyDir(string path)
	{
		if (Directory.Exists(path))
		{
			Directory.Delete(path, recursive: true);
		}
		Directory.CreateDirectory(path);
	}

	private static void WritePart(Stream dataVolume, int partIndex, Stream partFile, CancellationToken cancellationToken)
	{
		long num = (long)partIndex * 41412L * 4096;
		if (num > 0)
		{
			dataVolume.Seek(num, SeekOrigin.Current);
		}
		HashList hashList = new HashList();
		long position = partFile.Position;
		hashList.Write(partFile);
		byte[] array = new byte[835584];
		for (int i = 0; (long)i < 203L; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int num2 = ReadUpTo(dataVolume, array, cancellationToken);
			if (num2 == 0)
			{
				break;
			}
			HashList hashList2 = new HashList();
			int num3;
			for (int j = 0; j < num2; j += num3)
			{
				num3 = (int)Math.Min(4096uL, (ulong)(num2 - j));
				hashList2.AddBlockHash(array.AsSpan(j, num3));
			}
			hashList2.Write(partFile);
			hashList.AddBlockHash(hashList2.Bytes);
			partFile.Write(array, 0, num2);
			if (num2 < array.Length)
			{
				break;
			}
		}
		partFile.Seek(position, SeekOrigin.Begin);
		hashList.Write(partFile);
	}

	private static int ReadUpTo(Stream stream, byte[] buffer, CancellationToken cancellationToken)
	{
		int i;
		int num;
		for (i = 0; i < buffer.Length; i += num)
		{
			cancellationToken.ThrowIfCancellationRequested();
			num = stream.Read(buffer, i, buffer.Length - i);
			if (num == 0)
			{
				break;
			}
		}
		return i;
	}

	private static HashList ReadPartMht(GodFileLayout layout, int partIndex)
	{
		using FileStream stream = new FileStream(layout.PartFilePath(partIndex), FileMode.Open, FileAccess.Read, FileShare.Read);
		return HashList.Read(stream);
	}

	private static void WritePartMht(GodFileLayout layout, int partIndex, HashList mht)
	{
		using FileStream stream = new FileStream(layout.PartFilePath(partIndex), FileMode.Open, FileAccess.Write, FileShare.None);
		mht.Write(stream);
	}
}
