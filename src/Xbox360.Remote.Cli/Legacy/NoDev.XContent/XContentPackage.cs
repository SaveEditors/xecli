using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NoDev.Common;
using NoDev.Common.IO;
using NoDev.Xbox360;

namespace NoDev.XContent
{
    public class XContentPackage
    {
        public XContentHeader Header { get; private set; } = null!;
        public bool SignatureValidated { get; private set; }
        public string Filename { get; private set; } = string.Empty;
        public DriveInfo? Drive { get; private set; }

        private dynamic _device = null!;
        private EndianIO _io = null!;

        private readonly FileAccess _fileAccess;

        private static readonly Dictionary<XContentVolumeType, ConstructorInfo> Devices;

        static XContentPackage()
        {
            Devices = new Dictionary<XContentVolumeType, ConstructorInfo>(2);
        }

        private XContentPackage()
        {

        }

        public XContentPackage(string filename)
        {
            this.Filename = filename;
            EndianIO? io = null;
            try
            {
                this._fileAccess = (File.GetAttributes(filename) & FileAttributes.ReadOnly) != 0 ? FileAccess.Read : FileAccess.ReadWrite;
                io = new EndianIO(filename, EndianType.Big, FileMode.Open, this._fileAccess);
                this.Read(io);
            }
            catch
            {
                if (io != null)
                    io.Close();
                throw;
            }
        }

        private void Read(EndianIO io)
        {
            io.Position = 0x00;

            Header = new XContentHeader(io);

            if (this.Header.SignatureType == XContentSignatureType.Console)
            {
                io.Position = 0x22c;
                this.SignatureValidated = this.Header.Signature.Verify(io.ReadByteArray(0x118));
            }

            this._io = io;
        }

        public async Task Move(string newLocation)
        {
            if (File.Exists(newLocation))
                File.Delete(newLocation);

            bool mounted = this.IsMounted;

            this.Close();

            var fs1 = File.OpenRead(this.Filename);
            var fs2 = File.OpenWrite(newLocation);

            await fs1.CopyToAsync(fs2);

            fs1.Close();
            fs2.Close();

            this.Filename = newLocation;

            this.Open();

            if (mounted)
                this.Mount();
        }

        public XContentVolumeType VolumeType
        {
            get { return this.Header.Metadata.VolumeType; }
        }

        public string FormatFATXDevicePath(bool fixFileName = false)
        {
            var meta = this.Header.Metadata;
            return string.Format(
                @"Content\{0:X16}\{1:X8}\{2:X8}\{3}", 
                meta.Creator, 
                meta.ExecutionId.TitleID, 
                (int)meta.ContentType,
                fixFileName ? GetMagicFileName() : Path.GetFileName(this.Filename));
        }

        public string? GetMagicFileName()
        {
            switch (this.Header.Metadata.ContentType)
            {
                case XContentTypes.Profile:
                    return this.Header.Metadata.Creator.ToString("X16");
                case XContentTypes.GamerPicture:
                case XContentTypes.ThematicSkin:
                    if (this.Header.SignatureType != XContentSignatureType.Console)
                        goto createHash;
                    break;
                case XContentTypes.Installer:
                    return String.Format("tu{0:X8}_{1:X8}", this.Header.Metadata.ExecutionId.Version, 0);
                case XContentTypes.InstalledXbox360Title:
                case XContentTypes.Xbox360Title:
                    var hashName = new byte[17];
                    Array.Copy(this.Header.ContentID, hashName, 16);
                    hashName[16] = (byte)(this.Header.Metadata.ExecutionId.TitleID >> 24);
                    return Formatting.ByteArrayToHexString(hashName, true);
                case XContentTypes.GameTrailer:
                case XContentTypes.Arcade:
                case XContentTypes.Marketplace:
                case XContentTypes.Video:
                createHash:
                    hashName = new byte[21];
                    Array.Copy(this.Header.ContentID, hashName, 20);
                    hashName[20] = (byte)(this.Header.Metadata.ExecutionId.TitleID >> 24);
                    return Formatting.ByteArrayToHexString(hashName, true);
                case XContentTypes.AvatarAsset:
                    return Formatting.ByteArrayToHexString(this.Header.Metadata.AvatarAssetData.AssetID, true);
            }
            return null;
        }

