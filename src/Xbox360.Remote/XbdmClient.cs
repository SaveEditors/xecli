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

    public void Validate() {
        if (string.IsNullOrWhiteSpace(Host)) {
            throw new ArgumentException("Host cannot be empty or whitespace.", nameof(Host));
        }

        if (Port is < 1 or > 65535) {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 1 and 65535.");
        }

        if (TimeoutMs <= 0) {
            throw new ArgumentOutOfRangeException(nameof(TimeoutMs), TimeoutMs, "Timeout must be positive.");
        }
    }
}

public sealed class XbdmClient : IAsyncDisposable, IDisposable {
    private static readonly Encoding Ascii = Encoding.ASCII;
    private const int MaxMultiResponseLines = 65_536;
    private const int MaxMultiResponseBytes = 8 * 1024 * 1024;
    private const int MinMultiResponseDeadlineMs = 1_000;
    private const int MaxMultiResponseDeadlineMs = 60_000;

    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly XbdmLineReader reader;
    private readonly SemaphoreSlim ioLock = new SemaphoreSlim(1, 1);
    private readonly int timeoutMs;

    private XbdmClient(TcpClient client, NetworkStream stream, XbdmLineReader reader, int timeoutMs) {
        this.client = client;
        this.stream = stream;
        this.reader = reader;
        this.timeoutMs = timeoutMs;
    }

