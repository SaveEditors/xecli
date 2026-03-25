using System;
using System.Collections.Generic;
using NoDev.Common.IO;
using NoDev.Xbox360;

namespace NoDev.XContent
{
    public class XContentMetadata
    {
        public XContentTypes ContentType;
        public uint ContentMetadataVersion;
        public ulong ContentSize;
        public XexExecutionId ExecutionId;
        public dynamic Descriptor;
        public uint DataFiles;
        public ulong DataFilesSize;
        public XContentVolumeType VolumeType;
        public ulong Creator;
        public ulong OnlineCreator;
        public uint Category;
        public XContentMediaData MediaData;
        public XContentAvatarAssetData AvatarAssetData;
        public List<string> DisplayNames = new List<string>(9); // 9 or 12
        public List<string> Descriptions = new List<string>(9); // 9 or 12
        public string Publisher; // 128
        public string TitleName; // 128
        public XContentFlags Flags;

        private readonly byte[] _reserved; // 32

        public XContentInstallerTypes InstallerType;
        public XContentInstallerUpdateData InstallerUpdateData;
        public XContentInstallerProgressCacheData InstallerProgressCacheData;
        private byte[] _unknownInstallerData;

        private readonly int _firstDisplayNameLanguage = -1;
        private readonly int _firstDescriptionLanguage = -1;

        public string DisplayName
        {
            get
            {
                return this.DisplayNames[_firstDisplayNameLanguage];
            }
            set
            {
                this.DisplayNames[_firstDisplayNameLanguage] = value;
            }
        }

        public void SetAllDisplayNames(string newName)
        {
            for (int x = 0; x < this.DisplayNames.Count; x++)
                this.DisplayNames[x] = newName;
        }

        public string Description
        {
            get
            {
                return this.Descriptions[_firstDescriptionLanguage];
            }
            set
            {
                this.Descriptions[_firstDescriptionLanguage] = value;
            }
        }

        public void SetAllDescriptions(string newDescription)
        {
            for (int x = 0; x < this.Descriptions.Count; x++)
                this.Descriptions[x] = newDescription;
        }

        private byte[] _deviceId; // 20
        public byte[] DeviceID
        {
            get
            {
                return this._deviceId;
            }
            set
            {
                if (value.Length != 0x14)
                    throw new XContentException("Attempted to set a device ID with an invalid length.");
                this._deviceId = value;
            }
        }

        private byte[] _consoleId; // 5
        public byte[] ConsoleID
        {
            get
            {
                return this._consoleId;
            }
            set
            {
                if (value.Length != 0x05)
                    throw new XContentException("Attempted to set a console ID with an invalid length.");
                this._consoleId = value;
            }
        }

        private uint _thumbnailSize;
        private byte[] _thumbnail;
        public byte[] Thumbnail
        {
            get
            {
                var newThumb = new byte[this._thumbnailSize];
                Array.Copy(this._thumbnail, newThumb, this._thumbnailSize);
                return newThumb;
            }
            set
            {
                if (value.Length > MaxThumbnailSize)
                    throw new Exception(string.Format("Invalid thumbnail size ({0}).", value.Length));

                this._thumbnailSize = (uint)value.Length;

                this._thumbnail = new byte[MaxThumbnailSize];
                Array.Copy(value, this._thumbnail, value.Length);
            }
        }

        private uint _titleThumbnailSize;
        private byte[] _titleThumbnail;
        public byte[] TitleThumbnail
        {
            get
            {
                var newThumb = new byte[this._titleThumbnailSize];
                Array.Copy(this._titleThumbnail, newThumb, this._titleThumbnailSize);
                return newThumb;
            }
            set
            {
                if (value.Length > MaxThumbnailSize)
                    throw new Exception(string.Format("Invalid thumbnail size ({0}).", value.Length));

                this._titleThumbnailSize = (uint)value.Length;

                this._titleThumbnail = new byte[MaxThumbnailSize];
                Array.Copy(value, this._titleThumbnail, value.Length);
            }
        }

        internal int MaxThumbnailSize
        {
            get { return this.ContentMetadataVersion >= 2 ? 0x3D00 : 0x4000; }
        }

        public XContentMetadata()
        {
            this._consoleId = new byte[5];
            this._reserved = new byte[32];
            this.DeviceID = new byte[20];
            this._thumbnail = new byte[0x3D00];
            this.TitleThumbnail = new byte[0x3D00];
        }

        private byte[] _descriptorData;
        public void ParseDescriptor()
        {
            var io = new EndianIO(this._descriptorData, EndianType.Big);
            if (this.VolumeType == XContentVolumeType.STFS)
                this.Descriptor = new StfsVolumeDescriptor(io);
            else if (this.VolumeType == XContentVolumeType.SVOD)
                this.Descriptor = new SvodDeviceDescriptor(io);
            io.Close();
        }

        public bool DescriptorParsed
        {
            get { return this.Descriptor != null; }
        }

