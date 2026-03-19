using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Xbox360.Remote;

public sealed class XbdmConnectionOptions {
    public required string Host { get; init; }
    public int Port { get; init; } = 730;
    public int TimeoutMs { get; init; } = 5000;
}

public sealed class XbdmClient : IAsyncDisposable, IDisposable {
    private static readonly Encoding Ascii = Encoding.ASCII;

    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly XbdmLineReader reader;
    private readonly SemaphoreSlim ioLock = new SemaphoreSlim(1, 1);

    private XbdmClient(TcpClient client, NetworkStream stream, XbdmLineReader reader) {
        this.client = client;
        this.stream = stream;
        this.reader = reader;
    }

    public static async Task<XbdmClient> ConnectAsync(XbdmConnectionOptions options, CancellationToken cancellationToken) {
        TcpClient client = new TcpClient();
        client.ReceiveTimeout = options.TimeoutMs;
        client.SendTimeout = options.TimeoutMs;
        using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(options.TimeoutMs);
        Task connectTask = client.ConnectAsync(options.Host, options.Port, connectCts.Token).AsTask();
        try {
            await connectTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            client.Dispose();
            throw new TimeoutException($"XBDM connect timed out after {options.TimeoutMs} ms.");
        }

        NetworkStream stream = client.GetStream();
        XbdmLineReader reader = new XbdmLineReader(stream, 8192, options.TimeoutMs);
        string line = await reader.ReadLineAsync(cancellationToken);
        XbdmResponse response = XbdmResponseParser.Parse(line);

        if (response.ResponseType != XbdmResponseType.Connected && response.StatusCode != 201) {
            throw new IOException($"Unexpected XBDM handshake: {response.RawMessage}");
        }

        return new XbdmClient(client, stream, reader);
    }

    public async ValueTask DisposeAsync() {
        try {
            if (client.Connected) {
                await SendCommandAsync("bye", CancellationToken.None);
            }
        }
        catch {
            // ignored
        }
        finally {
            client.Dispose();
            ioLock.Dispose();
        }
    }

