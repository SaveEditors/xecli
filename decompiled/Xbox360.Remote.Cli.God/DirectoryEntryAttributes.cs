using System;

namespace Xbox360.Remote.Cli.God;

[Flags]
internal enum DirectoryEntryAttributes : byte
{
	ReadOnly = 1,
	Hidden = 2,
	System = 4,
	Directory = 0x10,
	Archive = 0x20,
	Normal = 0x80
}
