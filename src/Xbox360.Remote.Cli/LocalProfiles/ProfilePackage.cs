using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NoDev.Common.IO;
using NoDev.XContent;
using NoDev.Xdbf;
using NoDev.Xdbf.Records;
using NoDev.Xbox360;

using XdbfNamespace = NoDev.Xdbf.Namespace;

namespace Xbox360.Remote.Cli.LocalProfiles;

internal sealed partial class ProfilePackage : IDisposable
{
	public const uint DashboardTitleId = 0xFFFE07D1;

	private readonly StfsPackageReader _reader;
	private readonly string _workingDirectory;
	private readonly Dictionary<string, string> _tempFiles = new(StringComparer.OrdinalIgnoreCase);

	public ProfilePackage(string packagePath)
	{
		_reader = new StfsPackageReader(packagePath);
		_workingDirectory = Path.Combine(Path.GetTempPath(), "xecli-profile-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
		Directory.CreateDirectory(_workingDirectory);
	}

	public string PackagePath => _reader.PackagePath;

	public XContentSignatureType SignatureType => _reader.SignatureType;

	public ulong ProfileId => _reader.ProfileId;

	public string PackageDisplayName => _reader.DisplayName;

	public string PackageDescription => _reader.Description;

	public IReadOnlyList<StfsPackageEntry> Entries => _reader.Entries;

	public bool HasAccount => TryFindFile("Account", out _);

	public bool HasPec => TryFindFile("PEC", out _);

	public bool HasDashboardData => TryFindFile($"{DashboardTitleId:X8}.gpd", out _);

	public IReadOnlyList<uint> TitleDataFiles =>
		Entries
			.Where(static entry => !entry.IsDirectory && entry.Name.EndsWith(".gpd", StringComparison.OrdinalIgnoreCase))
			.Select(static entry => entry.Name[..^4])
			.Where(static value => value.Length == 8 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
			.Select(static value => uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
			.OrderBy(static value => value)
			.ToList();

	public ProfileAccountInfo ReadAccount()
	{
		return ProfileAccountInfo.Parse(_reader.ReadFile("Account"));
	}

	public IReadOnlyList<ProfileTitleInfo> ReadTitles()
	{
		DataFile? dataFile = null;
		try
		{
			dataFile = OpenDataFile($"{DashboardTitleId:X8}.gpd", DataFileOrigin.Profile);
			return dataFile
				.GetRecords(XdbfNamespace.Titles)
				.Select(record => ProfileTitleInfo.Parse(dataFile.GetData(record)))
				.OrderBy(static title => title.TitleId)
				.ToList();
		}
		finally
		{
			dataFile?.Close();
		}
	}

	public IReadOnlyList<ProfileAchievementInfo> ReadAchievements(uint titleId)
	{
		DataFile? dataFile = null;
		try
		{
			dataFile = OpenDataFile($"{titleId:X8}.gpd", DataFileOrigin.Profile);
			return dataFile
				.GetRecords(XdbfNamespace.Achievements)
				.Select(record => ProfileAchievementInfo.Parse(dataFile.GetData(record)))
				.OrderBy(static achievement => achievement.Id)
				.ToList();
		}
		finally
		{
			dataFile?.Close();
		}
	}

	public IReadOnlyList<(ProfileAchievementInfo Achievement, bool PendingSync)> ReadAchievementsWithSync(uint titleId)
	{
		DataFile? dataFile = null;
		try
		{
			dataFile = OpenDataFile($"{titleId:X8}.gpd", DataFileOrigin.Profile);
			return dataFile
				.GetRecords(XdbfNamespace.Achievements)
				.Select(record =>
				{
					ProfileAchievementInfo achievement = ProfileAchievementInfo.Parse(dataFile.GetData(record));
					return (Achievement: achievement, PendingSync: dataFile.IsPendingSync(XdbfNamespace.Achievements, achievement.Id));
				})
				.OrderBy(static item => item.Achievement.Id)
				.ToList();
		}
		finally
		{
			dataFile?.Close();
		}
	}

	public IReadOnlyList<ProfileSettingInfo> ReadSettings()
	{
		DataFile? dataFile = null;
		try
		{
			dataFile = OpenDataFile($"{DashboardTitleId:X8}.gpd", DataFileOrigin.Profile);
			return dataFile
				.GetRecords(XdbfNamespace.Settings)
				.Select(record => ProfileSettingInfo.Parse(dataFile.GetData(record)))
				.OrderBy(static setting => setting.Id)
				.ToList();
		}
		finally
		{
			dataFile?.Close();
		}
	}

	public ProfileSettingInfo ReadSetting(ulong id)
	{
		DataFile? dataFile = null;
		try
		{
			dataFile = OpenDataFile($"{DashboardTitleId:X8}.gpd", DataFileOrigin.Profile);
			return ProfileSettingInfo.Parse(dataFile.GetData(XdbfNamespace.Settings, id));
		}
		finally
		{
			dataFile?.Close();
		}
	}

	public bool IsSettingPendingSync(ulong id)
	{
		DataFile? dataFile = null;
		try
		{
			dataFile = OpenDataFile($"{DashboardTitleId:X8}.gpd", DataFileOrigin.Profile);
			return dataFile.IsPendingSync(XdbfNamespace.Settings, id);
		}
		finally
		{
			dataFile?.Close();
		}
	}

	public (int Written, int Skipped, long BytesWritten) ExtractAll(string outputDirectory, bool overwrite)
	{
		string fullOutputDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
		Directory.CreateDirectory(fullOutputDirectory);

		int written = 0;
		int skipped = 0;
		long bytesWritten = 0;

		foreach (StfsPackageEntry directory in Entries.Where(static entry => entry.IsDirectory))
		{
			Directory.CreateDirectory(Path.Combine(fullOutputDirectory, directory.FullPath.Replace('/', Path.DirectorySeparatorChar)));
		}

		foreach (StfsPackageEntry file in Entries.Where(static entry => !entry.IsDirectory))
		{
			string destination = Path.Combine(fullOutputDirectory, file.FullPath.Replace('/', Path.DirectorySeparatorChar));
			string? parent = Path.GetDirectoryName(destination);
			if (!string.IsNullOrWhiteSpace(parent))
			{
				Directory.CreateDirectory(parent);
			}

			if (!overwrite && File.Exists(destination))
			{
				skipped++;
				continue;
			}

			byte[] data = _reader.ReadFile(file.FullPath);
			File.WriteAllBytes(destination, data);
			written++;
			bytesWritten += data.LongLength;
		}

		return (written, skipped, bytesWritten);
	}

	public bool HasTitleDataFile(uint titleId) => TryFindFile($"{titleId:X8}.gpd", out _);

	public void Dispose()
	{
		_reader.Dispose();
		try
		{
			Directory.Delete(_workingDirectory, recursive: true);
		}
		catch
		{
		}
	}

	private DataFile OpenDataFile(string fileName, DataFileOrigin origin)
	{
		string tempPath = EnsureTempFile(fileName);
		return new DataFile(tempPath, origin);
	}

	private string EnsureTempFile(string fileName)
	{
		if (_tempFiles.TryGetValue(fileName, out string? cached))
		{
			return cached;
		}

		if (!TryFindFile(fileName, out StfsPackageEntry? entry))
		{
			throw new FileNotFoundException("Profile file not found in package: " + fileName);
		}

		string tempPath = Path.Combine(_workingDirectory, entry.FullPath.Replace('/', Path.DirectorySeparatorChar));
		string? parent = Path.GetDirectoryName(tempPath);
		if (!string.IsNullOrWhiteSpace(parent))
		{
			Directory.CreateDirectory(parent);
		}

		File.WriteAllBytes(tempPath, _reader.ReadFile(entry.FullPath));
		_tempFiles[fileName] = tempPath;
		return tempPath;
	}

	private bool TryFindFile(string name, out StfsPackageEntry? entry)
	{
		entry = Entries.FirstOrDefault(candidate =>
			!candidate.IsDirectory &&
			(candidate.FullPath.Equals(name, StringComparison.OrdinalIgnoreCase) ||
			 candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
		return entry != null;
	}
}

internal sealed partial class StfsPackageReader : IDisposable
{
	private const int BlockSize = 0x1000;

	private static readonly uint[] DataBlocksPerHashTreeLevel = { 0xAA, 0x70E4, 0x4AF768 };

	private readonly EndianIO _io;
	private readonly StfsVolumeDescriptor _descriptor;
	private readonly long _backingFileOffset;
	private readonly int _rootHashHierarchy;
	private readonly int _formatShift;
	private readonly uint[] _blockValues;
	private readonly Dictionary<(uint BlockNumber, int Level), StfsHashBlock> _hashBlocks = new();

	private IReadOnlyList<StfsPackageEntry>? _entries;

	public StfsPackageReader(string packagePath)
	{
		PackagePath = Path.GetFullPath(packagePath);

		XContentPackage? package = null;
		try
		{
			package = new XContentPackage(PackagePath);
			if (package.Header.Metadata.VolumeType != XContentVolumeType.STFS)
			{
				throw new InvalidDataException("Only STFS profile packages are supported.");
			}

			package.Header.Metadata.ParseDescriptor();
			if (package.Header.Metadata.Descriptor is not StfsVolumeDescriptor descriptor)
			{
				throw new InvalidDataException("Failed to parse the STFS descriptor.");
			}

			_descriptor = descriptor;
			ProfileId = package.Header.Metadata.Creator;
			DisplayName = package.Header.Metadata.DisplayName;
			Description = package.Header.Metadata.Description;
			SignatureType = package.Header.SignatureType;
		}
		finally
		{
			package?.Close();
		}

		_backingFileOffset = AlignToBlockSize(new FileInfo(PackagePath).Exists ? ReadHeaderSize(PackagePath) : 0);
		_formatShift = _descriptor.ReadOnlyFormat != 0 ? 0 : 1;
		_blockValues = _descriptor.ReadOnlyFormat != 0 ? new uint[] { 0xAB, 0x718F } : new uint[] { 0xAC, 0x723A };
		_rootHashHierarchy = _descriptor.NumberOfTotalBlocks > DataBlocksPerHashTreeLevel[1]
			? 2
			: _descriptor.NumberOfTotalBlocks > DataBlocksPerHashTreeLevel[0]
				? 1
				: 0;

		_io = new EndianIO(PackagePath, EndianType.Big);
	}

	public string PackagePath { get; }

	public ulong ProfileId { get; }

	public string DisplayName { get; }

	public string Description { get; }

	public XContentSignatureType SignatureType { get; }

	public IReadOnlyList<StfsPackageEntry> Entries => _entries ??= LoadEntries();

	public void Dispose()
	{
		_io.Close();
	}

	public byte[] ReadFile(string path)
	{
		StfsPackageEntry entry = FindEntry(path);
		if (entry.IsDirectory)
		{
			throw new IOException("Cannot read directory contents as a file: " + entry.FullPath);
		}

		return ReadBlockChain(entry.FirstBlockNumber, entry.AllocationBlocks, entry.Size, allowSequentialFallback: entry.Contiguous);
	}

	private IReadOnlyList<StfsPackageEntry> LoadEntries()
	{
		byte[] directoryData = ReadBlockChain(
			_descriptor.DirectoryFirstBlockNumber,
			_descriptor.DirectoryAllocationBlocks,
			(long)_descriptor.DirectoryAllocationBlocks * BlockSize,
			allowSequentialFallback: true);

		List<StfsPackageEntry> entries = new();
		for (int offset = 0; offset + 0x40 <= directoryData.Length; offset += 0x40)
		{
			StfsPackageEntry? entry = StfsPackageEntry.Parse(directoryData.AsSpan(offset, 0x40), offset / 0x40);
			if (entry != null)
			{
				entries.Add(entry);
			}
		}

		Dictionary<ushort, StfsPackageEntry> byIndex = entries.ToDictionary(static entry => entry.DirectoryEntryIndex);
		foreach (StfsPackageEntry entry in entries)
		{
			entry.FullPath = BuildFullPath(entry, byIndex);
		}

		return entries
			.OrderBy(static entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private StfsPackageEntry FindEntry(string path)
	{
		string normalizedPath = NormalizePath(path);
		StfsPackageEntry? direct = Entries.FirstOrDefault(entry => entry.FullPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
		if (direct != null)
		{
			return direct;
		}

		StfsPackageEntry? byLeaf = Entries.FirstOrDefault(entry => entry.Name.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
		if (byLeaf != null)
		{
			return byLeaf;
		}

		throw new FileNotFoundException("File not found inside package: " + path);
	}

	private byte[] ReadBlockChain(uint firstBlockNumber, uint allocationBlocks, long requestedLength, bool allowSequentialFallback)
	{
		if (allocationBlocks == 0 || requestedLength <= 0)
		{
			return Array.Empty<byte>();
		}

		if (firstBlockNumber == 0xFFFFFF)
		{
			return Array.Empty<byte>();
		}

		long remaining = requestedLength;
		var output = new MemoryStream((int)Math.Min(requestedLength, allocationBlocks * (long)BlockSize));
		var visited = new HashSet<uint>();

		uint currentBlock = firstBlockNumber;
		for (uint blockIndex = 0; blockIndex < allocationBlocks && remaining > 0; blockIndex++)
		{
			if (!visited.Add(currentBlock))
			{
				throw new InvalidDataException("STFS block chain loop detected at block 0x" + currentBlock.ToString("X6", CultureInfo.InvariantCulture));
			}

			byte[] block = ReadDataBlock(currentBlock);
			int writeLength = (int)Math.Min(remaining, BlockSize);
			output.Write(block, 0, writeLength);
			remaining -= writeLength;

			if (blockIndex + 1 >= allocationBlocks || remaining <= 0)
			{
				break;
			}

			currentBlock = ResolveNextBlockNumber(currentBlock, allowSequentialFallback);
		}

		return output.ToArray();
	}

	private uint ResolveNextBlockNumber(uint currentBlock, bool allowSequentialFallback)
	{
		StfsHashEntry hashEntry = ReadLevelZeroHashEntry(currentBlock);
		uint nextBlock = hashEntry.NextBlockNumber;
		if (nextBlock < _descriptor.NumberOfTotalBlocks)
		{
			return nextBlock;
		}

		if (allowSequentialFallback && currentBlock + 1 < _descriptor.NumberOfTotalBlocks)
		{
			return currentBlock + 1;
		}

		throw new InvalidDataException(
			"Encountered an invalid STFS next-block pointer: 0x" + nextBlock.ToString("X6", CultureInfo.InvariantCulture));
	}

	private byte[] ReadDataBlock(uint blockNumber)
	{
		uint backingBlockNumber = ComputeBackingDataBlockNumber(blockNumber);
		return ReadPhysicalBlock(backingBlockNumber, activeIndex: 0);
	}

	private StfsHashEntry ReadLevelZeroHashEntry(uint blockNumber)
	{
		StfsHashBlock hashBlock = ReadHashBlockForDataBlock(blockNumber, level: 0);
		return hashBlock.GetEntry(blockNumber);
	}

	private StfsHashBlock ReadHashBlockForDataBlock(uint blockNumber, int level)
	{
		uint normalizedBlockNumber = level == 0
			? blockNumber - (blockNumber % DataBlocksPerHashTreeLevel[0])
			: blockNumber - (blockNumber % DataBlocksPerHashTreeLevel[level]);

		if (_hashBlocks.TryGetValue((normalizedBlockNumber, level), out StfsHashBlock? cached))
		{
			return cached;
		}

		uint activeIndex = GetHashBlockActiveIndex(blockNumber, level);
		uint backingHashBlock = ComputeLevelNBackingHashBlockNumber(normalizedBlockNumber, level);
		byte[] data = ReadPhysicalBlock(backingHashBlock, activeIndex);
		var hashBlock = new StfsHashBlock(data);
		_hashBlocks[(normalizedBlockNumber, level)] = hashBlock;
		return hashBlock;
	}

	private uint GetHashBlockActiveIndex(uint blockNumber, int level)
	{
		if (level == _rootHashHierarchy)
		{
			return _descriptor.RootActiveIndex;
		}

		StfsHashBlock parent = ReadHashBlockForDataBlock(blockNumber, level + 1);
		uint parentIndex = blockNumber / DataBlocksPerHashTreeLevel[level];
		return parent.GetEntry(parentIndex).ActiveIndex;
	}

	private byte[] ReadPhysicalBlock(uint blockNumber, uint activeIndex)
	{
		long offset = _backingFileOffset + ((long)(blockNumber + activeIndex) << 12);
		_io.Position = offset;
		return _io.ReadByteArray(BlockSize);
	}

	private uint ComputeBackingDataBlockNumber(uint blockNumber)
	{
		uint translated = (((blockNumber + DataBlocksPerHashTreeLevel[0]) / DataBlocksPerHashTreeLevel[0]) << _formatShift) + blockNumber;
		if (blockNumber < DataBlocksPerHashTreeLevel[0])
		{
			return translated;
		}

		translated = (((blockNumber + DataBlocksPerHashTreeLevel[1]) / DataBlocksPerHashTreeLevel[1]) << _formatShift) + translated;
		if (blockNumber < DataBlocksPerHashTreeLevel[1])
		{
			return translated;
		}

		return translated + (uint)(1 << _formatShift);
	}

	private uint ComputeLevelNBackingHashBlockNumber(uint blockNumber, int level)
	{
		uint step = (uint)(1 << _formatShift);
		return level switch
		{
			0 => ComputeLevelZeroBackingHashBlockNumber(blockNumber, step),
			1 => ComputeLevelOneBackingHashBlockNumber(blockNumber, step),
			2 => _blockValues[1],
			_ => 0xFFFFFF
		};
	}

	private uint ComputeLevelZeroBackingHashBlockNumber(uint blockNumber, uint step)
	{
		uint treeIndex = blockNumber / DataBlocksPerHashTreeLevel[0];
		uint translated = treeIndex * _blockValues[0];
		if (treeIndex == 0)
		{
			return translated;
		}

		uint upperTreeIndex = blockNumber / DataBlocksPerHashTreeLevel[1];
		translated += (upperTreeIndex + 1) * step;
		if (upperTreeIndex == 0)
		{
			return translated;
		}

		return translated + step;
	}

	private uint ComputeLevelOneBackingHashBlockNumber(uint blockNumber, uint step)
	{
		uint treeIndex = blockNumber / DataBlocksPerHashTreeLevel[1];
		uint translated = treeIndex * _blockValues[1];
		return treeIndex == 0 ? translated + _blockValues[0] : translated + step;
	}

	private static long AlignToBlockSize(int value) => (value + 0xFFFL) & ~0xFFFL;

	private static int ReadHeaderSize(string packagePath)
	{
		var io = new EndianIO(packagePath, EndianType.Big);
		try
		{
			io.Position = 0;
			var header = new XContentHeader(io);
			return header.SizeOfHeaders;
		}
		finally
		{
			io.Close();
		}
	}

	private static string BuildFullPath(StfsPackageEntry entry, IReadOnlyDictionary<ushort, StfsPackageEntry> byIndex)
	{
		var parts = new Stack<string>();
		StfsPackageEntry? current = entry;
		var visited = new HashSet<ushort>();
		while (current != null && visited.Add(current.DirectoryEntryIndex))
		{
			parts.Push(current.Name);
			if (current.ParentDirectoryIndex == ushort.MaxValue || !byIndex.TryGetValue(current.ParentDirectoryIndex, out current))
			{
				break;
			}
		}

		return string.Join("/", parts);
	}

	private static string NormalizePath(string path) =>
		path.Trim().TrimStart('\\', '/').Replace('\\', '/');
}

internal sealed partial class StfsPackageEntry
{
	public required ushort DirectoryEntryIndex { get; init; }

	public required ushort ParentDirectoryIndex { get; init; }

	public required string Name { get; init; }

	public required bool IsDirectory { get; init; }

	public required bool Contiguous { get; init; }

	public required uint FirstBlockNumber { get; init; }

	public required uint AllocationBlocks { get; init; }

	public required uint ValidDataBlocks { get; set; }

	public required uint Size { get; set; }

	public string FullPath { get; set; } = string.Empty;

	public byte[] ToDirectoryEntryBytes()
	{
		var buffer = new byte[0x40];
		var io = new EndianIO(buffer, EndianType.Big);
		try
		{
			string safeName = Name.Length > 40 ? Name[..40] : Name;
			io.WriteAsciiString(safeName, 40);

			int nameLength = Math.Min(safeName.Length, 0x3F);
			byte attributes = (byte)nameLength;
			if (Contiguous)
			{
				attributes |= 0x40;
			}

			if (IsDirectory)
			{
				attributes |= 0x80;
			}

			io.Write(attributes);
			io.Endianness = EndianType.Little;
			io.WriteUInt24(ValidDataBlocks);
			io.WriteUInt24(AllocationBlocks);
			io.WriteUInt24(FirstBlockNumber);
			io.Endianness = EndianType.Big;
			io.Write(ParentDirectoryIndex);
			io.Write(Size);
			return buffer;
		}
		finally
		{
			io.Close();
		}
	}

	public static StfsPackageEntry? Parse(ReadOnlySpan<byte> data, int index)
	{
		var io = new EndianIO(data.ToArray(), EndianType.Big);
		try
		{
			string name = io.ReadAsciiString(40);
			byte attributes = io.ReadByte();
			byte nameLength = (byte)(attributes & 0x3F);
			if (nameLength == 0)
			{
				return null;
			}

			bool contiguous = ((attributes >> 6) & 1) != 0;
			bool isDirectory = ((attributes >> 7) & 1) != 0;

			io.Endianness = EndianType.Little;
			uint validDataBlocks = io.ReadUInt24();
			uint allocationBlocks = io.ReadUInt24();
			uint firstBlockNumber = io.ReadUInt24();
			io.Endianness = EndianType.Big;

			ushort parentDirectoryIndex = io.ReadUInt16();
			uint size = io.ReadUInt32();

			return new StfsPackageEntry
			{
				DirectoryEntryIndex = checked((ushort)index),
				ParentDirectoryIndex = parentDirectoryIndex,
				Name = name.Length > nameLength ? name[..nameLength] : name,
				IsDirectory = isDirectory,
				Contiguous = contiguous,
				FirstBlockNumber = firstBlockNumber,
				AllocationBlocks = allocationBlocks,
				ValidDataBlocks = validDataBlocks,
				Size = isDirectory ? 0 : size
			};
		}
		finally
		{
			io.Close();
		}
	}
}

internal sealed class StfsHashBlock
{
	private const int HashBlockSize = 0x1000;

	private const int EntrySize = 0x18;

	private readonly byte[] _data;

	public StfsHashBlock(byte[] data)
	{
		if (data.Length < HashBlockSize)
		{
			throw new InvalidDataException("Invalid STFS hash block size.");
		}

		_data = data;
	}

	public StfsHashEntry GetEntry(uint index)
	{
		int offset = checked((int)(index % 0xAA) * EntrySize);
		var io = new EndianIO(_data, EndianType.Big);
		try
		{
			io.Position = offset;
			byte[] hash = io.ReadByteArray(20);
			uint levelValue = io.ReadUInt32();
			return new StfsHashEntry(hash, levelValue);
		}
		finally
		{
			io.Close();
		}
	}
}

internal readonly record struct StfsHashEntry(byte[] Hash, uint LevelValue)
{
	public uint NextBlockNumber => LevelValue & 0x00FFFFFF;

	public uint ActiveIndex => (LevelValue >> 30) & 1;
}