    public void Dispose() {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async Task<XbdmResponse> SendCommandAsync(string command, CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync(command, cancellationToken);
            return await ReadResponseAsync(cancellationToken);
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> SendCommandForLinesAsync(string command, CancellationToken cancellationToken) {
        (XbdmResponse response, IReadOnlyList<string>? lines) = await SendCommandMaybeLinesAsync(command, cancellationToken);
        if (lines == null)
            throw new IOException($"Expected multi-response, got: {response.RawMessage}");
        return lines;
    }

    public async Task<(XbdmResponse Response, IReadOnlyList<string>? Lines)> SendCommandMaybeLinesAsync(string command, CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync(command, cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType == XbdmResponseType.MultiResponse || response.StatusCode == (int) XbdmResponseType.MultiResponse) {
                IReadOnlyList<string> lines = await ReadMultiLinesAsync(cancellationToken);
                return (response, lines);
            }

            return (response, null);
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task<XbdmConsoleInfo> GetConsoleInfoAsync(CancellationToken cancellationToken) {
        string consoleIdRaw = (await SendCommandAsync("getconsoleid", cancellationToken)).Message;
        string consoleId = consoleIdRaw;
        if (XbdmParamUtils.TryGetString(consoleIdRaw, "consoleid", out string? parsedId)) {
            consoleId = parsedId;
        }
        string debugName = (await SendCommandAsync("dbgname", cancellationToken)).Message;
        string execState = (await SendCommandAsync("getexecstate", cancellationToken)).Message;
        XbdmResponse altAddr = await SendCommandAsync("altaddr", cancellationToken);
        XbdmResponse pid = await SendCommandAsync("getpid", cancellationToken);

        string? ip = null;
        if (XbdmParamUtils.TryGetUInt32(altAddr.Message, "addr", out uint addr)) {
            IPAddress addrIp = new IPAddress(BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(addr) : addr);
            ip = addrIp.ToString();
        }

        uint? processId = null;
        if (XbdmParamUtils.TryGetUInt32(pid.Message, "pid", out uint pidValue)) {
            processId = BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(pidValue) : pidValue;
        }

        return new XbdmConsoleInfo {
            ConsoleId = consoleId,
            DebugName = debugName,
            ExecutionState = execState,
            TitleIp = ip,
            ProcessId = processId
        };
    }

    public async Task<string?> GetDmVersionAsync(CancellationToken cancellationToken) {
        XbdmResponse response = await SendCommandAsync("dmversion", cancellationToken);
        return response.Message;
    }

    public async Task<string?> GetRunningXexPathAsync(string? executable, CancellationToken cancellationToken) {
        string command = $"xbeinfo {(string.IsNullOrWhiteSpace(executable) ? "running" : $"name=\"{executable}\"")}";
        IReadOnlyList<string> lines = await SendCommandForLinesAsync(command, cancellationToken);
        foreach (string line in lines) {
            if (XbdmParamUtils.TryGetString(line, "name", out string? name)) {
                return name;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<XbdmModuleInfo>> GetModulesAsync(bool includeSections, CancellationToken cancellationToken) {
        IReadOnlyList<string> lines = await SendCommandForLinesAsync("modules", cancellationToken);
        List<XbdmModuleInfo> modules = new List<XbdmModuleInfo>(lines.Count);
        foreach (string line in lines) {
            if (!XbdmParamUtils.TryGetString(line, "name", out string? name) ||
                !XbdmParamUtils.TryGetUInt32(line, "base", out uint modBase) ||
                !XbdmParamUtils.TryGetUInt32(line, "size", out uint modSize)) {
                continue;
            }

            XbdmParamUtils.TryGetUInt32(line, "osize", out uint modOriginalSize);
            XbdmParamUtils.TryGetUInt32(line, "timestamp", out uint timestamp);
            DateTime? timestampDate = timestamp == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

            uint? entryPoint = null;
            (XbdmResponse entryResponse, IReadOnlyList<string>? entryLines) = await SendCommandMaybeLinesAsync($"xexfield module=\"{name}\" field=0x10100", cancellationToken);
            if (entryLines != null) {
                if (entryLines.Count == 2 && uint.TryParse(entryLines[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint ep)) {
                    entryPoint = ep;
                }
            }

            XbdmModuleInfo module = new XbdmModuleInfo {
                Name = name,
                BaseAddress = modBase,
                ModuleSize = modSize,
                OriginalModuleSize = modOriginalSize,
                Timestamp = timestampDate,
                EntryPoint = entryPoint
            };

            if (includeSections) {
                (XbdmResponse sectionResponse, IReadOnlyList<string>? sections) = await SendCommandMaybeLinesAsync($"modsections name=\"{name}\"", cancellationToken);
                if (sections != null && sectionResponse.StatusCode != 402) {
                    foreach (string sectionLine in sections) {
                        XbdmParamUtils.TryGetString(sectionLine, "name", out string? secName);
                        XbdmParamUtils.TryGetUInt32(sectionLine, "base", out uint secBase);
                        XbdmParamUtils.TryGetUInt32(sectionLine, "size", out uint secSize);
                        XbdmParamUtils.TryGetUInt32(sectionLine, "index", out uint secIndex);
                        XbdmParamUtils.TryGetUInt32(sectionLine, "flags", out uint secFlags);

                        module.Sections.Add(new XbdmSectionInfo {
                            Name = string.IsNullOrWhiteSpace(secName) ? null : secName,
                            BaseAddress = secBase,
                            Size = secSize,
                            Index = secIndex,
                            Flags = secFlags
                        });
                    }
                }
            }

            modules.Add(module);
        }

        return modules;
    }

    public async Task<IReadOnlyList<XbdmDriveEntry>> GetDrivesAsync(bool includeSize, CancellationToken cancellationToken) {
        IReadOnlyList<string> lines = await SendCommandForLinesAsync("drivelist", cancellationToken);
        List<XbdmDriveEntry> drives = new List<XbdmDriveEntry>(lines.Count);
        foreach (string line in lines) {
            if (!XbdmParamUtils.TryGetString(line, "drivename", out string? drive))
                continue;

            XbdmDriveEntry entry = new XbdmDriveEntry { Name = drive + ":" };
            if (includeSize) {
                IReadOnlyList<string> sizeLines = await SendCommandForLinesAsync($"drivefreespace name=\"{entry.Name}\\\"", cancellationToken);
                if (sizeLines.Count == 1) {
                    XbdmParamUtils.TryGetUInt32(sizeLines[0], "totalbyteslo", out uint totalLo);
                    XbdmParamUtils.TryGetUInt32(sizeLines[0], "totalbyteshi", out uint totalHi);
                    XbdmParamUtils.TryGetUInt32(sizeLines[0], "totalfreebyteslo", out uint freeLo);
                    XbdmParamUtils.TryGetUInt32(sizeLines[0], "totalfreebyteshi", out uint freeHi);
                    entry = entry with {
                        TotalBytes = ((ulong) totalHi << 32) | totalLo,
                        FreeBytes = ((ulong) freeHi << 32) | freeLo
                    };
                }
            }

            drives.Add(entry);
        }

        return drives;
    }

    public async Task<IReadOnlyList<XbdmUserInfo>> GetUserListAsync(CancellationToken cancellationToken) {
        (XbdmResponse response, IReadOnlyList<string>? lines) = await SendCommandMaybeLinesAsync("userlist", cancellationToken);
        if (lines == null)
            return Array.Empty<XbdmUserInfo>();

        List<XbdmUserInfo> users = new List<XbdmUserInfo>(lines.Count);
        foreach (string line in lines) {
            string? gamertag = null;
            if (!XbdmParamUtils.TryGetString(line, "name", out gamertag)) {
                if (!XbdmParamUtils.TryGetString(line, "gamertag", out gamertag)) {
                    XbdmParamUtils.TryGetString(line, "gtag", out gamertag);
                }
            }

            ulong? xuid = null;
            if (XbdmParamUtils.TryGetUInt64(line, "xuid", out ulong parsedXuid)) {
                xuid = parsedXuid;
            }

            users.Add(new XbdmUserInfo {
                Gamertag = gamertag,
                Xuid = xuid,
                SignInState = TryGetSignInState(line),
                RawLine = line
            });
        }

        return users;
    }

    private static uint? TryGetSignInState(string line) {
        if (XbdmParamUtils.TryGetUInt32(line, "signinstate", out uint signInState))
            return signInState;
        if (XbdmParamUtils.TryGetUInt32(line, "signedin", out uint signedIn))
            return signedIn;
        if (XbdmParamUtils.TryGetUInt32(line, "state", out uint state))
            return state;
        return null;
    }

    public async Task<IReadOnlyList<XbdmFileEntry>> GetDirectoryAsync(string path, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        if (!path.EndsWith("\\", StringComparison.Ordinal))
            path += "\\";

        IReadOnlyList<string> lines = await SendCommandForLinesAsync($"dirlist name=\"{path}\"", cancellationToken);
        List<XbdmFileEntry> entries = new List<XbdmFileEntry>(lines.Count);

        foreach (string line in lines) {
            if (!XbdmParamUtils.TryGetString(line, "name", out string? name))
                continue;

            XbdmParamUtils.TryGetUInt32(line, "sizelo", out uint sizeLo);
            XbdmParamUtils.TryGetUInt32(line, "sizehi", out uint sizeHi);
            XbdmParamUtils.TryGetUInt32(line, "createlo", out uint createLo);
            XbdmParamUtils.TryGetUInt32(line, "createhi", out uint createHi);
            XbdmParamUtils.TryGetUInt32(line, "changelo", out uint changeLo);
            XbdmParamUtils.TryGetUInt32(line, "changehi", out uint changeHi);

            DateTime? created = createLo == 0 && createHi == 0 ? null : DateTime.FromFileTimeUtc((long) (((ulong) createHi << 32) | createLo));
            DateTime? modified = changeLo == 0 && changeHi == 0 ? null : DateTime.FromFileTimeUtc((long) (((ulong) changeHi << 32) | changeLo));

            entries.Add(new XbdmFileEntry {
                Name = name,
                Size = ((ulong) sizeHi << 32) | sizeLo,
                CreatedUtc = created,
                ModifiedUtc = modified,
                IsDirectory = XbdmParamUtils.HasFlag(line, "directory")
            });
        }

        return entries;
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken) {
        XbdmResponse response = await SendCommandAsync($"delete name=\"{path}\"", cancellationToken);
        if (response.StatusCode == 414) {
            await SendCommandAsync($"delete name=\"{path}\" dir", cancellationToken);
        }
    }

    public Task DebugStopAsync(CancellationToken cancellationToken) {
        return SendCommandAsync("stop", cancellationToken);
    }

    public Task DebugGoAsync(CancellationToken cancellationToken) {
        return SendCommandAsync("go", cancellationToken);
    }

    public Task SuspendThreadAsync(uint threadId, CancellationToken cancellationToken) {
        return SendCommandAsync($"suspend thread=0x{threadId:X8}", cancellationToken);
    }

    public Task ResumeThreadAsync(uint threadId, CancellationToken cancellationToken) {
        return SendCommandAsync($"resume thread=0x{threadId:X8}", cancellationToken);
    }

    public Task MoveAsync(string oldPath, string newPath, CancellationToken cancellationToken) {
        return SendCommandAsync($"rename name=\"{oldPath}\" newname=\"{newPath}\"", cancellationToken);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) {
        return SendCommandAsync($"mkdir name=\"{path}\"", cancellationToken);
    }

    public async Task DownloadFileAsync(string remotePath, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync($"getfile name=\"{remotePath}\"", cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType != XbdmResponseType.BinaryResponse && response.StatusCode != (int) XbdmResponseType.BinaryResponse) {
                throw new IOException($"getfile failed: {response.RawMessage}");
            }

            int length = await reader.ReadInt32LEAsync(cancellationToken);
            await ReadBinaryToStreamAsync(destination, length, progress, cancellationToken);
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task UploadFileAsync(string remotePath, Stream source, long length, IProgress<long>? progress, CancellationToken cancellationToken) {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync($"sendfile name=\"{remotePath}\" length=0x{length:X8}", cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType != XbdmResponseType.ReadyForBinary && response.StatusCode != (int) XbdmResponseType.ReadyForBinary) {
                throw new IOException($"sendfile failed: {response.RawMessage}");
            }

            await WriteBinaryFromStreamAsync(source, length, progress, cancellationToken);
            XbdmResponse done = await ReadResponseAsync(cancellationToken);
            if (done.ResponseType != XbdmResponseType.SingleResponse && done.StatusCode != (int) XbdmResponseType.SingleResponse) {
                throw new IOException($"sendfile completion failed: {done.RawMessage}");
            }
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task ReadMemoryAsync(uint address, uint length, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync($"getmemex addr=0x{address:X8} length=0x{length:X8}", cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType != XbdmResponseType.BinaryResponse && response.StatusCode != (int) XbdmResponseType.BinaryResponse) {
                throw new IOException($"getmemex failed: {response.RawMessage}");
            }

            await ReadChunkedBinaryToStreamAsync(destination, length, progress, cancellationToken);
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task<byte[]> ReadMemoryBytesAsync(uint address, int length, CancellationToken cancellationToken) {
        using MemoryStream ms = new MemoryStream(length);
        await ReadMemoryAsync(address, (uint) length, ms, null, cancellationToken);
        return ms.ToArray();
    }

    public async Task<byte[]> ReadMemoryBytesReliableAsync(uint address, int length, CancellationToken cancellationToken) {
        if (length <= 0)
            return Array.Empty<byte>();

        if (length <= 0x4000) {
            try {
                byte[] legacy = await ReadMemoryLegacyBytesAsync(address, length, cancellationToken);
                if (legacy.Length == length)
                    return legacy;
            }
            catch {
                // Fall back to getmemex for targets that do not support legacy getmem reliably.
            }
        }

        byte[] data = await ReadMemoryBytesAsync(address, length, cancellationToken);
        if (length > 0x4000 || data.Length != length || data.Any(static b => b != 0))
            return data;

        try {
            byte[] legacy = await ReadMemoryLegacyBytesAsync(address, length, cancellationToken);
            if (legacy.Length == length)
                return legacy;
        }
        catch {
            // ignored
        }

        return data;
    }

    public async Task<XbdmScreenshot> CaptureScreenshotAsync(CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync("screenshot", cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType != XbdmResponseType.BinaryResponse && response.StatusCode != (int) XbdmResponseType.BinaryResponse) {
                throw new IOException($"screenshot failed: {response.RawMessage}");
            }

            byte firstByte = await reader.ReadByteAsync(cancellationToken);
            if (firstByte == (byte) 'p' || firstByte == (byte) 'P') {
                byte[] rest = await reader.ReadUntilAsync((byte) '\n', 512, cancellationToken);
                byte[] headerBytes = new byte[1 + rest.Length];
                headerBytes[0] = firstByte;
                Buffer.BlockCopy(rest, 0, headerBytes, 1, rest.Length);
                string headerLine = Ascii.GetString(headerBytes).TrimEnd('\r', '\n', '\0');
                if (!TryParseHeaderLine(headerLine, out XbdmScreenshotInfo infoParsed)) {
                    throw new IOException($"Unable to parse screenshot header: {headerLine}");
                }

                uint finalSize = infoParsed.FramebufferSize;
                if (finalSize == 0) {
                    ulong computedSize = (ulong) infoParsed.Pitch * infoParsed.Height;
                    if (computedSize == 0 || computedSize > int.MaxValue) {
                        throw new IOException("Invalid screenshot buffer size.");
                    }
                    finalSize = (uint) computedSize;
                    infoParsed = infoParsed with { FramebufferSize = finalSize };
                }

                byte[] data = new byte[finalSize];
                await reader.ReadExactAsync(data, 0, (int) finalSize, cancellationToken);
                return new XbdmScreenshot(infoParsed, data);
            }

            byte[] firstIntBytes = new byte[4];
            firstIntBytes[0] = firstByte;
            await reader.ReadExactAsync(firstIntBytes, 1, 3, cancellationToken);
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(firstIntBytes);
            if (first >= 65536 && first <= 128 * 1024 * 1024) {
                byte[] payload = new byte[first];
                await reader.ReadExactAsync(payload, 0, (int) first, cancellationToken);

                if (TryParseHeaderFromData(payload, out XbdmScreenshotInfo parsed, out int dataOffset)) {
                    byte[] data = dataOffset > 0 ? payload[dataOffset..] : payload;
                    XbdmScreenshotInfo info = parsed.FramebufferSize == 0 || parsed.FramebufferSize > data.Length
                        ? parsed with { FramebufferSize = (uint) data.Length }
                        : parsed;
                    return new XbdmScreenshot(info, data);
                }

                XbdmScreenshotInfo fallback = new XbdmScreenshotInfo(0, 0, 0, 0, 0, 0, (uint) payload.Length);
                return new XbdmScreenshot(fallback, payload);
            }

            uint a = first;
            uint b = (uint) await reader.ReadInt32LEAsync(cancellationToken);
            uint c = (uint) await reader.ReadInt32LEAsync(cancellationToken);
            uint format = (uint) await reader.ReadInt32LEAsync(cancellationToken);
            uint offsetX = (uint) await reader.ReadInt32LEAsync(cancellationToken);
            uint offsetY = (uint) await reader.ReadInt32LEAsync(cancellationToken);
            uint framebufferSize = (uint) await reader.ReadInt32LEAsync(cancellationToken);

            XbdmScreenshotInfo infoHeader = NormalizeScreenshotInfo(a, b, c, format, offsetX, offsetY, framebufferSize);

            ulong computed = (ulong) infoHeader.Pitch * infoHeader.Height;
            uint length = infoHeader.FramebufferSize;
            if (length == 0 || length > 128 * 1024 * 1024) {
                if (computed == 0 || computed > int.MaxValue) {
                    throw new IOException("Invalid screenshot buffer size.");
                }
                length = (uint) computed;
                infoHeader = infoHeader with { FramebufferSize = length };
            }

            byte[] dataHeader = new byte[length];
            await reader.ReadExactAsync(dataHeader, 0, (int) length, cancellationToken);
            return new XbdmScreenshot(infoHeader, dataHeader);
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task<IReadOnlyList<XbdmMemoryRegion>> GetMemoryRegionsAsync(CancellationToken cancellationToken) {
        IReadOnlyList<string> lines = await SendCommandForLinesAsync("walkmem", cancellationToken);
        List<XbdmMemoryRegion> regions = new List<XbdmMemoryRegion>(lines.Count);
        foreach (string line in lines) {
            XbdmParamUtils.TryGetUInt32(line, "base", out uint baseAddr);
            XbdmParamUtils.TryGetUInt32(line, "size", out uint size);
            XbdmParamUtils.TryGetUInt32(line, "protect", out uint protect);
            XbdmParamUtils.TryGetUInt32(line, "phys", out uint phys);
            regions.Add(new XbdmMemoryRegion {
                BaseAddress = baseAddr,
                Size = size,
                Protect = protect,
                Phys = phys
            });
        }
        return regions;
    }

    public async Task<IReadOnlyList<XbdmThreadInfo>> GetThreadsAsync(bool includeNames, CancellationToken cancellationToken) {
        IReadOnlyList<string> lines = await SendCommandForLinesAsync("threads", cancellationToken);
        List<XbdmThreadInfo> threads = new List<XbdmThreadInfo>(lines.Count);
        bool allowNames = includeNames;
        foreach (string line in lines) {
            if (!TryParseThreadId(line, out uint threadId))
                continue;

            (XbdmResponse response, IReadOnlyList<string>? infoLines) =
                await SendCommandMaybeLinesAsync($"threadinfo thread=0x{threadId:X8}", cancellationToken);
            if (infoLines == null || infoLines.Count == 0)
                continue;

            XbdmThreadInfo info = ParseThreadInfo(threadId, infoLines[0]);
            if (allowNames && info.NameAddress != 0 && info.NameLength != 0) {
                int length = (int) Math.Min(info.NameLength, 256);
                if (length > 0) {
                    try {
                        string? name = await ReadAsciiAsync(info.NameAddress, length, cancellationToken);
                        info = info with { Name = name };
                    }
                    catch {
                        allowNames = false;
                    }
                }
            }

            threads.Add(info);
        }

        return threads;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetThreadContextAsync(uint threadId, CancellationToken cancellationToken) {
        IReadOnlyList<string> lines = await SendCommandForLinesAsync($"getcontext thread=0x{threadId:X8} control int fp", cancellationToken);
        Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines) {
            int split = line.IndexOf('=');
            if (split <= 0)
                continue;
            string name = line.Substring(0, split).Trim();
            string value = line.Substring(split + 1).Trim();
            dict[name] = value;
        }
        return dict;
    }

    public async Task WriteMemoryAsync(uint address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            const int ChunkSize = 64;
            int offset = 0;
            while (offset < data.Length) {
                int writeCount = Math.Min(ChunkSize, data.Length - offset);
                string hex = ConvertToHex(data.Span.Slice(offset, writeCount));
                await WriteLineAsync($"setmem addr=0x{address + (uint) offset:X8} data={hex}", cancellationToken);
                XbdmResponse response = await ReadResponseAsync(cancellationToken);
                if (response.StatusCode != 200 && response.StatusCode != 404) {
                    throw new IOException($"setmem failed: {response.RawMessage}");
                }

                offset += writeCount;
            }
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task<(XbdmResponse Response, IReadOnlyList<string>? Lines)> SendRawAsync(string command, CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync(command, cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType == XbdmResponseType.MultiResponse || response.StatusCode == (int) XbdmResponseType.MultiResponse) {
                IReadOnlyList<string> lines = await ReadMultiLinesAsync(cancellationToken);
                return (response, lines);
            }

            return (response, null);
        }
        finally {
            ioLock.Release();
        }
    }

    private async Task<byte[]> ReadMemoryLegacyBytesAsync(uint address, int length, CancellationToken cancellationToken) {
        if (length <= 0)
            return Array.Empty<byte>();

        (XbdmResponse response, IReadOnlyList<string>? lines) = await SendRawAsync(
            $"getmem addr=0x{address:X8} length={length}",
            cancellationToken);

        if (response.StatusCode != 202 || lines == null || lines.Count == 0)
            throw new IOException($"getmem failed: {response.RawMessage}");

        string hex = string.Concat(lines);
        char[] filtered = hex.Where(Uri.IsHexDigit).ToArray();
        if (filtered.Length == 0)
            return Array.Empty<byte>();

        string normalized = new string(filtered);
        int maxChars = Math.Min(normalized.Length, length * 2);
        if ((maxChars & 1) != 0)
            maxChars--;
        if (maxChars <= 0)
            return Array.Empty<byte>();

        return Convert.FromHexString(normalized.Substring(0, maxChars));
    }

    private async Task WriteLineAsync(string command, CancellationToken cancellationToken) {
        byte[] bytes = Ascii.GetBytes(command + "\r\n");
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private async Task<XbdmResponse> ReadResponseAsync(CancellationToken cancellationToken) {
        string line = string.Empty;
        for (int attempt = 0; attempt < 10; attempt++) {
            line = await reader.ReadLineAsync(cancellationToken);
            if (IsResponseLine(line))
                return XbdmResponseParser.Parse(line);
        }
        throw new IOException($"Invalid XBDM response: {line}");
    }

    private static bool IsResponseLine(string line) {
        if (line.Length < 4)
            return false;
        if (!char.IsDigit(line[0]) || !char.IsDigit(line[1]) || !char.IsDigit(line[2]))
            return false;
        return line[3] == '-' || line[3] == ' ';
    }

    private async Task<IReadOnlyList<string>> ReadMultiLinesAsync(CancellationToken cancellationToken) {
        List<string> lines = new List<string>();
        while (true) {
            string line = await reader.ReadLineAsync(cancellationToken);
            if (line == ".")
                break;
            lines.Add(line);
        }

        return lines;
    }

    private static XbdmScreenshotInfo NormalizeScreenshotInfo(uint first, uint second, uint third, uint format, uint offsetX, uint offsetY, uint framebufferSize) {
        XbdmScreenshotInfo optionA = new XbdmScreenshotInfo(first, second, third, format, offsetX, offsetY, framebufferSize);
        XbdmScreenshotInfo optionB = new XbdmScreenshotInfo(third, second, first, format, offsetX, offsetY, framebufferSize);

        bool aOk = LooksPlausible(optionA);
        bool bOk = LooksPlausible(optionB);

        if (aOk && !bOk) return optionA;
        if (bOk && !aOk) return optionB;
        if (aOk && bOk) {
            ulong aSize = (ulong) optionA.Pitch * optionA.Height;
            ulong bSize = (ulong) optionB.Pitch * optionB.Height;
            if (framebufferSize != 0) {
                ulong diffA = aSize > framebufferSize ? aSize - framebufferSize : framebufferSize - aSize;
                ulong diffB = bSize > framebufferSize ? bSize - framebufferSize : framebufferSize - bSize;
                return diffA <= diffB ? optionA : optionB;
            }
            return aSize <= bSize ? optionA : optionB;
        }

        return optionA;
    }

    private static bool TryParseHeaderFromData(byte[] data, out XbdmScreenshotInfo info, out int dataOffset) {
        info = new XbdmScreenshotInfo(0, 0, 0, 0, 0, 0, 0);
        dataOffset = 0;
        if (data.Length < 28)
            return false;

        uint first = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        uint second = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
        uint third = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));
        uint format = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12, 4));
        uint offsetX = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16, 4));
        uint offsetY = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20, 4));
        uint framebufferSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24, 4));

        XbdmScreenshotInfo parsed = NormalizeScreenshotInfo(first, second, third, format, offsetX, offsetY, framebufferSize);
        if (!LooksPlausible(parsed))
            return false;

        if (parsed.FramebufferSize > 0 && parsed.FramebufferSize <= data.Length - 28) {
            info = parsed;
            dataOffset = 28;
            return true;
        }

        if (parsed.FramebufferSize > 0 && parsed.FramebufferSize <= data.Length) {
            info = parsed;
            dataOffset = 0;
            return true;
        }

        info = parsed;
        dataOffset = 28;
        return true;
    }

