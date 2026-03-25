using System;
using System.Collections.Generic;
using System.IO;
using NoDev.Common.IO;

namespace NoDev.Xbox360
{
    internal class XmiException : Exception
    {
        public XmiException(string message)
            : base("XMI: " + message)
        {

        }
    }

    public enum XMIOBJECTTYPE
    {
        XMIOBJECTTYPE_INVALID = 0x0,
        XMIOBJECTTYPE_FREE = 0x1,
        XMIOBJECTTYPE_MUSIC_TRACK = 0x2,
        XMIOBJECTTYPE_MUSIC_ALBUM = 0x3,
        XMIOBJECTTYPE_MUSIC_ARTIST = 0x4,
        XMIOBJECTTYPE_MUSIC_GENRE = 0x5,
        XMIOBJECTTYPE_MUSIC_PLAYLIST = 0x6,
        XMIOBJECTTYPE_HEADER = 0x7,
        XMIOBJECTTYPE_MASTERLIST = 0x8,
        XMIOBJECTTYPE_MUSIC_PLAYLIST_ENTRY = 0x9,
        XMIOBJECTTYPE_COUNT = 0xA
    }

    public struct XMIENTRY_HEADER
    {
        public static uint FieldSize = 24;

        public XMIOBJECTTYPE nObjectType;
        public uint dwSignature;
        public uint dwVersion;
        public XMILISTHEAD MasterListList;

        public XMIENTRY_HEADER(EndianIO reader)
        {
            this.nObjectType = (XMIOBJECTTYPE)reader.ReadUInt32();

            this.dwSignature = reader.ReadUInt32();

            if (this.dwSignature != 0x20494D58)
            {
                throw new XmiException("Invalid signature.");
            }

            this.dwVersion = reader.ReadUInt32();

            if (this.dwVersion != 0x02)
            {
                throw new XmiException("Invalid version.");
            }

            this.MasterListList = new XMILISTHEAD(reader);
        }
        public byte[] ToArray()
        {
            MemoryStream MS = new MemoryStream();
            EndianIO writer = new EndianIO(MS, EndianType.Big);

            writer.Write((uint)this.nObjectType);
            writer.Write(this.dwSignature);
            writer.Write(this.dwVersion);
            writer.Write(this.MasterListList.ToArray());

            writer.Close();

            return MS.ToArray();
        }
    }
    public struct XMILISTHEAD
    {
        public uint nPrevEntry;
        public uint nNextEntry;
        public uint nEntryCount;

        public XMILISTHEAD(EndianIO reader)
        {
            this.nPrevEntry = reader.ReadUInt32();
            this.nNextEntry = reader.ReadUInt32();
            this.nEntryCount = reader.ReadUInt32();
        }
        public byte[] ToArray()
        {
            MemoryStream ms = new MemoryStream();
            EndianIO writer = new EndianIO(ms, EndianType.Big);

            writer.Write(this.nPrevEntry);
            writer.Write(this.nNextEntry);
            writer.Write(this.nEntryCount);

            writer.Close();

            return ms.ToArray();
        }
    }
    public struct XMILISTENTRY
    {
        public uint nPrevEntry;
        public uint nNextEntry;
        public uint nParentEntry;

        public XMILISTENTRY(EndianIO reader)
        {
            this.nPrevEntry = reader.ReadUInt32();
            this.nNextEntry = reader.ReadUInt32();
            this.nParentEntry = reader.ReadUInt32();
        }
        public byte[] ToArray()
        {
            MemoryStream ms = new MemoryStream();
            EndianIO writer = new EndianIO(ms, EndianType.Big);

            writer.Write(this.nPrevEntry);
            writer.Write(this.nNextEntry);
            writer.Write(this.nParentEntry);

            writer.Close();

            return ms.ToArray();
        }
    }
    public struct XMIENTRY_MASTERLIST
    {
        public static uint FieldSize = 32;

        public XMIOBJECTTYPE nObjectType;
        public XMILISTENTRY MasterListEntry;
        public XMILISTHEAD EntryList;
        public XMIOBJECTTYPE nChildObjectType;

