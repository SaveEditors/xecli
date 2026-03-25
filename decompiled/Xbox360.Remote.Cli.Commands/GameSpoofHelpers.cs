using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xbox360.Remote.Cli.Commands;

internal static class GameSpoofHelpers
{
	internal sealed record RemoteSpoofProfile(int ClientCount, uint PlayerBaseAddress, uint PlayerStride, uint DisplayNameOffset, uint? MirrorNameOffset = null, int StartSlotIndex = 0, uint? XuidBinaryOffset = null, uint? XuidTextOffset = null);

	internal sealed record SupportedGameSpoofProfile(uint TitleId, string Slug, string Name, uint NameAddress, uint XuidBinaryAddress, uint XuidTextAddress, uint? SecondaryNameAddress = null, uint? PatchAddress = null, byte[]? PatchBytes = null, RemoteSpoofProfile? Remote = null);

	private const uint BlackOps2TitleId = 1096157379u;

	private const uint BlackOps2UnicodeNameAddress = 2175413724u;

	private const uint BlackOps2StubPrimaryAddress = 2176229248u;

	private const uint BlackOps2StubSecondaryAddress = 2171457600u;

	private const uint BlackOps2ScratchBase = 2175232648u;

	private const uint BlackOps2HookEntryAddress = 2186898816u;

	private const uint BlackOps2HookSecondaryAddress = 2190726804u;

	private const uint BlackOps2HookOptionalAddress = 2185354000u;

	private const uint BlackOps2ExportOrdinalKrnl = 315u;

	private const uint BlackOps2ExportOrdinalXamPrimary = 1128u;

	private const uint BlackOps2ExportOrdinalXamSecondary = 1080u;

	private const uint BlackOps2ScratchObject5Address = 2175232904u;

	private const uint BlackOps2ScratchObject6Address = 2175233160u;

	private const uint BlackOps2ScratchObject7Address = 2175233416u;

	private const uint BlackOps2ScratchHookAddress = 2175233672u;

	private const uint BlackOps2ScratchStateAddress = 2175233928u;

	private const uint BlackOps2AccountBlockSearchStart = 2205155328u;

	private const uint BlackOps2AccountBlockSearchEnd = 2214592512u;

	private const int BlackOps2AccountBlockChunkSize = 4096;

	private const int BlackOps2AccountBlockXuidOffset = 20;

	private const int BlackOps2AccountBlockGamertagOffset = 28;

	private const int BlackOps2AccountBlockGamertagFieldLength = 32;

	private const int BlackOps2AccountBlockXuidFieldLength = 8;

	private const int BlackOps2HookStubLength = 16;

	private const int BlackOps2TrampolineLength = 32;

	private const int BlackOps2SnapshotLength = 16;

	private const int BlackOps2PrimaryTrampolineOriginalOffset = 12;

	private const int BlackOps2ScratchClearLength = 1536;

	private static readonly byte[] BlackOps2RefreshPatchBytes = new byte[4] { 96, 0, 0, 0 };

	private static readonly byte[] BlackOps2AccountObject5Bytes = new byte[56]
	{
		144, 97, 0, 20, 144, 129, 0, 28, 144, 161,
		0, 36, 61, 96, 222, 173, 97, 107, 190, 239,
		145, 97, 255, 240, 129, 97, 255, 240, 129, 107,
		0, 16, 233, 107, 0, 0, 129, 65, 0, 36,
		249, 106, 0, 0, 56, 96, 0, 0, 78, 128,
		0, 32, 0, 0, 0, 0
	};

	private static readonly byte[] BlackOps2AccountObject6Bytes = new byte[96]
	{
		125, 136, 2, 166, 145, 129, 255, 248, 148, 33,
		255, 160, 248, 97, 0, 112, 248, 129, 0, 120,
		61, 96, 222, 173, 97, 107, 190, 239, 145, 97,
		0, 84, 129, 97, 0, 84, 129, 107, 0, 12,
		145, 97, 0, 80, 232, 129, 0, 120, 232, 97,
		0, 112, 129, 97, 0, 80, 125, 105, 3, 166,
		78, 128, 4, 33, 129, 97, 0, 84, 129, 107,
		0, 16, 232, 107, 0, 0, 56, 33, 0, 96,
		129, 129, 255, 248, 125, 136, 3, 166, 78, 128,
		0, 32, 0, 0, 0, 0
	};

	private static readonly byte[] BlackOps2PrimaryStubBytes = new byte[124]
	{
		125, 136, 2, 166, 145, 129, 255, 248, 148, 33,
		255, 160, 61, 96, 130, 89, 97, 107, 182, 160,
		124, 12, 88, 0, 64, 130, 0, 48, 61, 128,
		129, 182, 97, 140, 158, 148, 57, 64, 0, 32,
		125, 73, 3, 166, 57, 64, 0, 0, 124, 202,
		96, 174, 124, 202, 33, 174, 57, 74, 0, 1,
		66, 0, 255, 244, 56, 96, 0, 0, 72, 0,
		0, 8, 72, 0, 0, 21, 56, 33, 0, 96,
		129, 129, 255, 248, 125, 136, 3, 166, 78, 128,
		0, 32, 61, 128, 129, 109, 97, 140, 208, 80,
		125, 137, 3, 166, 125, 136, 2, 166, 75, 188,
		51, 217, 148, 33, 255, 80, 61, 96, 129, 170,
		78, 128, 4, 32
	};

	private static readonly byte[] BlackOps2SecondaryStubBytes = new byte[16]
	{
		61, 96, 129, 182, 97, 107, 159, 128, 125, 105,
		3, 166, 78, 128, 4, 32
	};

	private static readonly byte[] BlackOps2ExpectedPrimaryHookBytes = new byte[16]
	{
		125, 136, 2, 166, 145, 129, 255, 248, 251, 193,
		255, 232, 251, 225, 255, 240
	};

	private static readonly SupportedGameSpoofProfile[] Profiles;

	internal static IEnumerable<SupportedGameSpoofProfile> GetProfiles()
	{
		return Profiles;
	}

	internal static SupportedGameSpoofProfile? TryGet(uint titleId)
	{
		return Profiles.FirstOrDefault((SupportedGameSpoofProfile profile) => profile.TitleId == titleId);
	}

	internal static async Task<uint> GetCurrentTitleIdAsync(XbdmClient client, CancellationToken cancellationToken)
	{
		return await new Jrpc2Client(client).GetTitleIdAsync(cancellationToken);
	}

