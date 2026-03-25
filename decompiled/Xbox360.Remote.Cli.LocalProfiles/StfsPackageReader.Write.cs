using System;
using System.Collections.Generic;
using System.IO;
using NoDev.XContent;
using NoDev.Xbox360;

namespace Xbox360.Remote.Cli.LocalProfiles;

internal sealed partial class StfsPackageReader
{
	public void WriteFile(string path, byte[] data)
	{
		StfsPackageEntry entry = FindEntry(path);
		if (entry.IsDirectory)
		{
			throw new IOException("Cannot overwrite a directory entry: " + entry.FullPath);
		}

		uint requiredBlocks = data.Length == 0 ? 0u : checked((uint)((data.Length + BlockSize - 1) / BlockSize));
		if (requiredBlocks > entry.AllocationBlocks)
		{
			throw new InvalidOperationException(
				"Updated file grew beyond the current STFS allocation for " + entry.FullPath + ". " +
				"Required blocks: " + requiredBlocks + ", allocated blocks: " + entry.AllocationBlocks + ".");
		}

		IReadOnlyList<uint> blockChain = EnumerateBlockChain(entry.FirstBlockNumber, entry.AllocationBlocks, entry.Contiguous);
		WriteBlockChain(blockChain, data);

		entry.Size = checked((uint)data.Length);
		entry.ValidDataBlocks = requiredBlocks;
		WriteDirectoryEntry(entry);

		_hashBlocks.Clear();
	}

	public void SavePackageHeader()
	{
		_io.Position = 0;
		XContentHeader header = new(_io);
		header.Metadata.ParseDescriptor();
		if (header.Metadata.Descriptor is not StfsVolumeDescriptor descriptor)
		{
			throw new InvalidOperationException("Failed to reload the package STFS descriptor.");
		}

		descriptor.RootActiveIndex = _descriptor.RootActiveIndex;
		descriptor.DirectoryAllocationBlocks = _descriptor.DirectoryAllocationBlocks;
		descriptor.DirectoryFirstBlockNumber = _descriptor.DirectoryFirstBlockNumber;
		descriptor.RootHash = (byte[])_descriptor.RootHash.Clone();
		descriptor.NumberOfTotalBlocks = _descriptor.NumberOfTotalBlocks;
		descriptor.NumberOfFreeBlocks = _descriptor.NumberOfFreeBlocks;

		_io.Position = 0;
		_io.Write(header.ToArray());

		_io.Position = 0x344;
		int headerHashLength = (int)_backingFileOffset - 0x344;
		byte[] headerHash = XeCrypt.XeCryptSha(_io.ReadByteArray(headerHashLength));
		_io.Position = 0x32C;
		_io.Write(headerHash);

		if (header.SignatureType == XContentSignatureType.Console)
		{
			_io.Position = 0;
			_io.Write((int)XContentSignatureType.Console);
			_io.Write(KeyStorage.PublicKey);
			_io.Position = 0x22C;
			byte[] signature = XeCrypt.FormatSignature(KeyStorage.PrivateKeys, _io.ReadByteArray(0x118));
			_io.Position = 0x1AC;
			_io.Write(signature);
		}

		_io.Flush();
	}

	private void WriteDirectoryEntry(StfsPackageEntry entry)
	{
		byte[] directoryData = ReadBlockChain(
			_descriptor.DirectoryFirstBlockNumber,
			_descriptor.DirectoryAllocationBlocks,
			(long)_descriptor.DirectoryAllocationBlocks * BlockSize,
			allowSequentialFallback: true);

		int offset = checked(entry.DirectoryEntryIndex * 0x40);
		byte[] directoryEntryBytes = entry.ToDirectoryEntryBytes();
		Buffer.BlockCopy(directoryEntryBytes, 0, directoryData, offset, directoryEntryBytes.Length);

		IReadOnlyList<uint> directoryChain = EnumerateBlockChain(
			_descriptor.DirectoryFirstBlockNumber,
			_descriptor.DirectoryAllocationBlocks,
			allowSequentialFallback: true);
		WriteBlockChain(directoryChain, directoryData);
	}

	private void WriteBlockChain(IReadOnlyList<uint> blockChain, byte[] data)
	{
		int sourceOffset = 0;
		for (int index = 0; index < blockChain.Count; index++)
		{
			byte[] block = new byte[BlockSize];
			int bytesRemaining = data.Length - sourceOffset;
			if (bytesRemaining > 0)
			{
				int count = Math.Min(BlockSize, bytesRemaining);
				Buffer.BlockCopy(data, sourceOffset, block, 0, count);
				sourceOffset += count;
			}

			WriteDataBlock(blockChain[index], block);
		}
	}