        public XMIENTRY_MASTERLIST(EndianIO reader)
        {
            this.nObjectType = (XMIOBJECTTYPE)reader.ReadUInt32();
            this.MasterListEntry = new XMILISTENTRY(reader);
            this.EntryList = new XMILISTHEAD(reader);
            this.nChildObjectType = (XMIOBJECTTYPE)reader.ReadUInt32();
        }
        public byte[] ToArray()
        {
            MemoryStream ms = new MemoryStream();
            EndianIO writer = new EndianIO(ms, EndianType.Big);

            writer.Write((uint)this.nObjectType);
            writer.Write(this.MasterListEntry.ToArray());
            writer.Write(this.EntryList.ToArray());
            writer.Write((uint)nChildObjectType);

            writer.Close();

            return ms.ToArray();
        }

    }
    public struct XMIENTRY_MUSIC_TRACK
    {
        public static uint FieldSize = 148;

        public XMIOBJECTTYPE nObjectType;
        public XMILISTENTRY MasterListEntry;
        public string szName;
        public XMILISTENTRY Album;
        public XMILISTENTRY Artist;
        public XMILISTENTRY Genre;
        public XMILISTHEAD PlaylistEntryList;
        public uint dwDuration
        {
            get { return (dwDurationAndTrackNumber >> 9) & 0x007FFFFF; }
            set { dwDurationAndTrackNumber = (dwDurationAndTrackNumber & 0x1FF) | (value & 0x007FFFFF) << 9; }
        }
        public uint dwTrackNumber
        {
            get { return (dwDurationAndTrackNumber >> 2) & 0x7F; }
            set { dwDurationAndTrackNumber = (dwDurationAndTrackNumber & 0xFFFFFF03) | (value & 0x7F) << 2; }
        }
        public uint dwFormat
        {
            get { return dwDurationAndTrackNumber & 0x03; }
            set { dwDurationAndTrackNumber = (dwDurationAndTrackNumber & 0xFFFFFFFC) | (value & 0x03); }
        }
        public uint dwDurationAndTrackNumber;
        public uint EntryIndex;

        public XMIENTRY_MUSIC_TRACK(EndianIO reader)
        {
            this.nObjectType = (XMIOBJECTTYPE)reader.ReadUInt32();
            this.MasterListEntry = new XMILISTENTRY(reader);
            this.szName = reader.ReadUnicodeString(0x28).RemoveNullBytes();
            this.Album = new XMILISTENTRY(reader);
            this.Artist = new XMILISTENTRY(reader);
            this.Genre = new XMILISTENTRY(reader);
            this.PlaylistEntryList = new XMILISTHEAD(reader);
            this.dwDurationAndTrackNumber = reader.ReadUInt32();
            this.EntryIndex = 0xffffffff;
        }
    }
    public struct XMIENTRY_MUSIC_ALBUM
    {
        public static uint FieldSize = 376;

        public XMIOBJECTTYPE nObjectType;
        public XMILISTENTRY MasterListEntry;
        public string szName;
        public XMILISTHEAD TrackList;
        public XMILISTENTRY Artist;
        public XMILISTENTRY Genre;
        public uint dwReleaseYear;
        public byte[] AlbumId;
        public uint EntryIndex;

        public XMIENTRY_MUSIC_ALBUM(EndianIO reader)
        {
            this.nObjectType = (XMIOBJECTTYPE)reader.ReadUInt32();
            this.MasterListEntry = new XMILISTENTRY(reader);
            this.szName = reader.ReadUnicodeString(0x28).RemoveNullBytes();
            this.TrackList = new XMILISTHEAD(reader);
            this.Artist = new XMILISTENTRY(reader);
            this.Genre = new XMILISTENTRY(reader);
            this.dwReleaseYear = reader.ReadUInt32();
            this.AlbumId = reader.ReadByteArray(20);
            this.EntryIndex = 0xffffffff;
        }
    }
    public struct XMIENTRY_MUSIC_ARTIST
    {
        public XMIOBJECTTYPE nObjectType;
        public XMILISTENTRY MasterListEntry;
        public string szName;
        public XMILISTHEAD TrackList;
        public XMILISTHEAD AlbumList;
        public uint EntryIndex;