	internal static async Task<GameSpoofState> ReadStateAsync(XbdmClient client, SupportedGameSpoofProfile profile, CancellationToken cancellationToken)
	{
		byte[] nameBytes = await client.ReadMemoryBytesReliableAsync(profile.NameAddress, 32, cancellationToken);
		byte[] xuidBytes = await client.ReadMemoryBytesReliableAsync(profile.XuidBinaryAddress, 8, cancellationToken);
		byte[] xuidTextBytes = await client.ReadMemoryBytesReliableAsync(profile.XuidTextAddress, 32, cancellationToken);
		byte[] array = ((!profile.SecondaryNameAddress.HasValue) ? null : (await client.ReadMemoryBytesReliableAsync(profile.SecondaryNameAddress.Value, 32, cancellationToken)));
		byte[] array2 = array;
		return new GameSpoofState(ReadAsciiZ(nameBytes), ToCanonicalXuidHex(xuidBytes), Convert.ToHexString(xuidBytes), ReadAsciiZ(xuidTextBytes), (array2 == null) ? null : ReadAsciiLoose(array2));
	}

	internal static string GetIdentityCacheKey(string targetKey, uint titleId)
	{
		return $"{NormalizeTargetKey(targetKey)}|0x{titleId:X8}";
	}

	internal static void StoreOriginalIdentityIfMissing(string targetKey, uint titleId, GameSpoofState state)
	{
		CliConfig cliConfig = CliConfig.Load();
		CliConfig cliConfig2 = cliConfig;
		if (cliConfig2.SpoofIdentityCache == null)
		{
			Dictionary<string, CliConfig.SpoofIdentityCacheInfo> dictionary = (cliConfig2.SpoofIdentityCache = new Dictionary<string, CliConfig.SpoofIdentityCacheInfo>(StringComparer.OrdinalIgnoreCase));
		}
		string identityCacheKey = GetIdentityCacheKey(targetKey, titleId);
		if (!cliConfig.SpoofIdentityCache.ContainsKey(identityCacheKey))
		{
			cliConfig.SpoofIdentityCache[identityCacheKey] = new CliConfig.SpoofIdentityCacheInfo
			{
				TargetKey = NormalizeTargetKey(targetKey),
				TitleId = titleId,
				Gamertag = state.Gamertag,
				Xuid = state.CanonicalXuidHex,
				CapturedUtc = DateTimeOffset.UtcNow
			};
			cliConfig.Save();
		}
	}