        public bool IsReadOnly
        {
            get
            {
                if (this.Header.Metadata.VolumeType == XContentVolumeType.SVOD)
                    return true;

                if (!this.Header.Metadata.DescriptorParsed)
                    this.Header.Metadata.ParseDescriptor();

                return this.Header.Metadata.Descriptor.ReadOnlyFormat == 1;
            }
        }

        public void Mount()
        {
            if (!this.IsOpened)
                throw new XContentException("Attempted to mount a closed package.");

            if (this.IsMounted)
                throw new XContentException("Attempted to mount an already mounted device.");

            this.Drive = null;

            if (!this.SupportsVolume)
                throw new XContentException("Unsupported volume type specified in package header.");

            char driveLetter = Win32.GetFreeDriveLetter();

            string mountingPoint = driveLetter + ":";

            if (!this.Header.Metadata.DescriptorParsed)
                this.Header.Metadata.ParseDescriptor();

            object[] args;

            if (this.Header.Metadata.VolumeType == XContentVolumeType.STFS)
                args = new object[] { mountingPoint, this._io.Stream, this.CreateStfsPacket(), this.Header.Metadata.DisplayName };
            else
                args = new object[] { mountingPoint, this.Filename + ".data", this.CreateSvodPacket() };

            this._device = Devices[this.Header.Metadata.VolumeType].Invoke(args);

            this.Drive = new DriveInfo(driveLetter.ToString(CultureInfo.InvariantCulture));
        }

        public void UnMount()
        {
            if (!this.IsMounted)
                return;

            this._device.Unmount();
            this._device = null!;
            this.Drive = null;
        }

        public bool IsMounted
        {
            get { return this._device != null && this.Drive?.IsReady == true; }
        }

        public bool IsOpened
        {
            get { return this._io != null; }
        }

        public void Open()
        {
            if (this.IsOpened)
                return;


            this._io = new EndianIO(this.Filename, EndianType.Big, FileMode.Open, this._fileAccess);
        }

        public void Close()
        {
            this.UnMount();

            if (this._io != null)
            {
                this._io.Close();
                this._io = null!;
            }
        }

        public bool SupportsVolume
        {
            get { return Devices.ContainsKey(this.Header.Metadata.VolumeType); }
        }

        public static void RegisterDevice(XContentVolumeType volumeType, Type deviceType)
        {
            if (!deviceType.IsClass)
                return;

            ConstructorInfo? ctor;

            if (volumeType == XContentVolumeType.STFS)
                ctor = deviceType.GetConstructor(new[] { typeof(string), typeof(Stream), typeof(StfsCreatePacket), typeof(string) });
            else if (volumeType == XContentVolumeType.SVOD)
                ctor = deviceType.GetConstructor(new[] { typeof(string), typeof(string), typeof(SvodCreatePacket) });
            else
                throw new XContentException("Attempted to register an unknown XContentVolumeType.");

            if (ctor == null)
                throw new XContentException("No supported constructor was found in the device type.");

            Devices.Add(volumeType, ctor);
        }

        private StfsCreatePacket CreateStfsPacket()
        {
            var createPacket = new StfsCreatePacket();

            createPacket.BackingMaximumVolumeSize = ((this.Header.Metadata.ContentSize + 0x100000) - 1) & 0xFFFFFFFFFFF00000;
            if (createPacket.BackingMaximumVolumeSize == 0)
                createPacket.BackingMaximumVolumeSize = 0x0000000100000000;
            createPacket.BackingFileOffset = (this.Header.SizeOfHeaders + 0xFFF) & 0xFFFFF000;
            createPacket.BackingFilePresized = (byte)((this.Header.Metadata.ContentSize) == 0 ? 0 : 1);
            createPacket.DeviceExtensionSize = (uint)createPacket.BackingFileOffset + 0x19A;
            createPacket.VolumeDescriptor = (StfsVolumeDescriptor)this.Header.Metadata.Descriptor;
            createPacket.BlockCacheElementCount = 0x10;

            switch (this.Header.SignatureType)
            {
                case XContentSignatureType.LIVE:
                    createPacket.DeviceCharacteristics = 3;
                    break;
                case XContentSignatureType.PIRS:
                    createPacket.DeviceCharacteristics = 4;
                    break;
                case XContentSignatureType.Console:
                    // 2 = Signed to same console.
                    createPacket.DeviceCharacteristics = 1;
                    break;
            }

            return createPacket;
        }