        public XMIENTRY_MUSIC_ARTIST(EndianIO reader)
        {
            this.nObjectType = (XMIOBJECTTYPE)reader.ReadUInt32();
            this.MasterListEntry = new XMILISTENTRY(reader);
            this.szName = reader.ReadUnicodeString(0x28).RemoveNullBytes();
            this.TrackList = new XMILISTHEAD(reader);
            this.AlbumList = new XMILISTHEAD(reader);
            this.EntryIndex = 0xffffffff;
        }
    }
    public struct XMIENTRY_MUSIC_GENRE
    {
        public XMIOBJECTTYPE nObjectType;
        public XMILISTENTRY MasterListEntry;
        public string szName;
        public XMILISTHEAD TrackList;
        public XMILISTHEAD AlbumList;
        public uint EntryIndex;

        public XMIENTRY_MUSIC_GENRE(EndianIO reader)
        {
            this.nObjectType = (XMIOBJECTTYPE)reader.ReadUInt32();
            this.MasterListEntry = new XMILISTENTRY(reader);
            this.szName = reader.ReadUnicodeString(0x28).RemoveNullBytes();
            this.TrackList = new XMILISTHEAD(reader);
            this.AlbumList = new XMILISTHEAD(reader);
            this.EntryIndex = 0xffffffff;
        }
    }
    public struct XMIENTRY_MUSIC_PLAYLIST
    {
        public XMIOBJECTTYPE nObjectType;
        public XMILISTENTRY MasterListEntry;
        public string szName;
        public XMILISTHEAD PlaylistEntryList;
        public uint EntryIndex;

        public XMIENTRY_MUSIC_PLAYLIST(EndianIO reader)
        {
            this.nObjectType = (XMIOBJECTTYPE)reader.ReadUInt32();
            this.MasterListEntry = new XMILISTENTRY(reader);
            this.szName = reader.ReadUnicodeString(0x28).RemoveNullBytes();
            this.PlaylistEntryList = new XMILISTHEAD(reader);
            this.EntryIndex = 0xffffffff;
        }
    }

    public class MediaIndex
    {
        private static uint XMIENTRYSIZE = 600;
        private readonly EndianIO _io;

        public XMIENTRY_HEADER Header;
        public XMIENTRY_MASTERLIST[] MasterList;

        public List<XMIENTRY_MUSIC_ALBUM> Albums;
        public List<XMIENTRY_MUSIC_TRACK> Tracks;
        public List<XMIENTRY_MUSIC_ARTIST> Artists;
        public List<XMIENTRY_MUSIC_GENRE> Genres;
        public List<XMIENTRY_MUSIC_PLAYLIST> Playlists;

        public MediaIndex(string fileName)
            : this(new EndianIO(fileName, EndianType.Big))
        {

        }
        public MediaIndex(EndianIO io)
        {
            this._io = io;

            this.Validate();
            this.CreateMasterListEntries();
            this.ReadEntries();
        }

        private void Validate()
        {
            this.Header = new XMIENTRY_HEADER(this._io);
            this.MasterList = new XMIENTRY_MASTERLIST[this.Header.MasterListList.nEntryCount];
        }

        private void CreateMasterListEntries()
        {
            for (var x = 0; x < this.Header.MasterListList.nEntryCount; x++)
            {
                this._io.Position = (x + 1) * XMIENTRYSIZE;
                this.MasterList[x] = new XMIENTRY_MASTERLIST(this._io);
            }
        }