    public static async Task<XbdmClient> ConnectAsync(XbdmConnectionOptions options, CancellationToken cancellationToken) {
        options.Validate();
        TcpClient client = new TcpClient();
        client.ReceiveTimeout = options.TimeoutMs;
        client.SendTimeout = options.TimeoutMs;
        try {
            try {
                await ConnectWithDeadlineAsync(client, options.Host, options.Port, options.TimeoutMs, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException(
                    $"XBDM TCP connection to {options.Host}:{options.Port} did not complete within {options.TimeoutMs} ms.",
                    new TimeoutException($"TCP connection to {options.Host}:{options.Port} timed out."));
            }

            NetworkStream stream = client.GetStream();
            XbdmLineReader reader = new XbdmLineReader(stream, 8192, options.TimeoutMs);
            try {
                Task<string> greetingTask = reader.ReadLineAsync(cancellationToken);
                Task timeoutTask = Task.Delay(options.TimeoutMs, CancellationToken.None);
                if (await Task.WhenAny(greetingTask, timeoutTask) == timeoutTask) {
                    throw new TimeoutException(
                        $"XBDM handshake at {options.Host}:{options.Port} stalled after the TCP connection was accepted; no greeting arrived within {options.TimeoutMs} ms.",
                        new TimeoutException($"XBDM greeting from {options.Host}:{options.Port} timed out."));
                }

                string line = await greetingTask;
                XbdmResponse response = XbdmResponseParser.Parse(line);

                if (response.ResponseType != XbdmResponseType.Connected && response.StatusCode != 201) {
                    throw new IOException($"XBDM handshake rejected by {options.Host}:{options.Port}: {response.RawMessage}");
                }

                return new XbdmClient(client, stream, reader, options.TimeoutMs);
            }
            catch (TimeoutException ex) {
                throw new TimeoutException(
                    $"XBDM handshake at {options.Host}:{options.Port} stalled after the TCP connection was accepted; no greeting arrived within {options.TimeoutMs} ms.",
                    ex);
            }
            catch (IOException ex) when (ex.Message.Contains("XBDM connection closed", StringComparison.OrdinalIgnoreCase)) {
                throw new IOException($"XBDM handshake at {options.Host}:{options.Port} ended before the greeting arrived.", ex);
            }
        }
        catch (OperationCanceledException ex) {
            client.Dispose();
            throw new TimeoutException(
                $"XBDM connection to {options.Host}:{options.Port} did not complete within {options.TimeoutMs} ms. Check that the XBDM endpoint is powered on, not frozen, and reachable.",
                ex);
        }
        catch {
            client.Dispose();
            throw;
        }
    }

    private static async Task ConnectWithDeadlineAsync(TcpClient client, string host, int port, int timeoutMs, CancellationToken cancellationToken) {
        Task connectTask = client.ConnectAsync(host, port);
        await WaitForConnectDeadlineAsync(client, connectTask, host, port, timeoutMs, cancellationToken);
    }

    private static async Task WaitForConnectDeadlineAsync(TcpClient client, Task connectTask, string host, int port, int timeoutMs, CancellationToken cancellationToken) {
        Task delayTask = Task.Delay(timeoutMs, cancellationToken);
        Task completed = await Task.WhenAny(connectTask, delayTask);
        if (completed == connectTask) {
            await connectTask;
            return;
        }

        client.Dispose();
        _ = connectTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        throw new TimeoutException(
            $"XBDM TCP connection to {host}:{port} did not complete within {timeoutMs} ms.",
            new TimeoutException($"TCP connection to {host}:{port} timed out."));
    }

    public async ValueTask DisposeAsync() {
        try {
            if (client.Connected) {
                using CancellationTokenSource byeCts = new CancellationTokenSource(500);
                await SendCommandAsync("bye", byeCts.Token);
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
        (XbdmResponse _, IReadOnlyList<string> lines) = await SendCommandForLinesWithResponseAsync(command, cancellationToken);
        return lines;
    }

    private async Task<(XbdmResponse Response, IReadOnlyList<string> Lines)> SendCommandForLinesWithResponseAsync(string command, CancellationToken cancellationToken) {
        (XbdmResponse response, IReadOnlyList<string>? lines) = await SendCommandMaybeLinesAsync(command, cancellationToken);
        if (lines == null) {
            throw XbdmProtocolViolationException.ForUnexpectedResponse(
                command,
                response,
                new[] { (int) XbdmResponseType.MultiResponse },
                XbdmResponseType.MultiResponse,
                XbdmResponseBodyRequirement.Optional);
        }

        return (response, lines);
    }

    public async Task<(XbdmResponse Response, IReadOnlyList<string>? Lines)> SendCommandMaybeLinesAsync(string command, CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync(command, cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType == XbdmResponseType.MultiResponse || response.StatusCode == (int) XbdmResponseType.MultiResponse) {
                IReadOnlyList<string> lines = await ReadMultiLinesAsync(command, response, cancellationToken);
                return (response, lines);
            }

            return (response, null);
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task<XbdmConsoleInfo> GetConsoleInfoAsync(CancellationToken cancellationToken) {
        string consoleIdRaw = await SendCommandExpectTextPayloadAsync(
            "getconsoleid",
            "non-empty console ID text",
            cancellationToken);
        string consoleId = consoleIdRaw;
        if (XbdmParamUtils.TryGetString(consoleIdRaw, "consoleid", out string? parsedId)) {
            consoleId = parsedId;
        }
        string debugName = await SendCommandExpectTextPayloadAsync(
            "dbgname",
            "non-empty debug name text",
            cancellationToken);
        string execState = await SendCommandExpectTextPayloadAsync(
            "getexecstate",
            "non-empty execution state text",
            cancellationToken);
        string altAddr = await SendCommandExpectTextPayloadAsync(
            "altaddr",
            "non-empty title IP address text",
            cancellationToken);
        uint? processId = await GetCurrentProcessIdAsync(cancellationToken);

        string? ip = null;
        if (XbdmParamUtils.TryGetUInt32(altAddr, "addr", out uint addr)) {
            IPAddress addrIp = new IPAddress(BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(addr) : addr);
            ip = addrIp.ToString();
        }

        return new XbdmConsoleInfo {
            ConsoleId = consoleId,
            DebugName = debugName,
            ExecutionState = execState,
            TitleIp = ip,
            ProcessId = processId
        };
    }

    public async Task<uint?> GetCurrentProcessIdAsync(CancellationToken cancellationToken) {
        string pid = await SendCommandExpectTextPayloadAsync(
            "getpid",
            "non-empty process ID text",
            cancellationToken);
        if (!XbdmParamUtils.TryGetUInt32(pid, "pid", out uint pidValue))
            return null;

        return BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(pidValue) : pidValue;
    }

    public async Task<string?> GetDmVersionAsync(CancellationToken cancellationToken) {
        return await SendCommandExpectTextPayloadAsync(
            "dmversion",
            "non-empty debug monitor version text",
            cancellationToken);
    }

    public async Task<string?> GetRunningXexPathAsync(string? executable, CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(executable)) {
            foreach (char c in executable) {
                if (c == '"' || char.IsControl(c)) {
                    throw new ArgumentException("Executable cannot contain quotes or control characters.", nameof(executable));
                }
            }
        }

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
        (XbdmResponse modulesResponse, IReadOnlyList<string> moduleLines) = await SendCommandForLinesWithResponseAsync("modules", cancellationToken);
        IReadOnlyList<string> lines = modulesResponse.ExpectLines(
            "modules",
            moduleLines,
            "zero or more module lines with name, base, and size fields",
            validator: LinesAreModuleRecords);
        List<XbdmModuleInfo> modules = new List<XbdmModuleInfo>(lines.Count);
        foreach (string line in lines) {
            if (!TryParseModuleLine(line, out string name, out uint modBase, out uint modSize, out uint modOriginalSize, out uint timestamp)) {
                continue;
            }

            DateTime? timestampDate = timestamp == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

            uint? entryPoint = null;
            List<string> warnings = new List<string>();
            if (includeSections) {
                (XbdmResponse entryResponse, IReadOnlyList<string>? entryLines) = await SendCommandMaybeLinesAsync($"xexfield module=\"{name}\" field=0x10100", cancellationToken);
                if (entryLines != null) {
                    if (entryLines.Count == 2 && uint.TryParse(entryLines[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint ep)) {
                        entryPoint = ep;
                    }
                    else {
                        warnings.Add($"Entry point unavailable: xexfield returned an unexpected payload for {name}.");
                    }
                }
                else {
                    warnings.Add($"Entry point unavailable: xexfield returned status {entryResponse.StatusCode} for {name}.");
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
            module.Warnings.AddRange(warnings);

            if (includeSections) {
                (XbdmResponse sectionResponse, IReadOnlyList<string>? sections) = await SendCommandMaybeLinesAsync($"modsections name=\"{name}\"", cancellationToken);
                if (sections == null) {
                    module.Warnings.Add($"Sections unavailable: modsections returned status {sectionResponse.StatusCode} for {name}.");
                }
                else if (sectionResponse.StatusCode == 402) {
                    module.Warnings.Add($"Sections unavailable: modsections returned status 402 for {name}.");
                }
                else {
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
        (XbdmResponse drivesResponse, IReadOnlyList<string> driveLines) = await SendCommandForLinesWithResponseAsync("drivelist", cancellationToken);
        IReadOnlyList<string> lines = drivesResponse.ExpectLines(
            "drivelist",
            driveLines,
            "zero or more drive lines with a drivename field",
            validator: LinesAreDriveRecords);
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
        (XbdmResponse response, IReadOnlyList<string> rawLines) = await SendCommandForLinesWithResponseAsync("userlist", cancellationToken);
        IReadOnlyList<string> lines = response.ExpectLines(
            "userlist",
            rawLines,
            "zero or more user lines with name, gamertag, gtag, xuid, or sign-in state fields",
            validator: LinesAreUserRecords);

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

        path = ValidateCommandPath(path, nameof(path));
        if (!path.EndsWith("\\", StringComparison.Ordinal))
            path += "\\";

        string command = $"dirlist name=\"{path}\"";
        (XbdmResponse directoryResponse, IReadOnlyList<string> directoryLines) = await SendCommandForLinesWithResponseAsync(command, cancellationToken);
        IReadOnlyList<string> lines = directoryResponse.ExpectLines(
            command,
            directoryLines,
            "zero or more directory lines with a name field",
            validator: LinesAreDirectoryRecords);
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
        string commandPath = ValidateCommandPath(path, nameof(path));
        XbdmResponse response = await SendCommandAsync($"delete name=\"{commandPath}\"", cancellationToken);
        if (response.StatusCode == 414) {
            string directoryPath = NormalizeDeleteCommandPath(path, nameof(path));
            await SendCommandExpectOkAsync($"delete name=\"{directoryPath}\" dir", cancellationToken);
            return;
        }

        EnsureOkResponse(response, "delete");
    }

    public async Task DebugStopAsync(CancellationToken cancellationToken) {
        XbdmResponse response = await SendCommandAsync("stop", cancellationToken);
        bool alreadyStopped = response.StatusCode == 426 &&
            response.Message.Contains("already stopped", StringComparison.OrdinalIgnoreCase);
        if (!alreadyStopped)
            EnsureOkResponse(response, "stop");

        string observedState = await GetExecutionStateAsync(cancellationToken);
        if (!IsStoppedExecutionState(observedState)) {
            throw new IOException(
                $"stop was acknowledged, but execution-state verification returned '{observedState.Trim()}'.");
        }
    }

    public async Task DebugGoAsync(CancellationToken cancellationToken) {
        await SendCommandExpectOkAsync("go", cancellationToken);
        string observedState = await GetExecutionStateAsync(cancellationToken);
        if (!IsRunningExecutionState(observedState)) {
            throw new IOException(
                $"go was acknowledged, but execution-state verification returned '{observedState.Trim()}'.");
        }
    }

    public Task<string> GetExecutionStateAsync(CancellationToken cancellationToken) {
        return SendCommandExpectTextPayloadAsync(
            "getexecstate",
            "non-empty execution state text",
            cancellationToken);
    }

    public async Task<uint> GetBreakpointTypeAsync(uint address, CancellationToken cancellationToken) {
        string command = $"isbreak addr=0x{address:X8}";
        XbdmResponse response = await SendCommandAsync(command, cancellationToken);
        string payload = response.ExpectMessage(command, 200, XbdmResponseType.SingleResponse);
        if (!XbdmParamUtils.TryGetUInt32(payload, "type", out uint breakpointType)) {
            throw XbdmProtocolViolationException.ForInvalidBody(
                command,
                response,
                new[] { 200 },
                XbdmResponseType.SingleResponse,
                "parseable type=DWORD breakpoint metadata");
        }

        return breakpointType;
    }

    public Task SuspendThreadAsync(uint threadId, CancellationToken cancellationToken) {
        return SendCommandExpectOkAsync($"suspend thread=0x{threadId:X8}", cancellationToken);
    }

    public Task ResumeThreadAsync(uint threadId, CancellationToken cancellationToken) {
        return SendCommandExpectOkAsync($"resume thread=0x{threadId:X8}", cancellationToken);
    }

    public Task MoveAsync(string oldPath, string newPath, CancellationToken cancellationToken) {
        oldPath = ValidateCommandPath(oldPath, nameof(oldPath));
        newPath = ValidateCommandPath(newPath, nameof(newPath));
        return SendCommandExpectOkAsync($"rename name=\"{oldPath}\" newname=\"{newPath}\"", cancellationToken);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) {
        path = ValidateCommandPath(path, nameof(path));
        return SendCommandExpectOkAsync($"mkdir name=\"{path}\"", cancellationToken);
    }

    public async Task DownloadFileAsync(string remotePath, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) {
        remotePath = ValidateCommandPath(remotePath, nameof(remotePath));
        string command = $"getfile name=\"{remotePath}\"";
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync(command, cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            response.Expect(command, (int) XbdmResponseType.BinaryResponse, XbdmResponseType.BinaryResponse);

            int length = await reader.ReadInt32LEAsync(cancellationToken);
            if (length <= 0) {
                throw XbdmProtocolViolationException.ForInvalidBody(
                    command,
                    response,
                    new[] { (int) XbdmResponseType.BinaryResponse },
                    XbdmResponseType.BinaryResponse,
                    "positive binary length prefix");
            }

            await ReadBinaryToStreamAsync(destination, length, progress, cancellationToken);
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task UploadFileAsync(string remotePath, Stream source, long length, IProgress<long>? progress, CancellationToken cancellationToken) {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        remotePath = ValidateCommandPath(remotePath, nameof(remotePath));
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
        if (length == 0)
            return;

        if (length <= 0x4000) {
            try {
                byte[] legacy = await ReadMemoryLegacyBytesAsync(address, checked((int) length), cancellationToken);
                if (legacy.Length == length) {
                    await destination.WriteAsync(legacy, cancellationToken);
                    progress?.Report(legacy.Length);
                    return;
                }
            }
            catch (IOException ex) when (ex is not XbdmProtocolViolationException) {
                // Fall back to getmemex streaming when legacy getmem is unavailable.
            }
        }

        await ReadMemoryTransportAsync(address, length, destination, progress, cancellationToken);
    }

    private async Task ReadMemoryTransportAsync(uint address, uint length, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync($"getmemex addr=0x{address:X8} length=0x{length:X8}", cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType != XbdmResponseType.BinaryResponse && response.StatusCode != (int) XbdmResponseType.BinaryResponse) {
                throw new IOException($"getmemex failed: {response.RawMessage}");
            }

            await ReadChunkedBinaryToStreamAsync($"getmemex addr=0x{address:X8} length=0x{length:X8}", response, destination, length, progress, cancellationToken);
        }
        finally {
            ioLock.Release();
        }
    }

    public Task<byte[]> ReadMemoryBytesAsync(uint address, int length, CancellationToken cancellationToken) {
        return ReadMemoryBytesReliableAsync(address, length, cancellationToken);
    }

    private async Task<byte[]> ReadMemoryBytesTransportAsync(uint address, int length, CancellationToken cancellationToken) {
        using MemoryStream ms = new MemoryStream(length);
        await ReadMemoryTransportAsync(address, (uint) length, ms, null, cancellationToken);
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
            catch (IOException ex) when (ex is not XbdmProtocolViolationException) {
                // Fall back to getmemex for targets that do not support legacy getmem reliably.
            }
        }

        byte[] data = await ReadMemoryBytesTransportAsync(address, length, cancellationToken);
        if (length > 0x4000 || data.Length != length || data.Any(static b => b != 0))
            return data;

        try {
            byte[] legacy = await ReadMemoryLegacyBytesAsync(address, length, cancellationToken);
            if (legacy.Length == length)
                return legacy;
        }
        catch (IOException ex) when (ex is not XbdmProtocolViolationException) {
            throw new IOException(
                $"getmemex returned an all-zero {length} byte buffer at 0x{address:X8}, and legacy getmem validation failed; refusing ambiguous memory read success.",
                ex);
        }

        throw new IOException(
            $"getmemex returned an all-zero {length} byte buffer at 0x{address:X8}, and legacy getmem validation returned a different length; refusing ambiguous memory read success.");
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
                    response.ExpectBinaryLength("screenshot framebuffer", parsed.FramebufferSize, data.LongLength);
                    return new XbdmScreenshot(parsed, data);
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
        (XbdmResponse memoryResponse, IReadOnlyList<string> memoryLines) = await SendCommandForLinesWithResponseAsync("walkmem", cancellationToken);
        IReadOnlyList<string> lines = memoryResponse.ExpectLines(
            "walkmem",
            memoryLines,
            "zero or more memory region lines with base and size fields",
            validator: LinesAreMemoryRegionRecords);
        List<XbdmMemoryRegion> regions = new List<XbdmMemoryRegion>(lines.Count);
        foreach (string line in lines) {
            if (TryParseMemoryRegionLine(line, out XbdmMemoryRegion region))
                regions.Add(region);
        }
        return regions;
    }

    public async Task<IReadOnlyList<XbdmThreadInfo>> GetThreadsAsync(bool includeNames, CancellationToken cancellationToken) {
        (XbdmResponse threadsResponse, IReadOnlyList<string> threadLines) = await SendCommandForLinesWithResponseAsync("threads", cancellationToken);
        IReadOnlyList<string> lines = threadsResponse.ExpectLines(
            "threads",
            threadLines,
            "zero or more thread ID lines",
            validator: LinesAreThreadIdRecords);
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
                    catch (IOException ex) when (ex is not XbdmProtocolViolationException) {
                        allowNames = false;
                    }
                }
            }

            threads.Add(info);
        }

        return threads;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetThreadContextAsync(uint threadId, CancellationToken cancellationToken) {
        string command = $"getcontext thread=0x{threadId:X8} control int fp";
        (XbdmResponse response, IReadOnlyList<string> rawLines) = await SendCommandForLinesWithResponseAsync(command, cancellationToken);
        IReadOnlyList<string> lines = response.ExpectLines(
            command,
            rawLines,
            "one or more register=value context lines",
            minimumLineCount: 1,
            validator: LinesAreThreadContextRecords);
        Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines) {
            int split = line.IndexOf('=');
            string name = line.Substring(0, split).Trim();
            string value = line.Substring(split + 1).Trim();
            dict[name] = value;
        }
        return dict;
    }

    public Task SetThreadContextAsync(uint threadId, string registerName, ulong value, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(registerName))
            throw new ArgumentException("Register name cannot be empty.", nameof(registerName));

        string normalizedRegisterName = registerName.Trim();
        if (!IsAsciiAlphaNumeric(normalizedRegisterName))
            throw new ArgumentException("Register name can only contain ASCII letters and numbers.", nameof(registerName));

        string formattedValue = value <= uint.MaxValue ? $"0x{value:X8}" : $"0x{value:X16}";
        return SendCommandExpectOkAsync($"setcontext thread=0x{threadId:X8} {normalizedRegisterName}={formattedValue}", cancellationToken);
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
                EnsureOkResponse(response, "setmem");

                offset += writeCount;
            }
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task WriteMemoryVerifiedAsync(uint address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) {
        if (data.Length == 0)
            return;

        await WriteMemoryAsync(address, data, cancellationToken);
        byte[] verified = await ReadMemoryBytesReliableAsync(address, data.Length, cancellationToken);
        EnsureMemoryMatches(address, data.Span, verified);
    }

    public async Task<(XbdmResponse Response, IReadOnlyList<string>? Lines)> SendRawAsync(string command, CancellationToken cancellationToken) {
        await ioLock.WaitAsync(cancellationToken);
        try {
            await WriteLineAsync(command, cancellationToken);
            XbdmResponse response = await ReadResponseAsync(cancellationToken);
            if (response.ResponseType == XbdmResponseType.MultiResponse || response.StatusCode == (int) XbdmResponseType.MultiResponse) {
                IReadOnlyList<string> lines = await ReadMultiLinesAsync(command, response, cancellationToken);
                return (response, lines);
            }

            return (response, null);
        }
        finally {
            ioLock.Release();
        }
    }

    public async Task<XbdmResponse> SendCommandExpectOkAsync(string command, CancellationToken cancellationToken) {
        XbdmResponse response = await SendCommandAsync(command, cancellationToken);
        EnsureOkResponse(response, command);
        return response;
    }

    private async Task<string> SendCommandExpectTextPayloadAsync(
        string command,
        string expectedBodyDescription,
        CancellationToken cancellationToken) {
        (XbdmResponse response, IReadOnlyList<string>? lines) = await SendCommandMaybeLinesAsync(command, cancellationToken);
        if (lines != null) {
            IReadOnlyList<string> payloadLines = response.ExpectLines(
                command,
                lines,
                expectedBodyDescription,
                minimumLineCount: 1,
                validator: LinesContainNonEmptyText);
            return string.Join("\n", payloadLines).Trim();
        }

        return response.ExpectMessage(command, 200, XbdmResponseType.SingleResponse);
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

    private static void EnsureOkResponse(XbdmResponse response, string operation) {
        if (response.StatusCode != 200)
            throw new IOException($"{operation} failed: {response.RawMessage}");
    }

    private static bool IsStoppedExecutionState(string value) {
        return value.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("stopped", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("break", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRunningExecutionState(string value) {
        return value.Trim().Equals("start", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("running", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureMemoryMatches(uint address, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
        if (actual.Length != expected.Length) {
            throw new IOException($"memory verification failed at 0x{address:X8}: expected {expected.Length} byte(s), read back {actual.Length}.");
        }

        for (int i = 0; i < expected.Length; i++) {
            if (expected[i] == actual[i])
                continue;

            throw new IOException(
                $"memory verification failed at 0x{address + (uint) i:X8}: expected 0x{expected[i]:X2}, got 0x{actual[i]:X2}.");
        }
    }

    private static bool IsAsciiAlphaNumeric(string value) {
        foreach (char c in value) {
            bool isAsciiLetter = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            bool isAsciiNumber = c is >= '0' and <= '9';
            if (!isAsciiLetter && !isAsciiNumber)
                return false;
        }

        return value.Length > 0;
    }

    private static string ValidateCommandPath(string value, string paramName) {
        if (value is null)
            throw new ArgumentNullException(paramName);

        foreach (char c in value) {
            if (c == '"' || char.IsControl(c))
                throw new ArgumentException("Path cannot contain quotes or control characters.", paramName);
        }

        return NormalizeCommandPath(value);
    }

    private static string NormalizeCommandPath(string value) {
        string normalized = value.Replace('/', '\\');
        while (normalized.Contains("\\\\", StringComparison.Ordinal)) {
            normalized = normalized.Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (normalized.StartsWith("\\Device", StringComparison.OrdinalIgnoreCase))
            return normalized;

        if (normalized.StartsWith("\\", StringComparison.Ordinal)) {
            string remainder = normalized[1..];
            if (remainder.Length == 0)
                return normalized;

            int separator = remainder.IndexOf('\\');
            string root = separator >= 0 ? remainder[..separator] : remainder;
            if (LooksLikeDriveRoot(root)) {
                if (separator < 0)
                    return root + ":";

                return root + ":" + remainder[separator..];
            }
        }

        int colon = normalized.IndexOf(':');
        if (colon > 0) {
            string root = normalized[..colon];
            string remainder = normalized[(colon + 1)..].TrimStart('\\');
            if (remainder.Length == 0)
                return root + ":";

            return root + @":\" + remainder;
        }

        return normalized;
    }

    private static bool LooksLikeDriveRoot(string value) {
        if (value.Length == 0)
            return false;

        foreach (char c in value) {
            if (!char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }

    private static string NormalizeDeleteCommandPath(string value, string paramName) {
        string normalized = ValidateCommandPath(value, paramName);
        if (!normalized.EndsWith("\\", StringComparison.Ordinal))
            return normalized;

        string trimmed = normalized.TrimEnd('\\');
        if (trimmed.EndsWith(":", StringComparison.Ordinal))
            return trimmed + "\\";

        return trimmed;
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
        if (line.Length < 3)
            return false;
        if (!char.IsDigit(line[0]) || !char.IsDigit(line[1]) || !char.IsDigit(line[2]))
            return false;
        if (line.Length == 3)
            return true;
        return line[3] == '-' || line[3] == ' ';
    }

    private async Task<IReadOnlyList<string>> ReadMultiLinesAsync(string command, XbdmResponse response, CancellationToken cancellationToken) {
        List<string> lines = new List<string>();
        int totalBytes = 0;
        int deadlineMs = GetMultiResponseDeadlineMs();
        using CancellationTokenSource deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCts.CancelAfter(deadlineMs);

        try {
            while (true) {
                string line = await reader.ReadLineAsync(deadlineCts.Token);
                if (line == ".")
                    break;

                if (lines.Count >= MaxMultiResponseLines) {
                    throw CreateMultiResponseViolation(
                        command,
                        response,
                        $"terminated multi-response body within {MaxMultiResponseLines} lines");
                }

                int lineBytes = Ascii.GetByteCount(line) + 2;
                if (totalBytes > MaxMultiResponseBytes - lineBytes) {
                    throw CreateMultiResponseViolation(
                        command,
                        response,
                        $"terminated multi-response body within {MaxMultiResponseBytes} bytes");
                }

                totalBytes += lineBytes;
                lines.Add(line);
            }
        }
        catch (OperationCanceledException) when (deadlineCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            throw CreateMultiResponseViolation(
                command,
                response,
                $"terminated multi-response body within {deadlineMs} ms");
        }
        catch (TimeoutException) {
            throw CreateMultiResponseViolation(
                command,
                response,
                $"terminated multi-response body before the XBDM read timeout expired");
        }

        return lines;
    }

    private int GetMultiResponseDeadlineMs() {
        long deadline = (long) timeoutMs * 4L;
        if (deadline < MinMultiResponseDeadlineMs)
            return MinMultiResponseDeadlineMs;
        if (deadline > MaxMultiResponseDeadlineMs)
            return MaxMultiResponseDeadlineMs;
        return (int) deadline;
    }

    private static XbdmProtocolViolationException CreateMultiResponseViolation(
        string command,
        XbdmResponse response,
        string expectedBodyDescription) {
        return XbdmProtocolViolationException.ForInvalidBody(
            command,
            response,
            new[] { (int) XbdmResponseType.MultiResponse },
            XbdmResponseType.MultiResponse,
            expectedBodyDescription);
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

        uint expectedFramebufferSize = parsed.FramebufferSize;
        if (expectedFramebufferSize == 0) {
            ulong computedSize = (ulong) parsed.Pitch * parsed.Height;
            if (computedSize == 0 || computedSize > int.MaxValue)
                return false;
            expectedFramebufferSize = (uint) computedSize;
            parsed = parsed with { FramebufferSize = expectedFramebufferSize };
        }

        if ((long) expectedFramebufferSize + 28 == data.LongLength) {
            info = parsed;
            dataOffset = 28;
            return true;
        }

        if (expectedFramebufferSize == data.LongLength) {
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

    private async Task ReadChunkedBinaryToStreamAsync(
        string command,
        XbdmResponse response,
        Stream destination,
        uint length,
        IProgress<long>? progress,
        CancellationToken cancellationToken) {
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
                    if (chunkSize > remaining)
                        throw new IOException("XBDM returned more bytes than expected.");

                    int zeroCount = chunkSize;
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

    private static bool LinesAreModuleRecords(IReadOnlyList<string> lines) {
        foreach (string line in lines) {
            if (!TryParseModuleLine(line, out _, out _, out _, out _, out _))
                return false;
        }

        return true;
    }

    private static bool TryParseModuleLine(
        string line,
        out string name,
        out uint moduleBase,
        out uint moduleSize,
        out uint moduleOriginalSize,
        out uint timestamp) {
        name = string.Empty;
        moduleBase = 0;
        moduleSize = 0;
        moduleOriginalSize = 0;
        timestamp = 0;

        if (!XbdmParamUtils.TryGetString(line, "name", out string? parsedName) ||
            string.IsNullOrWhiteSpace(parsedName) ||
            !XbdmParamUtils.TryGetUInt32(line, "base", out moduleBase) ||
            !XbdmParamUtils.TryGetUInt32(line, "size", out moduleSize)) {
            return false;
        }

        name = parsedName;
        XbdmParamUtils.TryGetUInt32(line, "osize", out moduleOriginalSize);
        XbdmParamUtils.TryGetUInt32(line, "timestamp", out timestamp);
        return true;
    }

    private static bool LinesAreDriveRecords(IReadOnlyList<string> lines) {
        foreach (string line in lines) {
            if (!XbdmParamUtils.TryGetString(line, "drivename", out string? drive) ||
                string.IsNullOrWhiteSpace(drive)) {
                return false;
            }
        }

        return true;
    }

    private static bool LinesAreDirectoryRecords(IReadOnlyList<string> lines) {
        foreach (string line in lines) {
            if (!XbdmParamUtils.TryGetString(line, "name", out string? name) ||
                string.IsNullOrWhiteSpace(name)) {
                return false;
            }
        }

        return true;
    }

    private static bool LinesAreUserRecords(IReadOnlyList<string> lines) {
        foreach (string line in lines) {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            if (XbdmParamUtils.TryGetString(line, "name", out string? name) && !string.IsNullOrWhiteSpace(name))
                continue;
            if (XbdmParamUtils.TryGetString(line, "gamertag", out string? gamertag) && !string.IsNullOrWhiteSpace(gamertag))
                continue;
            if (XbdmParamUtils.TryGetString(line, "gtag", out string? gtag) && !string.IsNullOrWhiteSpace(gtag))
                continue;
            if (XbdmParamUtils.TryGetUInt64(line, "xuid", out _))
                continue;
            if (TryGetSignInState(line).HasValue)
                continue;

            return false;
        }

        return true;
    }

    private static bool LinesAreThreadContextRecords(IReadOnlyList<string> lines) {
        foreach (string line in lines) {
            int split = line.IndexOf('=');
            if (split <= 0)
                return false;

            string name = line.Substring(0, split).Trim();
            string value = line.Substring(split + 1).Trim();
            if (name.Length == 0 || value.Length == 0)
                return false;
        }

        return true;
    }

    private static bool LinesContainNonEmptyText(IReadOnlyList<string> lines) {
        foreach (string line in lines) {
            if (!string.IsNullOrWhiteSpace(line))
                return true;
        }

        return false;
    }

    private static bool LinesAreMemoryRegionRecords(IReadOnlyList<string> lines) {
        foreach (string line in lines) {
            if (!TryParseMemoryRegionLine(line, out _))
                return false;
        }

        return true;
    }

    private static bool TryParseMemoryRegionLine(string line, out XbdmMemoryRegion region) {
        region = new XbdmMemoryRegion();
        if (!XbdmParamUtils.TryGetUInt32(line, "base", out uint baseAddr) ||
            !XbdmParamUtils.TryGetUInt32(line, "size", out uint size)) {
            return false;
        }

        XbdmParamUtils.TryGetUInt32(line, "protect", out uint protect);
        XbdmParamUtils.TryGetUInt32(line, "phys", out uint phys);
        region = new XbdmMemoryRegion {
            BaseAddress = baseAddr,
            Size = size,
            Protect = protect,
            Phys = phys
        };
        return true;
    }

    private static bool LinesAreThreadIdRecords(IReadOnlyList<string> lines) {
        foreach (string line in lines) {
            if (!TryParseThreadId(line, out _))
                return false;
        }

        return true;
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
        XbdmParamUtils.TryGetUInt32(text, "start", out uint startAddress);
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
            StartAddress = startAddress,
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
        catch (IOException ex) when (ex is not XbdmProtocolViolationException) {
            bytes = await ReadMemoryBytesTransportAsync(address, length, cancellationToken);
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
            response.ExpectBinaryLength(
                $"getmem addr=0x{address:X8} length=0x{length:X8}",
                length,
                reported,
                "binary length prefix matching requested memory length");

            byte[] data = new byte[reported];
            await reader.ReadExactAsync(data, 0, reported, cancellationToken);
            return data;
        }
        finally {
            ioLock.Release();
        }
    }
}