        public XContentMetadata(EndianIO io)
        {
            this.ContentType = (XContentTypes)io.ReadUInt32();
            this.ContentMetadataVersion = io.ReadUInt32();
            this.ContentSize = io.ReadUInt64();
            this.ExecutionId = new XexExecutionId(io);
            this._consoleId = io.ReadByteArray(5);
            this.Creator = io.ReadUInt64();

            this._descriptorData = io.ReadByteArray(0x24);

            this.DataFiles = io.ReadUInt32();
            this.DataFilesSize = io.ReadUInt64();
            this.VolumeType = (XContentVolumeType)io.ReadUInt32();

            this.OnlineCreator = io.ReadUInt64();
            this.Category = io.ReadUInt32();

            this._reserved = io.ReadByteArray(32);

            if (this.ContentType == XContentTypes.AvatarAsset)
                this.AvatarAssetData = new XContentAvatarAssetData(io);
            else
                this.MediaData = new XContentMediaData(io);

            this._deviceId = io.ReadByteArray(20);

            int displayNameIndex = 0;
            for (; displayNameIndex < 9; displayNameIndex++)
            {
                this.DisplayNames.Add(io.ReadUnicodeString(128).RemoveNullBytes());
                if (_firstDisplayNameLanguage == -1 && this.DisplayNames[displayNameIndex].Length != 0)
                    _firstDisplayNameLanguage = displayNameIndex;
            }

            if (_firstDisplayNameLanguage == -1)
                _firstDisplayNameLanguage = 0;

            for (int x = 0; x < 9; x++)
            {
                this.Descriptions.Add(io.ReadUnicodeString(128).RemoveNullBytes());
                if (_firstDescriptionLanguage == -1 && this.Descriptions[x].Length != 0)
                    _firstDescriptionLanguage = x;
            }

            if (_firstDescriptionLanguage == -1)
                _firstDescriptionLanguage = 0;

            this.Publisher = io.ReadUnicodeString(64).RemoveNullBytes();
            this.TitleName = io.ReadUnicodeString(64).RemoveNullBytes();

            this.Flags = (XContentFlags)io.ReadByte();

            this._thumbnailSize = io.ReadUInt32();
            this._titleThumbnailSize = io.ReadUInt32();

            this._thumbnail = io.ReadByteArray(MaxThumbnailSize);

            if (this.ContentMetadataVersion >= 2)
                for (int x = 0; x < 3; x++)
                    DisplayNames.Add(io.ReadUnicodeString(128).RemoveNullBytes());

            this._titleThumbnail = io.ReadByteArray(MaxThumbnailSize);

            if (this.ContentMetadataVersion >= 2)
                for (int x = 0; x < 3; x++)
                    Descriptions.Add(io.ReadUnicodeString(128).RemoveNullBytes());

            if (this.ContentType != XContentTypes.Installer)
                return;

            this.InstallerType = (XContentInstallerTypes)io.ReadUInt32();

            switch (InstallerType)
            {
                case XContentInstallerTypes.SystemUpdate:
                case XContentInstallerTypes.TitleUpdate:
                    this.InstallerUpdateData = new XContentInstallerUpdateData(io);
                    break;
                case XContentInstallerTypes.SystemUpdateProgressCache:
                case XContentInstallerTypes.TitleUpdateProgressCache:
                case XContentInstallerTypes.TitleContentProgressCache:
                    this.InstallerProgressCacheData = new XContentInstallerProgressCacheData(io);
                    break;
                default:
                    this._unknownInstallerData = io.ReadByteArray(5616);
                    break;
            }
        }

        public void Write(EndianIO io)
        {
            io.Write((uint)ContentType);
            io.Write(this.ContentMetadataVersion);
            io.Write(this.ContentSize);
            this.ExecutionId.Write(io);
            io.Write(this._consoleId);
            io.Write(this.Creator);
            if (this.DescriptorParsed)
                io.Write((byte[])this.Descriptor.ToArray());
            else
                io.Write(this._descriptorData);
            io.Write(this.DataFiles);
            io.Write(this.DataFilesSize);
            io.Write((uint)this.VolumeType);
            io.Write(this.OnlineCreator);
            io.Write(this.Category);
            io.Write(this._reserved);

            if (this.ContentType == XContentTypes.AvatarAsset)
                this.AvatarAssetData.Write(io);
            else
                this.MediaData.Write(io);

            io.Write(this._deviceId);

            for (int x = 0; x < 9; x++)
                io.WriteUnicodeString(this.DisplayNames[x], 128);

            for (int x = 0; x < 9; x++)
                io.WriteUnicodeString(this.Descriptions[x], 128);

            io.WriteUnicodeString(this.Publisher, 64);
            io.WriteUnicodeString(this.TitleName, 64);

            io.Write((byte)this.Flags);

            io.Write(this._thumbnailSize);
            io.Write(this._titleThumbnailSize);

            io.Write(this._thumbnail);

            if (this.ContentMetadataVersion >= 2)
                for (int x = 9; x < 12; x++)
                    io.WriteUnicodeString(this.DisplayNames[x], 128);

            io.Write(this._titleThumbnail);

            if (this.ContentMetadataVersion >= 2)
                for (int x = 9; x < 12; x++)
                    io.WriteUnicodeString(this.Descriptions[x], 128);

            if (this.ContentType == XContentTypes.Installer)
            {
                io.Write((uint)this.InstallerType);
                switch (InstallerType)
                {
                    case XContentInstallerTypes.SystemUpdate:
                    case XContentInstallerTypes.TitleUpdate:
                        this.InstallerUpdateData.Write(io);
                        break;
                    case XContentInstallerTypes.SystemUpdateProgressCache:
                    case XContentInstallerTypes.TitleUpdateProgressCache:
                    case XContentInstallerTypes.TitleContentProgressCache:
                        this.InstallerProgressCacheData.Write(io);
                        break;
                    default:
                        io.Write(this._unknownInstallerData);
                        break;
                }
            }
        }
    }
}
