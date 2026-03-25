using System;
using System.Collections.Generic;
using System.IO;

namespace NoDev.Common.IO
{
    public sealed class MultiFileIO : EndianIO
    {
        private readonly Stream[] _io;
        private readonly long[] _sizeUpTill;
        private int _currentFileIndex;
        private Stream _currentStream;

        public MultiFileIO(List<string> filePaths, EndianType endianType)
            : base(endianType)
        {
            _io = new Stream[filePaths.Count];
            _sizeUpTill = new long[filePaths.Count + 1];
            for (int x = 0; x < filePaths.Count; x++)
            {
                _io[x] = new FileStream(
                    NativeMethods.CreateFile(filePaths[x], FileAccess.ReadWrite, FileShare.None,
                    IntPtr.Zero, FileMode.Open, 0xC0000040, IntPtr.Zero), FileAccess.ReadWrite, 4096, true);
                _sizeUpTill[x + 1] = _sizeUpTill[x] + _io[x].Length;
            }
            this.OpenFile(0);
        }

        public MultiFileIO(List<Stream> streams, EndianType endianType)
            : base(streams[0], endianType)
        {
            _io = streams.ToArray();
            _sizeUpTill = new long[streams.Count + 1];
            for (int x = 0; x < streams.Count; x++)
                _sizeUpTill[x + 1] = _sizeUpTill[x] + _io[x].Length;
            this.OpenFile(0);
        }

        private void OpenFile(int fileIndex)
        {
            if (_io == null)
                return;

            _currentFileIndex = fileIndex;
            _currentStream = _io[_currentFileIndex];
            _currentStream.Position = 0;

            this.Stream = _currentStream;
        }

        public override void Flush()
        {
            foreach (Stream s in this._io)
                s.Flush();
        }

        public override long Position
        {
            get
            {
                return _sizeUpTill[_currentFileIndex] + _currentStream.Position;
            }
            set
            {
                int newIndex = GetFileIndexFromPosition(value);
                if (newIndex != _currentFileIndex)
                {
                    OpenFile(newIndex);
                    _currentFileIndex = newIndex;
                }
                _currentStream.Position = value - _sizeUpTill[_currentFileIndex];
            }
        }

        private int GetFileIndexFromPosition(long position)
        {
            for (int x = 0; x < _io.Length; x++)
                if (_sizeUpTill[x + 1] > position)
                    return x;
            throw new IOException("Cannot seek passed the end of the stream.");
        }

        public override long Length
        {
            get
            {
                return _sizeUpTill[_sizeUpTill.Length - 1];
            }
        }

        public override void Seek(long seekLength, SeekOrigin orgin)
        {
            switch (orgin)
            {
                case SeekOrigin.Begin:
                    Position = seekLength;
                    break;
                case SeekOrigin.Current:
                    Position += seekLength;
                    break;
                case SeekOrigin.End:
                    Position = Length - seekLength;
                    break;
            }
        }

        private int BytesLeftInCurrentFile
        {
            get
            {
                return (int)(_currentStream.Length - _currentStream.Position);
            }
        }

        public override int Read(byte[] buffer, int index, int count)
        {
            int bytesToRead = count;
            while (bytesToRead != 0)
            {
                if (_currentStream.Length == _currentStream.Position)
                    OpenFile(_currentFileIndex + 1);
                int readBytes = bytesToRead > BytesLeftInCurrentFile
                    ? BytesLeftInCurrentFile : bytesToRead;
                _currentStream.Read(buffer, index, readBytes);
                bytesToRead -= readBytes;
                index += readBytes;
            }
            return count;
        }

        public override void Write(byte[] buffer, int index, int count)
        {
            while (count != 0)
            {
                if (_currentStream.Length == _currentStream.Position)
                    OpenFile(_currentFileIndex + 1);
                int writeBytes = count > BytesLeftInCurrentFile
                    ? BytesLeftInCurrentFile : count;
                _currentStream.Write(buffer, index, writeBytes);
                count -= writeBytes;
                index += writeBytes;
            }
        }
    }
}