        private void ReadEntries()
        {
            for (var x = 0; x < this.Header.MasterListList.nEntryCount; x++)
            {
                XMIENTRY_MASTERLIST entry = this.MasterList[x];
                uint nextEntry = entry.EntryList.nNextEntry;

                switch (entry.nChildObjectType)
                {
                    case XMIOBJECTTYPE.XMIOBJECTTYPE_MUSIC_TRACK:

                        this.Tracks = new List<XMIENTRY_MUSIC_TRACK>();
                        for (var i = 0; i < entry.EntryList.nEntryCount; i++)
                        {
                            this._io.Position = nextEntry * XMIENTRYSIZE;

                            XMIENTRY_MUSIC_TRACK track = new XMIENTRY_MUSIC_TRACK(this._io);
                            track.EntryIndex = nextEntry;
                            nextEntry = track.MasterListEntry.nNextEntry;

                            this.Tracks.Add(track);
                        }
                        break;
                    case XMIOBJECTTYPE.XMIOBJECTTYPE_MUSIC_ALBUM:

                        this.Albums = new List<XMIENTRY_MUSIC_ALBUM>();

                        for (var i = 0; i < entry.EntryList.nEntryCount; i++)
                        {
                            this._io.Position = nextEntry * XMIENTRYSIZE;

                            XMIENTRY_MUSIC_ALBUM album = new XMIENTRY_MUSIC_ALBUM(this._io);
                            album.EntryIndex = nextEntry;
                            nextEntry = album.MasterListEntry.nNextEntry;

                            this.Albums.Add(album);
                        }
                        break;
                    case XMIOBJECTTYPE.XMIOBJECTTYPE_MUSIC_ARTIST:

                        this.Artists = new List<XMIENTRY_MUSIC_ARTIST>();
                        for (var i = 0; i < entry.EntryList.nEntryCount; i++)
                        {
                            this._io.Position = nextEntry * XMIENTRYSIZE;

                            XMIENTRY_MUSIC_ARTIST artist = new XMIENTRY_MUSIC_ARTIST(this._io);
                            artist.EntryIndex = nextEntry;
                            nextEntry = artist.MasterListEntry.nNextEntry;

                            this.Artists.Add(artist);
                        }
                        break;
                    case XMIOBJECTTYPE.XMIOBJECTTYPE_MUSIC_GENRE:

                        this.Genres = new List<XMIENTRY_MUSIC_GENRE>();
                        for (var i = 0; i < entry.EntryList.nEntryCount; i++)
                        {
                            this._io.Position = nextEntry * XMIENTRYSIZE;

                            XMIENTRY_MUSIC_GENRE genre = new XMIENTRY_MUSIC_GENRE(this._io);
                            genre.EntryIndex = nextEntry;
                            nextEntry = genre.MasterListEntry.nNextEntry;

                            this.Genres.Add(genre);
                        }
                        break;
                    case XMIOBJECTTYPE.XMIOBJECTTYPE_MUSIC_PLAYLIST:

                        this.Playlists = new List<XMIENTRY_MUSIC_PLAYLIST>();
                        for (var i = 0; i < entry.EntryList.nEntryCount; i++)
                        {
                            this._io.Position = nextEntry * XMIENTRYSIZE;

                            XMIENTRY_MUSIC_PLAYLIST playlist = new XMIENTRY_MUSIC_PLAYLIST(this._io);
                            playlist.EntryIndex = nextEntry;
                            nextEntry = playlist.MasterListEntry.nNextEntry;

                            this.Playlists.Add(playlist);
                        }
                        break;
                }
            }
        }

        private string GetMediaPath(uint entryIndex)
        {
            return string.Format("media\\{0:X4}\\{1:X4}", entryIndex >> 12, entryIndex & 0xFFF);
        }
    }

    public struct XMIMEDIAFILEAUDIO
    {
        public uint dwSignature;
        public uint dwVersion;
        public uint dwMediaType;
        public string szTrackName;
        public string szAlbumName;
        public string szAlbumArtistName;
        public string szTrackArtistName;
        public string szAlbumGenreName;
        public string szTrackGenreName;
        public uint dwDuration;
        public uint dwTrackNumber;
        public uint dwAlbumReleaseYear;
        public byte[] AlbumId;

        public EndianIO IO;

        public XMIMEDIAFILEAUDIO(EndianIO io)
        {
            this.IO = io;
            EndianIO reader = io;

            this.dwSignature = reader.ReadUInt32();
            this.dwVersion = reader.ReadUInt32();
            this.dwMediaType = reader.ReadUInt32();
            this.szTrackName = reader.ReadUnicodeString(0x100).RemoveNullBytes();
            this.szAlbumName = reader.ReadUnicodeString(0x100).RemoveNullBytes();
            this.szAlbumArtistName = reader.ReadUnicodeString(0x100).RemoveNullBytes();
            this.szTrackArtistName = reader.ReadUnicodeString(0x100).RemoveNullBytes();
            this.szAlbumGenreName = reader.ReadUnicodeString(0x100).RemoveNullBytes();
            this.szTrackGenreName = reader.ReadUnicodeString(0x100).RemoveNullBytes();
            this.dwDuration = reader.ReadUInt32();
            this.dwTrackNumber = reader.ReadUInt32();
            this.dwAlbumReleaseYear = reader.ReadUInt32();
            this.AlbumId = reader.ReadByteArray(240);
        }
        public byte[] ReadAudio()
        {
            this.IO.Position = 0xD08;
            return this.IO.ReadByteArray((int)this.IO.Length - 0xD08);
        }
    }
}
