using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace NoDev.Common
{
    using ACCESS_MASK = UInt32;

    [SuppressUnmanagedCodeSecurity]
    public static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string lpFileName, FileAccess dwDesiredAccess, FileShare dwShareMode,
        IntPtr lpSecurityAttributes, FileMode dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("User32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        [DllImport("User32.dll")]
        public static extern int RegisterWindowMessage(string message);
    }

    public static class Win32
    {
        private static readonly char[] DriveLetters;

        static Win32()
        {
            var driveLetters = new List<char>(25);
            for (int x = 68; x <= 90; x++)
                driveLetters.Add((char)x);
            DriveLetters = driveLetters.ToArray();
        }

        public static char GetFreeDriveLetter()
        {
            DriveInfo[] logicalDrives = DriveInfo.GetDrives();

            char driveLetter = DriveLetters.FirstOrDefault(dL => logicalDrives.All(driveInfo => driveInfo.Name[0] != dL));

            if (driveLetter == '\0')
                throw new Exception("There are no available drive letters to use on your computer.");

            return driveLetter;
        }

        public static IEnumerable<char> GetFreeDriveLetters()
        {
            DriveInfo[] logicalDrives = DriveInfo.GetDrives();

            return DriveLetters.Where(dL => logicalDrives.All(driveInfo => driveInfo.Name[0] != dL));
        }

        public static string ProgramDataFolder
        {
            get
            {
                string dataFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\NoDev Development";
                if (!Directory.Exists(dataFolder))
                    Directory.CreateDirectory(dataFolder);
                return dataFolder;
            }
        }

        public static void ObDissectName(string path, out string firstName, out string remainingName)
        {
            if (path.Length == 0x00)
            {
                firstName = "";
                remainingName = "";
                return;
            }

            var startIndex = path[0x00] == '\\' ? 0x01 : 0x00;
            var idx = path.IndexOf('\\', startIndex);

            if (idx == -1)
            {
                firstName = path.Substring(startIndex, path.Length - startIndex);
                remainingName = "";
            }
            else
            {
                firstName = path.Substring(startIndex, idx - startIndex);
                remainingName = path.Substring(++idx, path.Length - idx);
            }
        }
    }

    public static class NT
    {
// ReSharper disable InconsistentNaming
        public const uint STATUS_SUCCESS = 0x00000000;
        public const uint STATUS_INVALID_PARAMETER = 0xC000000D;
        public const uint STATUS_NO_SUCH_FILE = 0xC000000F;
        public const uint STATUS_END_OF_FILE = 0xC0000011;
        public const uint STATUS_ACCESS_DENIED = 0xC0000022;
        public const uint STATUS_DISK_CORRUPT_ERROR = 0xC0000032;
        public const uint STATUS_OBJECT_NAME_INVALID = 0xC0000033;
        public const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;
        public const uint STATUS_OBJECT_NAME_COLLISION = 0xC0000035;
        public const uint STATUS_OBJECT_PATH_NOT_FOUND = 0xC000003A;
        public const uint STATUS_DELETE_PENDING = 0xC0000056;
        public const uint STATUS_DISK_FULL = 0xC000007F;
        public const uint STATUS_INSUFFICIENT_RESOURCES = 0xC000009A;
        public const uint STATUS_FILE_IS_A_DIRECTORY = 0xC00000BA;
        public const uint STATUS_DIRECTORY_NOT_EMPTY = 0xC0000101;
        public const uint STATUS_FILE_CORRUPT_ERROR = 0xC0000102;
        public const uint STATUS_NOT_A_DIRECTORY = 0xC0000103;
        public const uint STATUS_FILE_CLOSED = 0xC0000128;
        public const uint STATUS_VOLUME_DISMOUNTED = 0xC000026E;

        public const uint STATUS_BUFFER_OVERFLOW = 0x80000005;
        public const uint STATUS_NO_MORE_FILES = 0x80000006;
// ReSharper restore InconsistentNaming
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct LargeInteger
    {
        [FieldOffset(4)]
        public uint HighPart;
        [FieldOffset(0)]
        public uint LowPart;
        [FieldOffset(0)]
        public long QuadPart;
    }

    public enum FileInformationClass
    {
        FileBasicInformation = 0x04,
        FileRenameInformation = 0x0a,
        FileDispositionInformation = 0x0d,
        FilePositionInformation = 0x0e,
        FileAllocationInformation = 0x13,
        FileEndOfFileInformation = 0x14,
        FileMountPartitionInformation = 0x17
    }

    public struct FileEndOfFileInfo
    {
        public LargeInteger EndOfFile;

        public FileEndOfFileInfo(byte[] buffer)
        {
            EndOfFile = new LargeInteger { QuadPart = buffer.ReadInt64() };
        }
    }

    public struct FileAllocationInformation
    {
        public LargeInteger AllocationSize;
        public FileAllocationInformation(byte[] buffer)
        {
            AllocationSize = new LargeInteger { QuadPart = buffer.ReadInt64(0x00) };
        }
    }

    public struct FileRenameInformation
    {
        public bool ReplaceIfExists;
        public byte[] RootDirectory;
        public string FileName;

        public FileRenameInformation(byte[] buffer)
        {
            ReplaceIfExists = (buffer[0x00] & 0x01) != 0;
            RootDirectory = buffer.Read(0x04, 0x04);
            var fileNameLength = buffer.ReadInt16(0x08);
            FileName = System.Text.Encoding.ASCII.GetString(buffer.Read(0xC, fileNameLength));
        }
    }

    public struct FileDispositionInformation
    {
        public bool DeleteFile;
        public FileDispositionInformation(byte[] buffer)
        {
            DeleteFile = buffer[0x00] != 0x00;
        }
    }

    public struct FileBasicInformation
    {
        public DateTime CreationTime;
        public DateTime LastAccessTime;
        public DateTime LastWriteTime;
        public DateTime ChangeTime;
        public uint FileAttributes;
        public FileBasicInformation(byte[] buffer)
        {
            CreationTime = DateTime.FromBinary(buffer.ReadInt64(0x00));
            LastAccessTime = DateTime.FromBinary(buffer.ReadInt64(0x08));
            LastWriteTime = DateTime.FromBinary(buffer.ReadInt64(0x10));
            ChangeTime = DateTime.FromBinary(buffer.ReadInt64(0x18));
            FileAttributes = buffer.ReadUInt32(0x20);
        }
    }

    public class IoDirectoryEnumContext
    {
        public uint CurrentDirectoryOffset;
        public string SearchPattern;
    }

    public abstract class IoFcb
    {

    }

    public class IoFsdHandle
    {
        // Also contains the I/O Request Packet (IRP)

        public IoFcb Fcb;
        public IoDirectoryEnumContext DirectoryEnumContext;
        public int UncleanCount;
        public IRP Irp;
        public IoStackLocation Sp;
        public IoFsdHandle()
        {
            Sp = Irp.Sp = new IoStackLocation();
        }
    }

    public struct IRP
    {
        public struct IrpOverlay
        {
            public LargeInteger AllocationSize;
        }

        //
        // Define the create disposition values
        //

        public const uint FILE_SUPERSEDE = 0x00000000;
        public const uint FILE_OPEN = 0x00000001;
        public const uint FILE_CREATE = 0x00000002;
        public const uint FILE_OPEN_IF = 0x00000003;
        public const uint FILE_OVERWRITE = 0x00000004;
        public const uint FILE_OVERWRITE_IF = 0x00000005;
        public const uint FILE_MAXIMUM_DISPOSITION = 0x00000005;

        //
        // Define the create/open option flags
        //
        public const uint FILE_DIRECTORY_FILE = 0x00000001;
        public const uint FILE_NON_DIRECTORY_FILE = 0x00000040;

        public byte[] UserBuffer;


        public IoStatusBlock UserIosb;
        public IrpOverlay Overlay;
        public IoStackLocation Sp;
    }

    public class IoStackLocation
    {
        public const int SL_RESTART_SCAN = 0x01;
        public const int SL_RETURN_SINGLE_ENTRY = 0x02;
        public const int SL_OPEN_TARGET_DIRECTORY = 0x04;

        public byte MajorFunction;
        public byte MinorFunction;
        public byte Flags;
        public byte Control;
        public IrpCreateParameters CreateParametersParameters;
        public IrpParametersSetFile SetFileParameters;
        public IrpParametersQueryDirectory QueryDirectoryParameters;
        public IrpParametersRead ReadParameters;
        public DeviceObject DeviceObject;
        public FileObject FileObject = new FileObject();
    }

    public struct ShareAccess
    {
        public byte OpenCount;
        public byte Readers;
        public byte Writers;
        public byte Deleters;
        public byte SharedRead;
        public byte SharedWrite;
        public byte SharedDelete;
    }

    public struct IrpCreateParameters
    {
        public uint DesiredAccess;
        public uint Options;
        public ushort FileAttributes;
        public ushort ShareAccess;
        public string RemainingName;
    }

    public struct IrpParametersSetFile
    {
        public uint Length;
        public FileInformationClass FileInformationClass;
        public FileObject FileObject;
    }

    public struct IrpParametersQueryDirectory
    {
        public int Length;
        public string FileName;
    }

    public struct IrpParametersRead
    {
        public uint Length;
        public uint BufferOffset;
        public byte[] CacheBuffer;
        public long ByteOffset;
    }

    public struct DiskGeometry
    {
        public int Sectors;
        public uint BytesPerSector;
    }

    public struct IoFsdDirectoryInformation
    {
        public uint NextEntryOffset;
        public uint FileIndex;
        public DateTime CreationTime;
        public DateTime LastAccessTime;
        public DateTime LastWriteTime;
        public DateTime ChangeTime;
        public long EndOfFile;
        public long AllocationSize;
        public uint FileAttributes;
        public uint FileNameLength { get { return (uint)FileName.Length; } }
        public string FileName;
    }

    public struct DeviceObject
    {

    }

    public class FileObject
    {
        public short Type;
        public byte Flags;
        public byte Flags2;
        public DeviceObject DeviceObject;
        public IoFcb FsContext;
        public IoDirectoryEnumContext FsContext2;
        public int FinalStatus;
        public LargeInteger CurrentByteOffset;
        public FileObject RelatedFileObject;
        public IoCompletionContext CompletionContext;
        public int LockCount;
        public KEvent Lock;
        public KEvent Event;
        public ListEntry ProcessListEntry;
        public ListEntry FileSystemListEntry;
        public byte IoPriority;
        public byte[] PoolPading;
    }

    public struct IoStatusBlock
    {
        public uint Status;
        public IntPtr Pointer;
        public uint Information;
    }

    public struct DispatchHeader
    {
        public byte Type;
        public byte Absolule;
        public byte ProcessType;
        public byte Inserted;
        public int SignalState;
        public ListEntry WaitListHead;
    }

    public struct KEvent
    {
        public DispatchHeader DispatchHeader;
    }

    public struct IoCompletionContext
    {
        public IntPtr Port;
        public IntPtr Key;
    }

    public struct ListEntry
    {
        public IntPtr Flink;
        public IntPtr Blink;
    }

    public class IoFsd
    {
        public static void IoCreateDirectoryEnumContext(string searchPattern, bool unknown, out IoDirectoryEnumContext directoryEnumContext)
        {
            directoryEnumContext = new IoDirectoryEnumContext { CurrentDirectoryOffset = 0x00 };
            if (searchPattern == null || (searchPattern.Length == 0x01 && searchPattern[0x00] == '*'))
                return;
            directoryEnumContext.SearchPattern = searchPattern;
        }
        public static void IoSetShareAccess(uint desiredAccess, ushort desiredShareAccess, FileObject fileObject, ref ShareAccess shareAccess)
        {

        }
        public static uint IoCheckShareAccess(ACCESS_MASK desiredAccess, uint desiredShareAccess, FileObject fileObject, ref ShareAccess shareAccess, bool update)
        {

            return 0x00;
        }
    }

    public class IoLink
    {
        public IoFcb Fcb;
        public IoLink Flink;
        public IoLink Blink;

        public IoLink(IoFcb fcb)
        {
            Fcb = fcb;
        }
        public IoFcb GetFcb()
        {
            return Fcb;
        }
    }

    public class IoLinkledList
    {
        private IoLink _first;
        public bool IsEmpty
        {
            get
            {
                return _first == null;
            }
        }
        public IoLinkledList()
        {
            _first = null;
        }

        public IoLink Insert(IoFcb fcb)
        {
            IoLink link = new IoLink(fcb);
            link.Flink = _first;

            if (_first != null)
                _first.Blink = link;

            _first = link;

            return link;
        }

        public void InitializeListHead(IoFcb fcb)
        {
            if (_first == null)
            {
                _first = new IoLink(fcb);
                _first.Flink = _first.Blink = _first;
            }
        }

        public IoLink InsertHeadList(IoFcb fcb)
        {
            var entry = new IoLink(fcb);
            var nextEntry = _first.Flink;
            entry.Flink = nextEntry;
            entry.Blink = _first;
            nextEntry.Blink = entry;
            _first.Flink = entry;

            return entry;
        }

        public void RemoveHeadList()
        {
            var entry = _first.Flink;
            var nextEntry = entry.Flink;
            _first.Flink = nextEntry;
            nextEntry.Blink = _first;
        }

        public void InsertHeadList(IoLink entry)
        {
            var nextEntry = _first.Flink;
            entry.Flink = nextEntry;
            entry.Blink = _first;
            nextEntry.Blink = entry;
            _first.Flink = entry;
        }

        public IoFcb FirstEntrySList()
        {
            return _first.GetFcb();
        }
    }
}
