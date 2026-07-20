using System;
using System.Collections.Generic;
using System.IO;
using NoDev.Common.IO;

namespace Xbox360.Remote.Cli.LocalProfiles;

internal sealed class ProfileAvatarColors
{
	public const ulong AvatarInfo1SettingId = 0x63E80044;

	private const int ColorOffset = 0xFC;
	private const int ColorCount = 9;

	private readonly byte[] _payload;

	private ProfileAvatarColors(byte[] payload)
	{
		_payload = payload;
	}

	public uint SkinArgb { get; private set; }

	public uint HairArgb { get; private set; }

	public uint LipArgb { get; private set; }

	public uint EyeArgb { get; private set; }

	public uint EyebrowArgb { get; private set; }

	public uint EyeshadowArgb { get; private set; }

	public uint FacialHairArgb { get; private set; }

	public uint FacePaintPrimaryArgb { get; private set; }

	public uint FacePaintSecondaryArgb { get; private set; }

	public static ProfileAvatarColors Parse(byte[] payload)
	{
		if (payload == null)
		{
			throw new ArgumentNullException(nameof(payload));
		}

		if (payload.Length < ColorOffset + (ColorCount * sizeof(int)))
		{
			throw new InvalidDataException(
				"Avatar color payload is too small. Expected at least " +
				(ColorOffset + (ColorCount * sizeof(int))).ToString() +
				" bytes, got " +
				payload.Length.ToString() +
				".");
		}

		byte[] clone = (byte[])payload.Clone();
		var result = new ProfileAvatarColors(clone);
		var io = new EndianIO(clone, EndianType.Big);
		try
		{
			io.Stream.Position = ColorOffset;
			result.SkinArgb = unchecked((uint)io.ReadInt32());
			result.HairArgb = unchecked((uint)io.ReadInt32());
			result.LipArgb = unchecked((uint)io.ReadInt32());
			result.EyeArgb = unchecked((uint)io.ReadInt32());
			result.EyebrowArgb = unchecked((uint)io.ReadInt32());
			result.EyeshadowArgb = unchecked((uint)io.ReadInt32());
			result.FacialHairArgb = unchecked((uint)io.ReadInt32());
			result.FacePaintPrimaryArgb = unchecked((uint)io.ReadInt32());
			result.FacePaintSecondaryArgb = unchecked((uint)io.ReadInt32());
			return result;
		}
		finally
		{
			io.Close();
		}
	}

	public IEnumerable<(string Name, uint Argb)> Enumerate()
	{
		yield return ("Skin", SkinArgb);
		yield return ("Hair", HairArgb);
		yield return ("Lip", LipArgb);
		yield return ("Eye", EyeArgb);
		yield return ("Eyebrow", EyebrowArgb);
		yield return ("Eyeshadow", EyeshadowArgb);
		yield return ("FacialHair", FacialHairArgb);
		yield return ("FacePaintPrimary", FacePaintPrimaryArgb);
		yield return ("FacePaintSecondary", FacePaintSecondaryArgb);
	}

	public ProfileAvatarColors WithOverrides(
		uint? skinArgb = null,
		uint? hairArgb = null,
		uint? lipArgb = null,
		uint? eyeArgb = null,
		uint? eyebrowArgb = null,
		uint? eyeshadowArgb = null,
		uint? facialHairArgb = null,
		uint? facePaintPrimaryArgb = null,
		uint? facePaintSecondaryArgb = null)
	{
		byte[] clone = (byte[])_payload.Clone();
		var io = new EndianIO(clone, EndianType.Big);
		try
		{
			io.Stream.Position = ColorOffset;
			io.Write(unchecked((int)(skinArgb ?? SkinArgb)));
			io.Write(unchecked((int)(hairArgb ?? HairArgb)));
			io.Write(unchecked((int)(lipArgb ?? LipArgb)));
			io.Write(unchecked((int)(eyeArgb ?? EyeArgb)));
			io.Write(unchecked((int)(eyebrowArgb ?? EyebrowArgb)));
			io.Write(unchecked((int)(eyeshadowArgb ?? EyeshadowArgb)));
			io.Write(unchecked((int)(facialHairArgb ?? FacialHairArgb)));
			io.Write(unchecked((int)(facePaintPrimaryArgb ?? FacePaintPrimaryArgb)));
			io.Write(unchecked((int)(facePaintSecondaryArgb ?? FacePaintSecondaryArgb)));
			byte[] updated = io.ToArray();
			return Parse(updated);
		}
		finally
		{
			io.Close();
		}
	}

	public byte[] ToArray() => (byte[])_payload.Clone();
}
