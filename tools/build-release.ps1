[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?(?:\+[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Version,

    [ValidateSet('Local', 'Promotion')]
    [string]$Mode = 'Local',

    [switch]$RequireSigning,
    [switch]$AllowUnsignedPromotion,
    [switch]$BuildInstaller,
    [switch]$DryRun,
    [string]$DotNetPath = 'dotnet',
    [string]$PackagesPath,
    [string]$IsccPath,
    [string]$SignToolPath,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl,
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$MinimumPowerShellVersion = [Version]'7.1'
if ($PSVersionTable.PSVersion -lt $MinimumPowerShellVersion) {
    throw "XeCLI release builds require PowerShell $MinimumPowerShellVersion or later."
}

$Runtime = 'win-x64'
$PublicUnsignedWarning = 'WARNING: Publisher is Unknown. Windows SmartScreen may warn or block this unsigned public release. Verify the SHA256 inventory before running any artifact.'
$QuickBootX360DllSha256 = 'F90691B92A91FFA941BB111039DE031907A82A510C777231FE9C6D356DA437A9'
$RequiredThirdPartyLicenseFiles = @(
    'MIT.txt',
    'dotnet-10.0.0-THIRD-PARTY-NOTICES.txt'
)
$RequiredPackagedThirdPartyLicensePaths = @(
    'MIT.txt',
    'dotnet-10.0.0-THIRD-PARTY-NOTICES.txt',
    'XeLL/GCC-RUNTIME-LIBRARY-EXCEPTION.txt',
    'XeLL/GPL-2.0.txt',
    'XeLL/NEWLIB-NOTICES.txt',
    'XeLL/ADDITIONAL-NOTICES.txt'
)
$RequiredFirstPartyPayloadPaths = @(
    'payload/xell.bin',
    'launch/XellLaunch/default.xex',
    'launch/QuickBoot/default.xex'
)
$FirstPartyPayloadStagePaths = [ordered]@{
    'payload/xell.bin' = 'Assets/XellLaunch/xell.bin'
    'launch/XellLaunch/default.xex' = 'Assets/XellLaunch/default.xex'
    'launch/QuickBoot/default.xex' = 'Assets/QuickBoot/default.xex'
}
$FirstPartyPayloadReleaseManifestPath = 'PROVENANCE/CONSOLE-PAYLOADS.sha256'
$CorrespondingSourceReleasePath = 'PROVENANCE/CORRESPONDING-SOURCE.md'
$PublicRepositoryUrl = 'https://github.com/SaveEditors/xecli'
$script:FirstPartyPayloadManifestRecords = $null
$script:X360SourceManifestRecords = $null
$script:PinnedPromotionGitRevision = $null
$script:PinnedPromotionGitTree = $null
$OwnedFilesManifestName = 'xecli-owned-files.txt'
$OwnedHashesManifestName = 'xecli-owned-hashes.sha256'
$PreviousOwnedFilesManifestName = 'xecli-owned-files.previous.txt'
$PreviousOwnedFilesManifestTempName = 'xecli-owned-files.previous.tmp'
$ReleaseManifestName = 'release-manifest.json'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepoRoot 'artifacts\release'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-ToolPath {
    param(
        [string]$Candidate,
        [string]$DisplayName
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        throw "$DisplayName was not provided."
    }

    if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "$DisplayName was not found: $Candidate"
    }

    return $command.Source
}

function Invoke-NativeCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$FailureMessage
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Get-RelativeFilePath {
    param(
        [string]$Root,
        [string]$Path
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $filePath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $rootPath + [System.IO.Path]::DirectorySeparatorChar
    if (-not $filePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected root: $filePath"
    }

    return $filePath.Substring($prefix.Length)
}

function Assert-GeneratedPath {
    param(
        [string]$Path,
        [string]$Parent
    )

    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    $candidatePath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $parentPath + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidatePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the release output root: $candidatePath"
    }
}

function Test-PathEntryExistsNoFollow {
    param([string]$Path)

    try {
        [void][System.IO.File]::GetAttributes([System.IO.Path]::GetFullPath($Path))
        return $true
    }
    catch [System.IO.FileNotFoundException], [System.IO.DirectoryNotFoundException] {
        return $false
    }
}

function New-GeneratedDirectory {
    param(
        [string]$Path,
        [string]$Parent
    )

    Assert-GeneratedPath -Path $Path -Parent $Parent
    if (Test-PathEntryExistsNoFollow -Path $Path) {
        throw "Refusing to reuse a generated directory: $Path"
    }
    New-Item -ItemType Directory -Path $Path | Out-Null
}

function Assert-NotReparseEntry {
    param(
        [string]$Path,
        [string]$Label
    )

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label is a reparse entry and cannot be packaged safely: $Path"
    }
}

function Assert-NoReparseEntries {
    param(
        [string]$Root,
        [string]$Label
    )

    Assert-NotReparseEntry -Path $Root -Label $Label
    $reparseEntries = @(
        Get-ChildItem -LiteralPath $Root -Force -Recurse |
            Where-Object {
                ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
            }
    )
    if ($reparseEntries.Count -ne 0) {
        $paths = ($reparseEntries | ForEach-Object FullName) -join ', '
        throw "$Label contains reparse entries and cannot be packaged safely: $paths"
    }
}

function Initialize-ReleaseFileSystemInterop {
    if ($null -eq ('XeCLI.Release.HandleBoundTreeDeleter' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XeCLI.Release
{
    public static class HandleBoundTreeDeleter
    {
        private const uint DeleteAccess = 0x00010000;
        private const uint FileListDirectory = 0x00000001;
        private const uint FileReadAttributes = 0x00000080;
        private const uint FileWriteAttributes = 0x00000100;
        private const uint Synchronize = 0x00100000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint FileShareAll = 0x00000007;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint ObjCaseInsensitive = 0x00000040;
        private const uint FileOpen = 1;
        private const uint FileCreate = 2;
        private const uint FileDirectoryFile = 0x00000001;
        private const uint FileSynchronousIoNonAlert = 0x00000020;
        private const uint FileOpenForBackupIntent = 0x00004000;
        private const uint FileOpenReparsePoint = 0x00200000;
        private const uint FileAttributeReadonly = 0x00000001;
        private const uint FileAttributeDirectory = 0x00000010;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint FileAttributeReparsePoint = 0x00000400;
        private const int FileBasicInfo = 0;
        private const int FileDispositionInfo = 4;
        private const int FileAttributeTagInfo = 9;
        private const int FileDispositionInfoEx = 21;
        private const uint FileDispositionDelete = 0x00000001;
        private const uint FileDispositionPosixSemantics = 0x00000002;
        private const uint FileDispositionIgnoreReadonlyAttribute = 0x00000010;
        private const int FileNamesInformation = 12;
        private const int StatusSuccess = 0;
        private const int StatusNoMoreFiles = unchecked((int)0x80000006);
        private const int ErrorInvalidFunction = 1;
        private const int ErrorFileNotFound = 2;
        private const int ErrorPathNotFound = 3;
        private const int ErrorNotSupported = 50;
        private const int ErrorInvalidParameter = 87;
        private const int ErrorDirectoryNotEmpty = 145;
        private const int MaxDepth = 256;
        private const int MaxDirectoryPasses = 8;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectAttributes
        {
            public uint Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoStatusBlock
        {
            public IntPtr Status;
            public UIntPtr Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileAttributeTagInfoBody
        {
            public uint FileAttributes;
            public uint ReparseTag;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileDispositionInfoExBody
        {
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FileDispositionInfoBody
        {
            public byte DeleteFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileBasicInfoBody
        {
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public long ChangeTime;
            public uint FileAttributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTimeBody
        {
            public uint LowDateTime;
            public uint HighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformationBody
        {
            public uint FileAttributes;
            public FileTimeBody CreationTime;
            public FileTimeBody LastAccessTime;
            public FileTimeBody LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformationBody information);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            int informationClass,
            out FileAttributeTagInfoBody information,
            uint bufferSize);

        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandleEx(
            SafeFileHandle file,
            int informationClass,
            ref FileDispositionInfoExBody information,
            uint bufferSize);

        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandleLegacy(
            SafeFileHandle file,
            int informationClass,
            ref FileDispositionInfoBody information,
            uint bufferSize);

        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandleBasic(
            SafeFileHandle file,
            int informationClass,
            ref FileBasicInfoBody information,
            uint bufferSize);

        [DllImport("ntdll.dll")]
        private static extern int NtCreateFile(
            out IntPtr fileHandle,
            uint desiredAccess,
            ref ObjectAttributes objectAttributes,
            out IoStatusBlock ioStatusBlock,
            IntPtr allocationSize,
            uint fileAttributes,
            uint shareAccess,
            uint createDisposition,
            uint createOptions,
            IntPtr eaBuffer,
            uint eaLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryDirectoryFile(
            SafeFileHandle fileHandle,
            IntPtr eventHandle,
            IntPtr apcRoutine,
            IntPtr apcContext,
            out IoStatusBlock ioStatusBlock,
            IntPtr fileInformation,
            uint length,
            int fileInformationClass,
            [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry,
            IntPtr fileName,
            [MarshalAs(UnmanagedType.U1)] bool restartScan);

        [DllImport("ntdll.dll")]
        private static extern uint RtlNtStatusToDosError(int status);

        public static SafeFileHandle CreatePinnedRoot(string rootPath)
        {
            string nativePath = ToNativePath(rootPath);
            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr unicodeStringBuffer = IntPtr.Zero;
            try
            {
                ObjectAttributes attributes = CreateObjectAttributes(
                    nativePath,
                    IntPtr.Zero,
                    out nameBuffer,
                    out unicodeStringBuffer);
                IoStatusBlock ioStatus;
                IntPtr rawHandle;
                int status = NtCreateFile(
                    out rawHandle,
                    FileListDirectory | FileReadAttributes | Synchronize,
                    ref attributes,
                    out ioStatus,
                    IntPtr.Zero,
                    0,
                    FileShareRead | FileShareWrite,
                    FileCreate,
                    FileDirectoryFile | FileSynchronousIoNonAlert |
                        FileOpenForBackupIntent | FileOpenReparsePoint,
                    IntPtr.Zero,
                    0);
                if (status != StatusSuccess)
                    throw new Win32Exception(
                        unchecked((int)RtlNtStatusToDosError(status)),
                        "Unable to atomically create and pin the release output root.");

                SafeFileHandle root = new SafeFileHandle(rawHandle, true);
                try
                {
                    ValidatePinnedDirectory(root);
                    return root;
                }
                catch
                {
                    root.Dispose();
                    throw;
                }
            }
            finally
            {
                FreeObjectAttributes(nameBuffer, unicodeStringBuffer);
            }
        }

        public static SafeFileHandle OpenPinnedRoot(string rootPath)
        {
            string nativePath = ToNativePath(rootPath);
            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr unicodeStringBuffer = IntPtr.Zero;
            try
            {
                ObjectAttributes attributes = CreateObjectAttributes(
                    nativePath,
                    IntPtr.Zero,
                    out nameBuffer,
                    out unicodeStringBuffer);
                IoStatusBlock ioStatus;
                IntPtr rawHandle;
                int status = NtCreateFile(
                    out rawHandle,
                    FileListDirectory | FileReadAttributes | Synchronize,
                    ref attributes,
                    out ioStatus,
                    IntPtr.Zero,
                    0,
                    FileShareRead | FileShareWrite,
                    FileOpen,
                    FileDirectoryFile | FileSynchronousIoNonAlert |
                        FileOpenForBackupIntent | FileOpenReparsePoint,
                    IntPtr.Zero,
                    0);
                if (status != StatusSuccess)
                    throw new Win32Exception(
                        unchecked((int)RtlNtStatusToDosError(status)),
                        "Unable to open and pin the release output path root.");

                SafeFileHandle root = new SafeFileHandle(rawHandle, true);
                try
                {
                    ValidatePinnedDirectory(root);
                    return root;
                }
                catch
                {
                    root.Dispose();
                    throw;
                }
            }
            finally
            {
                FreeObjectAttributes(nameBuffer, unicodeStringBuffer);
            }
        }

        public static SafeFileHandle OpenPinnedChildDirectory(
            SafeFileHandle parent,
            string entryName)
        {
            ValidateEntryName(entryName);
            ValidatePinnedDirectory(parent);
            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr unicodeStringBuffer = IntPtr.Zero;
            bool parentHandleAdded = false;
            try
            {
                parent.DangerousAddRef(ref parentHandleAdded);
                ObjectAttributes attributes = CreateObjectAttributes(
                    entryName,
                    parent.DangerousGetHandle(),
                    out nameBuffer,
                    out unicodeStringBuffer);
                IoStatusBlock ioStatus;
                IntPtr rawHandle;
                int status = NtCreateFile(
                    out rawHandle,
                    FileListDirectory | FileReadAttributes | Synchronize,
                    ref attributes,
                    out ioStatus,
                    IntPtr.Zero,
                    0,
                    FileShareRead | FileShareWrite,
                    FileOpen,
                    FileDirectoryFile | FileSynchronousIoNonAlert |
                        FileOpenForBackupIntent | FileOpenReparsePoint,
                    IntPtr.Zero,
                    0);
                if (status != StatusSuccess)
                {
                    int error = unchecked((int)RtlNtStatusToDosError(status));
                    if (error == ErrorFileNotFound || error == ErrorPathNotFound)
                        return null;
                    throw new Win32Exception(error,
                        "Unable to open and pin a release output path component.");
                }

                SafeFileHandle child = new SafeFileHandle(rawHandle, true);
                try
                {
                    ValidatePinnedDirectory(child);
                    return child;
                }
                catch
                {
                    child.Dispose();
                    throw;
                }
            }
            finally
            {
                FreeObjectAttributes(nameBuffer, unicodeStringBuffer);
                if (parentHandleAdded)
                    parent.DangerousRelease();
            }
        }

        public static SafeFileHandle CreatePinnedChildDirectory(
            SafeFileHandle parent,
            string entryName)
        {
            ValidateEntryName(entryName);
            ValidatePinnedDirectory(parent);
            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr unicodeStringBuffer = IntPtr.Zero;
            bool parentHandleAdded = false;
            try
            {
                parent.DangerousAddRef(ref parentHandleAdded);
                ObjectAttributes attributes = CreateObjectAttributes(
                    entryName,
                    parent.DangerousGetHandle(),
                    out nameBuffer,
                    out unicodeStringBuffer);
                IoStatusBlock ioStatus;
                IntPtr rawHandle;
                int status = NtCreateFile(
                    out rawHandle,
                    DeleteAccess | FileListDirectory | FileReadAttributes |
                        FileWriteAttributes | Synchronize,
                    ref attributes,
                    out ioStatus,
                    IntPtr.Zero,
                    0,
                    FileShareRead | FileShareWrite,
                    FileCreate,
                    FileDirectoryFile | FileSynchronousIoNonAlert |
                        FileOpenForBackupIntent | FileOpenReparsePoint,
                    IntPtr.Zero,
                    0);
                if (status != StatusSuccess)
                    throw new Win32Exception(
                        unchecked((int)RtlNtStatusToDosError(status)),
                        "Unable to atomically create and pin release scratch relative to the output root.");

                SafeFileHandle child = new SafeFileHandle(rawHandle, true);
                try
                {
                    ValidatePinnedDirectory(child);
                    return child;
                }
                catch
                {
                    child.Dispose();
                    throw;
                }
            }
            finally
            {
                FreeObjectAttributes(nameBuffer, unicodeStringBuffer);
                if (parentHandleAdded)
                    parent.DangerousRelease();
            }
        }

        public static void AssertPathMatchesHandle(SafeFileHandle expected, string path)
        {
            ValidatePinnedDirectory(expected);
            IntPtr rawActual = CreateFileW(
                ToExtendedPath(path),
                FileListDirectory | FileReadAttributes | Synchronize,
                FileShareAll,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (rawActual == InvalidHandleValue)
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "A pinned release workspace path is unavailable; the build was aborted.");

            using (SafeFileHandle actual = new SafeFileHandle(rawActual, true))
            {
                ValidatePinnedDirectory(actual);
                ByHandleFileInformationBody expectedInfo = GetIdentity(expected);
                ByHandleFileInformationBody actualInfo = GetIdentity(actual);
                if (expectedInfo.VolumeSerialNumber != actualInfo.VolumeSerialNumber ||
                    expectedInfo.FileIndexHigh != actualInfo.FileIndexHigh ||
                    expectedInfo.FileIndexLow != actualInfo.FileIndexLow)
                    throw new IOException(
                        "A pinned release workspace path changed identity; the build was aborted and scratch was retained.");
            }
        }

        public static void DeletePinnedTree(SafeFileHandle entry)
        {
            ValidatePinnedDirectory(entry);
            DeleteOpenedEntry(entry, 0);
        }

        public static void DeleteChild(SafeFileHandle parent, string entryName)
        {
            ValidateEntryName(entryName);
            ValidatePinnedDirectory(parent);
            using (SafeFileHandle child = OpenRelative(parent, entryName))
            {
                if (child == null)
                    return;
                DeleteOpenedEntry(child, 0);
            }
        }

        public static void AssertChildMissing(SafeFileHandle parent, string entryName)
        {
            ValidateEntryName(entryName);
            ValidatePinnedDirectory(parent);
            using (SafeFileHandle child = OpenRelative(parent, entryName))
            {
                if (child != null)
                    throw new IOException(
                        "Release scratch remained after handle-bound cleanup; the build was aborted.");
            }
        }

        private static void DeleteOpenedEntry(SafeFileHandle entry, int depth)
        {
            if (depth > MaxDepth)
                throw new IOException("Scratch cleanup exceeded the maximum directory depth.");

            FileAttributeTagInfoBody info = GetAttributes(entry);
            bool isDirectory = (info.FileAttributes & FileAttributeDirectory) != 0;
            bool isReparsePoint = (info.FileAttributes & FileAttributeReparsePoint) != 0;
            if (!isDirectory || isReparsePoint)
            {
                MarkDelete(entry);
                return;
            }

            for (int pass = 0; pass < MaxDirectoryPasses; pass++)
            {
                foreach (string childName in EnumerateNames(entry))
                {
                    using (SafeFileHandle child = OpenRelative(entry, childName))
                    {
                        if (child != null)
                            DeleteOpenedEntry(child, depth + 1);
                    }
                }

                int deleteError;
                if (TryMarkDelete(entry, out deleteError))
                    return;
                if (deleteError != ErrorDirectoryNotEmpty)
                    throw new Win32Exception(deleteError,
                        "Handle-bound scratch directory deletion failed.");
            }

            throw new IOException(
                "Scratch directory remained non-empty during handle-bound cleanup; it was retained.");
        }

        private static SafeFileHandle OpenRelative(SafeFileHandle parent, string name)
        {
            ValidateEntryName(name);
            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr unicodeStringBuffer = IntPtr.Zero;
            bool parentHandleAdded = false;
            try
            {
                parent.DangerousAddRef(ref parentHandleAdded);
                ObjectAttributes attributes = CreateObjectAttributes(
                    name,
                    parent.DangerousGetHandle(),
                    out nameBuffer,
                    out unicodeStringBuffer);
                IoStatusBlock ioStatus;
                IntPtr rawHandle;
                int status = NtCreateFile(
                    out rawHandle,
                    DeleteAccess | FileListDirectory | FileReadAttributes |
                        FileWriteAttributes | Synchronize,
                    ref attributes,
                    out ioStatus,
                    IntPtr.Zero,
                    0,
                    FileShareAll,
                    FileOpen,
                    FileSynchronousIoNonAlert | FileOpenForBackupIntent |
                        FileOpenReparsePoint,
                    IntPtr.Zero,
                    0);
                if (status == StatusSuccess)
                    return new SafeFileHandle(rawHandle, true);

                int error = unchecked((int)RtlNtStatusToDosError(status));
                if (error == ErrorFileNotFound || error == ErrorPathNotFound)
                    return null;
                throw new Win32Exception(error,
                    "Unable to open a scratch entry relative to its pinned parent handle.");
            }
            finally
            {
                FreeObjectAttributes(nameBuffer, unicodeStringBuffer);
                if (parentHandleAdded)
                    parent.DangerousRelease();
            }
        }

        private static ObjectAttributes CreateObjectAttributes(
            string name,
            IntPtr rootDirectory,
            out IntPtr nameBuffer,
            out IntPtr unicodeStringBuffer)
        {
            nameBuffer = IntPtr.Zero;
            unicodeStringBuffer = IntPtr.Zero;
            try
            {
                nameBuffer = Marshal.StringToHGlobalUni(name);
                UnicodeString unicodeName = new UnicodeString
                {
                    Length = checked((ushort)(name.Length * 2)),
                    MaximumLength = checked((ushort)((name.Length + 1) * 2)),
                    Buffer = nameBuffer
                };
                unicodeStringBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UnicodeString)));
                Marshal.StructureToPtr(unicodeName, unicodeStringBuffer, false);
                return new ObjectAttributes
                {
                    Length = (uint)Marshal.SizeOf(typeof(ObjectAttributes)),
                    RootDirectory = rootDirectory,
                    ObjectName = unicodeStringBuffer,
                    Attributes = ObjCaseInsensitive,
                    SecurityDescriptor = IntPtr.Zero,
                    SecurityQualityOfService = IntPtr.Zero
                };
            }
            catch
            {
                FreeObjectAttributes(nameBuffer, unicodeStringBuffer);
                nameBuffer = IntPtr.Zero;
                unicodeStringBuffer = IntPtr.Zero;
                throw;
            }
        }

        private static void FreeObjectAttributes(IntPtr nameBuffer, IntPtr unicodeStringBuffer)
        {
            if (unicodeStringBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(unicodeStringBuffer);
            if (nameBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(nameBuffer);
        }

        private static List<string> EnumerateNames(SafeFileHandle directory)
        {
            const int BufferSize = 64 * 1024;
            List<string> names = new List<string>();
            IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
            try
            {
                bool restartScan = true;
                while (true)
                {
                    IoStatusBlock ioStatus;
                    int status = NtQueryDirectoryFile(
                        directory,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        out ioStatus,
                        buffer,
                        BufferSize,
                        FileNamesInformation,
                        false,
                        IntPtr.Zero,
                        restartScan);
                    restartScan = false;
                    if (status == StatusNoMoreFiles)
                        break;
                    if (status != StatusSuccess)
                        throw new Win32Exception(
                            unchecked((int)RtlNtStatusToDosError(status)),
                            "Unable to enumerate scratch through its directory handle.");

                    ulong byteCountValue = ioStatus.Information.ToUInt64();
                    if (byteCountValue == 0 || byteCountValue > BufferSize)
                        throw new IOException("Scratch directory enumeration returned an invalid byte count.");
                    int byteCount = (int)byteCountValue;
                    int offset = 0;
                    while (true)
                    {
                        if (offset > byteCount - 12)
                            throw new IOException("Scratch directory enumeration returned a malformed record.");
                        uint nextOffset = unchecked((uint)Marshal.ReadInt32(buffer, offset));
                        int nameLength = Marshal.ReadInt32(buffer, offset + 8);
                        if (nameLength < 0 || (nameLength & 1) != 0 ||
                            nameLength > byteCount - offset - 12)
                            throw new IOException("Scratch directory enumeration returned a malformed name.");
                        string name = Marshal.PtrToStringUni(
                            IntPtr.Add(buffer, offset + 12), nameLength / 2);
                        if (name != "." && name != "..")
                        {
                            ValidateEntryName(name);
                            names.Add(name);
                        }
                        if (nextOffset == 0)
                            break;
                        if (nextOffset < 12 || nextOffset > (uint)(byteCount - offset))
                            throw new IOException("Scratch directory enumeration returned a malformed offset.");
                        offset = checked(offset + (int)nextOffset);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return names;
        }

        private static FileAttributeTagInfoBody GetAttributes(SafeFileHandle entry)
        {
            FileAttributeTagInfoBody info;
            if (!GetFileInformationByHandleEx(
                entry,
                FileAttributeTagInfo,
                out info,
                (uint)Marshal.SizeOf(typeof(FileAttributeTagInfoBody))))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to inspect an opened scratch entry.");
            return info;
        }

        private static ByHandleFileInformationBody GetIdentity(SafeFileHandle entry)
        {
            ByHandleFileInformationBody info;
            if (!GetFileInformationByHandle(entry, out info))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to identify an opened release workspace directory.");
            return info;
        }

        private static void ValidatePinnedDirectory(SafeFileHandle directory)
        {
            if (directory == null || directory.IsClosed || directory.IsInvalid)
                throw new IOException("A pinned release workspace handle is unavailable; scratch was retained.");

            FileAttributeTagInfoBody info = GetAttributes(directory);
            if ((info.FileAttributes & FileAttributeDirectory) == 0 ||
                (info.FileAttributes & FileAttributeReparsePoint) != 0)
                throw new IOException(
                    "A pinned release workspace handle is not a non-reparse directory; scratch was retained.");
        }

        private static void MarkDelete(SafeFileHandle entry)
        {
            int error;
            if (!TryMarkDelete(entry, out error))
                throw new Win32Exception(error,
                    "Handle-bound scratch entry deletion failed.");
        }

        private static bool TryMarkDelete(SafeFileHandle entry, out int error)
        {
            FileDispositionInfoExBody disposition = new FileDispositionInfoExBody
            {
                Flags = FileDispositionDelete | FileDispositionPosixSemantics |
                    FileDispositionIgnoreReadonlyAttribute
            };
            bool deleted = SetFileInformationByHandleEx(
                entry,
                FileDispositionInfoEx,
                ref disposition,
                (uint)Marshal.SizeOf(typeof(FileDispositionInfoExBody)));
            if (deleted)
            {
                error = 0;
                return true;
            }

            int extendedError = Marshal.GetLastWin32Error();
            if (extendedError != ErrorInvalidParameter &&
                extendedError != ErrorNotSupported &&
                extendedError != ErrorInvalidFunction)
            {
                error = extendedError;
                return false;
            }

            return TryMarkDeleteLegacy(entry, out error);
        }

        private static bool TryMarkDeleteLegacy(SafeFileHandle entry, out int error)
        {
            FileAttributeTagInfoBody originalInfo = GetAttributes(entry);
            bool clearedReadonly =
                (originalInfo.FileAttributes & FileAttributeReadonly) != 0;
            if (clearedReadonly)
            {
                int attributeError;
                if (!TrySetAttributes(
                    entry,
                    originalInfo.FileAttributes & ~FileAttributeReadonly,
                    out attributeError))
                {
                    error = attributeError;
                    return false;
                }
            }

            FileDispositionInfoBody disposition = new FileDispositionInfoBody
            {
                DeleteFile = 1
            };
            bool deleted = SetFileInformationByHandleLegacy(
                entry,
                FileDispositionInfo,
                ref disposition,
                (uint)Marshal.SizeOf(typeof(FileDispositionInfoBody)));
            if (deleted)
            {
                error = 0;
                return true;
            }

            int dispositionError = Marshal.GetLastWin32Error();
            if (clearedReadonly)
            {
                int restoreError;
                if (!TrySetAttributes(entry, originalInfo.FileAttributes, out restoreError))
                {
                    error = restoreError;
                    return false;
                }
            }
            error = dispositionError;
            return false;
        }

        private static bool TrySetAttributes(
            SafeFileHandle entry,
            uint attributes,
            out int error)
        {
            FileBasicInfoBody basicInfo = new FileBasicInfoBody
            {
                FileAttributes = attributes == 0 ? FileAttributeNormal : attributes
            };
            bool updated = SetFileInformationByHandleBasic(
                entry,
                FileBasicInfo,
                ref basicInfo,
                (uint)Marshal.SizeOf(typeof(FileBasicInfoBody)));
            error = updated ? 0 : Marshal.GetLastWin32Error();
            return updated;
        }

        private static void ValidateEntryName(string name)
        {
            if (String.IsNullOrEmpty(name) || name == "." || name == ".." ||
                name.IndexOf('\\') >= 0 || name.IndexOf('/') >= 0 ||
                name.IndexOf('\0') >= 0)
                throw new IOException("Scratch cleanup received an unsafe relative entry name.");
        }

        private static string ToNativePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\??\UNC\" + fullPath.Substring(8);
            if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
                return @"\??\" + fullPath.Substring(4);
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\??\UNC\" + fullPath.Substring(2);
            return @"\??\" + fullPath;
        }

        private static string ToExtendedPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
                return fullPath;
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + fullPath.Substring(2);
            return @"\\?\" + fullPath;
        }
    }
}
'@
    }
}

function New-PinnedReleaseOutputRoot {
    param([string]$Path)

    Initialize-ReleaseFileSystemInterop
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($pathRoot)) {
        throw "Release output root has no filesystem root: $fullPath"
    }

    $relativePath = $fullPath.Substring($pathRoot.Length)
    $entries = @(
        $relativePath.Split(
            [char[]]@('\', '/'),
            [System.StringSplitOptions]::RemoveEmptyEntries)
    )
    if ($entries.Count -eq 0) {
        throw "Release output root cannot be a filesystem root: $fullPath"
    }

    $ancestors = [System.Collections.ArrayList]::new()
    $outputHandle = $null
    try {
        $parentPath = $pathRoot
        $parentHandle = [XeCLI.Release.HandleBoundTreeDeleter]::OpenPinnedRoot($pathRoot)
        [void]$ancestors.Add([pscustomobject]@{
            Handle = $parentHandle
            Path = $parentPath
        })

        for ($index = 0; $index -lt ($entries.Count - 1); $index++) {
            $entryName = $entries[$index]
            $parentPath = Join-Path $parentPath $entryName
            $childHandle = [XeCLI.Release.HandleBoundTreeDeleter]::OpenPinnedChildDirectory(
                $parentHandle,
                $entryName)
            if ($null -eq $childHandle) {
                $childHandle = [XeCLI.Release.HandleBoundTreeDeleter]::CreatePinnedChildDirectory(
                    $parentHandle,
                    $entryName)
            }
            [XeCLI.Release.HandleBoundTreeDeleter]::AssertPathMatchesHandle(
                $childHandle,
                $parentPath)
            [void]$ancestors.Add([pscustomobject]@{
                Handle = $childHandle
                Path = $parentPath
            })
            $parentHandle = $childHandle
        }

        $outputHandle = [XeCLI.Release.HandleBoundTreeDeleter]::CreatePinnedChildDirectory(
            $parentHandle,
            $entries[$entries.Count - 1])
        [XeCLI.Release.HandleBoundTreeDeleter]::AssertPathMatchesHandle(
            $outputHandle,
            $fullPath)
        return [pscustomobject]@{
            Handle = $outputHandle
            Ancestors = $ancestors
        }
    }
    catch {
        if ($null -ne $outputHandle) {
            $outputHandle.Dispose()
        }
        for ($index = $ancestors.Count - 1; $index -ge 0; $index--) {
            $ancestors[$index].Handle.Dispose()
        }
        throw
    }
}

function New-PinnedReleaseWorkRoot {
    param(
        [Microsoft.Win32.SafeHandles.SafeFileHandle]$ParentHandle,
        [string]$EntryName
    )

    Initialize-ReleaseFileSystemInterop
    return [XeCLI.Release.HandleBoundTreeDeleter]::CreatePinnedChildDirectory(
        $ParentHandle,
        $EntryName)
}

function New-PinnedGeneratedDirectory {
    param(
        [Microsoft.Win32.SafeHandles.SafeFileHandle]$ParentHandle,
        [string]$EntryName,
        [string]$Path
    )

    Initialize-ReleaseFileSystemInterop
    $handle = [XeCLI.Release.HandleBoundTreeDeleter]::CreatePinnedChildDirectory(
        $ParentHandle,
        $EntryName)
    try {
        [XeCLI.Release.HandleBoundTreeDeleter]::AssertPathMatchesHandle($handle, $Path)
        return [pscustomobject]@{
            Handle = $handle
            Path = $Path
        }
    }
    catch {
        $handle.Dispose()
        throw
    }
}

function Assert-PinnedGeneratedDirectories {
    param([System.Collections.IList]$Directories)

    Initialize-ReleaseFileSystemInterop
    foreach ($directory in $Directories) {
        [XeCLI.Release.HandleBoundTreeDeleter]::AssertPathMatchesHandle(
            $directory.Handle,
            $directory.Path)
    }
}

function Close-PinnedGeneratedDirectories {
    param([System.Collections.IList]$Directories)

    for ($index = $Directories.Count - 1; $index -ge 0; $index--) {
        $Directories[$index].Handle.Dispose()
    }
    $Directories.Clear()
}

function Add-PinnedGeneratedDirectory {
    param(
        [Microsoft.Win32.SafeHandles.SafeFileHandle]$ParentHandle,
        [string]$EntryName,
        [string]$Path,
        [System.Collections.IList]$AllDirectories,
        [System.Collections.IList]$LifetimeDirectories
    )

    $directory = New-PinnedGeneratedDirectory `
        -ParentHandle $ParentHandle `
        -EntryName $EntryName `
        -Path $Path
    [void]$AllDirectories.Add($directory)
    [void]$LifetimeDirectories.Add($directory)
    return $directory
}

function Assert-PinnedReleaseWorkspace {
    param(
        [Microsoft.Win32.SafeHandles.SafeFileHandle]$OutputRootHandle,
        [string]$OutputRootPath,
        [Microsoft.Win32.SafeHandles.SafeFileHandle]$WorkRootHandle,
        [string]$WorkRootPath,
        [System.Collections.IList]$AncestorDirectories,
        [System.Collections.IList]$GeneratedDirectories
    )

    Initialize-ReleaseFileSystemInterop
    [XeCLI.Release.HandleBoundTreeDeleter]::AssertPathMatchesHandle(
        $OutputRootHandle,
        $OutputRootPath)
    [XeCLI.Release.HandleBoundTreeDeleter]::AssertPathMatchesHandle(
        $WorkRootHandle,
        $WorkRootPath)
    Assert-PinnedGeneratedDirectories -Directories $AncestorDirectories
    Assert-PinnedGeneratedDirectories -Directories $GeneratedDirectories
}

function Remove-PinnedGeneratedTree {
    param([Microsoft.Win32.SafeHandles.SafeFileHandle]$Handle)

    Initialize-ReleaseFileSystemInterop
    [XeCLI.Release.HandleBoundTreeDeleter]::DeletePinnedTree($Handle)
}

function Assert-PinnedGeneratedTreeRemoved {
    param(
        [Microsoft.Win32.SafeHandles.SafeFileHandle]$ParentHandle,
        [string]$EntryName
    )

    Initialize-ReleaseFileSystemInterop
    [XeCLI.Release.HandleBoundTreeDeleter]::AssertChildMissing($ParentHandle, $EntryName)
}

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $versionNodes = @($project.SelectNodes('/Project/PropertyGroup/Version'))
    if ($versionNodes.Count -ne 1) {
        throw "Expected exactly one Version property in $ProjectPath."
    }

    return [string]$versionNodes[0].InnerText
}

function Assert-ProjectVersions {
    param(
        [string[]]$ProjectPaths,
        [string]$ExpectedVersion
    )

    foreach ($projectPath in $ProjectPaths) {
        $projectVersion = Get-ProjectVersion -ProjectPath $projectPath
        if (-not $projectVersion.Equals($ExpectedVersion, [System.StringComparison]::Ordinal)) {
            throw "Version $ExpectedVersion does not match $projectVersion in $projectPath."
        }
    }
}

function Assert-ProvenanceStatus {
    $policyPath = Join-Path $RepoRoot '.github\release-policy.json'
    if (-not (Test-Path -LiteralPath $policyPath -PathType Leaf)) {
        throw '.github/release-policy.json is required for release packaging.'
    }
    Assert-NotReparseEntry -Path $policyPath -Label 'Release policy'

    try {
        $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw ".github/release-policy.json is invalid: $($_.Exception.Message)"
    }

    if ([int]$policy.schemaVersion -ne 1) {
        throw '.github/release-policy.json must use schemaVersion 1.'
    }

    $status = [string]$policy.provenanceStatus
    $status = $status.Trim().ToLowerInvariant()
    if ($status -notin @('resolved', 'unresolved')) {
        throw ".github/release-policy.json declares an unknown provenanceStatus: $status"
    }

    if ($Mode -eq 'Promotion') {
        if ($status -ne 'resolved') {
            throw 'Promotion requires .github/release-policy.json to declare provenanceStatus: resolved.'
        }
    }

    $script:ProvenanceStatus = $status
}

function Get-RequiredJsonProperty {
    param(
        [object]$Object,
        [string]$Name,
        [string]$DocumentLabel
    )

    if ($null -eq $Object) {
        throw "$DocumentLabel is missing required property $Name."
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$DocumentLabel is missing required property $Name."
    }

    return $property.Value
}

function Assert-RequiredJsonString {
    param(
        [object]$Object,
        [string]$Name,
        [string]$Expected,
        [string]$DocumentLabel,
        [System.StringComparison]$Comparison = [System.StringComparison]::Ordinal
    )

    $actual = [string](Get-RequiredJsonProperty -Object $Object -Name $Name -DocumentLabel $DocumentLabel)
    if (-not $actual.Equals($Expected, $Comparison)) {
        throw "$DocumentLabel property $Name mismatch. Expected '$Expected'; found '$actual'."
    }

    return $actual
}

function Assert-ManifestModifiedFiles {
    param(
        [object[]]$Entries,
        [System.Collections.IDictionary]$ExpectedEntries,
        [System.Collections.IDictionary]$FileRecords,
        [string]$MetadataRoot,
        [string]$DocumentLabel
    )

    if (@($Entries).Count -ne $ExpectedEntries.Count) {
        throw "$DocumentLabel must declare exactly $($ExpectedEntries.Count) modified file record(s)."
    }

    $seen = @{}
    foreach ($entry in @($Entries)) {
        $path = [string](Get-RequiredJsonProperty -Object $entry -Name 'Path' -DocumentLabel "$DocumentLabel modified file record")
        $path = $path.Trim().Replace('\', '/')
        if ($seen.ContainsKey($path)) {
            throw "$DocumentLabel contains a duplicate modified file record: $path"
        }
        $seen[$path] = $true
        if (-not $ExpectedEntries.Contains($path)) {
            throw "$DocumentLabel contains an unexpected modified file record: $path"
        }

        $expected = $ExpectedEntries[$path]
        $disposition = [string](Get-RequiredJsonProperty -Object $entry -Name 'Disposition' -DocumentLabel "$DocumentLabel modified file $path")
        if (-not $disposition.Equals([string]$expected.Disposition, [System.StringComparison]::Ordinal)) {
            throw "$DocumentLabel disposition mismatch for $path."
        }
        $capturedHash = [string](Get-RequiredJsonProperty -Object $entry -Name 'CapturedSourceSha256' -DocumentLabel "$DocumentLabel modified file $path")
        if (-not $capturedHash.Equals([string]$expected.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$DocumentLabel captured SHA-256 mismatch for $path."
        }

        $fullPath = ($MetadataRoot.TrimEnd('/') + '/' + $path).TrimStart('/')
        if (-not $FileRecords.Contains($fullPath)) {
            throw "$DocumentLabel modified file is absent from the exact file inventory: $fullPath"
        }
        $fileRecordHash = [string]$FileRecords[$fullPath]['ExpectedHash']
        if (-not $fileRecordHash.Equals($capturedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$DocumentLabel modified-file metadata does not match its file record: $fullPath"
        }
    }

    foreach ($expectedPath in $ExpectedEntries.Keys) {
        if (-not $seen.ContainsKey([string]$expectedPath)) {
            throw "$DocumentLabel is missing modified file metadata: $expectedPath"
        }
    }
}

function Resolve-ManifestPath {
    param(
        [string]$Root,
        [string]$RelativePath,
        [string]$RecordLabel
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw "$RecordLabel contains an empty path."
    }
    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "$RecordLabel contains an absolute path: $RelativePath"
    }

    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $normalizedRelativePath = $RelativePath.Replace('/', '\')
    $candidatePath = [System.IO.Path]::GetFullPath((Join-Path $rootPath $normalizedRelativePath))
    $prefix = $rootPath + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidatePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$RecordLabel escapes its manifest root: $RelativePath"
    }

    return $candidatePath
}

function Assert-IntegrityFileRecord {
    param(
        [string]$Path,
        [string]$RelativePath,
        [string]$ExpectedSha256,
        [long]$ExpectedSize = -1,
        [string]$RecordLabel
    )

    if ($ExpectedSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw "$RecordLabel declares an invalid SHA-256 for $RelativePath."
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$RecordLabel references a missing file: $RelativePath"
    }
    Assert-NotReparseEntry -Path $Path -Label "$RecordLabel file"

    $file = Get-Item -LiteralPath $Path
    if ($ExpectedSize -ge 0 -and $file.Length -ne $ExpectedSize) {
        throw "$RecordLabel size mismatch for $RelativePath; expected $ExpectedSize bytes, found $($file.Length)."
    }

    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not $actualHash.Equals($ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$RecordLabel SHA-256 mismatch for $RelativePath; expected $ExpectedSha256, found $actualHash."
    }
}

function Assert-FirstPartyPayloadManifest {
    $payloadRoot = Join-Path $RepoRoot 'XeCLI-XellFetch'
    $manifestPath = Join-Path $payloadRoot 'SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'XeCLI-XellFetch/SHA256SUMS.txt is required for release packaging.'
    }
    Assert-NotReparseEntry -Path $manifestPath -Label 'First-party payload checksum manifest'

    $records = @{}
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $manifestPath) {
        $lineNumber++
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#', [System.StringComparison]::Ordinal)) {
            continue
        }

        $match = [regex]::Match(
            $line,
            '^\s*(?<Hash>[0-9A-Fa-f]{64})[ \t]+(?:\*)?(?<Path>\S(?:.*\S)?)\s*$')
        if (-not $match.Success) {
            throw "First-party payload checksum manifest has an invalid record at line $lineNumber."
        }

        $relativePath = $match.Groups['Path'].Value.Replace('\', '/')
        if ($records.ContainsKey($relativePath)) {
            throw "First-party payload checksum manifest contains a duplicate path: $relativePath"
        }

        $expectedHash = $match.Groups['Hash'].Value.ToUpperInvariant()
        $records[$relativePath] = [ordered]@{
            RelativePath = $relativePath
            ExpectedHash = $expectedHash
            Path = Resolve-ManifestPath `
                -Root $payloadRoot `
                -RelativePath $relativePath `
                -RecordLabel 'First-party payload checksum manifest'
        }
    }

    foreach ($requiredPath in $RequiredFirstPartyPayloadPaths) {
        if (-not $records.ContainsKey($requiredPath)) {
            throw "First-party payload checksum manifest is missing required release payload: $requiredPath"
        }
    }
    if ($records.Count -ne $RequiredFirstPartyPayloadPaths.Count) {
        $unexpectedPaths = @($records.Keys | Where-Object { $_ -notin $RequiredFirstPartyPayloadPaths } | Sort-Object)
        throw "First-party payload checksum manifest contains unexpected records: $($unexpectedPaths -join ', ')"
    }

    foreach ($record in $records.Values) {
        Assert-IntegrityFileRecord `
            -Path $record.Path `
            -RelativePath $record.RelativePath `
            -ExpectedSha256 $record.ExpectedHash `
            -RecordLabel 'First-party payload checksum manifest'

        if (-not $FirstPartyPayloadStagePaths.Contains($record.RelativePath)) {
            throw "First-party payload has no repository asset mapping: $($record.RelativePath)"
        }
        $repositoryAssetPath = [string]$FirstPartyPayloadStagePaths[$record.RelativePath]
        Assert-IntegrityFileRecord `
            -Path (Resolve-ManifestPath `
                -Root $RepoRoot `
                -RelativePath $repositoryAssetPath `
                -RecordLabel 'Repository first-party payload mirror') `
            -RelativePath $repositoryAssetPath `
            -ExpectedSha256 $record.ExpectedHash `
            -RecordLabel 'Repository first-party payload mirror'
    }

    $script:FirstPartyPayloadManifestRecords = $records
}

function Assert-XeLLSourceManifest {
    param([string]$SourceRoot)

    if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
        $SourceRoot = Join-Path $RepoRoot 'XeCLI-XellFetch\source'
    }
    $SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
    $manifestPath = Join-Path $SourceRoot 'SOURCE-MANIFEST.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'XeCLI-XellFetch/source/SOURCE-MANIFEST.json is required for release packaging.'
    }
    Assert-NotReparseEntry -Path $manifestPath -Label 'XeLL source manifest'

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "XeLL source manifest is invalid JSON: $($_.Exception.Message)"
    }

    $schemaVersion = Get-RequiredJsonProperty -Object $manifest -Name 'SchemaVersion' -DocumentLabel 'XeLL source manifest'
    if ([int]$schemaVersion -ne 2) {
        throw 'XeLL source manifest must use schema version 2.'
    }
    [void](Assert-RequiredJsonString `
        -Object $manifest `
        -Name 'Component' `
        -Expected 'XeCLI XeLL corresponding source' `
        -DocumentLabel 'XeLL source manifest')

    $payload = Get-RequiredJsonProperty -Object $manifest -Name 'Payload' -DocumentLabel 'XeLL source manifest'
    [void](Assert-RequiredJsonString `
        -Object $payload `
        -Name 'RepositoryPath' `
        -Expected 'XeCLI-XellFetch/payload/xell.bin' `
        -DocumentLabel 'XeLL payload record')
    $payloadReleasePath = [string](Get-RequiredJsonProperty -Object $payload -Name 'ReleasePath' -DocumentLabel 'XeLL payload record')
    if (-not $payloadReleasePath.Equals('Assets/XellLaunch/xell.bin', [System.StringComparison]::Ordinal)) {
        throw "XeLL source manifest must identify Assets/XellLaunch/xell.bin; found '$payloadReleasePath'."
    }
    $payloadLength = [long](Get-RequiredJsonProperty -Object $payload -Name 'Length' -DocumentLabel 'XeLL payload record')
    if ($payloadLength -ne 262144) {
        throw "XeLL source manifest payload length mismatch. Expected 262144; found $payloadLength."
    }
    $payloadHash = [string](Get-RequiredJsonProperty -Object $payload -Name 'Sha256' -DocumentLabel 'XeLL payload record')
    if (-not $payloadHash.Equals('E364FAD816CD651B1F27C1A7DCD5AE8A0C3A3C70BF11D04B6A66123A38AF2C9C', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "XeLL source manifest payload SHA-256 mismatch: $payloadHash"
    }

    $sources = Get-RequiredJsonProperty -Object $manifest -Name 'Sources' -DocumentLabel 'XeLL source manifest'
    $xellSource = Get-RequiredJsonProperty -Object $sources -Name 'XeLL' -DocumentLabel 'XeLL source records'
    [void](Assert-RequiredJsonString -Object $xellSource -Name 'Repository' -Expected 'https://github.com/alex-free/xell-reloaded.git' -DocumentLabel 'XeLL source record')
    $xellCommit = [string](Get-RequiredJsonProperty -Object $xellSource -Name 'Commit' -DocumentLabel 'XeLL source record')
    if (-not $xellCommit.Equals('d4f08b423f6335eef9d67674307f221f0086ddf8', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "XeLL source manifest commit mismatch: $xellCommit"
    }
    $xellLicense = [string](Get-RequiredJsonProperty -Object $xellSource -Name 'License' -DocumentLabel 'XeLL source record')
    if (-not $xellLicense.Equals('GPL-2.0-only', [System.StringComparison]::Ordinal)) {
        throw "XeLL source manifest license mismatch: $xellLicense"
    }
    [void](Assert-RequiredJsonString -Object $xellSource -Name 'Description' -Expected 'v0.993-d4f08b4' -DocumentLabel 'XeLL source record')
    [void](Assert-RequiredJsonString -Object $xellSource -Name 'Root' -Expected 'xell' -DocumentLabel 'XeLL source record')

    $libXenonSource = Get-RequiredJsonProperty -Object $sources -Name 'LibXenon' -DocumentLabel 'XeLL source records'
    [void](Assert-RequiredJsonString -Object $libXenonSource -Name 'Repository' -Expected 'https://github.com/Free60Project/libxenon.git' -DocumentLabel 'LibXenon source record')
    $libXenonCommit = [string](Get-RequiredJsonProperty -Object $libXenonSource -Name 'Commit' -DocumentLabel 'LibXenon source record')
    if (-not $libXenonCommit.Equals('2b5e0e572db8db72c5e6a396d173b18aa2046394', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "LibXenon source manifest commit mismatch: $libXenonCommit"
    }
    [void](Assert-RequiredJsonString -Object $libXenonSource -Name 'Root' -Expected 'dependencies/libxenon' -DocumentLabel 'LibXenon source record')
    [void](Assert-RequiredJsonString `
        -Object $libXenonSource `
        -Name 'BuiltLibrarySha256' `
        -Expected '32B6BE390932649B008CEBBEB11CDA67BCC1EB6556A506F5274409135A139184' `
        -DocumentLabel 'LibXenon source record' `
        -Comparison ([System.StringComparison]::OrdinalIgnoreCase))
    $fatXenonSource = Get-RequiredJsonProperty -Object $sources -Name 'FatXenon' -DocumentLabel 'XeLL source records'
    [void](Assert-RequiredJsonString -Object $fatXenonSource -Name 'Repository' -Expected 'https://github.com/Free60Project/fat-xenon.git' -DocumentLabel 'fat-xenon source record')
    $fatXenonCommit = [string](Get-RequiredJsonProperty -Object $fatXenonSource -Name 'Commit' -DocumentLabel 'fat-xenon source record')
    if (-not $fatXenonCommit.Equals('0f2092a9a86026e07e903b596c903c1629159762', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "fat-xenon source manifest commit mismatch: $fatXenonCommit"
    }
    [void](Assert-RequiredJsonString -Object $fatXenonSource -Name 'Root' -Expected 'dependencies/fat-xenon' -DocumentLabel 'fat-xenon source record')
    [void](Assert-RequiredJsonString `
        -Object $fatXenonSource `
        -Name 'BuiltLibrarySha256' `
        -Expected 'EEBCD21CE7B63CDB4D3D7DF3A70DC9CAE1A16729D96F4074CED5DE188143D4FA' `
        -DocumentLabel 'fat-xenon source record' `
        -Comparison ([System.StringComparison]::OrdinalIgnoreCase))
    $newlibSource = Get-RequiredJsonProperty -Object $sources -Name 'Newlib' -DocumentLabel 'XeLL source records'
    [void](Assert-RequiredJsonString -Object $newlibSource -Name 'Version' -Expected '3.1.0' -DocumentLabel 'Newlib source record')
    [void](Assert-RequiredJsonString -Object $newlibSource -Name 'SourceUrl' -Expected 'https://sourceware.org/pub/newlib/newlib-3.1.0.tar.gz' -DocumentLabel 'Newlib source record')
    $newlibArchive = [string](Get-RequiredJsonProperty -Object $newlibSource -Name 'Archive' -DocumentLabel 'Newlib source record')
    if (-not $newlibArchive.Equals('dependencies/newlib-3.1.0.tar.gz', [System.StringComparison]::Ordinal)) {
        throw "Newlib source manifest archive mismatch: $newlibArchive"
    }
    $newlibHash = [string](Get-RequiredJsonProperty -Object $newlibSource -Name 'ArchiveSha256' -DocumentLabel 'Newlib source record')
    if (-not $newlibHash.Equals('FB4FA1CC21E9060719208300A61420E4089D6DE6EF59CF533B57FE74801D102A', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Newlib source archive SHA-256 mismatch: $newlibHash"
    }

    $toolchain = Get-RequiredJsonProperty -Object $manifest -Name 'Toolchain' -DocumentLabel 'XeLL source manifest'
    [void](Assert-RequiredJsonString -Object $toolchain -Name 'GccVersion' -Expected '9.2.0' -DocumentLabel 'XeLL toolchain record')
    [void](Assert-RequiredJsonString -Object $toolchain -Name 'BinutilsVersion' -Expected '2.32' -DocumentLabel 'XeLL toolchain record')
    [void](Assert-RequiredJsonString -Object $toolchain -Name 'NewlibVersion' -Expected '3.1.0' -DocumentLabel 'XeLL toolchain record')
    [void](Assert-RequiredJsonString -Object $toolchain -Name 'RulesSha256' -Expected '6713178AEA6A70B6FD5B3B095188C2CC2BA1925596AEF37CEACF8EF4DAEBB3F0' -DocumentLabel 'XeLL toolchain record' -Comparison ([System.StringComparison]::OrdinalIgnoreCase))
    [void](Assert-RequiredJsonString -Object $toolchain -Name 'VersionHeader' -Expected 'build-metadata/version.h' -DocumentLabel 'XeLL toolchain record')
    [void](Assert-RequiredJsonString -Object $toolchain -Name 'VersionHeaderSha256' -Expected '5F69C432E46416CC825E394B8C5BF9A36E38A383E31E58898F7B77971139D577' -DocumentLabel 'XeLL toolchain record' -Comparison ([System.StringComparison]::OrdinalIgnoreCase))

    Assert-NoReparseEntries -Root $SourceRoot -Label 'XeLL corresponding source'
    $manifestFiles = @(Get-RequiredJsonProperty -Object $manifest -Name 'Files' -DocumentLabel 'XeLL source manifest')
    if ($manifestFiles.Count -eq 0) {
        throw 'XeLL source manifest must contain at least one file record.'
    }

    $records = @{}
    foreach ($manifestFile in $manifestFiles) {
        $relativePath = [string](Get-RequiredJsonProperty -Object $manifestFile -Name 'Path' -DocumentLabel 'XeLL source file record')
        $relativePath = $relativePath.Trim().Replace('\', '/')
        if ($relativePath.Equals('SOURCE-MANIFEST.json', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'XeLL source manifest must not include itself.'
        }
        if ($records.ContainsKey($relativePath)) {
            throw "XeLL source manifest contains a duplicate path: $relativePath"
        }

        $sizeValue = Get-RequiredJsonProperty -Object $manifestFile -Name 'Length' -DocumentLabel "XeLL source record $relativePath"
        $sizeText = ([string]$sizeValue).Trim()
        if ($sizeText -notmatch '^[0-9]+$') {
            throw "XeLL source record $relativePath declares an invalid length."
        }
        $expectedSize = [long]::Parse($sizeText, [System.Globalization.NumberStyles]::None, [System.Globalization.CultureInfo]::InvariantCulture)
        $expectedHash = [string](Get-RequiredJsonProperty -Object $manifestFile -Name 'Sha256' -DocumentLabel "XeLL source record $relativePath")
        $filePath = Resolve-ManifestPath -Root $SourceRoot -RelativePath $relativePath -RecordLabel 'XeLL source manifest'
        $records[$relativePath] = [ordered]@{
            RelativePath = $relativePath
            ExpectedHash = $expectedHash
            ExpectedSize = $expectedSize
            Path = $filePath
        }
    }

    $actualPaths = @(
        Get-ChildItem -LiteralPath $SourceRoot -File -Force -Recurse |
            ForEach-Object {
                (Get-RelativeFilePath -Root $SourceRoot -Path $_.FullName).Replace('\', '/')
            } |
            Where-Object { -not $_.Equals('SOURCE-MANIFEST.json', [System.StringComparison]::OrdinalIgnoreCase) }
    )
    foreach ($actualPath in $actualPaths) {
        if (-not $records.ContainsKey($actualPath)) {
            throw "XeLL source manifest does not record source file: $actualPath"
        }
    }
    if ($records.Count -ne $actualPaths.Count) {
        $missingFiles = @($records.Keys | Where-Object { $_ -notin $actualPaths } | Sort-Object)
        throw "XeLL source manifest references files outside the exact source package: $($missingFiles -join ', ')"
    }

    foreach ($record in $records.Values) {
        Assert-IntegrityFileRecord `
            -Path $record.Path `
            -RelativePath $record.RelativePath `
            -ExpectedSha256 $record.ExpectedHash `
            -ExpectedSize $record.ExpectedSize `
            -RecordLabel 'XeLL source manifest'
    }

    $requiredPaths = @(
        'README.md',
        'BUILDING.md',
        'COMPONENT-NOTICES.md',
        'GCC-RUNTIME-LIBRARY-EXCEPTION.txt',
        'LICENSE.GPL-2.0.txt',
        'NEWLIB-NOTICES.txt',
        'build.sh',
        'build-metadata/version.h',
        'dependencies/newlib-3.1.0.tar.gz',
        'dependencies/libxenon/libxenon/LICENSE',
        'dependencies/libxenon/toolchain/build-xenon-toolchain',
        'dependencies/fat-xenon/Makefile',
        'xell/Makefile',
        'xell/Makefile_lv2.mk',
        'xell/source/lv2/httpd/httpd.c',
        'xell/source/lv2/httpd/httpd.h',
        'xell/source/lv2/httpd/httpd_flash.c',
        'xell/source/lv2/main.c',
        'xell/source/lv2/xecli_branding.c',
        'xell/source/lv2/xecli_branding.h'
    )
    foreach ($requiredPath in $requiredPaths) {
        if (-not $records.ContainsKey($requiredPath)) {
            throw "XeLL source manifest is missing required source material: $requiredPath"
        }
    }
    if (-not $records[$newlibArchive].ExpectedHash.Equals($newlibHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'XeLL source manifest Newlib metadata does not match its file record.'
    }

    $versionHeaderPath = [string](Get-RequiredJsonProperty -Object $toolchain -Name 'VersionHeader' -DocumentLabel 'XeLL toolchain record')
    $versionHeaderHash = [string](Get-RequiredJsonProperty -Object $toolchain -Name 'VersionHeaderSha256' -DocumentLabel 'XeLL toolchain record')
    if (-not $records[$versionHeaderPath].ExpectedHash.Equals($versionHeaderHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'XeLL source manifest version-header metadata does not match its file record.'
    }

    $xellExpectedModifiedFiles = [ordered]@{
        'Makefile' = [pscustomobject]@{ Disposition = 'modified'; Sha256 = '309E716F80117F2519D76F552B68A137E4A825E2B153057538B27F0186EAD4EE' }
        'Makefile_lv2.mk' = [pscustomobject]@{ Disposition = 'modified'; Sha256 = '9DE4413778D56F4794ECFD69028B23FF94A41FAFA92689B57B6B4FD76CA440FF' }
        'source/lv2/httpd/httpd.c' = [pscustomobject]@{ Disposition = 'modified'; Sha256 = '4EDDEB98D8EC09D87122ABDF0AC4F129CB83261C7B0A6F310BDE4A747687C4D0' }
        'source/lv2/httpd/httpd.h' = [pscustomobject]@{ Disposition = 'modified'; Sha256 = '73B79EEAF22546F7109133946588A3C4A2B7178F6D1D6856883EEB87004AE572' }
        'source/lv2/httpd/httpd_flash.c' = [pscustomobject]@{ Disposition = 'modified'; Sha256 = '49945E2F685519287815F78FD401BB85FF65EAF154652CF5CD411D639125B01F' }
        'source/lv2/main.c' = [pscustomobject]@{ Disposition = 'modified'; Sha256 = '30426501762F4ABD7A448560276242E29FFA7526F8A70840D398673D1D4CA992' }
        'source/lv2/xecli_branding.c' = [pscustomobject]@{ Disposition = 'added'; Sha256 = 'DFE9389E76589DE99D47221EE679EE6D29B37334153AC6C766350EBADBDAE42A' }
        'source/lv2/xecli_branding.h' = [pscustomobject]@{ Disposition = 'added'; Sha256 = 'C3EA10F4B1EE14DAE500C3B07DEF4BF79B1B9782C15A4A617201909974449877' }
    }
    Assert-ManifestModifiedFiles `
        -Entries @(Get-RequiredJsonProperty -Object $xellSource -Name 'ModifiedFiles' -DocumentLabel 'XeLL source record') `
        -ExpectedEntries $xellExpectedModifiedFiles `
        -FileRecords $records `
        -MetadataRoot 'xell' `
        -DocumentLabel 'XeLL source record'

    $libXenonExpectedModifiedFiles = [ordered]@{
        'libxenon/ports/xenon/Makefile' = [pscustomobject]@{ Disposition = 'modified'; Sha256 = 'CA04D79621870CB2AB7CC94F7DC78A2BB684BB26E312D0E6F9D34CAE6851BC6E' }
        'toolchain/build-xenon-toolchain' = [pscustomobject]@{ Disposition = 'modified'; Sha256 = '88566FEB468BB015E1B0A851662D95BF1C615268DAAF351A117710C52CAB6C2D' }
    }
    Assert-ManifestModifiedFiles `
        -Entries @(Get-RequiredJsonProperty -Object $libXenonSource -Name 'ModifiedFiles' -DocumentLabel 'LibXenon source record') `
        -ExpectedEntries $libXenonExpectedModifiedFiles `
        -FileRecords $records `
        -MetadataRoot 'dependencies/libxenon' `
        -DocumentLabel 'LibXenon source record'

    $xellMakefileNotice = '# Modified by XeCLI contributors on 2026-07-19 to enforce ordered stage builds.'
    $xellMakefileText = [System.IO.File]::ReadAllText($records['xell/Makefile'].Path)
    if (-not $xellMakefileText.StartsWith($xellMakefileNotice, [System.StringComparison]::Ordinal)) {
        throw 'XeLL Makefile is missing the required dated modification notice.'
    }

    $script:XeLLSourceManifestRecords = $records
}

function Assert-X360SourceManifest {
    param([string]$SourceRoot)

    if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
        $SourceRoot = Join-Path $RepoRoot 'third_party\X360'
    }
    $SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
    $manifestPath = Join-Path $SourceRoot 'SOURCE-MANIFEST.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'third_party/X360/SOURCE-MANIFEST.json is required for release packaging.'
    }
    Assert-NotReparseEntry -Path $manifestPath -Label 'X360 source manifest'

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "X360 source manifest is invalid JSON: $($_.Exception.Message)"
    }

    $schemaVersion = Get-RequiredJsonProperty -Object $manifest -Name 'SchemaVersion' -DocumentLabel 'X360 source manifest'
    if ([int]$schemaVersion -ne 1) {
        throw 'X360 source manifest must use schema version 1.'
    }
    [void](Assert-RequiredJsonString -Object $manifest -Name 'Component' -Expected 'X360.dll' -DocumentLabel 'X360 source manifest')
    [void](Assert-RequiredJsonString -Object $manifest -Name 'License' -Expected 'GPL-3.0-only' -DocumentLabel 'X360 source manifest')

    $upstream = Get-RequiredJsonProperty -Object $manifest -Name 'Upstream' -DocumentLabel 'X360 source manifest'
    [void](Assert-RequiredJsonString -Object $upstream -Name 'Repository' -Expected 'https://github.com/mtolly/X360' -DocumentLabel 'X360 upstream record')
    [void](Assert-RequiredJsonString -Object $upstream -Name 'Revision' -Expected '573dda3cd841ba370b2110567a6b4ee2b7099c9c' -DocumentLabel 'X360 upstream record' -Comparison ([System.StringComparison]::OrdinalIgnoreCase))
    [void](Assert-RequiredJsonString -Object $upstream -Name 'Version' -Expected '1.0.0.41' -DocumentLabel 'X360 upstream record')
    [void](Assert-RequiredJsonString -Object $upstream -Name 'SourceArchiveSha256' -Expected 'D8A6EF4F9A176DB6FCF3AB7FF8EDF20F73BBF4BC40469666395A40DEFAC8E8B7' -DocumentLabel 'X360 upstream record' -Comparison ([System.StringComparison]::OrdinalIgnoreCase))
    [void](Assert-RequiredJsonString -Object $upstream -Name 'BinaryArchiveSha256' -Expected '1D1FAAA1AEDCA584F6FD791504E34A0EB6098A6A964FB2FC24805B3F9DDB562B' -DocumentLabel 'X360 upstream record' -Comparison ([System.StringComparison]::OrdinalIgnoreCase))
    [void](Assert-RequiredJsonString -Object $manifest -Name 'MaintainedVersion' -Expected '1.0.0.42' -DocumentLabel 'X360 source manifest')
    [void](Assert-RequiredJsonString -Object $manifest -Name 'ModifiedDate' -Expected '2026-07-19' -DocumentLabel 'X360 source manifest')

    $build = Get-RequiredJsonProperty -Object $manifest -Name 'Build' -DocumentLabel 'X360 source manifest'
    [void](Assert-RequiredJsonString -Object $build -Name 'Project' -Expected 'src/X360/X360.csproj' -DocumentLabel 'X360 build record')
    [void](Assert-RequiredJsonString -Object $build -Name 'Configuration' -Expected 'Release' -DocumentLabel 'X360 build record')
    [void](Assert-RequiredJsonString -Object $build -Name 'Platform' -Expected 'AnyCPU' -DocumentLabel 'X360 build record')
    [void](Assert-RequiredJsonString -Object $build -Name 'TargetFramework' -Expected '.NET Framework 3.5' -DocumentLabel 'X360 build record')
    $declaredOutput = [string](Get-RequiredJsonProperty -Object $build -Name 'Output' -DocumentLabel 'X360 build record')
    $declaredOutput = $declaredOutput.Trim().Replace('\', '/')
    if (-not $declaredOutput.Equals('lib/X360.dll', [System.StringComparison]::Ordinal)) {
        throw "X360 source manifest must declare lib/X360.dll as its output; found '$declaredOutput'."
    }
    $declaredHash = [string](Get-RequiredJsonProperty -Object $build -Name 'Sha256' -DocumentLabel 'X360 build record')
    if (-not $declaredHash.Equals($QuickBootX360DllSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "X360 source manifest output SHA-256 mismatch. Expected $QuickBootX360DllSha256; found $declaredHash."
    }
    $declaredLength = [long](Get-RequiredJsonProperty -Object $build -Name 'Length' -DocumentLabel 'X360 build record')
    if ($declaredLength -ne 210944) {
        throw "X360 source manifest output length mismatch. Expected 210944; found $declaredLength."
    }

    $records = @{}
    $manifestFiles = @(Get-RequiredJsonProperty -Object $manifest -Name 'Files' -DocumentLabel 'X360 source manifest')
    if ($manifestFiles.Count -eq 0) {
        throw 'X360 source manifest must contain at least one file record.'
    }
    foreach ($manifestFile in $manifestFiles) {
        $relativePath = [string](Get-RequiredJsonProperty -Object $manifestFile -Name 'Path' -DocumentLabel 'X360 source file record')
        $relativePath = $relativePath.Trim().Replace('\', '/')
        if ($relativePath.Equals('SOURCE-MANIFEST.json', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'X360 source manifest must not include itself.'
        }
        if ($records.ContainsKey($relativePath)) {
            throw "X360 source manifest contains a duplicate path: $relativePath"
        }

        $sizeValue = Get-RequiredJsonProperty -Object $manifestFile -Name 'Length' -DocumentLabel "X360 source record $relativePath"
        $sizeText = ([string]$sizeValue).Trim()
        if ($sizeText -notmatch '^[0-9]+$') {
            throw "X360 source record $relativePath declares an invalid length."
        }
        $expectedSize = [long]::Parse($sizeText, [System.Globalization.NumberStyles]::None, [System.Globalization.CultureInfo]::InvariantCulture)
        $expectedHash = [string](Get-RequiredJsonProperty -Object $manifestFile -Name 'Sha256' -DocumentLabel "X360 source record $relativePath")
        $filePath = Resolve-ManifestPath -Root $SourceRoot -RelativePath $relativePath -RecordLabel 'X360 source manifest'
        $records[$relativePath] = [ordered]@{
            RelativePath = $relativePath
            ExpectedHash = $expectedHash
            ExpectedSize = $expectedSize
            Path = $filePath
        }
    }

    $actualPaths = @(
        Get-ChildItem -LiteralPath $SourceRoot -File -Force -Recurse |
            ForEach-Object { (Get-RelativeFilePath -Root $SourceRoot -Path $_.FullName).Replace('\', '/') } |
            Where-Object {
                -not $_.Equals('SOURCE-MANIFEST.json', [System.StringComparison]::OrdinalIgnoreCase) -and
                $_ -notmatch '(^|/)(bin|obj)/'
            }
    )
    foreach ($actualPath in $actualPaths) {
        if (-not $records.ContainsKey($actualPath)) {
            throw "X360 source manifest does not record source file: $actualPath"
        }
    }
    if ($records.Count -ne $actualPaths.Count) {
        $missingFiles = @($records.Keys | Where-Object { $_ -notin $actualPaths } | Sort-Object)
        throw "X360 source manifest references files outside the exact source tree: $($missingFiles -join ', ')"
    }

    foreach ($record in $records.Values) {
        Assert-IntegrityFileRecord `
            -Path $record.Path `
            -RelativePath $record.RelativePath `
            -ExpectedSha256 $record.ExpectedHash `
            -ExpectedSize $record.ExpectedSize `
            -RecordLabel 'X360 source manifest'
    }
    if (-not $records.ContainsKey('lib/X360.dll') -or
        -not $records['lib/X360.dll'].ExpectedHash.Equals($QuickBootX360DllSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "X360 source manifest must record lib/X360.dll with SHA-256 $QuickBootX360DllSha256."
    }
    if ($records['lib/X360.dll'].ExpectedSize -ne $declaredLength) {
        throw 'X360 source manifest build length does not match its library file record.'
    }

    $requiredPaths = @(
        'README.md',
        'build.ps1',
        'lib/X360.dll',
        'src/X360/X360.csproj',
        'src/X360/Resources/GPL30.txt'
    )
    foreach ($requiredPath in $requiredPaths) {
        if (-not $records.ContainsKey($requiredPath)) {
            throw "X360 source manifest is missing required corresponding source material: $requiredPath"
        }
    }
    $x360ReadmeNotice = '> Modified by XeCLI contributors on 2026-07-19.'
    $x360ReadmeText = [System.IO.File]::ReadAllText($records['README.md'].Path)
    if (-not $x360ReadmeText.Contains($x360ReadmeNotice, [System.StringComparison]::Ordinal)) {
        throw 'X360 README is missing the required dated modification notice.'
    }

    $script:X360SourceManifestRecords = $records
}

function Assert-StagedFirstPartyPayloads {
    param([string]$StageRoot)

    if ($null -eq $script:FirstPartyPayloadManifestRecords -or
        $script:FirstPartyPayloadManifestRecords.Count -ne $RequiredFirstPartyPayloadPaths.Count) {
        throw 'First-party payload manifest records are unavailable for staged release validation.'
    }

    foreach ($relativePath in $RequiredFirstPartyPayloadPaths) {
        if (-not $FirstPartyPayloadStagePaths.Contains($relativePath)) {
            throw "First-party release payload has no staged-path mapping: $relativePath"
        }

        $record = $script:FirstPartyPayloadManifestRecords[$relativePath]
        $stageRelativePath = [string]$FirstPartyPayloadStagePaths[$relativePath]
        $stagePath = Resolve-ManifestPath `
            -Root $StageRoot `
            -RelativePath $stageRelativePath `
            -RecordLabel 'Staged first-party release payload'
        Assert-IntegrityFileRecord `
            -Path $stagePath `
            -RelativePath $stageRelativePath `
            -ExpectedSha256 $record.ExpectedHash `
            -RecordLabel 'Staged first-party release payload'
    }
}

function Get-LeadingCommentBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label was not found: $Path"
    }
    Assert-NotReparseEntry -Path $Path -Label $Label
    $lines = [System.IO.File]::ReadAllLines($Path)
    if ($lines.Count -eq 0 -or -not $lines[0].TrimStart().StartsWith('/*', [System.StringComparison]::Ordinal)) {
        throw "$Label does not begin with a C comment block: $Path"
    }

    $endIndex = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index].Contains('*/', [System.StringComparison]::Ordinal)) {
            $endIndex = $index
            break
        }
    }
    if ($endIndex -lt 0) {
        throw "$Label has an unterminated leading comment block: $Path"
    }

    return [string]::Join([Environment]::NewLine, $lines[0..$endIndex]).TrimEnd()
}

function Copy-XeLLBinaryLicenseNotices {
    param([Parameter(Mandatory = $true)][string]$DestinationRoot)

    $sourceRoot = Join-Path $RepoRoot 'XeCLI-XellFetch\source'
    $destination = Join-Path $DestinationRoot 'XeLL'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    $copies = [ordered]@{
        'LICENSE.GPL-2.0.txt' = 'GPL-2.0.txt'
        'NEWLIB-NOTICES.txt' = 'NEWLIB-NOTICES.txt'
        'GCC-RUNTIME-LIBRARY-EXCEPTION.txt' = 'GCC-RUNTIME-LIBRARY-EXCEPTION.txt'
    }
    foreach ($sourceName in $copies.Keys) {
        $sourcePath = Join-Path $sourceRoot $sourceName
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Required XeLL binary notice was not found: $sourcePath"
        }
        Assert-NotReparseEntry -Path $sourcePath -Label 'XeLL binary notice'
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $destination $copies[$sourceName]) -Force
    }

    $libXenonLicensePath = Join-Path $sourceRoot 'dependencies\libxenon\libxenon\LICENSE'
    if (-not (Test-Path -LiteralPath $libXenonLicensePath -PathType Leaf)) {
        throw "LibXenon binary notice was not found: $libXenonLicensePath"
    }
    Assert-NotReparseEntry -Path $libXenonLicensePath -Label 'LibXenon binary notice'

    $sections = @(
        "XeLL additional binary notices",
        "",
        "This file contains the compact BSD and zlib-style notices required by code linked into the bundled XeLL payload. Complete corresponding source is identified in PROVENANCE/CORRESPONDING-SOURCE.md.",
        "",
        "===== LibXenon =====",
        [System.IO.File]::ReadAllText($libXenonLicensePath).TrimEnd(),
        "",
        "===== fat-xenon: Michael Chisholm and Dave Murphy =====",
        (Get-LeadingCommentBlock -Path (Join-Path $sourceRoot 'dependencies\fat-xenon\include\fat.h') -Label 'fat-xenon primary notice'),
        "",
        "===== fat-xenon: Michael Chisholm =====",
        (Get-LeadingCommentBlock -Path (Join-Path $sourceRoot 'dependencies\fat-xenon\source\disc.c') -Label 'fat-xenon Michael Chisholm notice'),
        "",
        "===== fat-xenon: Sven Peter =====",
        (Get-LeadingCommentBlock -Path (Join-Path $sourceRoot 'dependencies\fat-xenon\source\lock.h') -Label 'fat-xenon Sven Peter notice'),
        "",
        "===== puff =====",
        (Get-LeadingCommentBlock -Path (Join-Path $sourceRoot 'xell\source\lv1\puff\puff.h') -Label 'puff notice')
    )
    $additionalNoticePath = Join-Path $destination 'ADDITIONAL-NOTICES.txt'
    [System.IO.File]::WriteAllText(
        $additionalNoticePath,
        [string]::Join([Environment]::NewLine, $sections) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Assert-PackagedThirdPartyLicenseInventory {
    param([Parameter(Mandatory = $true)][string]$StageRoot)

    $licenseRoot = Join-Path $StageRoot 'THIRD-PARTY-LICENSES'
    Assert-NoReparseEntries -Root $licenseRoot -Label 'Packaged third-party license directory'
    $actualPaths = @(
        Get-ChildItem -LiteralPath $licenseRoot -File -Force -Recurse |
            ForEach-Object { (Get-RelativeFilePath -Root $licenseRoot -Path $_.FullName).Replace('\', '/') } |
            Sort-Object
    )
    $expectedPaths = @($RequiredPackagedThirdPartyLicensePaths | Sort-Object)
    if ($actualPaths.Count -ne $expectedPaths.Count) {
        throw "Packaged third-party license inventory must contain exactly: $($expectedPaths -join ', ')"
    }
    for ($index = 0; $index -lt $expectedPaths.Count; $index++) {
        if (-not $actualPaths[$index].Equals($expectedPaths[$index], [System.StringComparison]::Ordinal)) {
            throw "Packaged third-party license inventory must contain exactly: $($expectedPaths -join ', ')"
        }
    }
}

function Write-CorrespondingSourceNotice {
    param(
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$GitRevision
    )

    if ($GitRevision -notmatch '^[0-9A-Fa-f]{40}$') {
        throw "Corresponding-source notice requires a full git revision; found '$GitRevision'."
    }

    $tag = "v$Version"
    $tagUrl = "$PublicRepositoryUrl/tree/$tag"
    $archiveUrl = "$PublicRepositoryUrl/archive/refs/tags/$tag.zip"
    $text = @"
# Corresponding source

This XeCLI $Version binary release was built from git revision **$GitRevision**.

Complete corresponding source and build scripts for the bundled GPL components are available from the matching public tag:

- Tag: [$tag]($tagUrl)
- Source archive: [$tag.zip]($archiveUrl)
- X360 library source: **third_party/X360**
- XeLL payload source: **XeCLI-XellFetch/source**

The source tag contains the exact manifests, modifications, build instructions, linked Newlib source, and license material needed to reproduce the distributed components. Source is kept outside the installer and portable package so end users do not receive an unnecessary 22 MiB development tree.

Binary identities:

- **Assets/QuickBoot/X360.dll:** $QuickBootX360DllSha256
- **Assets/XellLaunch/xell.bin:** E364FAD816CD651B1F27C1A7DCD5AE8A0C3A3C70BF11D04B6A66123A38AF2C9C

**CONSOLE-PAYLOADS.sha256** records the exact bundled console payload checksums.
"@
    $parent = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    [System.IO.File]::WriteAllText(
        $DestinationPath,
        $text.TrimStart() + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Assert-ReleaseSourceInputs {
    $licenseRoot = Join-Path $RepoRoot 'THIRD-PARTY-LICENSES'
    if (-not (Test-Path -LiteralPath $licenseRoot -PathType Container)) {
        throw 'THIRD-PARTY-LICENSES is required for release packaging.'
    }
    Assert-NoReparseEntries -Root $licenseRoot -Label 'Third-party license directory'
    $licenseDirectories = @(Get-ChildItem -LiteralPath $licenseRoot -Directory -Force -Recurse)
    if ($licenseDirectories.Count -ne 0) {
        throw 'THIRD-PARTY-LICENSES must contain only the two required shared notice files at its root.'
    }
    $actualLicenseFiles = @(
        Get-ChildItem -LiteralPath $licenseRoot -File -Force |
            ForEach-Object Name |
            Sort-Object
    )
    $expectedLicenseFiles = @($RequiredThirdPartyLicenseFiles | Sort-Object)
    if ($actualLicenseFiles.Count -ne $expectedLicenseFiles.Count) {
        throw "THIRD-PARTY-LICENSES must contain exactly: $($expectedLicenseFiles -join ', ')"
    }
    for ($index = 0; $index -lt $expectedLicenseFiles.Count; $index++) {
        if (-not $actualLicenseFiles[$index].Equals($expectedLicenseFiles[$index], [System.StringComparison]::Ordinal)) {
            throw "THIRD-PARTY-LICENSES must contain exactly: $($expectedLicenseFiles -join ', ')"
        }
    }

    Assert-FirstPartyPayloadManifest
    Assert-XeLLSourceManifest
    Assert-X360SourceManifest
}

function Assert-PromotionArtifactRequirements {
    if ($Mode -eq 'Promotion' -and -not $BuildInstaller) {
        throw 'Promotion requires -BuildInstaller so both the portable ZIP and setup executable are produced.'
    }
}

function Get-PinnedDotNetSdkVersion {
    $globalJsonPath = Join-Path $RepoRoot 'global.json'
    if (-not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
        throw 'global.json is required for promotion builds.'
    }
    Assert-NotReparseEntry -Path $globalJsonPath -Label 'global.json'

    try {
        $globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "global.json is invalid: $($_.Exception.Message)"
    }

    $sdk = Get-RequiredJsonProperty -Object $globalJson -Name 'sdk' -DocumentLabel 'global.json'
    $version = [string](Get-RequiredJsonProperty -Object $sdk -Name 'version' -DocumentLabel 'global.json sdk')
    $version = $version.Trim()
    if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$') {
        throw "global.json declares an invalid pinned SDK version: $version"
    }

    return $version
}

function Get-ResolvedDotNetSdkVersion {
    param([string]$DotNetExecutable)

    Push-Location -LiteralPath $RepoRoot
    try {
        $versionOutput = @(& $DotNetExecutable --version 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw ".NET SDK version resolution failed for $DotNetExecutable (exit code $exitCode)."
    }
    $versionLines = @(
        $versionOutput |
            ForEach-Object { ([string]$_).Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($versionLines.Count -ne 1) {
        throw ".NET SDK version resolution returned unexpected output: $($versionLines -join ' | ')"
    }

    return $versionLines[0]
}

function Assert-PromotionDotNetSdk {
    param([string]$DotNetExecutable)

    if ($Mode -ne 'Promotion') {
        return
    }

    $pinnedVersion = Get-PinnedDotNetSdkVersion
    $resolvedVersion = Get-ResolvedDotNetSdkVersion -DotNetExecutable $DotNetExecutable
    if (-not $resolvedVersion.Equals($pinnedVersion, [System.StringComparison]::Ordinal)) {
        throw "Promotion requires the exact global.json SDK $pinnedVersion; $DotNetExecutable resolved $resolvedVersion."
    }

    $script:PinnedDotNetSdkVersion = $pinnedVersion
}

function Get-GitValue {
    param([string[]]$Arguments)

    $value = & git -C $RepoRoot @Arguments
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
        throw "Unable to read required git metadata."
    }

    return ([string]$value).Trim()
}

function Assert-CleanPromotionTree {
    if ($Mode -ne 'Promotion') {
        return
    }

    # Locked restore can rewrite packages.lock.json byte-for-byte identically.
    # Git may then report a stat-only porcelain modification even though the
    # worktree blob still matches the index. Probe semantic content, the index,
    # and untracked files independently so promotion rejects real drift without
    # treating an identical lock-file rewrite as source drift.
    & git -C $RepoRoot diff --quiet --ignore-submodules=none --
    $worktreeExitCode = $LASTEXITCODE
    if ($worktreeExitCode -gt 1) {
        throw 'Unable to verify the git worktree state.'
    }
    if ($worktreeExitCode -eq 1) {
        throw 'Promotion requires a clean git worktree.'
    }

    & git -C $RepoRoot diff --cached --quiet --ignore-submodules=none --
    $indexExitCode = $LASTEXITCODE
    if ($indexExitCode -gt 1) {
        throw 'Unable to verify the git index state.'
    }
    if ($indexExitCode -eq 1) {
        throw 'Promotion requires a clean git index.'
    }

    $untracked = @(& git -C $RepoRoot ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to verify untracked git files.'
    }
    if ($untracked.Count -ne 0) {
        throw 'Promotion requires a worktree without untracked files.'
    }
}

function Resolve-ReleasePolicy {
    if ($Mode -eq 'Local') {
        if ($RequireSigning -or $AllowUnsignedPromotion -or
            -not [string]::IsNullOrWhiteSpace($CertificateThumbprint) -or
            -not [string]::IsNullOrWhiteSpace($SignToolPath) -or -not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
            throw 'Local mode does not accept promotion policy switches or signing inputs.'
        }

        $script:ReleaseKind = 'local-unsigned'
        $script:Publisher = 'Unknown (local build)'
        $script:SmartScreenWarning = ''
        return
    }

    if ($RequireSigning -and $AllowUnsignedPromotion) {
        throw 'Promotion policy is ambiguous: -RequireSigning and -AllowUnsignedPromotion are mutually exclusive.'
    }
    if (-not $RequireSigning -and -not $AllowUnsignedPromotion) {
        throw 'Promotion requires exactly one explicit policy: -RequireSigning or -AllowUnsignedPromotion.'
    }

    if ($AllowUnsignedPromotion) {
        if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint) -or
            -not [string]::IsNullOrWhiteSpace($SignToolPath) -or -not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
            throw 'Unsigned promotion rejects -CertificateThumbprint, -SignToolPath, and -TimestampUrl.'
        }

        $script:ReleaseKind = 'public-unsigned'
        $script:Publisher = 'Unknown'
        $script:SmartScreenWarning = $PublicUnsignedWarning
        return
    }

    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw 'Promotion signing requires -CertificateThumbprint.'
    }
    if ([string]::IsNullOrWhiteSpace($SignToolPath)) {
        throw 'Promotion signing requires -SignToolPath.'
    }
    if ([string]::IsNullOrWhiteSpace($TimestampUrl)) {
        throw 'Promotion signing requires -TimestampUrl.'
    }

    $script:ResolvedSignToolPath = Resolve-ToolPath -Candidate $SignToolPath -DisplayName 'SignTool'
    $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
        Select-Object -First 1
    if ($null -eq $certificate) {
        throw "Signing certificate was not found in the current-user or local-machine personal store: $normalizedThumbprint"
    }

    $script:NormalizedCertificateThumbprint = $normalizedThumbprint
    $script:ReleaseKind = 'promotion-signed'
    $script:Publisher = $certificate.Subject
    $script:SmartScreenWarning = ''
}

function Copy-MergedPublishTree {
    param(
        [string]$Source,
        [string]$Destination
    )

    Assert-NoReparseEntries -Root $Source -Label 'Publish source'
    Assert-NoReparseEntries -Root $Destination -Label 'Publish destination'
    $files = Get-ChildItem -LiteralPath $Source -File -Recurse | Sort-Object FullName
    foreach ($file in $files) {
        $relativePath = Get-RelativeFilePath -Root $Source -Path $file.FullName
        $destinationPath = Join-Path $Destination $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

        if (Test-PathEntryExistsNoFollow -Path $destinationPath) {
            if (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
                throw "Publish outputs contain a file/directory type collision at $relativePath."
            }
            $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $destinationHash = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
            if (-not $sourceHash.Equals($destinationHash, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Publish outputs contain a non-identical collision at $relativePath."
            }
            continue
        }

        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath
    }
    Assert-NoReparseEntries -Root $Destination -Label 'Merged publish destination'
}

function Copy-RequiredDirectory {
    param(
        [string]$Source,
        [string]$Destination,
        [string]$DisplayName
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "$DisplayName was not found: $Source"
    }
    Assert-NoReparseEntries -Root $Source -Label $DisplayName
    if (Test-PathEntryExistsNoFollow -Path $Destination) {
        throw "$DisplayName destination already exists: $Destination"
    }

    $destinationParent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
    Assert-NoReparseEntries -Root $Destination -Label "$DisplayName destination"
}

function Invoke-AuthenticodeSigning {
    param([string[]]$Paths)

    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required signing target was not found: $path"
        }

        Invoke-NativeCommand -FilePath $script:ResolvedSignToolPath -Arguments @(
            'sign', '/sha1', $script:NormalizedCertificateThumbprint,
            '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', $path
        ) -FailureMessage "Signing failed for $path"

        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Signature verification failed for ${path}: $($signature.StatusMessage)"
        }
    }
}

function Assert-PublishedVersion {
    param(
        [string]$Path,
        [string]$ExpectedVersion
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Published version target was not found: $Path"
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $reportedVersion = $versionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($reportedVersion)) {
        $reportedVersion = $versionInfo.FileVersion
    }
    if ([string]::IsNullOrWhiteSpace($reportedVersion) -or
        -not $reportedVersion.StartsWith($ExpectedVersion, [System.StringComparison]::Ordinal)) {
        throw "Published file version mismatch for $Path. Expected $ExpectedVersion, found $reportedVersion."
    }
}

function Assert-UnsignedAuthenticode {
    param([string[]]$Paths)

    foreach ($path in $Paths) {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
            throw "Local unsigned target has unexpected signature status for ${path}: $($signature.Status)"
        }
    }
}

function Invoke-ExecutableBodyProbe {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$ExpectedPattern,
        [string]$Label,
        [string]$ProfileRoot
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $false
    $startInfo.Environment['APPDATA'] = Join-Path $ProfileRoot 'roaming'
    $startInfo.Environment['LOCALAPPDATA'] = Join-Path $ProfileRoot 'local'
    [void]$startInfo.Environment.Remove('XECLI_HOME')
    $startInfo.Environment['XECLI_LANG'] = 'en'

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "$Label did not start."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            throw "$Label timed out after 30 seconds."
        }
        [void][System.Threading.Tasks.Task]::WaitAll(@($stdoutTask, $stderrTask), 5000)
        $stdout = $stdoutTask.Result.Trim()
        $stderr = $stderrTask.Result.Trim()
        if ($process.ExitCode -ne 0) {
            throw "$Label exited with code $($process.ExitCode). STDERR: $stderr"
        }
        if ([string]::IsNullOrWhiteSpace($stdout) -or $stdout -notmatch $ExpectedPattern) {
            throw "$Label returned an unexpected output body: $stdout"
        }
        Write-Host "Body probe passed: $Label"
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-PortableBodyProbes {
    param(
        [string]$StageRoot,
        [string]$ProbeRoot,
        [string]$ProfileRoot
    )

    Copy-MergedPublishTree -Source $StageRoot -Destination $ProbeRoot

    $escapedVersion = [regex]::Escape($Version)
    $rghPath = Join-Path $ProbeRoot 'rgh.exe'
    $terminalPath = Join-Path $ProbeRoot 'XeTerminal.exe'
    $portableConfigPath = Join-Path $ProbeRoot 'UserData\config.json'
    $savedXeCliHome = Get-Item -LiteralPath 'Env:XECLI_HOME' -ErrorAction SilentlyContinue
    try {
        Remove-Item -LiteralPath 'Env:XECLI_HOME' -ErrorAction SilentlyContinue

        Invoke-ExecutableBodyProbe -FilePath $rghPath -Arguments @('language', '--set', 'en') -ExpectedPattern '(?i)UI language updated' -Label 'rgh.exe portable config write' -ProfileRoot $profileRoot
        if (-not (Test-Path -LiteralPath $portableConfigPath -PathType Leaf)) {
            throw "Portable config probe did not write package-local UserData: $portableConfigPath"
        }

        Invoke-ExecutableBodyProbe -FilePath $rghPath -Arguments @('--version') -ExpectedPattern $escapedVersion -Label 'rgh.exe --version' -ProfileRoot $profileRoot
        Invoke-ExecutableBodyProbe -FilePath $rghPath -Arguments @('help') -ExpectedPattern '(?i)usage|rgh' -Label 'rgh.exe help' -ProfileRoot $profileRoot
        Invoke-ExecutableBodyProbe -FilePath $terminalPath -Arguments @('--version') -ExpectedPattern "^XeTerminal $escapedVersion$" -Label 'XeTerminal.exe --version' -ProfileRoot $profileRoot
        Invoke-ExecutableBodyProbe -FilePath $terminalPath -Arguments @('-v') -ExpectedPattern "^XeTerminal $escapedVersion$" -Label 'XeTerminal.exe -v' -ProfileRoot $profileRoot
        Invoke-ExecutableBodyProbe -FilePath $terminalPath -Arguments @('version') -ExpectedPattern "^XeTerminal $escapedVersion$" -Label 'XeTerminal.exe version' -ProfileRoot $profileRoot

        $expectedProfileDirectories = @(
            (Join-Path $ProfileRoot 'roaming'),
            (Join-Path $ProfileRoot 'local')
        )
        $profileWrites = @(
            Get-ChildItem -LiteralPath $ProfileRoot -Force -Recurse |
                Where-Object { $expectedProfileDirectories -notcontains $_.FullName }
        )
        if ($profileWrites.Count -ne 0) {
            $writtenPaths = ($profileWrites | ForEach-Object FullName) -join ', '
            throw "Portable body probes wrote to the isolated Windows profile: $writtenPaths"
        }
    }
    finally {
        if ($null -eq $savedXeCliHome) {
            Remove-Item -LiteralPath 'Env:XECLI_HOME' -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -LiteralPath 'Env:XECLI_HOME' -Value $savedXeCliHome.Value
        }
    }

    Write-Host 'Portable isolation probe passed: writes stayed under package-local UserData.'
    return $true
}

function Get-ReleaseFileInventory {
    param(
        [string]$StageRoot,
        [string]$ExcludedPath
    )

    $records = @()
    $files = Get-ChildItem -LiteralPath $StageRoot -File -Recurse |
        Where-Object { [string]::IsNullOrWhiteSpace($ExcludedPath) -or $_.FullName -ne $ExcludedPath } |
        Sort-Object { (Get-RelativeFilePath -Root $StageRoot -Path $_.FullName).Replace('\', '/') }

    foreach ($file in $files) {
        $records += [ordered]@{
            Path = (Get-RelativeFilePath -Root $StageRoot -Path $file.FullName).Replace('\', '/')
            Size = $file.Length
            Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

    return $records
}

function Test-IsInstallerOwnedStagePath {
    param([string]$RelativePath)

    $normalizedPath = $RelativePath.Replace('\', '/')
    if ($normalizedPath.Equals('xecli.portable', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $firstSeparator = $normalizedPath.IndexOf('/')
    $topLevelName = if ($firstSeparator -lt 0) { $normalizedPath } else { $normalizedPath.Substring(0, $firstSeparator) }
    if ($topLevelName.Equals('UserData', [System.StringComparison]::OrdinalIgnoreCase) -or
        $topLevelName.Equals('logs', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    return $true
}

function Get-InstallerOwnedStagePaths {
    param(
        [string]$StageRoot,
        [switch]$IncludeOwnedHashesManifest
    )

    $paths = [System.Collections.Generic.List[string]]::new()
    $seenPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $StageRoot -File -Recurse) {
        $relativePath = (Get-RelativeFilePath -Root $StageRoot -Path $file.FullName).Replace('\', '/')
        if (-not (Test-IsInstallerOwnedStagePath -RelativePath $relativePath)) {
            continue
        }
        if ($relativePath.Equals($PreviousOwnedFilesManifestName, [System.StringComparison]::OrdinalIgnoreCase) -or
            $relativePath.Equals($PreviousOwnedFilesManifestTempName, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Installer-owned staging collides with reserved upgrade evidence path: $relativePath"
        }
        if ($relativePath.Equals($OwnedFilesManifestName, [System.StringComparison]::OrdinalIgnoreCase) -or
            $relativePath.Equals($OwnedHashesManifestName, [System.StringComparison]::OrdinalIgnoreCase) -or
            $relativePath.Equals($ReleaseManifestName, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if (-not $seenPaths.Add($relativePath)) {
            throw "Installer-owned staging contains a case-insensitive duplicate path: $relativePath"
        }
        $paths.Add($relativePath)
    }

    $requiredPaths = @($OwnedFilesManifestName, $ReleaseManifestName)
    if ($IncludeOwnedHashesManifest) {
        $requiredPaths += $OwnedHashesManifestName
    }
    foreach ($requiredPath in $requiredPaths) {
        if (-not $seenPaths.Add($requiredPath)) {
            throw "Installer-owned staging collides with required metadata path: $requiredPath"
        }
        $paths.Add($requiredPath)
    }

    $result = $paths.ToArray()
    [System.Array]::Sort($result, [System.StringComparer]::Ordinal)
    return $result
}

function Assert-InstallerOwnedStagePaths {
    param(
        [string]$StageRoot,
        [string[]]$ExpectedPaths,
        [switch]$IncludeOwnedHashesManifest
    )

    $requiredPaths = @($OwnedFilesManifestName, $ReleaseManifestName)
    if ($IncludeOwnedHashesManifest) {
        $requiredPaths += $OwnedHashesManifestName
    }
    foreach ($requiredPath in $requiredPaths) {
        $requiredFile = Join-Path $StageRoot $requiredPath
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Installer-owned staging changed while the installer was being compiled: required file is missing: $requiredPath"
        }
    }

    $actualPaths = @(Get-InstallerOwnedStagePaths -StageRoot $StageRoot `
        -IncludeOwnedHashesManifest:$IncludeOwnedHashesManifest)
    if ($actualPaths.Count -ne $ExpectedPaths.Count) {
        throw 'Installer-owned staging changed while the installer was being compiled.'
    }
    for ($index = 0; $index -lt $ExpectedPaths.Count; $index++) {
        if (-not $actualPaths[$index].Equals($ExpectedPaths[$index], [System.StringComparison]::Ordinal)) {
            throw "Installer-owned staging changed while the installer was being compiled at inventory line $($index + 1)."
        }
    }
}

function Write-InstallerOwnedFilesManifest {
    param(
        [string]$StageRoot,
        [switch]$IncludeOwnedHashesManifest
    )

    $manifestPath = Join-Path $StageRoot $OwnedFilesManifestName
    $ownedPaths = @(Get-InstallerOwnedStagePaths -StageRoot $StageRoot `
        -IncludeOwnedHashesManifest:$IncludeOwnedHashesManifest)
    if ($ownedPaths.Count -eq 0) {
        throw 'Refusing to write an empty installer-owned file manifest.'
    }

    [System.IO.File]::WriteAllText(
        $manifestPath,
        (($ownedPaths -join "`n") + "`n"),
        [System.Text.UTF8Encoding]::new($false))
    return $ownedPaths
}

function Get-InstallerOwnedHashRecords {
    param(
        [string]$StageRoot,
        [string[]]$OwnedPaths
    )

    if ($OwnedPaths -notcontains $OwnedHashesManifestName) {
        throw "Installer-owned inventory does not include $OwnedHashesManifestName."
    }

    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $OwnedPaths) {
        if ($relativePath.Equals($OwnedHashesManifestName, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $filePath = Join-Path $StageRoot $relativePath.Replace('/', '\')
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "Cannot hash missing installer-owned file: $relativePath"
        }
        $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $records.Add("$hash *$relativePath")
    }
    return $records.ToArray()
}

function Write-InstallerOwnedHashesManifest {
    param(
        [string]$StageRoot,
        [string[]]$OwnedPaths
    )

    $hashManifestPath = Join-Path $StageRoot $OwnedHashesManifestName
    $records = @(Get-InstallerOwnedHashRecords -StageRoot $StageRoot -OwnedPaths $OwnedPaths)
    if ($records.Count -ne ($OwnedPaths.Count - 1)) {
        throw 'Installer-owned hash inventory does not cover every owned file except itself.'
    }
    [System.IO.File]::WriteAllText(
        $hashManifestPath,
        (($records -join "`n") + "`n"),
        [System.Text.UTF8Encoding]::new($false))
    return $records
}

function Assert-InstallerOwnedHashesManifest {
    param(
        [string]$StageRoot,
        [string[]]$OwnedPaths,
        [string[]]$ExpectedRecords
    )

    $hashManifestPath = Join-Path $StageRoot $OwnedHashesManifestName
    $actualRecords = [System.IO.File]::ReadAllLines($hashManifestPath, [System.Text.Encoding]::UTF8)
    $currentRecords = @(Get-InstallerOwnedHashRecords -StageRoot $StageRoot -OwnedPaths $OwnedPaths)
    if ($actualRecords.Count -ne $ExpectedRecords.Count -or
        $currentRecords.Count -ne $ExpectedRecords.Count) {
        throw 'Installer-owned hash inventory count changed after generation.'
    }
    for ($index = 0; $index -lt $ExpectedRecords.Count; $index++) {
        if (-not $actualRecords[$index].Equals($ExpectedRecords[$index], [System.StringComparison]::Ordinal) -or
            -not $currentRecords[$index].Equals($ExpectedRecords[$index], [System.StringComparison]::Ordinal)) {
            throw "Installer-owned hash inventory changed at line $($index + 1)."
        }
    }
}

function Assert-InstallerOwnedFilesReleaseManifest {
    param(
        [string]$StageRoot,
        [string[]]$ExpectedPaths
    )

    $ownedManifestPath = Join-Path $StageRoot $OwnedFilesManifestName
    $actualPaths = [System.IO.File]::ReadAllLines($ownedManifestPath, [System.Text.Encoding]::UTF8)
    if ($actualPaths.Count -ne $ExpectedPaths.Count) {
        throw 'Installer-owned file manifest changed after it was generated.'
    }
    for ($index = 0; $index -lt $ExpectedPaths.Count; $index++) {
        if (-not $actualPaths[$index].Equals($ExpectedPaths[$index], [System.StringComparison]::Ordinal)) {
            throw "Installer-owned file manifest is not the expected ordinal inventory at line $($index + 1)."
        }
    }

    $releaseManifestPath = Join-Path $StageRoot $ReleaseManifestName
    $releaseManifest = Get-Content -LiteralPath $releaseManifestPath -Raw | ConvertFrom-Json
    $ownedRecords = @($releaseManifest.Files | Where-Object {
        $_.Path -ceq $OwnedFilesManifestName
    })
    if ($ownedRecords.Count -ne 1) {
        throw "Release manifest must declare exactly one $OwnedFilesManifestName record."
    }

    $ownedFile = Get-Item -LiteralPath $ownedManifestPath
    $ownedHash = (Get-FileHash -LiteralPath $ownedManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([long]$ownedRecords[0].Size -ne $ownedFile.Length -or
        -not ([string]$ownedRecords[0].Sha256).Equals($ownedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release manifest does not match the exact $OwnedFilesManifestName body."
    }
}

function Assert-InstallerOwnedFilesZipEntry {
    param(
        [string]$StageRoot,
        [string]$ZipPath,
        [string]$ArchiveRootName
    )

    $expectedPath = "$ArchiveRootName/$OwnedFilesManifestName"
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -ceq $expectedPath })
        if ($entries.Count -ne 1) {
            throw "Portable ZIP must contain exactly one $expectedPath entry."
        }

        $stream = $entries[0].Open()
        try {
            $hasher = [System.Security.Cryptography.SHA256]::Create()
            try {
                $zipHash = [System.Convert]::ToHexString($hasher.ComputeHash($stream)).ToLowerInvariant()
            }
            finally {
                $hasher.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }

        $stageHash = (Get-FileHash -LiteralPath (Join-Path $StageRoot $OwnedFilesManifestName) -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $zipHash.Equals($stageHash, [System.StringComparison]::Ordinal)) {
            throw "Portable ZIP $OwnedFilesManifestName does not match staging."
        }
    }
    finally {
        $archive.Dispose()
    }
}

function New-ReleaseMetadataDocument {
    param(
        [object[]]$Files,
        [string]$GitRevision,
        [string]$BuiltUtc,
        [string]$InstallerStatus,
        [bool]$PortableMarkerValidated,
        [bool]$PortableBodyValidated,
        [object[]]$Artifacts,
        [string]$SubjectArtifact
    )

    if (-not $PortableMarkerValidated -or -not $PortableBodyValidated) {
        throw 'Refusing to emit validated release metadata before portable validation succeeds.'
    }

    $document = [ordered]@{
        SchemaVersion = 2
        AppVersion = $Version
        Runtime = $Runtime
        ReleaseKind = $script:ReleaseKind
        GitRevision = $GitRevision
        BuiltUtc = $BuiltUtc
        ProvenanceStatus = $script:ProvenanceStatus
        PortableMarkerValidated = $PortableMarkerValidated
        PortableBodyValidated = $PortableBodyValidated
        InstallerStatus = $InstallerStatus
        Publisher = $script:Publisher
        SmartScreenWarning = $script:SmartScreenWarning
        Files = @($Files)
    }

    if ($null -ne $Artifacts) {
        $document.Artifacts = @($Artifacts)
    }
    if (-not [string]::IsNullOrWhiteSpace($SubjectArtifact)) {
        $document.SubjectArtifact = $SubjectArtifact
    }

    return $document
}

function Write-JsonDocument {
    param(
        [string]$Path,
        [object]$Value
    )

    $json = $Value | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Write-ReleaseManifest {
    param(
        [string]$StageRoot,
        [string]$GitRevision,
        [string]$BuiltUtc,
        [string]$InstallerStatus,
        [bool]$PortableMarkerValidated,
        [bool]$PortableBodyValidated
    )

    $manifestPath = Join-Path $StageRoot $ReleaseManifestName
    $records = @(Get-ReleaseFileInventory -StageRoot $StageRoot -ExcludedPath $manifestPath)
    $manifest = New-ReleaseMetadataDocument `
        -Files $records `
        -GitRevision $GitRevision `
        -BuiltUtc $BuiltUtc `
        -InstallerStatus $InstallerStatus `
        -PortableMarkerValidated $PortableMarkerValidated `
        -PortableBodyValidated $PortableBodyValidated
    Write-JsonDocument -Path $manifestPath -Value $manifest
    return $records
}

function Set-DeterministicTimestamps {
    param(
        [string]$Root,
        [DateTimeOffset]$Timestamp
    )

    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse) {
        $file.LastWriteTimeUtc = $Timestamp.UtcDateTime
    }
}

function New-DeterministicZip {
    param(
        [string]$StageRoot,
        [string]$ZipPath,
        [string]$ArchiveRootName,
        [DateTimeOffset]$Timestamp
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    $stream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false,
            [System.Text.Encoding]::UTF8)
        try {
            $files = Get-ChildItem -LiteralPath $StageRoot -File -Recurse |
                Sort-Object { (Get-RelativeFilePath -Root $StageRoot -Path $_.FullName).Replace('\', '/') }
            foreach ($file in $files) {
                $relativePath = (Get-RelativeFilePath -Root $StageRoot -Path $file.FullName).Replace('\', '/')
                $entry = $archive.CreateEntry(
                    "$ArchiveRootName/$relativePath",
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $Timestamp
                $input = $file.OpenRead()
                try {
                    $output = $entry.Open()
                    try {
                        $input.CopyTo($output)
                    }
                    finally {
                        $output.Dispose()
                    }
                }
                finally {
                    $input.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Write-Sha256Sidecar {
    param([string]$FilePath)

    $hash = (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sidecarPath = "$FilePath.sha256"
    $record = "$hash *$([System.IO.Path]::GetFileName($FilePath))$([Environment]::NewLine)"
    [System.IO.File]::WriteAllText($sidecarPath, $record, [System.Text.UTF8Encoding]::new($false))
    return $sidecarPath
}

function New-ReleaseArtifactRecord {
    param(
        [string]$OutputRoot,
        [string]$FilePath,
        [string]$Kind,
        [string]$Sha256SidecarPath
    )

    $relativePath = (Get-RelativeFilePath -Root $OutputRoot -Path $FilePath).Replace('\', '/')
    return [ordered]@{
        Path = $relativePath
        Kind = $Kind
        Size = (Get-Item -LiteralPath $FilePath).Length
        Sha256 = (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
        Sha256Sidecar = (Get-RelativeFilePath -Root $OutputRoot -Path $Sha256SidecarPath).Replace('\', '/')
        MetadataSidecar = "$relativePath.release.json"
    }
}

function Write-ReleaseMetadataDocuments {
    param(
        [string]$OutputRoot,
        [object[]]$PortableFiles,
        [object[]]$InstallerFiles,
        [string]$GitRevision,
        [string]$BuiltUtc,
        [string]$InstallerStatus,
        [bool]$PortableMarkerValidated,
        [bool]$PortableBodyValidated,
        [string]$ZipPath,
        [string]$ZipHashPath,
        [string]$InstallerPath,
        [string]$InstallerHashPath
    )

    $artifacts = @(
        New-ReleaseArtifactRecord `
            -OutputRoot $OutputRoot `
            -FilePath $ZipPath `
            -Kind 'portable-zip' `
            -Sha256SidecarPath $ZipHashPath
    )
    if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
        $artifacts += New-ReleaseArtifactRecord `
            -OutputRoot $OutputRoot `
            -FilePath $InstallerPath `
            -Kind 'installer' `
            -Sha256SidecarPath $InstallerHashPath
    }

    $summaryPath = Join-Path $OutputRoot 'release-summary.json'
    $summary = New-ReleaseMetadataDocument `
        -Files $PortableFiles `
        -GitRevision $GitRevision `
        -BuiltUtc $BuiltUtc `
        -InstallerStatus $InstallerStatus `
        -PortableMarkerValidated $PortableMarkerValidated `
        -PortableBodyValidated $PortableBodyValidated `
        -Artifacts $artifacts
    Write-JsonDocument -Path $summaryPath -Value $summary

    foreach ($artifact in $artifacts) {
        $artifactFiles = if ($artifact.Kind -eq 'installer') {
            if ($null -eq $InstallerFiles -or $InstallerFiles.Count -eq 0) {
                throw 'Installer metadata requires the exact installer-stage file inventory.'
            }
            $InstallerFiles
        }
        else {
            $PortableFiles
        }
        $sidecarPath = Join-Path $OutputRoot $artifact.MetadataSidecar.Replace('/', '\')
        $sidecar = New-ReleaseMetadataDocument `
            -Files $artifactFiles `
            -GitRevision $GitRevision `
            -BuiltUtc $BuiltUtc `
            -InstallerStatus $InstallerStatus `
            -PortableMarkerValidated $PortableMarkerValidated `
            -PortableBodyValidated $PortableBodyValidated `
            -Artifacts $artifacts `
            -SubjectArtifact $artifact.Path
        Write-JsonDocument -Path $sidecarPath -Value $sidecar
    }

    return $summaryPath
}

function Assert-ExactSha256Sidecar {
    param(
        [string]$ArtifactPath,
        [string]$SidecarPath
    )

    $expectedSidecarPath = "$ArtifactPath.sha256"
    if (-not [System.IO.Path]::GetFullPath($SidecarPath).Equals(
            [System.IO.Path]::GetFullPath($expectedSidecarPath),
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifact checksum sidecar path is not canonical: $SidecarPath"
    }

    $hash = (Get-FileHash -LiteralPath $ArtifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedBody = "$hash *$([System.IO.Path]::GetFileName($ArtifactPath))$([Environment]::NewLine)"
    $actualBody = [System.IO.File]::ReadAllText($SidecarPath)
    if (-not $actualBody.Equals($expectedBody, [System.StringComparison]::Ordinal)) {
        throw "Artifact checksum sidecar does not match $ArtifactPath exactly: $SidecarPath"
    }
}

function Pin-PromotionGitIdentity {
    if ($Mode -ne 'Promotion') {
        return
    }

    Assert-CleanPromotionTree
    $script:PinnedPromotionGitRevision = Get-GitValue -Arguments @('rev-parse', 'HEAD')
    $script:PinnedPromotionGitTree = Get-GitValue -Arguments @('rev-parse', 'HEAD^{tree}')
}

function Assert-PromotionGitIdentity {
    if ($Mode -ne 'Promotion') {
        return
    }
    if ([string]::IsNullOrWhiteSpace($script:PinnedPromotionGitRevision) -or
        [string]::IsNullOrWhiteSpace($script:PinnedPromotionGitTree)) {
        throw 'Promotion git identity was not pinned before the build.'
    }

    Assert-CleanPromotionTree
    $actualRevision = Get-GitValue -Arguments @('rev-parse', 'HEAD')
    $actualTree = Get-GitValue -Arguments @('rev-parse', 'HEAD^{tree}')
    if (-not $actualRevision.Equals($script:PinnedPromotionGitRevision, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $actualTree.Equals($script:PinnedPromotionGitTree, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Promotion git identity changed while release artifacts were being built.'
    }
}

function Assert-PromotionReleaseArtifacts {
    param(
        [string]$OutputRoot,
        [string]$ZipPath,
        [string]$ZipHashPath,
        [string]$InstallerPath,
        [string]$InstallerHashPath,
        [string]$SummaryPath,
        [string]$GitRevision,
        [string]$InstallerStatus,
        [object[]]$PortableFiles,
        [object[]]$InstallerFiles
    )

    if ($Mode -ne 'Promotion') {
        return
    }
    if (-not $BuildInstaller) {
        throw 'Promotion artifact validation requires -BuildInstaller.'
    }

    $expectedZipPath = Join-Path $OutputRoot "XeCLI-$Version-$Runtime.zip"
    $expectedInstallerPath = Join-Path $OutputRoot "XeCLI-$Version-$Runtime-setup.exe"
    $expectedSummaryPath = Join-Path $OutputRoot 'release-summary.json'
    if (-not [System.IO.Path]::GetFullPath($ZipPath).Equals(
            [System.IO.Path]::GetFullPath($expectedZipPath),
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Promotion portable ZIP path is not canonical: $ZipPath"
    }
    if (-not [System.IO.Path]::GetFullPath($InstallerPath).Equals(
            [System.IO.Path]::GetFullPath($expectedInstallerPath),
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Promotion installer path is not canonical: $InstallerPath"
    }
    if (-not [System.IO.Path]::GetFullPath($SummaryPath).Equals(
            [System.IO.Path]::GetFullPath($expectedSummaryPath),
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Promotion release summary path is not canonical: $SummaryPath"
    }

    $zipMetadataPath = "$ZipPath.release.json"
    $installerMetadataPath = "$InstallerPath.release.json"
    $requiredFiles = @(
        $ZipPath,
        $ZipHashPath,
        $zipMetadataPath,
        $InstallerPath,
        $InstallerHashPath,
        $installerMetadataPath,
        $SummaryPath
    )
    foreach ($requiredFile in $requiredFiles) {
        if ([string]::IsNullOrWhiteSpace($requiredFile) -or
            -not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Promotion did not produce a required publication artifact or sidecar: $requiredFile"
        }
        Assert-NotReparseEntry -Path $requiredFile -Label 'Promotion publication artifact'
        if ((Get-Item -LiteralPath $requiredFile).Length -eq 0) {
            throw "Promotion publication artifact is empty: $requiredFile"
        }
    }

    Assert-ExactSha256Sidecar -ArtifactPath $ZipPath -SidecarPath $ZipHashPath
    Assert-ExactSha256Sidecar -ArtifactPath $InstallerPath -SidecarPath $InstallerHashPath

    $expectedInstallerStatus = if ($script:ReleaseKind -eq 'promotion-signed') {
        'built-signed'
    }
    else {
        'built-unsigned'
    }
    if (-not $InstallerStatus.Equals($expectedInstallerStatus, [System.StringComparison]::Ordinal)) {
        throw "Promotion installer status must be $expectedInstallerStatus; found $InstallerStatus."
    }

    $zipRelativePath = (Get-RelativeFilePath -Root $OutputRoot -Path $ZipPath).Replace('\', '/')
    $installerRelativePath = (Get-RelativeFilePath -Root $OutputRoot -Path $InstallerPath).Replace('\', '/')
    $expectedArtifacts = @{}
    $expectedArtifacts[$zipRelativePath] = [ordered]@{
        Kind = 'portable-zip'
        Path = $ZipPath
        Sha256Sidecar = (Get-RelativeFilePath -Root $OutputRoot -Path $ZipHashPath).Replace('\', '/')
        MetadataSidecar = (Get-RelativeFilePath -Root $OutputRoot -Path $zipMetadataPath).Replace('\', '/')
    }
    $expectedArtifacts[$installerRelativePath] = [ordered]@{
        Kind = 'installer'
        Path = $InstallerPath
        Sha256Sidecar = (Get-RelativeFilePath -Root $OutputRoot -Path $InstallerHashPath).Replace('\', '/')
        MetadataSidecar = (Get-RelativeFilePath -Root $OutputRoot -Path $installerMetadataPath).Replace('\', '/')
    }

    $metadataDocuments = @(
        [pscustomobject]@{ Path = $SummaryPath; SubjectArtifact = ''; Files = $PortableFiles },
        [pscustomobject]@{ Path = $zipMetadataPath; SubjectArtifact = $zipRelativePath; Files = $PortableFiles },
        [pscustomobject]@{ Path = $installerMetadataPath; SubjectArtifact = $installerRelativePath; Files = $InstallerFiles }
    )
    foreach ($metadataDocument in $metadataDocuments) {
        try {
            $document = Get-Content -LiteralPath $metadataDocument.Path -Raw | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "Promotion release metadata is invalid JSON at $($metadataDocument.Path): $($_.Exception.Message)"
        }

        $schemaVersion = Get-RequiredJsonProperty `
            -Object $document `
            -Name 'SchemaVersion' `
            -DocumentLabel $metadataDocument.Path
        if ([int]$schemaVersion -ne 2) {
            throw "Promotion release metadata must use SchemaVersion 2: $($metadataDocument.Path)"
        }

        $metadataChecks = [ordered]@{
            AppVersion = $Version
            Runtime = $Runtime
            ReleaseKind = $script:ReleaseKind
            GitRevision = $GitRevision
            InstallerStatus = $expectedInstallerStatus
        }
        foreach ($propertyName in $metadataChecks.Keys) {
            $actualValue = [string](Get-RequiredJsonProperty `
                -Object $document `
                -Name $propertyName `
                -DocumentLabel $metadataDocument.Path)
            $expectedValue = [string]$metadataChecks[$propertyName]
            if (-not $actualValue.Equals($expectedValue, [System.StringComparison]::Ordinal)) {
                throw "Promotion release metadata $propertyName mismatch in $($metadataDocument.Path); expected $expectedValue, found $actualValue."
            }
        }

        foreach ($validationProperty in @('PortableMarkerValidated', 'PortableBodyValidated')) {
            $validationValue = Get-RequiredJsonProperty `
                -Object $document `
                -Name $validationProperty `
                -DocumentLabel $metadataDocument.Path
            if ($validationValue -ne $true) {
                throw "Promotion release metadata requires $validationProperty=true: $($metadataDocument.Path)"
            }
        }

        $subjectProperty = $document.PSObject.Properties['SubjectArtifact']
        if ([string]::IsNullOrWhiteSpace($metadataDocument.SubjectArtifact)) {
            if ($null -ne $subjectProperty) {
                throw "Release summary must not declare SubjectArtifact: $($metadataDocument.Path)"
            }
        }
        elseif ($null -eq $subjectProperty -or
            -not ([string]$subjectProperty.Value).Equals(
                $metadataDocument.SubjectArtifact,
                [System.StringComparison]::Ordinal)) {
            throw "Promotion metadata SubjectArtifact mismatch: $($metadataDocument.Path)"
        }

        $expectedFiles = @($metadataDocument.Files)
        $actualFiles = @(Get-RequiredJsonProperty `
            -Object $document `
            -Name 'Files' `
            -DocumentLabel $metadataDocument.Path)
        if ($actualFiles.Count -ne $expectedFiles.Count) {
            throw "Promotion release metadata file inventory count mismatch: $($metadataDocument.Path)"
        }
        $expectedFilesByPath = @{}
        foreach ($expectedFile in $expectedFiles) {
            $expectedFilesByPath[[string]$expectedFile.Path] = $expectedFile
        }
        foreach ($actualFile in $actualFiles) {
            $actualPath = [string](Get-RequiredJsonProperty `
                -Object $actualFile `
                -Name 'Path' `
                -DocumentLabel $metadataDocument.Path)
            if (-not $expectedFilesByPath.ContainsKey($actualPath)) {
                throw "Promotion release metadata contains an unexpected file record: $actualPath"
            }
            $expectedFile = $expectedFilesByPath[$actualPath]
            $actualSize = [long](Get-RequiredJsonProperty `
                -Object $actualFile `
                -Name 'Size' `
                -DocumentLabel "$($metadataDocument.Path) file $actualPath")
            $actualHash = [string](Get-RequiredJsonProperty `
                -Object $actualFile `
                -Name 'Sha256' `
                -DocumentLabel "$($metadataDocument.Path) file $actualPath")
            if ($actualSize -ne [long]$expectedFile.Size -or
                -not $actualHash.Equals([string]$expectedFile.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Promotion release metadata file inventory mismatch: $actualPath"
            }
        }

        $artifactRecords = @(Get-RequiredJsonProperty `
            -Object $document `
            -Name 'Artifacts' `
            -DocumentLabel $metadataDocument.Path)
        if ($artifactRecords.Count -ne $expectedArtifacts.Count) {
            throw "Promotion release metadata must describe exactly two primary artifacts: $($metadataDocument.Path)"
        }

        $seenArtifactPaths = @{}
        foreach ($artifactRecord in $artifactRecords) {
            $artifactRelativePath = [string](Get-RequiredJsonProperty `
                -Object $artifactRecord `
                -Name 'Path' `
                -DocumentLabel $metadataDocument.Path)
            $artifactRelativePath = $artifactRelativePath.Replace('\', '/')
            if (-not $expectedArtifacts.ContainsKey($artifactRelativePath)) {
                throw "Promotion release metadata describes an unexpected artifact: $artifactRelativePath"
            }
            if ($seenArtifactPaths.ContainsKey($artifactRelativePath)) {
                throw "Promotion release metadata contains a duplicate artifact: $artifactRelativePath"
            }
            $seenArtifactPaths[$artifactRelativePath] = $true

            $expectedArtifact = $expectedArtifacts[$artifactRelativePath]
            $artifactChecks = [ordered]@{
                Kind = $expectedArtifact.Kind
                Size = (Get-Item -LiteralPath $expectedArtifact.Path).Length
                Sha256 = (Get-FileHash -LiteralPath $expectedArtifact.Path -Algorithm SHA256).Hash.ToLowerInvariant()
                Sha256Sidecar = $expectedArtifact.Sha256Sidecar
                MetadataSidecar = $expectedArtifact.MetadataSidecar
            }
            foreach ($propertyName in $artifactChecks.Keys) {
                $actualValue = [string](Get-RequiredJsonProperty `
                    -Object $artifactRecord `
                    -Name $propertyName `
                    -DocumentLabel "$($metadataDocument.Path) artifact $artifactRelativePath")
                $expectedValue = [string]$artifactChecks[$propertyName]
                if (-not $actualValue.Equals($expectedValue, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Promotion artifact metadata $propertyName mismatch for $artifactRelativePath in $($metadataDocument.Path)."
                }
            }
        }
    }
}

function Assert-ExactPromotionPublicationRoot {
    param([string]$OutputRoot)

    if ($Mode -ne 'Promotion') {
        return
    }

    $expectedFileNames = @(
        "XeCLI-$Version-$Runtime.zip",
        "XeCLI-$Version-$Runtime.zip.sha256",
        "XeCLI-$Version-$Runtime.zip.release.json",
        "XeCLI-$Version-$Runtime-setup.exe",
        "XeCLI-$Version-$Runtime-setup.exe.sha256",
        "XeCLI-$Version-$Runtime-setup.exe.release.json",
        'release-summary.json'
    )
    $expectedDirectoryNames = @('stage')

    Assert-NotReparseEntry -Path $OutputRoot -Label 'Promotion publication root'
    $entries = @(Get-ChildItem -LiteralPath $OutputRoot -Force)
    foreach ($entry in $entries) {
        Assert-NotReparseEntry -Path $entry.FullName -Label 'Promotion publication-root entry'
    }

    $actualFileNames = @(
        $entries |
            Where-Object { -not $_.PSIsContainer } |
            ForEach-Object { $_.Name }
    )
    $actualDirectoryNames = @(
        $entries |
            Where-Object { $_.PSIsContainer } |
            ForEach-Object { $_.Name }
    )
    $missingFiles = @($expectedFileNames | Where-Object { $actualFileNames -cnotcontains $_ })
    $unexpectedFiles = @($actualFileNames | Where-Object { $expectedFileNames -cnotcontains $_ })
    if ($actualFileNames.Count -ne $expectedFileNames.Count -or
        $missingFiles.Count -ne 0 -or
        $unexpectedFiles.Count -ne 0) {
        throw "Promotion publication root must contain exactly the seven canonical files. Expected: $($expectedFileNames -join ', '). Found: $($actualFileNames -join ', ')."
    }

    $missingDirectories = @($expectedDirectoryNames | Where-Object { $actualDirectoryNames -cnotcontains $_ })
    $unexpectedDirectories = @($actualDirectoryNames | Where-Object { $expectedDirectoryNames -cnotcontains $_ })
    if ($actualDirectoryNames.Count -ne $expectedDirectoryNames.Count -or
        $missingDirectories.Count -ne 0 -or
        $unexpectedDirectories.Count -ne 0) {
        throw "Promotion publication root must contain exactly the internal stage directory. Expected: $($expectedDirectoryNames -join ', '). Found: $($actualDirectoryNames -join ', ')."
    }

    Assert-NoReparseEntries -Root $OutputRoot -Label 'Promotion publication root'
}

function Invoke-ReleaseCheck {
    param(
        [string]$ValidatorPath,
        [string]$StageRoot,
        [string]$ProjectPath,
        [string]$ZipPath,
        [string]$HashPath,
        [string]$SummaryPath,
        [string]$ProfileRoot,
        [switch]$StageOnly
    )

    $arguments = @(
        'release', 'check',
        '--publish-dir', $StageRoot,
        '--project', $ProjectPath,
        '--expected-release-kind', $script:ReleaseKind,
        '--json'
    )
    if ($Mode -eq 'Promotion' -and -not $StageOnly) {
        $arguments += '--promotion'
    }
    if (-not [string]::IsNullOrWhiteSpace($ZipPath)) {
        $arguments += @('--zip', $ZipPath, '--hash', $HashPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
        $arguments += @('--summary', $SummaryPath)
    }

    $roamingRoot = Join-Path $ProfileRoot 'roaming'
    $localRoot = Join-Path $ProfileRoot 'local'
    $configRoot = Join-Path $roamingRoot 'XeCLI'
    [System.IO.File]::WriteAllText(
        (Join-Path $configRoot 'config.json'),
        '{"UiLanguage":"en","PathPromptHandled":true}',
        [System.Text.UTF8Encoding]::new($false))

    $savedEnvironment = @{
        APPDATA = $env:APPDATA
        LOCALAPPDATA = $env:LOCALAPPDATA
        XECLI_HOME = $env:XECLI_HOME
        XECLI_LANG = $env:XECLI_LANG
    }
    try {
        $env:APPDATA = $roamingRoot
        $env:LOCALAPPDATA = $localRoot
        $env:XECLI_HOME = Join-Path $ProfileRoot 'home'
        $env:XECLI_LANG = 'en'
        Invoke-NativeCommand -FilePath $ValidatorPath -Arguments $arguments -FailureMessage 'Release contract validation failed'
    }
    finally {
        $env:APPDATA = $savedEnvironment.APPDATA
        $env:LOCALAPPDATA = $savedEnvironment.LOCALAPPDATA
        $env:XECLI_HOME = $savedEnvironment.XECLI_HOME
        $env:XECLI_LANG = $savedEnvironment.XECLI_LANG
    }
}

function Invoke-InnoBuild {
    param(
        [string]$CompilerPath,
        [string]$StageRoot,
        [string]$InstallerOutputRoot
    )

    $arguments = @(
        "/DAppVersion=$Version",
        "/DReleaseDir=$StageRoot",
        "/DTargetRuntime=$Runtime",
        "/DOutputDir=$InstallerOutputRoot"
    )

    if ($script:ReleaseKind -eq 'promotion-signed') {
        $signCommand = '"{0}" sign /sha1 {1} /fd SHA256 /tr "{2}" /td SHA256 "$f"' -f `
            $script:ResolvedSignToolPath, $script:NormalizedCertificateThumbprint, $TimestampUrl
        $arguments += "/Sxecli=$signCommand"
        $arguments += '/DXeCliAuthenticode=1'
    }
    $arguments += (Join-Path $RepoRoot 'installer\XeCLI.iss')

    Invoke-NativeCommand -FilePath $CompilerPath -Arguments $arguments -FailureMessage 'Inno Setup compilation failed'
}

if ($Version -eq '0.0.0-dev') {
    throw 'Release builds require an explicit real version; 0.0.0-dev is not permitted.'
}
Assert-PromotionArtifactRequirements
if (-not $DryRun -and (Test-PathEntryExistsNoFollow -Path $OutputRoot)) {
    throw "Release output root must not already exist: $OutputRoot"
}

$projectPaths = @(
    (Join-Path $RepoRoot 'src\Xbox360.Remote.Cli\Xbox360.Remote.Cli.csproj'),
    (Join-Path $RepoRoot 'src\XeTerminal\XeTerminal.csproj'),
    (Join-Path $RepoRoot 'src\Xbox360.Remote\Xbox360.Remote.csproj'),
    (Join-Path $RepoRoot 'src\Xbox360.Fatx\Xbox360.Fatx.csproj')
)
Assert-ProjectVersions -ProjectPaths $projectPaths -ExpectedVersion $Version
$licensePath = Join-Path $RepoRoot 'LICENSE'
if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) {
    throw 'LICENSE is required for release packaging.'
}
Assert-NotReparseEntry -Path $licensePath -Label 'Project license'
Assert-ReleaseSourceInputs
Resolve-ReleasePolicy
Assert-ProvenanceStatus

$resolvedDotNetPath = Resolve-ToolPath -Candidate $DotNetPath -DisplayName '.NET SDK'
Assert-PromotionDotNetSdk -DotNetExecutable $resolvedDotNetPath
Pin-PromotionGitIdentity
$resolvedIsccPath = $null
if ($BuildInstaller) {
    $resolvedIsccPath = Resolve-ToolPath -Candidate $IsccPath -DisplayName 'Inno Setup compiler'
}
if (-not [string]::IsNullOrWhiteSpace($PackagesPath) -and -not (Test-Path -LiteralPath $PackagesPath -PathType Container)) {
    throw "NuGet package cache was not found: $PackagesPath"
}

$archiveRootName = "XeCLI-$Version-$Runtime"
$workRootName = '.work-' + [guid]::NewGuid().ToString('N')
$workRoot = Join-Path $OutputRoot $workRootName
$cliPublishRoot = Join-Path $workRoot 'cli'
$terminalPublishRoot = Join-Path $workRoot 'xeterminal'
$bodyProbeRoot = Join-Path $workRoot 'body-probe'
$bodyProbeUserDataRoot = Join-Path $bodyProbeRoot 'UserData'
$bodyProfileRoot = Join-Path $workRoot 'body-profile'
$bodyProfileRoamingRoot = Join-Path $bodyProfileRoot 'roaming'
$bodyProfileLocalRoot = Join-Path $bodyProfileRoot 'local'
$installerStageRoot = Join-Path $workRoot 'installer-stage'
$validationProfile = Join-Path $workRoot 'release-check-profile'
$validationRoamingRoot = Join-Path $validationProfile 'roaming'
$validationLocalRoot = Join-Path $validationProfile 'local'
$validationHomeRoot = Join-Path $validationProfile 'home'
$validationConfigRoot = Join-Path $validationRoamingRoot 'XeCLI'
$stageParent = Join-Path $OutputRoot 'stage'
$stageRoot = Join-Path $stageParent $archiveRootName
$zipPath = Join-Path $OutputRoot "$archiveRootName.zip"
$installerOutputRoot = $OutputRoot

if ($DryRun) {
    Write-Host "Release build dry run passed." -ForegroundColor Green
    Write-Host "Mode: $Mode"
    Write-Host "Release kind: $script:ReleaseKind"
    Write-Host "Promotion policy: $(if ($Mode -eq 'Promotion') { $script:ReleaseKind } else { 'not-applicable' })"
    Write-Host "Version: $Version"
    Write-Host "Runtime: $Runtime"
    Write-Host "Portable stage: $stageRoot"
    Write-Host "ZIP: $zipPath"
    Write-Host "Installer requested: $BuildInstaller"
    if ($Mode -eq 'Promotion') {
        Write-Host "Pinned .NET SDK: $script:PinnedDotNetSdkVersion"
    }
    if ($script:ReleaseKind -eq 'public-unsigned') {
        Write-Host $PublicUnsignedWarning -ForegroundColor Yellow
    }
    exit 0
}

Write-Step "Preparing $Mode release workspace"
$outputRootHandle = $null
$workRootHandle = $null
$workRootCreated = $false
$releaseFailure = $null
$pinnedAncestorDirectories = [System.Collections.ArrayList]::new()
$pinnedGeneratedDirectories = [System.Collections.ArrayList]::new()
$pinnedWorkDirectories = [System.Collections.ArrayList]::new()
$pinnedOutputDirectories = [System.Collections.ArrayList]::new()
try {
try {
$outputRootBinding = New-PinnedReleaseOutputRoot -Path $OutputRoot
$outputRootHandle = $outputRootBinding.Handle
$pinnedAncestorDirectories = $outputRootBinding.Ancestors
$workRootHandle = New-PinnedReleaseWorkRoot `
    -ParentHandle $outputRootHandle `
    -EntryName $workRootName
$workRootCreated = $true
$workspaceIdentityParameters = @{
    OutputRootHandle = $outputRootHandle
    OutputRootPath = $OutputRoot
    WorkRootHandle = $workRootHandle
    WorkRootPath = $workRoot
    AncestorDirectories = $pinnedAncestorDirectories
    GeneratedDirectories = $pinnedGeneratedDirectories
}
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
$stageParentDirectory = Add-PinnedGeneratedDirectory `
    -ParentHandle $outputRootHandle `
    -EntryName 'stage' `
    -Path $stageParent `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedOutputDirectories
$stageRootDirectory = Add-PinnedGeneratedDirectory `
    -ParentHandle $stageParentDirectory.Handle `
    -EntryName $archiveRootName `
    -Path $stageRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedOutputDirectories
[void](Add-PinnedGeneratedDirectory `
    -ParentHandle $workRootHandle `
    -EntryName 'cli' `
    -Path $cliPublishRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories)
[void](Add-PinnedGeneratedDirectory `
    -ParentHandle $workRootHandle `
    -EntryName 'xeterminal' `
    -Path $terminalPublishRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories)
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters

$oldPackagesPath = $env:NUGET_PACKAGES
try {
    if (-not [string]::IsNullOrWhiteSpace($PackagesPath)) {
        $env:NUGET_PACKAGES = [System.IO.Path]::GetFullPath($PackagesPath)
    }

    Write-Step 'Restoring locked dependencies'
    $restoreArguments = @(
        'restore',
        (Join-Path $RepoRoot 'XeCLI.sln'),
        '--locked-mode',
        '--runtime', $Runtime,
        '--force-evaluate'
    )
    if (-not [string]::IsNullOrWhiteSpace($PackagesPath)) {
        $restoreArguments += @('--packages', [System.IO.Path]::GetFullPath($PackagesPath))
    }
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
    Invoke-NativeCommand -FilePath $resolvedDotNetPath -Arguments $restoreArguments -FailureMessage 'Locked restore failed'
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters

    $publishProperties = @(
        "-p:Version=$Version",
        "-p:InformationalVersion=$Version",
        '-p:ContinuousIntegrationBuild=true',
        '-p:Deterministic=true',
        '-p:PublishSingleFile=false'
    )

    Write-Step 'Publishing CLI and GUI payload'
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
    $cliPublishArguments = @(
        'publish', $projectPaths[0], '-c', 'Release', '-r', $Runtime,
        '--self-contained', 'true', '--no-restore', '-o', $cliPublishRoot
    ) + $publishProperties
    Invoke-NativeCommand -FilePath $resolvedDotNetPath -Arguments $cliPublishArguments -FailureMessage 'CLI publish failed'
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters

    Write-Step 'Publishing XeTerminal launcher'
    $terminalPublishArguments = @(
        'publish', $projectPaths[1], '-c', 'Release', '-r', $Runtime,
        '--self-contained', 'true', '--no-restore', '-o', $terminalPublishRoot
    ) + $publishProperties
    Invoke-NativeCommand -FilePath $resolvedDotNetPath -Arguments $terminalPublishArguments -FailureMessage 'XeTerminal publish failed'
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
}
finally {
    $env:NUGET_PACKAGES = $oldPackagesPath
}

Write-Step 'Merging publish outputs and staging portable mode'
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
$portableMarkerValidated = $false
$portableBodyValidated = $false
Copy-MergedPublishTree -Source $cliPublishRoot -Destination $stageRoot
Copy-MergedPublishTree -Source $terminalPublishRoot -Destination $stageRoot
$portableMarkerPath = Join-Path $stageRoot 'xecli.portable'
[System.IO.File]::WriteAllBytes($portableMarkerPath, [byte[]]::new(0))
$portableMarkerValidated = Test-Path -LiteralPath $portableMarkerPath -PathType Leaf
if (-not $portableMarkerValidated) {
    throw "Portable marker validation failed: $portableMarkerPath"
}

$noticeSource = Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.md'
if (-not (Test-Path -LiteralPath $noticeSource -PathType Leaf)) {
    throw 'THIRD-PARTY-NOTICES.md is required for release packaging.'
}
Assert-NotReparseEntry -Path $noticeSource -Label 'Third-party notices'
Copy-Item -LiteralPath $noticeSource -Destination (Join-Path $stageRoot 'THIRD-PARTY-NOTICES.md') -Force

Copy-RequiredDirectory `
    -Source (Join-Path $RepoRoot 'THIRD-PARTY-LICENSES') `
    -Destination (Join-Path $stageRoot 'THIRD-PARTY-LICENSES') `
    -DisplayName 'Third-party license directory'
Copy-XeLLBinaryLicenseNotices -DestinationRoot (Join-Path $stageRoot 'THIRD-PARTY-LICENSES')
Assert-PackagedThirdPartyLicenseInventory -StageRoot $stageRoot

$correspondingSourceGitRevision = if ($Mode -eq 'Promotion') {
    $script:PinnedPromotionGitRevision
}
else {
    Get-GitValue -Arguments @('rev-parse', 'HEAD')
}
Write-CorrespondingSourceNotice `
    -DestinationPath (Join-Path $stageRoot $CorrespondingSourceReleasePath.Replace('/', '\')) `
    -GitRevision $correspondingSourceGitRevision
$firstPartyChecksumSource = Join-Path $RepoRoot 'XeCLI-XellFetch\SHA256SUMS.txt'
$firstPartyChecksumDestination = Join-Path $stageRoot $FirstPartyPayloadReleaseManifestPath.Replace('/', '\')
Copy-Item -LiteralPath $firstPartyChecksumSource -Destination $firstPartyChecksumDestination -Force
Assert-IntegrityFileRecord `
    -Path $firstPartyChecksumDestination `
    -RelativePath $FirstPartyPayloadReleaseManifestPath `
    -ExpectedSha256 (Get-FileHash -LiteralPath $firstPartyChecksumSource -Algorithm SHA256).Hash `
    -RecordLabel 'Staged first-party payload checksum manifest'
Assert-StagedFirstPartyPayloads -StageRoot $stageRoot
Assert-NoReparseEntries -Root $stageRoot -Label 'Portable stage'
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters

Assert-PublishedVersion -Path (Join-Path $stageRoot 'rgh.exe') -ExpectedVersion $Version
Assert-PublishedVersion -Path (Join-Path $stageRoot 'rgh.dll') -ExpectedVersion $Version
Assert-PublishedVersion -Path (Join-Path $stageRoot 'XeTerminal.exe') -ExpectedVersion $Version
Assert-PublishedVersion -Path (Join-Path $stageRoot 'XeTerminal.dll') -ExpectedVersion $Version

$signTargets = @(
    (Join-Path $stageRoot 'rgh.exe'),
    (Join-Path $stageRoot 'rgh.dll'),
    (Join-Path $stageRoot 'XeTerminal.exe'),
    (Join-Path $stageRoot 'XeTerminal.dll'),
    (Join-Path $stageRoot 'Xbox360.Remote.dll'),
    (Join-Path $stageRoot 'Xbox360.Fatx.dll')
)
if ($script:ReleaseKind -eq 'promotion-signed') {
    Write-Step 'Signing promotion binaries'
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
    Invoke-AuthenticodeSigning -Paths $signTargets
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
}
else {
    Assert-UnsignedAuthenticode -Paths $signTargets
}

Write-Step 'Probing published executable bodies'
$stageUserDataPath = Join-Path $stageRoot 'UserData'
if (Test-PathEntryExistsNoFollow -Path $stageUserDataPath) {
    throw 'The portable stage must not contain a pre-existing UserData entry.'
}
$bodyProbeDirectory = Add-PinnedGeneratedDirectory `
    -ParentHandle $workRootHandle `
    -EntryName 'body-probe' `
    -Path $bodyProbeRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories
[void](Add-PinnedGeneratedDirectory `
    -ParentHandle $bodyProbeDirectory.Handle `
    -EntryName 'UserData' `
    -Path $bodyProbeUserDataRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories)
$bodyProfileDirectory = Add-PinnedGeneratedDirectory `
    -ParentHandle $workRootHandle `
    -EntryName 'body-profile' `
    -Path $bodyProfileRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories
[void](Add-PinnedGeneratedDirectory `
    -ParentHandle $bodyProfileDirectory.Handle `
    -EntryName 'roaming' `
    -Path $bodyProfileRoamingRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories)
[void](Add-PinnedGeneratedDirectory `
    -ParentHandle $bodyProfileDirectory.Handle `
    -EntryName 'local' `
    -Path $bodyProfileLocalRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories)
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
$portableBodyValidated = Invoke-PortableBodyProbes `
    -StageRoot $stageRoot `
    -ProbeRoot $bodyProbeRoot `
    -ProfileRoot $bodyProfileRoot
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
if (-not $portableBodyValidated) {
    throw 'Portable body validation did not report success.'
}

Write-Step 'Writing deterministic package ownership inventory'
$portableOwnedPaths = @(Write-InstallerOwnedFilesManifest -StageRoot $stageRoot)

Assert-PromotionGitIdentity
$gitRevision = if ($Mode -eq 'Promotion') {
    $script:PinnedPromotionGitRevision
}
else {
    Get-GitValue -Arguments @('rev-parse', 'HEAD')
}
if (-not $gitRevision.Equals($correspondingSourceGitRevision, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Corresponding-source notice git revision changed during release packaging.'
}
$sourceTimestampText = Get-GitValue -Arguments @('show', '-s', '--format=%cI', $gitRevision)
$sourceTimestamp = [DateTimeOffset]::Parse($sourceTimestampText, [System.Globalization.CultureInfo]::InvariantCulture)
$sourceTimestamp = $sourceTimestamp.ToUniversalTime()
$builtUtc = $sourceTimestamp.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)

$installerPath = ''
$installerHashPath = ''
$installerFileInventory = @()
$installerStatus = if (-not $BuildInstaller) {
    'not-requested'
}
elseif ($script:ReleaseKind -eq 'promotion-signed') {
    'built-signed'
}
else {
    'built-unsigned'
}

Write-Step 'Writing release manifest body for portable and installer payloads'
$fileInventory = @(Write-ReleaseManifest `
    -StageRoot $stageRoot `
    -GitRevision $gitRevision `
    -BuiltUtc $builtUtc `
    -InstallerStatus $installerStatus `
    -PortableMarkerValidated $portableMarkerValidated `
    -PortableBodyValidated $portableBodyValidated)

if ($BuildInstaller) {
    # Keep integrity metadata in an installer-only stage to avoid a checksum cycle with release-manifest.json.
    Write-Step 'Preparing deterministic installer integrity payload'
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
    [void](Add-PinnedGeneratedDirectory `
        -ParentHandle $workRootHandle `
        -EntryName 'installer-stage' `
        -Path $installerStageRoot `
        -AllDirectories $pinnedGeneratedDirectories `
        -LifetimeDirectories $pinnedWorkDirectories)
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
    Copy-MergedPublishTree -Source $stageRoot -Destination $installerStageRoot
    $installerOwnedPaths = @(Write-InstallerOwnedFilesManifest `
        -StageRoot $installerStageRoot `
        -IncludeOwnedHashesManifest)
    [void](Write-ReleaseManifest `
        -StageRoot $installerStageRoot `
        -GitRevision $gitRevision `
        -BuiltUtc $builtUtc `
        -InstallerStatus $installerStatus `
        -PortableMarkerValidated $portableMarkerValidated `
        -PortableBodyValidated $portableBodyValidated)
    $installerOwnedHashRecords = @(Write-InstallerOwnedHashesManifest `
        -StageRoot $installerStageRoot `
        -OwnedPaths $installerOwnedPaths)
    Assert-NoReparseEntries -Root $installerStageRoot -Label 'Installer stage'

    Write-Step 'Building Inno Setup installer'
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
    Invoke-InnoBuild -CompilerPath $resolvedIsccPath -StageRoot $installerStageRoot -InstallerOutputRoot $installerOutputRoot
    Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
    Assert-InstallerOwnedStagePaths `
        -StageRoot $installerStageRoot `
        -ExpectedPaths $installerOwnedPaths `
        -IncludeOwnedHashesManifest
    Assert-InstallerOwnedFilesReleaseManifest `
        -StageRoot $installerStageRoot `
        -ExpectedPaths $installerOwnedPaths
    Assert-InstallerOwnedHashesManifest `
        -StageRoot $installerStageRoot `
        -OwnedPaths $installerOwnedPaths `
        -ExpectedRecords $installerOwnedHashRecords
    $installerFileInventory = @(
        Get-ReleaseFileInventory -StageRoot $installerStageRoot |
            Where-Object { Test-IsInstallerOwnedStagePath -RelativePath ([string]$_.Path) }
    )
    if ($installerFileInventory.Count -ne $installerOwnedPaths.Count) {
        throw 'Installer metadata inventory does not match the exact installer-owned stage.'
    }
    $installerInventoryPaths = @($installerFileInventory | ForEach-Object { [string]$_.Path })
    foreach ($ownedPath in $installerOwnedPaths) {
        if ($installerInventoryPaths -cnotcontains $ownedPath) {
            throw "Installer metadata inventory is missing installer-owned path: $ownedPath"
        }
    }
    $installerPath = Join-Path $installerOutputRoot "XeCLI-$Version-$Runtime-setup.exe"
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Expected installer output was not found: $installerPath"
    }
    if ($script:ReleaseKind -eq 'promotion-signed') {
        $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Installer signature verification failed: $($signature.StatusMessage)"
        }
    }
    else {
        Assert-UnsignedAuthenticode -Paths @($installerPath)
    }
    $installerHashPath = Write-Sha256Sidecar -FilePath $installerPath
}

Write-Step 'Finalizing and validating release manifest body'
Assert-InstallerOwnedFilesReleaseManifest -StageRoot $stageRoot -ExpectedPaths $portableOwnedPaths
Set-DeterministicTimestamps -Root $stageRoot -Timestamp $sourceTimestamp
$validatorPath = Join-Path $cliPublishRoot 'rgh.exe'
$validationProfileDirectory = Add-PinnedGeneratedDirectory `
    -ParentHandle $workRootHandle `
    -EntryName 'release-check-profile' `
    -Path $validationProfile `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories
$validationRoamingDirectory = Add-PinnedGeneratedDirectory `
    -ParentHandle $validationProfileDirectory.Handle `
    -EntryName 'roaming' `
    -Path $validationRoamingRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories
[void](Add-PinnedGeneratedDirectory `
    -ParentHandle $validationProfileDirectory.Handle `
    -EntryName 'local' `
    -Path $validationLocalRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories)
[void](Add-PinnedGeneratedDirectory `
    -ParentHandle $validationProfileDirectory.Handle `
    -EntryName 'home' `
    -Path $validationHomeRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories)
[void](Add-PinnedGeneratedDirectory `
    -ParentHandle $validationRoamingDirectory.Handle `
    -EntryName 'XeCLI' `
    -Path $validationConfigRoot `
    -AllDirectories $pinnedGeneratedDirectories `
    -LifetimeDirectories $pinnedWorkDirectories)
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
Invoke-ReleaseCheck -ValidatorPath $validatorPath -StageRoot $stageRoot -ProjectPath $projectPaths[0] -ProfileRoot $validationProfile -StageOnly
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters

Write-Step 'Creating deterministic portable ZIP and checksum'
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
Assert-NoReparseEntries -Root $stageRoot -Label 'Final portable stage'
Assert-GeneratedPath -Path $zipPath -Parent $OutputRoot
New-DeterministicZip -StageRoot $stageRoot -ZipPath $zipPath -ArchiveRootName $archiveRootName -Timestamp $sourceTimestamp
Assert-InstallerOwnedFilesZipEntry -StageRoot $stageRoot -ZipPath $zipPath -ArchiveRootName $archiveRootName
$hashPath = Write-Sha256Sidecar -FilePath $zipPath
Assert-PromotionGitIdentity
$summaryPath = Write-ReleaseMetadataDocuments `
    -OutputRoot $OutputRoot `
    -PortableFiles $fileInventory `
    -InstallerFiles $installerFileInventory `
    -GitRevision $gitRevision `
    -BuiltUtc $builtUtc `
    -InstallerStatus $installerStatus `
    -PortableMarkerValidated $portableMarkerValidated `
    -PortableBodyValidated $portableBodyValidated `
    -ZipPath $zipPath `
    -ZipHashPath $hashPath `
    -InstallerPath $installerPath `
    -InstallerHashPath $installerHashPath
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
Invoke-ReleaseCheck `
    -ValidatorPath $validatorPath `
    -StageRoot $stageRoot `
    -ProjectPath $projectPaths[0] `
    -ZipPath $zipPath `
    -HashPath $hashPath `
    -SummaryPath $summaryPath `
    -ProfileRoot $validationProfile
Assert-PinnedReleaseWorkspace @workspaceIdentityParameters
Assert-PromotionGitIdentity
Assert-PromotionReleaseArtifacts `
    -OutputRoot $OutputRoot `
    -ZipPath $zipPath `
    -ZipHashPath $hashPath `
    -InstallerPath $installerPath `
    -InstallerHashPath $installerHashPath `
    -SummaryPath $summaryPath `
    -GitRevision $gitRevision `
    -InstallerStatus $installerStatus `
    -PortableFiles $fileInventory `
    -InstallerFiles $installerFileInventory
}
catch {
    $releaseFailure = $_.Exception
    throw
}
finally {
    try {
        Close-PinnedGeneratedDirectories -Directories $pinnedWorkDirectories
        if ($workRootCreated) {
            try {
                Remove-PinnedGeneratedTree -Handle $workRootHandle
            }
            finally {
                $workRootHandle.Dispose()
                $workRootHandle = $null
            }
            Assert-PinnedGeneratedTreeRemoved `
                -ParentHandle $outputRootHandle `
                -EntryName $workRootName
        }
    }
    catch {
        if ($null -ne $releaseFailure) {
            $failures = [System.Exception[]]@($releaseFailure, $_.Exception)
            throw ([System.AggregateException]::new(
                'Release packaging and handle-bound cleanup both failed.',
                $failures))
        }
        throw
    }
}
[XeCLI.Release.HandleBoundTreeDeleter]::AssertPathMatchesHandle($outputRootHandle, $OutputRoot)
Assert-PinnedGeneratedDirectories -Directories $pinnedOutputDirectories
Assert-ExactPromotionPublicationRoot -OutputRoot $OutputRoot
Assert-PromotionGitIdentity

Write-Host "Release package completed: $zipPath" -ForegroundColor Green
if ($Mode -eq 'Local') {
    Write-Host 'Artifact status: local unsigned build (not eligible for promotion).' -ForegroundColor Yellow
}
elseif ($script:ReleaseKind -eq 'public-unsigned') {
    Write-Host 'Artifact status: explicit public unsigned promotion.' -ForegroundColor Yellow
    Write-Host $PublicUnsignedWarning -ForegroundColor Yellow
}
}
finally {
    try {
        Close-PinnedGeneratedDirectories -Directories $pinnedWorkDirectories
    }
    finally {
        try {
            Close-PinnedGeneratedDirectories -Directories $pinnedOutputDirectories
        }
        finally {
            try {
                if ($null -ne $workRootHandle) {
                    $workRootHandle.Dispose()
                }
            }
            finally {
                try {
                    if ($null -ne $outputRootHandle) {
                        $outputRootHandle.Dispose()
                    }
                }
                finally {
                    Close-PinnedGeneratedDirectories -Directories $pinnedAncestorDirectories
                }
            }
        }
    }
}