        private SvodCreatePacket CreateSvodPacket()
        {
            var descriptor = (SvodDeviceDescriptor) this.Header.Metadata.Descriptor;

            return new SvodCreatePacket
            {
                Descriptor = descriptor,
                FsCache = new byte[(descriptor.BlockCacheElementCount << 0xC) & 0x000FF000]
            };
        }

        public void SaveIfModified()
        {
            if (this.Header.Metadata.VolumeType == XContentVolumeType.STFS)
            {
                if (!this.IsMounted)
                    return;

                if (this._device.IsModified)
                    this.Save();
            }
        }

        public void Save()
        {
            if (this.Header.Metadata.VolumeType != XContentVolumeType.STFS)
                return;

            if (this.Header.SignatureType == XContentSignatureType.Console)
                KeyStorage.EnsureLoaded();

            this.Open();

            if (this.IsMounted)
            {
                this._device.StfsFlush();

                if (this.Header.Metadata.ContentSize != 0)
                    this.Header.Metadata.ContentSize = (this._device.VolumeExtension.NumberOfExtendedBlocks * 0x1000);

                this._device.SetVolumeLabel(this.Header.Metadata.DisplayName);
            }

            this.SaveContentHeader();

            if (this.Header.SignatureType == XContentSignatureType.Console)
            {
                this._io.Position = 0x00;
                this._io.Write((int)XContentSignatureType.Console);
                this._io.Write(KeyStorage.PublicKey);

                this._io.Position = 0x22C;
                byte[] sig = XeCrypt.FormatSignature(KeyStorage.PrivateKeys, this._io.ReadByteArray(0x118));

                this._io.Position = 0x1ac;
                this._io.Write(sig);

                this.SignatureValidated = true;
            }

            this._io.Flush();
        }

        private void SaveContentHeader()
        {
            this._io.Position = 0;
            this._io.Write(this.Header.ToArray());

            this._io.Position = 0x344;

            int length;
            if (this.IsMounted)
                length = (int)this._device.VolumeExtension.BackingFileOffset;
            else
                length = (int)((this.Header.SizeOfHeaders + 0xFFF) & 0xFFFFF000);

            length -= 0x344;

            byte[] hash = this._io.ReadByteArray(length).ComputeSHA1();

            this._io.Position = 0x32c;
            this._io.Write(hash);
        }

        public static XContentPackage CreateStfsPackage(string filename, XContentMetadata metaData)
        {
            if (!Devices.ContainsKey(XContentVolumeType.STFS))
                throw new XContentException("The STFS volume is not implemented.");

            char driveLetter = Win32.GetFreeDriveLetter();

            var package = new XContentPackage();

            package.Filename = filename;

            var io = new EndianIO(filename, EndianType.Big, FileMode.Create);

            io.SetLength(0xA000);

            var header = new XContentHeader();

            header.SignatureType = XContentSignatureType.Console;

            header.LicenseDescriptors = new XContentLicense[0x10];

            header.LicenseDescriptors[0].Type = ushort.MaxValue;
            header.LicenseDescriptors[0].Data = ulong.MaxValue;
            header.LicenseDescriptors[0].Bits = 0;
            header.LicenseDescriptors[0].Flags = 0;

            header.ContentID = new byte[0x14];
            header.SizeOfHeaders = 0x0000971A;

            metaData.Descriptor = new StfsVolumeDescriptor();

            header.Metadata = metaData;

            io.Write(header.ToArray());

            package.Header = header;

            var createPacket = new StfsCreatePacket
            {
                BackingFileOffset = 0xA000,
                BackingMaximumVolumeSize = 0x0000000100000000,
                DeviceExtensionSize = 0xA19A,
                BlockCacheElementCount = 0x10,
                VolumeDescriptor = (StfsVolumeDescriptor)metaData.Descriptor,
                DeviceCharacteristics = 0x01
            };

            package._device = Devices[XContentVolumeType.STFS].Invoke(new object[] { driveLetter + ":", io, createPacket, metaData.DisplayName });

            package.Drive = new DriveInfo(driveLetter.ToString(CultureInfo.InvariantCulture));

            package._io = io;

            package.Save();

            return package;
        }
    }
}
