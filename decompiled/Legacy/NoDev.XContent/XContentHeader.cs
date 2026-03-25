using System.IO;
using NoDev.Common.IO;
using NoDev.Xbox360;

namespace NoDev.XContent
{
    public class XContentHeader
    {
        public XContentSignatureType SignatureType;
        public readonly dynamic Signature;
        public XContentLicense[] LicenseDescriptors;
        public byte[] ContentID;
        public int SizeOfHeaders;
        public XContentMetadata Metadata;

        public XContentHeader()
        {

        }

        public XContentHeader(EndianIO mainIo)
        {
            var io = new EndianIO(mainIo.ReadByteArray(0x344), EndianType.Big);

            this.SignatureType = (XContentSignatureType)io.ReadInt32();


            switch (this.SignatureType)
            {
                case XContentSignatureType.Console:
                    this.Signature = new XeConsoleSignature(io);
                    break;
                case XContentSignatureType.LIVE:
                case XContentSignatureType.PIRS:
                    this.Signature = new XContentSignature
                    {
                        Signature = io.ReadByteArray(256),
                        Reserved = io.ReadByteArray(296)
                    };
                    break;
                default:
                    throw new XContentException("Invalid signature type detected."
                        + "\nPlease make sure the file is a valid Xbox 360 XContent Package ('LIVE', 'PIRS', 'CON ')");
            }

            this.LicenseDescriptors = new XContentLicense[0x10];

            for (int x = 0; x < 0x10; x++)
                this.LicenseDescriptors[x] = new XContentLicense(io);

            this.ContentID = io.ReadByteArray(0x14); // also the highest level hash
            this.SizeOfHeaders = io.ReadInt32();

            io.Close();

            int remainingHeader = (int)((this.SizeOfHeaders + 0xFFF) & 0xFFFFF000) - 0x344;

            io = new EndianIO(mainIo.ReadByteArray(remainingHeader), EndianType.Big);
            this.Metadata = new XContentMetadata(io);
            io.Close();
        }

        public byte[] ToArray()
        {
            var io = new EndianIO(new MemoryStream(this.SizeOfHeaders), EndianType.Big);

            io.Write((uint)SignatureType);
            switch (this.SignatureType)
            {
                case XContentSignatureType.Console:
                    io.Write(new byte[0x228]);
                    break;
                case XContentSignatureType.LIVE:
                case XContentSignatureType.PIRS:
                    XContentSignature signature = this.Signature;
                    io.Write(signature.Signature);
                    io.Write(signature.Reserved);
                    break;
                default:
                    throw new XContentException("Invalid signature type detected.");
            }

            for (int x = 0; x < 0x10; x++)
                this.LicenseDescriptors[x].Write(io);

            io.Write(this.ContentID);
            io.Write(this.SizeOfHeaders);

            this.Metadata.Write(io);

            io.Close();

            return io.ToArray();
        }
    }
}