	private IReadOnlyList<uint> EnumerateBlockChain(uint firstBlockNumber, uint allocationBlocks, bool allowSequentialFallback)
	{
		if (allocationBlocks == 0 || firstBlockNumber == 0xFFFFFF)
		{
			return Array.Empty<uint>();
		}

		List<uint> blockNumbers = new(checked((int)allocationBlocks));
		var visited = new HashSet<uint>();
		uint currentBlock = firstBlockNumber;
		for (uint blockIndex = 0; blockIndex < allocationBlocks; blockIndex++)
		{
			if (!visited.Add(currentBlock))
			{
				throw new InvalidDataException("STFS block chain loop detected at block 0x" + currentBlock.ToString("X6") + ".");
			}

			blockNumbers.Add(currentBlock);
			if (blockIndex + 1 >= allocationBlocks)
			{
				break;
			}

			currentBlock = ResolveNextBlockNumber(currentBlock, allowSequentialFallback);
		}

		return blockNumbers;
	}

	private void WriteDataBlock(uint dataBlockNumber, byte[] block)
	{
		if (block.Length != BlockSize)
		{
			throw new InvalidOperationException("STFS data blocks must be 4096 bytes.");
		}

		uint backingBlockNumber = ComputeBackingDataBlockNumber(dataBlockNumber);
		WritePhysicalBlock(backingBlockNumber, activeIndex: 0, block);
		UpdateHashForDataBlock(dataBlockNumber, XeCrypt.XeCryptSha(block));
	}

	private void UpdateHashForDataBlock(uint dataBlockNumber, byte[] hash)
	{
		byte[] hashBlockData = LoadHashBlockData(dataBlockNumber, level: 0);
		int entryOffset = checked((int)(dataBlockNumber % 0xAA) * 0x18);
		Buffer.BlockCopy(hash, 0, hashBlockData, entryOffset, hash.Length);
		WriteHashBlockAndPropagate(dataBlockNumber, level: 0, hashBlockData);
	}

	private void WriteHashBlockAndPropagate(uint dataBlockNumber, int level, byte[] hashBlockData)
	{
		uint normalizedBlockNumber = NormalizeBlockNumber(dataBlockNumber, level);
		uint activeIndex = GetHashBlockActiveIndex(dataBlockNumber, level);
		uint backingHashBlock = ComputeLevelNBackingHashBlockNumber(normalizedBlockNumber, level);

		WritePhysicalBlock(backingHashBlock, activeIndex, hashBlockData);
		byte[] hash = XeCrypt.XeCryptSha(hashBlockData);
		if (level == _rootHashHierarchy)
		{
			_descriptor.RootHash = hash;
			return;
		}

		uint parentEntryIndex = dataBlockNumber / DataBlocksPerHashTreeLevel[level];
		byte[] parentHashBlockData = LoadHashBlockData(dataBlockNumber, level + 1);
		int parentOffset = checked((int)(parentEntryIndex % 0xAA) * 0x18);
		Buffer.BlockCopy(hash, 0, parentHashBlockData, parentOffset, hash.Length);
		WriteHashBlockAndPropagate(dataBlockNumber, level + 1, parentHashBlockData);
	}

	private byte[] LoadHashBlockData(uint dataBlockNumber, int level)
	{
		uint normalizedBlockNumber = NormalizeBlockNumber(dataBlockNumber, level);
		uint activeIndex = GetHashBlockActiveIndex(dataBlockNumber, level);
		uint backingHashBlock = ComputeLevelNBackingHashBlockNumber(normalizedBlockNumber, level);
		return ReadPhysicalBlock(backingHashBlock, activeIndex);
	}

	private void WritePhysicalBlock(uint blockNumber, uint activeIndex, byte[] data)
	{
		long offset = _backingFileOffset + ((long)(blockNumber + activeIndex) << 12);
		_io.Position = offset;
		_io.Write(data, 0, data.Length);
		_io.Flush();
	}

	private static uint NormalizeBlockNumber(uint dataBlockNumber, int level)
	{
		if (level == 0)
		{
			return dataBlockNumber - (dataBlockNumber % DataBlocksPerHashTreeLevel[0]);
		}

		return dataBlockNumber - (dataBlockNumber % DataBlocksPerHashTreeLevel[level]);
	}
}