    private static bool TryParseHeaderLine(string header, out XbdmScreenshotInfo info) {
        info = new XbdmScreenshotInfo(0, 0, 0, 0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(header))
            return false;

        if (!XbdmParamUtils.TryGetUInt32(header, "pitch", out uint pitch))
            return false;
        XbdmParamUtils.TryGetUInt32(header, "width", out uint width);
        XbdmParamUtils.TryGetUInt32(header, "height", out uint height);
        XbdmParamUtils.TryGetUInt32(header, "format", out uint format);
        XbdmParamUtils.TryGetUInt32(header, "offsetx", out uint offsetX);
        XbdmParamUtils.TryGetUInt32(header, "offsety", out uint offsetY);
        XbdmParamUtils.TryGetUInt32(header, "framebuffersize", out uint framebufferSize);

        info = new XbdmScreenshotInfo(pitch, height, width, format, offsetX, offsetY, framebufferSize);
        return true;
    }

    private static bool LooksPlausible(XbdmScreenshotInfo info) {
        if (info.Width == 0 || info.Height == 0)
            return false;
        if (info.Width > 8192 || info.Height > 8192)
            return false;
        if (info.Pitch < info.Width)
            return false;

        ulong minBytesPerRow = info.Width * 2UL;
        if (info.Pitch < minBytesPerRow || info.Pitch > info.Width * 8UL)
            return false;

        ulong computed = info.Pitch * info.Height;
        if (computed == 0 || computed > 128UL * 1024UL * 1024UL)
            return false;

        if (info.FramebufferSize != 0 && info.FramebufferSize < computed)
            return false;

        return true;
    }

