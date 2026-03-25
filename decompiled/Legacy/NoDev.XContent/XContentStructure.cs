using System;
using NoDev.Common.IO;

namespace NoDev.XContent
{
    public enum XContentSignatureType
    {
        Console = 0x434F4E20,
        LIVE = 0x4C495645,
        PIRS = 0x50495253
    }

    public struct XContentSignature
    {
        public byte[] Signature; // 256
        public byte[] Reserved;  // 296
    }
    
    public struct XContentLicense
    {
        private ulong _id;

        public ushort Type
        {
            get { return (ushort)(this._id >> 48); }
            set { this._id |= (ulong)value << 48; }
        }

        public ulong Data
        {
            get { return this._id & 0xffffffffffff; }
            set { this._id &= 0xffff000000000000 | (value & 0xffffffffffff); }
        }

        public uint Bits;
        public uint Flags;

        public XContentLicense(EndianIO io)
        {
            this._id = io.ReadUInt64();
            this.Bits = io.ReadUInt32();
            this.Flags = io.ReadUInt32();
        }

        public void Write(EndianIO io)
        {
            io.Write(this._id);
            io.Write(this.Bits);
            io.Write(this.Flags);
        }
    }

    public enum XContentVolumeType
    {
        STFS = 0x00,
        SVOD = 0x01
    }

    public struct XContentMediaData
    {
        public byte[] SeriesID;
        public byte[] SeasonID;
        public ushort SeasonNumber;
        public ushort EpisodeNumber;

        public XContentMediaData(EndianIO io)
        {
            this.SeriesID = io.ReadByteArray(16);
            this.SeasonID = io.ReadByteArray(16);
            this.SeasonNumber = io.ReadUInt16();
            this.EpisodeNumber = io.ReadUInt16();
        }

        public void Write(EndianIO io)
        {
            io.Write(this.SeriesID);
            io.Write(this.SeasonID);
            io.Write(this.SeasonNumber);
            io.Write(this.EpisodeNumber);
        }
    }

    public struct XContentAvatarAssetData
    {
        public uint SubCategory;
        public int Colorizable;
        public byte[] AssetID; // 16
        public byte SkeletonVersionMask;
        public byte[] Reserved; // 11

        public XContentAvatarAssetData(EndianIO io)
        {
            this.SubCategory = io.ReadUInt32();
            this.Colorizable = io.ReadInt32();
            this.AssetID = io.ReadByteArray(16);
            this.SkeletonVersionMask = io.ReadByte();
            this.Reserved = io.ReadByteArray(11);
        }

        public void Write(EndianIO io)
        {
            io.Write(this.SubCategory);
            io.Write(this.Colorizable);
            io.Write(this.AssetID);
            io.Write(this.SkeletonVersionMask);
            io.Write(this.Reserved);
        }
    }

    public struct XexExecutionId
    {
        public uint MediaID;
        public uint Version;
        public uint BaseVersion;
        public uint TitleID;
        public byte Platform;
        public byte ExecutableType;
        public byte DiscNumber;
        public byte DiscsInSet;
        public uint SaveGameID;

        public ushort PublisherID
        {
            get { return (ushort)(this.TitleID >> 16); }
            set { this.TitleID |= (uint)value << 16; }
        }

        public ushort GameID
        {
            get { return (ushort)(this.TitleID & 0xffff); }
            set { this.TitleID &= 0xffff0000 | ((uint)value & 0xffff); }
        }

        public XexExecutionId(EndianIO io)
        {
            this.MediaID = io.ReadUInt32();
            this.Version = io.ReadUInt32();
            this.BaseVersion = io.ReadUInt32();
            this.TitleID = io.ReadUInt32();
            this.Platform = io.ReadByte();
            this.ExecutableType = io.ReadByte();
            this.DiscNumber = io.ReadByte();
            this.DiscsInSet = io.ReadByte();
            this.SaveGameID = io.ReadUInt32();
        }

        public void Write(EndianIO io)
        {
            io.Write(this.MediaID);
            io.Write(this.Version);
            io.Write(this.BaseVersion);
            io.Write(this.TitleID);
            io.Write(this.Platform);
            io.Write(this.ExecutableType);
            io.Write(this.DiscNumber);
            io.Write(this.DiscsInSet);
            io.Write(this.SaveGameID);
        }
    }

    public struct XContentInstallerUpdateData
    {
        private readonly int _baseVersion;
        private readonly int _version;
        private readonly byte[] _reserved;