	internal static CliConfig.SpoofIdentityCacheInfo? TryGetCachedIdentity(string targetKey, uint titleId)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (cliConfig.SpoofIdentityCache == null || cliConfig.SpoofIdentityCache.Count == 0)
		{
			return null;
		}
		string identityCacheKey = GetIdentityCacheKey(targetKey, titleId);
		if (!cliConfig.SpoofIdentityCache.TryGetValue(identityCacheKey, out CliConfig.SpoofIdentityCacheInfo value))
		{
			return null;
		}
		return value;
	}

	internal static async Task<GameSpoofApplyResult> ApplyIdentityAsync(XbdmClient client, SupportedGameSpoofProfile profile, string gamertag, string xuidHex, CancellationToken cancellationToken)
	{
		if (profile.TitleId == 1096157379)
		{
			return await ApplyBlackOps2IdentityAsync(client, profile, gamertag, xuidHex, cancellationToken);
		}
		byte[] gamertagBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
		string canonicalXuid = NormalizeXuid(xuidHex);
		byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
		byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");
		await client.WriteMemoryAsync(profile.NameAddress, new byte[64], cancellationToken);
		await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[32], cancellationToken);
		await client.WriteMemoryAsync(profile.NameAddress, gamertagBytes, cancellationToken);
		await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
		await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);
		if (profile.SecondaryNameAddress.HasValue)
		{
			await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[64], cancellationToken);
			await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, gamertagBytes, cancellationToken);
		}
		if (profile.PatchAddress.HasValue)
		{
			byte[] patchBytes = profile.PatchBytes;
			if (patchBytes != null && patchBytes.Length > 0)
			{
				await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
			}
		}
		GameSpoofState gameSpoofState = await ReadStateAsync(client, profile, cancellationToken);
		return new GameSpoofApplyResult(gameSpoofState, string.Equals(gameSpoofState.Gamertag, gamertag, StringComparison.Ordinal) && string.Equals(gameSpoofState.CanonicalXuidHex, canonicalXuid, StringComparison.OrdinalIgnoreCase), profile.PatchAddress.HasValue);
	}

	private static async Task<GameSpoofApplyResult> ApplyBlackOps2IdentityAsync(XbdmClient client, SupportedGameSpoofProfile profile, string gamertag, string xuidHex, CancellationToken cancellationToken)
	{
		byte[] gamertagBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
		byte[] unicodeGamertagBytes = ToWideAsciiBytes(gamertag);
		string canonicalXuid = NormalizeXuid(xuidHex);
		byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
		byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");
		GameSpoofState preState = await ReadStateAsync(client, profile, cancellationToken);
		await client.WriteMemoryAsync(2176229248u, BlackOps2PrimaryStubBytes, cancellationToken);
		await client.WriteMemoryAsync(2171457600u, BlackOps2SecondaryStubBytes, cancellationToken);
		await StageBlackOps2AccountLayerAsync(client, gamertag, canonicalXuid, cancellationToken);
		await client.WriteMemoryAsync(profile.NameAddress, new byte[64], cancellationToken);
		await client.WriteMemoryAsync(profile.NameAddress, gamertagBytes, cancellationToken);
		if (profile.SecondaryNameAddress.HasValue)
		{
			await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[64], cancellationToken);
			await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, gamertagBytes, cancellationToken);
		}
		await client.WriteMemoryAsync(2175413724u, new byte[Math.Max(64, unicodeGamertagBytes.Length)], cancellationToken);
		await client.WriteMemoryAsync(2175413724u, unicodeGamertagBytes, cancellationToken);
		await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
		await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[32], cancellationToken);
		await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);
		if (profile.PatchAddress.HasValue)
		{
			byte[] patchBytes = profile.PatchBytes;
			if (patchBytes != null && patchBytes.Length > 0)
			{
				await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
			}
		}
		uint? accountBlockAddress = await TryResolveBlackOps2AccountBlockAsync(client, preState.Gamertag, preState.CanonicalXuidHex, cancellationToken);
		if (accountBlockAddress.HasValue)
		{
			await WriteBlackOps2AccountBlockXuidAsync(client, accountBlockAddress.Value, xuidBinaryBytes, cancellationToken);
			await WriteBlackOps2AccountBlockGamertagAsync(client, accountBlockAddress.Value, gamertagBytes, cancellationToken);
		}
		GameSpoofState gameSpoofState = await ReadStateAsync(client, profile, cancellationToken);
		bool verifiedMatch = string.Equals(gameSpoofState.Gamertag, gamertag, StringComparison.Ordinal) && string.Equals(gameSpoofState.CanonicalXuidHex, canonicalXuid, StringComparison.OrdinalIgnoreCase) && string.Equals(gameSpoofState.SecondaryName, gamertag, StringComparison.Ordinal);
		return new GameSpoofApplyResult(gameSpoofState, verifiedMatch, PatchApplied: true);
	}

	internal static async Task<GameSpoofApplyResult> ApplyGamertagAsync(XbdmClient client, SupportedGameSpoofProfile profile, string gamertag, CancellationToken cancellationToken)
	{
		if (profile.TitleId == 1096157379)
		{
			GameSpoofState gameSpoofState = await ReadStateAsync(client, profile, cancellationToken);
			byte[] gamertagBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
			byte[] unicodeGamertagBytes = ToWideAsciiBytes(gamertag);
			byte[] xuidBinaryBytes = ToStoredXuidBytes(gameSpoofState.CanonicalXuidHex);
			byte[] xuidTextBytes = Encoding.ASCII.GetBytes(gameSpoofState.CanonicalXuidHex + "\0");
			uint? accountBlockAddress = await TryResolveBlackOps2AccountBlockAsync(client, gameSpoofState.Gamertag, gameSpoofState.CanonicalXuidHex, cancellationToken);
			await client.WriteMemoryAsync(profile.NameAddress, new byte[64], cancellationToken);
			await client.WriteMemoryAsync(profile.NameAddress, gamertagBytes, cancellationToken);
			if (profile.SecondaryNameAddress.HasValue)
			{
				await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[64], cancellationToken);
				await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, gamertagBytes, cancellationToken);
			}
			await client.WriteMemoryAsync(2175413724u, new byte[Math.Max(64, unicodeGamertagBytes.Length)], cancellationToken);
			await client.WriteMemoryAsync(2175413724u, unicodeGamertagBytes, cancellationToken);
			await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
			await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[32], cancellationToken);
			await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);
			if (accountBlockAddress.HasValue)
			{
				await WriteBlackOps2AccountBlockGamertagAsync(client, accountBlockAddress.Value, gamertagBytes, cancellationToken);
			}
			if (profile.PatchAddress.HasValue)
			{
				byte[] patchBytes = profile.PatchBytes;
				if (patchBytes != null && patchBytes.Length > 0)
				{
					await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
				}
			}
			GameSpoofState gameSpoofState2 = await ReadStateAsync(client, profile, cancellationToken);
			bool verifiedMatch = string.Equals(gameSpoofState2.Gamertag, gamertag, StringComparison.Ordinal) && string.Equals(gameSpoofState2.SecondaryName, gamertag, StringComparison.Ordinal);
			return new GameSpoofApplyResult(gameSpoofState2, verifiedMatch, PatchApplied: true);
		}
		byte[] bytes = Encoding.ASCII.GetBytes(gamertag + "\0");
		await client.WriteMemoryAsync(profile.NameAddress, new byte[64], cancellationToken);
		await client.WriteMemoryAsync(profile.NameAddress, bytes, cancellationToken);
		if (profile.SecondaryNameAddress.HasValue)
		{
			await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[64], cancellationToken);
			await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, bytes, cancellationToken);
		}
		if (profile.PatchAddress.HasValue)
		{
			byte[] patchBytes = profile.PatchBytes;
			if (patchBytes != null && patchBytes.Length > 0)
			{
				await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
			}
		}
		GameSpoofState gameSpoofState3 = await ReadStateAsync(client, profile, cancellationToken);
		bool verifiedMatch2 = string.Equals(gameSpoofState3.Gamertag, gamertag, StringComparison.Ordinal) && (!profile.SecondaryNameAddress.HasValue || string.Equals(gameSpoofState3.SecondaryName, gamertag, StringComparison.Ordinal));
		return new GameSpoofApplyResult(gameSpoofState3, verifiedMatch2, profile.PatchAddress.HasValue);
	}

	internal static async Task<GameSpoofApplyResult> ApplyXuidAsync(XbdmClient client, SupportedGameSpoofProfile profile, string xuidHex, CancellationToken cancellationToken)
	{
		string canonicalXuid = NormalizeXuid(xuidHex);
		byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
		byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");
		GameSpoofState gameSpoofState = ((profile.TitleId != 1096157379) ? null : (await ReadStateAsync(client, profile, cancellationToken)));
		GameSpoofState bo2PreState = gameSpoofState;
		await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
		await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[32], cancellationToken);
		await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);
		if (profile.TitleId == 1096157379)
		{
			await client.WriteMemoryAsync(2176229248u, BlackOps2PrimaryStubBytes, cancellationToken);
			await client.WriteMemoryAsync(2171457600u, BlackOps2SecondaryStubBytes, cancellationToken);
			await StageBlackOps2AccountLayerAsync(client, bo2PreState.Gamertag, canonicalXuid, cancellationToken);
			if (profile.PatchAddress.HasValue)
			{
				byte[] patchBytes = profile.PatchBytes;
				if (patchBytes != null && patchBytes.Length > 0)
				{
					await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
				}
			}
			uint? num = await TryResolveBlackOps2AccountBlockAsync(client, bo2PreState.Gamertag, bo2PreState.CanonicalXuidHex, cancellationToken);
			if (num.HasValue)
			{
				await WriteBlackOps2AccountBlockXuidAsync(client, num.Value, xuidBinaryBytes, cancellationToken);
			}
		}
		GameSpoofState obj = await ReadStateAsync(client, profile, cancellationToken);
		bool verifiedMatch = string.Equals(obj.CanonicalXuidHex, canonicalXuid, StringComparison.OrdinalIgnoreCase);
		return new GameSpoofApplyResult(obj, verifiedMatch, profile.PatchAddress.HasValue);
	}

	internal static async Task<IReadOnlyList<RemoteClientState>> ReadRemoteClientsAsync(XbdmClient client, SupportedGameSpoofProfile profile, CancellationToken cancellationToken)
	{
		if (profile.Remote == null)
		{
			throw new InvalidOperationException("The current title does not expose a remote spoof profile.");
		}
		List<RemoteClientState> states = new List<RemoteClientState>(profile.Remote.ClientCount);
		for (int slot = 0; slot < profile.Remote.ClientCount; slot++)
		{
			int actualSlot = slot + profile.Remote.StartSlotIndex;
			uint primary = (uint)((int)profile.Remote.PlayerBaseAddress + actualSlot * (int)profile.Remote.PlayerStride) + profile.Remote.DisplayNameOffset;
			string name;
			try
			{
				name = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(primary, 32, cancellationToken));
			}
			catch
			{
				name = string.Empty;
			}
			string mirrorName = null;
			if (profile.Remote.MirrorNameOffset.HasValue)
			{
				uint address = (uint)((int)profile.Remote.PlayerBaseAddress + actualSlot * (int)profile.Remote.PlayerStride) + profile.Remote.MirrorNameOffset.Value;
				try
				{
					mirrorName = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(address, 32, cancellationToken));
				}
				catch
				{
					mirrorName = null;
				}
			}
			uint? xuidAddress = null;
			string xuid = null;
			if (profile.Remote.XuidBinaryOffset.HasValue)
			{
				xuidAddress = (uint)((int)profile.Remote.PlayerBaseAddress + actualSlot * (int)profile.Remote.PlayerStride) + profile.Remote.XuidBinaryOffset.Value;
				try
				{
					xuid = ToCanonicalXuidHex(await client.ReadMemoryBytesReliableAsync(xuidAddress.Value, 8, cancellationToken));
				}
				catch
				{
					xuid = null;
				}
			}
			states.Add(new RemoteClientState(actualSlot + 1, primary, profile.Remote.MirrorNameOffset.HasValue ? new uint?((uint)((int)profile.Remote.PlayerBaseAddress + actualSlot * (int)profile.Remote.PlayerStride) + profile.Remote.MirrorNameOffset.Value) : ((uint?)null), name, mirrorName, xuidAddress, xuid));
		}
		return states;
	}

	internal static async Task<RemoteSpoofApplyResult> ApplyRemoteTextAsync(XbdmClient client, SupportedGameSpoofProfile profile, IReadOnlyList<int> slots, string text, CancellationToken cancellationToken, string? xuidHex = null)
	{
		if (profile.Remote == null)
		{
			throw new InvalidOperationException("The current title does not expose a remote spoof profile.");
		}
		List<RemoteClientState> applied = new List<RemoteClientState>(slots.Count);
		foreach (int slot in slots)
		{
			uint baseAddress = profile.Remote.PlayerBaseAddress + (uint)(slot * (int)profile.Remote.PlayerStride);
			string text2 = text.Replace("{slot}", (slot + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
			byte[] bytes = Encoding.ASCII.GetBytes(text2 + "\0");
			uint primary = baseAddress + profile.Remote.DisplayNameOffset;
			await client.WriteMemoryAsync(primary, new byte[32], cancellationToken);
			await client.WriteMemoryAsync(primary, bytes, cancellationToken);
			uint? mirror = null;
			if (profile.Remote.MirrorNameOffset.HasValue)
			{
				mirror = baseAddress + profile.Remote.MirrorNameOffset.Value;
				await client.WriteMemoryAsync(mirror.Value, new byte[32], cancellationToken);
				await client.WriteMemoryAsync(mirror.Value, bytes, cancellationToken);
			}
			uint? xuidAddress = null;
			string verifiedXuid = null;
			if (!string.IsNullOrWhiteSpace(xuidHex) && profile.Remote.XuidBinaryOffset.HasValue)
			{
				string canonicalXuid = NormalizeXuid(xuidHex);
				byte[] array = ToStoredXuidBytes(canonicalXuid);
				xuidAddress = baseAddress + profile.Remote.XuidBinaryOffset.Value;
				await client.WriteMemoryAsync(xuidAddress.Value, array, cancellationToken);
				if (profile.Remote.XuidTextOffset.HasValue)
				{
					byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");
					await client.WriteMemoryAsync(baseAddress + profile.Remote.XuidTextOffset.Value, new byte[32], cancellationToken);
					await client.WriteMemoryAsync(baseAddress + profile.Remote.XuidTextOffset.Value, xuidTextBytes, cancellationToken);
				}
				verifiedXuid = ToCanonicalXuidHex(await client.ReadMemoryBytesReliableAsync(xuidAddress.Value, 8, cancellationToken));
			}
			string verified = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(primary, 32, cancellationToken));
			string mirrorName = null;
			if (mirror.HasValue)
			{
				mirrorName = ReadAsciiZ(await client.ReadMemoryBytesReliableAsync(mirror.Value, 32, cancellationToken));
			}
			applied.Add(new RemoteClientState(slot + 1, primary, mirror, verified, mirrorName, xuidAddress, verifiedXuid));
		}
		string canonicalXuidForVerify = (string.IsNullOrWhiteSpace(xuidHex) ? null : NormalizeXuid(xuidHex));
		return new RemoteSpoofApplyResult(applied, applied.All((RemoteClientState x) => string.Equals(x.Name, text.Replace("{slot}", x.Slot.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), StringComparison.Ordinal) && (canonicalXuidForVerify == null || string.Equals(x.Xuid, canonicalXuidForVerify, StringComparison.OrdinalIgnoreCase))));
	}

	internal static bool TryResolveIdentity(ProfileHelpers.XamUserInfo? currentUser, string? gamertag, string? xuid, bool useCurrentUser, out string resolvedGamertag, out string resolvedXuid, out string? error)
	{
		error = null;
		resolvedGamertag = string.Empty;
		resolvedXuid = string.Empty;
		if (useCurrentUser || (string.IsNullOrWhiteSpace(gamertag) && string.IsNullOrWhiteSpace(xuid)))
		{
			if (currentUser == null || string.IsNullOrWhiteSpace(currentUser.Gamertag) || string.IsNullOrWhiteSpace(currentUser.Xuid))
			{
				error = "No signed-in user was available. Provide explicit values or use --current-user when signed in.";
				return false;
			}
			resolvedGamertag = currentUser.Gamertag.Trim();
			resolvedXuid = NormalizeXuid(currentUser.Xuid);
			return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
		}
		if (string.IsNullOrWhiteSpace(gamertag) || string.IsNullOrWhiteSpace(xuid))
		{
			error = "Provide both gamertag and XUID, or use --current-user.";
			return false;
		}
		resolvedGamertag = gamertag.Trim();
		resolvedXuid = NormalizeXuid(xuid);
		return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
	}

	internal static bool TryResolveRestoreIdentity(ProfileHelpers.XamUserInfo? currentUser, CliConfig.SpoofIdentityCacheInfo? cached, string? gamertag, string? xuid, bool useCurrentUser, out string resolvedGamertag, out string resolvedXuid, out string? error)
	{
		error = null;
		resolvedGamertag = string.Empty;
		resolvedXuid = string.Empty;
		if (!string.IsNullOrWhiteSpace(gamertag) || !string.IsNullOrWhiteSpace(xuid))
		{
			if (string.IsNullOrWhiteSpace(gamertag) || string.IsNullOrWhiteSpace(xuid))
			{
				error = "Provide both gamertag and XUID, or omit both to use cache/current user.";
				return false;
			}
			resolvedGamertag = gamertag.Trim();
			resolvedXuid = NormalizeXuid(xuid);
			return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
		}
		if (!useCurrentUser && cached != null && !string.IsNullOrWhiteSpace(cached.Gamertag) && !string.IsNullOrWhiteSpace(cached.Xuid))
		{
			resolvedGamertag = cached.Gamertag.Trim();
			resolvedXuid = NormalizeXuid(cached.Xuid);
			return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
		}
		if (currentUser != null && !string.IsNullOrWhiteSpace(currentUser.Gamertag) && !string.IsNullOrWhiteSpace(currentUser.Xuid))
		{
			resolvedGamertag = currentUser.Gamertag.Trim();
			resolvedXuid = NormalizeXuid(currentUser.Xuid);
			return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
		}
		if (cached != null && !string.IsNullOrWhiteSpace(cached.Gamertag) && !string.IsNullOrWhiteSpace(cached.Xuid))
		{
			resolvedGamertag = cached.Gamertag.Trim();
			resolvedXuid = NormalizeXuid(cached.Xuid);
			return ValidateIdentity(resolvedGamertag, resolvedXuid, out error);
		}
		error = "No signed-in user or cached spoof identity was available. Provide explicit values or use --current-user when signed in.";
		return false;
	}

	internal static bool TryResolveGamertag(ProfileHelpers.XamUserInfo? currentUser, string? value, bool useCurrentUser, out string gamertag, out string? error)
	{
		gamertag = string.Empty;
		error = null;
		if (useCurrentUser || string.IsNullOrWhiteSpace(value))
		{
			if (currentUser == null || string.IsNullOrWhiteSpace(currentUser.Gamertag))
			{
				error = "No signed-in user was available. Provide --value explicitly or use --current-user when signed in.";
				return false;
			}
			gamertag = currentUser.Gamertag.Trim();
		}
		else
		{
			gamertag = value.Trim();
		}
		int length = gamertag.Length;
		if ((length < 1 || length > 15) ? true : false)
		{
			error = "Gamertag must be between 1 and 15 characters.";
			return false;
		}
		if (!char.IsLetter(gamertag[0]))
		{
			error = "Gamertag must start with a letter.";
			return false;
		}
		if (gamertag.Any((char ch) => ch < ' ' || ch > '~'))
		{
			error = "Gamertag must be ASCII for this spoof path.";
			return false;
		}
		return true;
	}

	internal static bool TryResolveXuid(ProfileHelpers.XamUserInfo? currentUser, string? value, bool useCurrentUser, out string xuid, out string? error)
	{
		error = null;
		xuid = string.Empty;
		if (useCurrentUser || string.IsNullOrWhiteSpace(value))
		{
			if (currentUser == null || string.IsNullOrWhiteSpace(currentUser.Xuid))
			{
				error = "No signed-in user was available. Provide --value explicitly or use --current-user when signed in.";
				return false;
			}
			xuid = NormalizeXuid(currentUser.Xuid);
		}
		else
		{
			xuid = NormalizeXuid(value);
		}
		if (xuid.Length != 16 || !xuid.All(Uri.IsHexDigit))
		{
			error = "XUID must be exactly 16 hex characters.";
			return false;
		}
		return true;
	}

	internal static bool TryResolveSlots(SupportedGameSpoofProfile profile, int? slot, bool all, out IReadOnlyList<int> slots, out string? error)
	{
		error = null;
		slots = Array.Empty<int>();
		if (profile.Remote == null)
		{
			error = "The current title does not expose a remote spoof profile.";
			return false;
		}
		if (all == slot.HasValue)
		{
			error = "Provide either --slot or --all.";
			return false;
		}
		if (all)
		{
			slots = Enumerable.Range(profile.Remote.StartSlotIndex, profile.Remote.ClientCount).ToArray();
			return true;
		}
		int num = slot.Value - 1;
		int value = profile.Remote.StartSlotIndex + 1;
		int value2 = profile.Remote.StartSlotIndex + profile.Remote.ClientCount;
		if (num < profile.Remote.StartSlotIndex || num >= profile.Remote.StartSlotIndex + profile.Remote.ClientCount)
		{
			error = $"Slot must be between {value} and {value2}.";
			return false;
		}
		slots = new int[1] { num };
		return true;
	}

	private static SupportedGameSpoofProfile NewProfile(uint titleId, string slug, string name, uint nameAddress, uint xuidOffset = 36u, uint xuidTextOffset = 44u, uint? secondaryNameAddress = null, uint? patchAddress = null, byte[]? patchBytes = null, RemoteSpoofProfile? remote = null)
	{
		return new SupportedGameSpoofProfile(titleId, slug, name, nameAddress, nameAddress + xuidOffset, nameAddress + xuidTextOffset, secondaryNameAddress, patchAddress, patchBytes, remote);
	}

	private static bool ValidateIdentity(string gamertag, string xuid, out string? error)
	{
		error = null;
		int length = gamertag.Length;
		if ((length < 1 || length > 15) ? true : false)
		{
			error = "Gamertag must be between 1 and 15 characters.";
			return false;
		}
		if (!char.IsLetter(gamertag[0]))
		{
			error = "Gamertag must start with a letter.";
			return false;
		}
		if (gamertag.Any((char ch) => ch < ' ' || ch > '~'))
		{
			error = "Gamertag must be ASCII for this spoof path.";
			return false;
		}
		if (xuid.Length != 16 || !xuid.All(Uri.IsHexDigit))
		{
			error = "XUID must be exactly 16 hex characters.";
			return false;
		}
		return true;
	}

	private static string NormalizeXuid(string value)
	{
		string text = value.Trim();
		if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring(2);
		}
		return text.ToUpperInvariant();
	}

	private static string NormalizeTargetKey(string targetKey)
	{
		return targetKey.Trim().ToLowerInvariant();
	}

	private static async Task StageBlackOps2AccountLayerAsync(XbdmClient client, string gamertag, string xuidHex, CancellationToken cancellationToken)
	{
		Jrpc2Client jrpc = new Jrpc2Client(client);
		uint krnlResolve = await jrpc.ResolveFunctionAsync("xboxkrnl.exe", 315u, cancellationToken);
		uint xamPrimaryResolve = await jrpc.ResolveFunctionAsync("xam.xex", 1128u, cancellationToken);
		uint value = await jrpc.ResolveFunctionAsync("xam.xex", 1080u, cancellationToken);
		byte[] array = new byte[24];
		WriteUInt32BigEndian(array, 0, krnlResolve);
		WriteUInt32BigEndian(array, 4, xamPrimaryResolve);
		WriteUInt32BigEndian(array, 8, value);
		WriteUInt32BigEndian(array, 12, 2175233672u);
		WriteUInt32BigEndian(array, 16, 2175233928u);
		WriteUInt32BigEndian(array, 20, 0u);
		await client.WriteMemoryAsync(2175232648u, array, cancellationToken);
		await client.WriteMemoryAsync(2175232904u, BlackOps2AccountObject5Bytes, cancellationToken);
		await client.WriteMemoryAsync(2175233160u, BlackOps2AccountObject6Bytes, cancellationToken);
		await PatchBlackOps2EmbeddedPointerAsync(client, 2175232648u, 2175232904u, 12u, cancellationToken);
		await PatchBlackOps2EmbeddedPointerAsync(client, 2175232648u, 2175233160u, 20u, cancellationToken);
		byte[] array2 = ToStoredXuidBytes(NormalizeXuid(xuidHex));
		await client.WriteMemoryAsync(2175233928u, array2, cancellationToken);
		await EnsureBlackOps2TrampolineSnapshotAsync(client, 2186898816u, 2175233672u, cancellationToken);
		await EnsureBlackOps2SecondaryHookSnapshotAsync(client, 2190726804u, 2175233416u, cancellationToken);
		await ApplyBlackOps2BranchStubAsync(client, 2190726804u, 2175232904u, cancellationToken);
		await ApplyBlackOps2BranchStubAsync(client, 2186898816u, 2175233160u, cancellationToken);
	}

	internal static async Task<GameSpoofApplyResult> RestoreIdentityAsync(XbdmClient client, SupportedGameSpoofProfile profile, string gamertag, string xuidHex, CancellationToken cancellationToken)
	{
		if (profile.TitleId == 1096157379)
		{
			return await RestoreBlackOps2IdentityAsync(client, profile, gamertag, xuidHex, cancellationToken);
		}
		return await ApplyIdentityAsync(client, profile, gamertag, xuidHex, cancellationToken);
	}

	internal static void ClearCachedIdentity(string targetKey, uint titleId)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (cliConfig.SpoofIdentityCache != null && cliConfig.SpoofIdentityCache.Count != 0)
		{
			string identityCacheKey = GetIdentityCacheKey(targetKey, titleId);
			if (cliConfig.SpoofIdentityCache.Remove(identityCacheKey))
			{
				cliConfig.Save();
			}
		}
	}

	private static async Task<GameSpoofApplyResult> RestoreBlackOps2IdentityAsync(XbdmClient client, SupportedGameSpoofProfile profile, string gamertag, string xuidHex, CancellationToken cancellationToken)
	{
		byte[] gamertagBytes = Encoding.ASCII.GetBytes(gamertag + "\0");
		byte[] unicodeGamertagBytes = ToWideAsciiBytes(gamertag);
		string canonicalXuid = NormalizeXuid(xuidHex);
		byte[] xuidBinaryBytes = ToStoredXuidBytes(canonicalXuid);
		byte[] xuidTextBytes = Encoding.ASCII.GetBytes(canonicalXuid + "\0");
		GameSpoofState preState = await ReadStateAsync(client, profile, cancellationToken);
		await RestoreBlackOps2AccountLayerAsync(client, cancellationToken);
		await client.WriteMemoryAsync(profile.NameAddress, new byte[64], cancellationToken);
		await client.WriteMemoryAsync(profile.NameAddress, gamertagBytes, cancellationToken);
		if (profile.SecondaryNameAddress.HasValue)
		{
			await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, new byte[64], cancellationToken);
			await client.WriteMemoryAsync(profile.SecondaryNameAddress.Value, gamertagBytes, cancellationToken);
		}
		await client.WriteMemoryAsync(2175413724u, new byte[Math.Max(64, unicodeGamertagBytes.Length)], cancellationToken);
		await client.WriteMemoryAsync(2175413724u, unicodeGamertagBytes, cancellationToken);
		await client.WriteMemoryAsync(profile.XuidBinaryAddress, xuidBinaryBytes, cancellationToken);
		await client.WriteMemoryAsync(profile.XuidTextAddress, new byte[32], cancellationToken);
		await client.WriteMemoryAsync(profile.XuidTextAddress, xuidTextBytes, cancellationToken);
		if (profile.PatchAddress.HasValue)
		{
			byte[] patchBytes = profile.PatchBytes;
			if (patchBytes != null && patchBytes.Length > 0)
			{
				await client.WriteMemoryAsync(profile.PatchAddress.Value, profile.PatchBytes, cancellationToken);
			}
		}
		uint? accountBlockAddress = await TryResolveBlackOps2AccountBlockAsync(client, preState.Gamertag, preState.CanonicalXuidHex, cancellationToken);
		if (accountBlockAddress.HasValue)
		{
			await WriteBlackOps2AccountBlockXuidAsync(client, accountBlockAddress.Value, xuidBinaryBytes, cancellationToken);
			await WriteBlackOps2AccountBlockGamertagAsync(client, accountBlockAddress.Value, gamertagBytes, cancellationToken);
		}
		GameSpoofState gameSpoofState = await ReadStateAsync(client, profile, cancellationToken);
		bool verifiedMatch = string.Equals(gameSpoofState.Gamertag, gamertag, StringComparison.Ordinal) && string.Equals(gameSpoofState.CanonicalXuidHex, canonicalXuid, StringComparison.OrdinalIgnoreCase) && string.Equals(gameSpoofState.SecondaryName, gamertag, StringComparison.Ordinal);
		return new GameSpoofApplyResult(gameSpoofState, verifiedMatch, PatchApplied: true);
	}

	private static async Task PatchBlackOps2EmbeddedPointerAsync(XbdmClient client, uint sourceAddress, uint destinationAddress, uint offset, CancellationToken cancellationToken)
	{
		uint value = 0x3D600000 | (sourceAddress >> 16);
		uint value2 = 0x616B0000 | (sourceAddress & 0xFFFF);
		byte[] array = new byte[8];
		WriteUInt32BigEndian(array, 0, value);
		WriteUInt32BigEndian(array, 4, value2);
		await client.WriteMemoryAsync(destinationAddress + offset, array, cancellationToken);
	}

	private static async Task EnsureBlackOps2TrampolineSnapshotAsync(XbdmClient client, uint sourceAddress, uint destinationAddress, CancellationToken cancellationToken)
	{
		byte[] existingHook = await client.ReadMemoryBytesReliableAsync(sourceAddress, 16, cancellationToken);
		byte[] array = BuildBlackOps2BranchStub(2175233160u);
		if (((ReadOnlySpan<byte>)existingHook.AsSpan()).SequenceEqual((ReadOnlySpan<byte>)array))
		{
			if (!HasAnyNonZeroByte(await TryReadBlackOps2MemoryAsync(client, destinationAddress, 32, cancellationToken)))
			{
				throw new InvalidOperationException("BO2 hook entry is already patched, but the primary trampoline snapshot is missing.");
			}
			return;
		}
		if (!LooksLikeExpectedPrimaryHook(existingHook))
		{
			throw new InvalidOperationException("BO2 hook entry bytes did not match the expected prologue. Aborting to avoid corrupting the title.");
		}
		byte[] array2 = await TryReadBlackOps2MemoryAsync(client, destinationAddress, 32, cancellationToken);
		if (HasAnyNonZeroByte(array2))
		{
			if (!((ReadOnlySpan<byte>)array2.AsSpan(12, 16)).SequenceEqual((ReadOnlySpan<byte>)existingHook))
			{
				throw new InvalidOperationException("BO2 primary trampoline snapshot does not match the live hook entry bytes.");
			}
		}
		else
		{
			byte[] array3 = BuildBlackOps2TrampolineSnapshot(sourceAddress, existingHook);
			await client.WriteMemoryAsync(destinationAddress, array3, cancellationToken);
		}
	}

	private static async Task EnsureBlackOps2SecondaryHookSnapshotAsync(XbdmClient client, uint sourceAddress, uint destinationAddress, CancellationToken cancellationToken)
	{
		byte[] existingHook = await client.ReadMemoryBytesReliableAsync(sourceAddress, 16, cancellationToken);
		byte[] array = BuildBlackOps2BranchStub(2175232904u);
		if (((ReadOnlySpan<byte>)existingHook.AsSpan()).SequenceEqual((ReadOnlySpan<byte>)array))
		{
			if (!HasAnyNonZeroByte(await TryReadBlackOps2MemoryAsync(client, destinationAddress, 16, cancellationToken)))
			{
				throw new InvalidOperationException("BO2 secondary hook is already patched, but the original-byte snapshot is missing.");
			}
			return;
		}
		byte[] array2 = await TryReadBlackOps2MemoryAsync(client, destinationAddress, 16, cancellationToken);
		if (HasAnyNonZeroByte(array2))
		{
			if (((ReadOnlySpan<byte>)array2.AsSpan()).SequenceEqual((ReadOnlySpan<byte>)existingHook))
			{
				return;
			}
			throw new InvalidOperationException("BO2 secondary hook bytes no longer match the saved original snapshot.");
		}
		if (!LooksLikeExecutableHook(existingHook))
		{
			throw new InvalidOperationException("BO2 secondary hook bytes did not look executable. Aborting to avoid corrupting the title.");
		}
		await client.WriteMemoryAsync(destinationAddress, existingHook, cancellationToken);
	}

	private static async Task ApplyBlackOps2BranchStubAsync(XbdmClient client, uint sourceAddress, uint destinationAddress, CancellationToken cancellationToken)
	{
		byte[] array = BuildBlackOps2BranchStub(destinationAddress);
		await client.WriteMemoryAsync(sourceAddress, array, cancellationToken);
	}

	private static async Task RestoreBlackOps2AccountLayerAsync(XbdmClient client, CancellationToken cancellationToken)
	{
		byte[] array = await TryReadBlackOps2MemoryAsync(client, 2175233672u, 32, cancellationToken);
		if (HasAnyNonZeroByte(array))
		{
			byte[] array2 = array.Skip(12).Take(16).ToArray();
			if (array2.Length == 16 && HasAnyNonZeroByte(array2))
			{
				await client.WriteMemoryAsync(2186898816u, array2, cancellationToken);
			}
		}
		byte[] array3 = await TryReadBlackOps2MemoryAsync(client, 2175233416u, 16, cancellationToken);
		if (HasAnyNonZeroByte(array3))
		{
			await client.WriteMemoryAsync(2190726804u, array3, cancellationToken);
		}
		await client.WriteMemoryAsync(2176229248u, new byte[BlackOps2PrimaryStubBytes.Length], cancellationToken);
		await client.WriteMemoryAsync(2171457600u, new byte[BlackOps2SecondaryStubBytes.Length], cancellationToken);
		await client.WriteMemoryAsync(2175232648u, new byte[1536], cancellationToken);
	}

	private static byte[] BuildBlackOps2TrampolineSnapshot(uint sourceAddress, ReadOnlySpan<byte> originalBytes)
	{
		uint num = sourceAddress + 16;
		byte[] array = new byte[32];
		uint num2 = 1029701632 + ((num >> 16) & 0xFFFF);
		if ((num & 0x8000) != 0)
		{
			num2++;
		}
		uint value = 962592768 + (num & 0xFFFF);
		WriteUInt32BigEndian(array, 0, num2);
		WriteUInt32BigEndian(array, 4, value);
		WriteUInt32BigEndian(array, 8, 2104034214u);
		originalBytes.Slice(0, 16).CopyTo(array.AsSpan(12));
		WriteUInt32BigEndian(array, 28, 1317012512u);
		return array;
	}

	private static byte[] BuildBlackOps2BranchStub(uint destinationAddress)
	{
		byte[] array = new byte[16];
		uint num = 1029701632 + ((destinationAddress >> 16) & 0xFFFF);
		if ((destinationAddress & 0x8000) != 0)
		{
			num++;
		}
		uint value = 962592768 + (destinationAddress & 0xFFFF);
		WriteUInt32BigEndian(array, 0, num);
		WriteUInt32BigEndian(array, 4, value);
		WriteUInt32BigEndian(array, 8, 2104034214u);
		WriteUInt32BigEndian(array, 12, 1317012512u);
		return array;
	}

	private static bool LooksLikeExpectedPrimaryHook(ReadOnlySpan<byte> currentBytes)
	{
		if (currentBytes.Length >= 16)
		{
			return currentBytes.Slice(0, 16).SequenceEqual(BlackOps2ExpectedPrimaryHookBytes);
		}
		return false;
	}

	private static bool LooksLikeExecutableHook(ReadOnlySpan<byte> currentBytes)
	{
		if (currentBytes.Length < 16)
		{
			return false;
		}
		if (!HasAnyNonZeroByte(currentBytes))
		{
			return false;
		}
		uint num = BinaryPrimitives.ReadUInt32BigEndian(currentBytes.Slice(0, 4));
		if (num != 0 && num != uint.MaxValue)
		{
			return num != 1317012512;
		}
		return false;
	}

	private static bool HasAnyNonZeroByte(ReadOnlySpan<byte> data)
	{
		ReadOnlySpan<byte> readOnlySpan = data;
		for (int i = 0; i < readOnlySpan.Length; i++)
		{
			if (readOnlySpan[i] != 0)
			{
				return true;
			}
		}
		return false;
	}

	private static async Task<byte[]> TryReadBlackOps2MemoryAsync(XbdmClient client, uint address, int length, CancellationToken cancellationToken)
	{
		try
		{
			return await client.ReadMemoryBytesReliableAsync(address, length, cancellationToken);
		}
		catch
		{
			return new byte[length];
		}
	}

	private static async Task<uint?> TryResolveBlackOps2AccountBlockAsync(XbdmClient client, string gamertag, string canonicalXuid, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(gamertag) || string.IsNullOrWhiteSpace(canonicalXuid) || canonicalXuid == new string('0', 16))
		{
			return null;
		}
		byte[] pattern = BuildBlackOps2AccountBlockSignature(gamertag, canonicalXuid);
		int overlap = pattern.Length - 1;
		byte[] carry = Array.Empty<byte>();
		for (uint address = 2205155328u; address < 2214592512u; address += 4096)
		{
			int length = (int)Math.Min(4096uL, 2214592512u - address);
			byte[] array = await TryReadBlackOps2MemoryAsync(client, address, length, cancellationToken);
			if (HasAnyNonZeroByte(array))
			{
				byte[] array2;
				if (carry.Length == 0)
				{
					array2 = array;
				}
				else
				{
					array2 = new byte[carry.Length + array.Length];
					Buffer.BlockCopy(carry, 0, array2, 0, carry.Length);
					Buffer.BlockCopy(array, 0, array2, carry.Length, array.Length);
				}
				int num = IndexOfPattern(array2, pattern);
				if (num >= 0)
				{
					long num2 = address - carry.Length + num;
					return (uint)num2;
				}
				if (overlap > 0)
				{
					int num3 = Math.Min(overlap, array2.Length);
					carry = new byte[num3];
					Buffer.BlockCopy(array2, array2.Length - num3, carry, 0, num3);
				}
			}
		}
		return null;
	}

	private static byte[] BuildBlackOps2AccountBlockSignature(string gamertag, string canonicalXuid)
	{
		byte[] bytes = Encoding.ASCII.GetBytes(gamertag + "\0");
		byte[] array = ToStoredXuidBytes(canonicalXuid);
		byte[] array2 = new byte[20 + array.Length + bytes.Length];
		array2[0] = 219;
		array2[1] = 71;
		Buffer.BlockCopy(array, 0, array2, 20, array.Length);
		Buffer.BlockCopy(bytes, 0, array2, 28, bytes.Length);
		return array2;
	}

	private static int IndexOfPattern(byte[] buffer, byte[] pattern)
	{
		if (pattern.Length == 0 || buffer.Length < pattern.Length)
		{
			return -1;
		}
		for (int i = 0; i <= buffer.Length - pattern.Length; i++)
		{
			bool flag = true;
			for (int j = 0; j < pattern.Length; j++)
			{
				if (buffer[i + j] != pattern[j])
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				return i;
			}
		}
		return -1;
	}

	private static async Task WriteBlackOps2AccountBlockGamertagAsync(XbdmClient client, uint blockAddress, byte[] gamertagBytes, CancellationToken cancellationToken)
	{
		uint fieldAddress = blockAddress + 28;
		await client.WriteMemoryAsync(fieldAddress, new byte[32], cancellationToken);
		await client.WriteMemoryAsync(fieldAddress, gamertagBytes, cancellationToken);
	}

	private static async Task WriteBlackOps2AccountBlockXuidAsync(XbdmClient client, uint blockAddress, byte[] xuidBytes, CancellationToken cancellationToken)
	{
		await client.WriteMemoryAsync(blockAddress + 20, xuidBytes, cancellationToken);
	}

	private static void WriteUInt32BigEndian(Span<byte> destination, int offset, uint value)
	{
		BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, 4), value);
	}

	private static byte[] ToStoredXuidBytes(string canonicalXuid)
	{
		byte[] array = Convert.FromHexString(canonicalXuid);
		Array.Reverse(array);
		return array;
	}

	private static string ToCanonicalXuidHex(byte[] storedBytes)
	{
		byte[] array = storedBytes.ToArray();
		Array.Reverse(array);
		return Convert.ToHexString(array);
	}

	private static string ReadAsciiZ(byte[] data)
	{
		int num = Array.IndexOf(data, (byte)0);
		if (num < 0)
		{
			num = data.Length;
		}
		if (num != 0)
		{
			return Encoding.ASCII.GetString(data, 0, num).Trim();
		}
		return string.Empty;
	}

	private static string ReadAsciiLoose(byte[] data)
	{
		return Encoding.ASCII.GetString(data).Trim('\0').Trim();
	}

	private static byte[] ToWideAsciiBytes(string value)
	{
		byte[] array = new byte[value.Length * 2 + 2];
		int num = 1;
		foreach (char c in value)
		{
			array[num] = (byte)c;
			num += 2;
		}
		return array;
	}

	static GameSpoofHelpers()
	{
		SupportedGameSpoofProfile[] array = new SupportedGameSpoofProfile[8];
		uint? xuidBinaryOffset = 10268u;
		uint? xuidTextOffset = 10276u;
		RemoteSpoofProfile remote = new RemoteSpoofProfile(17, 2216255452u, 10792u, 10232u, null, 0, xuidBinaryOffset, xuidTextOffset);
		array[0] = NewProfile(1096157269u, "bo1", "Call of Duty: Black Ops", 2216265684u, 36u, 44u, null, null, null, remote);
		remote = new RemoteSpoofProfile(17, 2207669792u, 14720u, 13196u, 13332u, 0, 13232u, 13240u);
		array[1] = NewProfile(1096157387u, "mw3", "Call of Duty: Modern Warfare 3", 2207682988u, 36u, 44u, null, null, null, remote);
		uint? secondaryNameAddress = 2176229012u;
		remote = new RemoteSpoofProfile(11, 2216543888u, 22520u, 0u, 136u, 1, 32u, 40u);
		array[2] = NewProfile(1096157379u, "bo2", "Call of Duty: Black Ops II", 2216565552u, 32u, 40u, secondaryNameAddress, null, null, remote);
		array[3] = NewProfile(1096157158u, "cod4", "Call of Duty 4: Modern Warfare", 2227325884u);
		array[4] = NewProfile(1096157212u, "waw", "Call of Duty: World at War", 2233677493u, 35u, 43u);
		array[5] = NewProfile(1096157207u, "mw2", "Call of Duty: Modern Warfare 2", 2206967844u);
		array[6] = NewProfile(1096157436u, "ghosts", "Call of Duty: Ghosts", 2213585756u);
		array[7] = NewProfile(1096157460u, "aw", "Call of Duty: Advanced Warfare", 2218650804u);
		Profiles = array;
	}
}