    private async Task ReadBinaryToStreamAsync(Stream destination, int length, IProgress<long>? progress, CancellationToken cancellationToken) {
        const int BufferSize = 64 * 1024;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long totalRead = 0;
        try {
            int remaining = length;
            while (remaining > 0) {
                int read = Math.Min(remaining, buffer.Length);
                await reader.ReadExactAsync(buffer, 0, read, cancellationToken);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
                totalRead += read;
                progress?.Report(totalRead);
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ReadChunkedBinaryToStreamAsync(Stream destination, uint length, IProgress<long>? progress, CancellationToken cancellationToken) {
        const int BufferSize = 64 * 1024;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long totalRead = 0;
        uint remaining = length;
        try {
            while (remaining > 0) {
                ushort header = await reader.ReadUInt16LEAsync(cancellationToken);
                int chunkSize = header & 0x7FFF;
                bool statusFlag = (header & 0x8000) != 0;
                if (chunkSize == 0)
                    continue;
                if (statusFlag) {
                    int zeroCount = (int) Math.Min(remaining, (uint) chunkSize);
                    Array.Clear(buffer, 0, Math.Min(buffer.Length, zeroCount));
                    while (zeroCount > 0) {
                        int write = Math.Min(zeroCount, buffer.Length);
                        await destination.WriteAsync(buffer.AsMemory(0, write), cancellationToken);
                        zeroCount -= write;
                        remaining -= (uint) write;
                        totalRead += write;
                        progress?.Report(totalRead);
                    }
                    continue;
                }

                if (chunkSize > remaining)
                    throw new IOException("XBDM returned more bytes than expected.");

                int toRead = chunkSize;
                while (toRead > 0) {
                    int read = Math.Min(toRead, buffer.Length);
                    await reader.ReadExactAsync(buffer, 0, read, cancellationToken);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    toRead -= read;
                    remaining -= (uint) read;
                    totalRead += read;
                    progress?.Report(totalRead);
                }
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task WriteBinaryFromStreamAsync(Stream source, long length, IProgress<long>? progress, CancellationToken cancellationToken) {
        const int BufferSize = 64 * 1024;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long totalWritten = 0;
        try {
            long remaining = length;
            while (remaining > 0) {
                int read = await source.ReadAsync(buffer.AsMemory(0, (int) Math.Min(remaining, buffer.Length)), cancellationToken);
                if (read == 0)
                    throw new IOException("Unexpected end of source stream.");
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
                totalWritten += read;
                progress?.Report(totalWritten);
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string ConvertToHex(ReadOnlySpan<byte> data) {
        char[] chars = new char[data.Length * 2];
        for (int i = 0; i < data.Length; i++) {
            byte b = data[i];
            chars[i * 2] = GetHex((byte) (b >> 4));
            chars[i * 2 + 1] = GetHex((byte) (b & 0x0F));
        }

        return new string(chars);
    }

    private static char GetHex(byte value) {
        return (char) (value < 10 ? '0' + value : 'A' + (value - 10));
    }

    private static bool TryParseThreadId(string text, out uint id) {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
        }
        if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            return true;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int signed)) {
            id = unchecked((uint) signed);
            return true;
        }
        return false;
    }

    private static XbdmThreadInfo ParseThreadInfo(uint threadId, string text) {
        XbdmParamUtils.TryGetUInt32(text, "suspend", out uint suspendCount);
        XbdmParamUtils.TryGetUInt32(text, "priority", out uint priority);
        XbdmParamUtils.TryGetUInt32(text, "tlsbase", out uint tlsBaseAddress);
        XbdmParamUtils.TryGetUInt32(text, "base", out uint baseAddress);
        XbdmParamUtils.TryGetUInt32(text, "limit", out uint stackLimit);
        XbdmParamUtils.TryGetUInt32(text, "slack", out uint stackSlack);
        XbdmParamUtils.TryGetUInt32(text, "nameaddr", out uint nameAddress);
        XbdmParamUtils.TryGetUInt32(text, "namelen", out uint nameLength);
        XbdmParamUtils.TryGetUInt32(text, "proc", out uint currentProcessor);
        XbdmParamUtils.TryGetUInt32(text, "lasterr", out uint lastError);

        return new XbdmThreadInfo {
            Id = threadId,
            SuspendCount = suspendCount,
            Priority = priority,
            TlsBaseAddress = tlsBaseAddress,
            BaseAddress = baseAddress,
            StackLimit = stackLimit,
            StackSlack = stackSlack,
            NameAddress = nameAddress,
            NameLength = nameLength,
            CurrentProcessor = currentProcessor,
            LastError = lastError
        };
    }

    private async Task<string?> ReadAsciiAsync(uint address, int length, CancellationToken cancellationToken) {
        byte[] bytes;
        try {
            bytes = await ReadMemoryBytesSmallAsync(address, length, cancellationToken);
        }
        catch {
            bytes = await ReadMemoryBytesAsync(address, length, cancellationToken);
        }
        int end = Array.IndexOf(bytes, (byte) 0);
        if (end < 0)
            end = bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, end);
    }

    private async Task<byte[]> ReadMemoryBytesSmallAsync(uint address, int length, CancellationToken cancellationToken) {
        if (length <= 0)
            return Array.Empty<byte>();
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync($"getmem addr=0x{address:X8} length=0x{length:X8}", cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType != XbdmResponseType.BinaryResponse && response.StatusCode != (int) XbdmResponseType.BinaryResponse) {
                throw new IOException($"getmem failed: {response.RawMessage}");
            }

            int reported = await reader.ReadInt32LEAsync(cancellationToken);
            int toRead = Math.Min(reported, length);
            byte[] data = new byte[toRead];
            await reader.ReadExactAsync(data, 0, toRead, cancellationToken);
            int remaining = reported - toRead;
            if (remaining > 0) {
                byte[] sink = ArrayPool<byte>.Shared.Rent(Math.Min(remaining, 64 * 1024));
                try {
                    while (remaining > 0) {
                        int read = Math.Min(remaining, sink.Length);
                        await reader.ReadExactAsync(sink, 0, read, cancellationToken);
                        remaining -= read;
                    }
                }
                finally {
                    ArrayPool<byte>.Shared.Return(sink);
                }
            }
            return data;
        }
        finally {
            ioLock.Release();
        }
    }
}