        public Version BaseVersion
        {
            get { return ToVersion(this._baseVersion); }
        }

        public Version Version
        {
            get { return ToVersion(this._version); }
        }

        private static Version ToVersion(int x)
        {
            return new Version(x >> 28, (x >> 24) & 0x0f, (x >> 8) & 0xff, x & 0xff);
        }

        internal XContentInstallerUpdateData(EndianIO io)
        {
            this._baseVersion = io.ReadInt32();
            this._version = io.ReadInt32();
            this._reserved = io.ReadByteArray(5608);
        }

        internal void Write(EndianIO io)
        {
            io.Write(this._baseVersion);
            io.Write(this._version);
            io.Write(this._reserved);
        }
    }

    public struct XContentInstallerProgressCacheData
    {
        public XOnlineContentResumeStates ResumeState;
        public int CurrentFileIndex;
        public long CurrentFileOffset;
        public long BytesProcessed;
        public long LastModified;

        private byte[] _cabResumeData;
        public byte[] CabResumeData
        {
            get { return this._cabResumeData; }
            set
            {
                if (value.Length != 5584)
                    throw new XContentException("Invalid cab resume data length.");
                this._cabResumeData = value;
            }
        }

        internal XContentInstallerProgressCacheData(EndianIO io)
        {
            this.ResumeState = (XOnlineContentResumeStates)io.ReadUInt32();
            this.CurrentFileIndex = io.ReadInt32();
            this.CurrentFileOffset = io.ReadInt64();
            this.BytesProcessed = io.ReadInt64();
            this.LastModified = io.ReadInt64();
            this._cabResumeData = io.ReadByteArray(5584);
        }

        internal void Write(EndianIO io)
        {
            io.Write((uint)this.ResumeState);
            io.Write(this.CurrentFileIndex);
            io.Write(this.CurrentFileOffset);
            io.Write(this.BytesProcessed);
            io.Write(this.LastModified);
            io.Write(this._cabResumeData);
        }
    }

    public enum XOnlineContentResumeStates : uint
    {
        FileHeadersNotReady = 0x46494C48,
        NewFolder = 0x666F6C64,
        NewFolderResumeAttempt1 = 0x666F6C31,
        NewFolderResumeAttempt2 = 0x666F6C32,
        NewFolderResumeAttemptUnknown = 0x666F6C3F,
        NewFolderResumeAttemptSpecific = 0x666F6C40
    }

    public enum XContentInstallerTypes : uint
    {
        None = 0x00,
        SystemUpdate = 0x53555044, // SUPD
        TitleUpdate = 0x54555044, // TUPD
        SystemUpdateProgressCache = 0x50245355, // P$SU
        TitleUpdateProgressCache = 0x50245455, // P$TU
        TitleContentProgressCache = 0x50245443 // P$TC
    }

    [Flags]
    public enum XContentFlags : byte
    {
        None = 0x00,
        DeepLinkSupported = 0x04,
        DisableNetworkStorage = 0x08,
        KinectEnabled = 0x10,
        MoveOnlyTransfer = 0x20,
        DeviceTransfer = 0x40,
        ProfileTransfer = 0x80
    }

    public enum XContentTypes
    {
        SavedGame = 0x01,
        Marketplace = 0x02,
        Publisher = 0x03,
        IPTVDVR = 0x1000,
        IPTVPauseBuffer = 0x2000,
        XNACommunity = 0x3000,
        InstalledXbox360Title = 0x4000,
        XboxTitle = 0x5000,
        SocialTitle = 0x6000,
        Xbox360Title = 0x7000,
        SystemUpdateStoragePack = 0x8000,
        AvatarAsset = 0x9000,
        Profile = 0x10000,
        GamerPicture = 0x20000,
        ThematicSkin = 0x30000,
        Cache = 0x40000,
        StorageDownload = 0x50000,
        XboxSavedGame = 0x60000,
        XboxDownload = 0x70000,
        GameDemo = 0x80000,
        Video = 0x90000,
        GameTitle = 0xA0000,
        Installer = 0xB0000,
        GameTrailer = 0xC0000,
        Arcade = 0xD0000,
        XNA = 0xE0000,
        LicenseStore = 0xF0000,
        Movie = 0x100000,
        TV = 0x200000,
        MusicVideo = 0x300000,
        Promotional = 0x400000,
        PodcastVideo = 0x500000,
        ViralVideo = 0x600000,
        CommunityGame = 0x02000000,
        Unknown
    }
}