using System.Globalization;
using System.Text;

namespace Xbox360.Remote;

public enum SensorType {
    CPU,
    GPU,
    EDRAM,
    MotherBoard
}

public enum RpcDataType : uint {
    Void = 0,
    Int = 1,
    String = 2,
    Float = 3,
    Byte = 4,
    IntArray = 5,
    FloatArray = 6,
    ByteArray = 7,
    Uint64 = 8,
    Uint64Array = 9
}

public enum ThreadType {
    System,
    Title
}

public enum RpcArgType {
    Int,
    UInt,
    Bool,
    Float,
    Double,
    String,
    Byte,
    UInt64,
    Int64,
    Bytes
}

public sealed record RpcArgument(RpcArgType Type, object Value);

public sealed class Jrpc2Client {
    public const uint MinimumTemperatureCelsius = 1;
    public const uint MaximumTemperatureCelsius = 125;

    private readonly XbdmClient xbdm;

    public Jrpc2Client(XbdmClient xbdm) {
        this.xbdm = xbdm;
    }

    public async Task<string> GetCpuKeyAsync(CancellationToken cancellationToken) {
        const string command = "consolefeatures ver=2 type=10 params=\"A\\0\\A\\0\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        return response.ExpectParsed<string>(command, 200, TryExtractCpuKey, "32 hexadecimal CPU key", XbdmResponseType.SingleResponse);
    }

    public async Task<uint> GetDashboardVersionAsync(CancellationToken cancellationToken) {
        const string command = "consolefeatures ver=2 type=13 params=\"A\\0\\A\\4\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        return response.ExpectParsed<uint>(command, 200, TryParseDecimalUInt32, "decimal dashboard version", XbdmResponseType.SingleResponse);
    }

    public async Task<uint> GetTemperatureAsync(SensorType sensorType, CancellationToken cancellationToken) {
        string command = $"consolefeatures ver=2 type=15 params=\"A\\0\\A\\1\\{(uint) RpcDataType.Int}\\{(int) sensorType}\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        uint temperature = response.ExpectParsed<uint>(command, 200, TryParseHexUInt32, "hexadecimal temperature value", XbdmResponseType.SingleResponse);
        if (temperature < MinimumTemperatureCelsius || temperature > MaximumTemperatureCelsius) {
            throw XbdmProtocolViolationException.ForInvalidBody(
                command,
                response,
                new[] { 200 },
                XbdmResponseType.SingleResponse,
                $"hexadecimal Celsius temperature from {MinimumTemperatureCelsius} through {MaximumTemperatureCelsius}");
        }

        return temperature;
    }

    public async Task<uint> GetTitleIdAsync(CancellationToken cancellationToken) {
        const string command = "consolefeatures ver=2 type=16 params=\"A\\0\\A\\0\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        return response.ExpectParsed<uint>(command, 200, TryParseHexUInt32, "hexadecimal title ID", XbdmResponseType.SingleResponse);
    }

    public async Task<string> GetMotherboardTypeAsync(CancellationToken cancellationToken) {
        const string command = "consolefeatures ver=2 type=17 params=\"A\\0\\A\\0\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        return NormalizeJrpcScalar(response.ExpectMessage(command, 200, XbdmResponseType.SingleResponse));
    }

    public async Task SetLedsAsync(int topLeft, int topRight, int bottomLeft, int bottomRight, CancellationToken cancellationToken) {
        string command = $"consolefeatures ver=2 type=14 params=\"A\\0\\A\\4\\" +
                         $"{(uint) RpcDataType.Int}\\{topLeft}\\" +
                         $"{(uint) RpcDataType.Int}\\{topRight}\\" +
                         $"{(uint) RpcDataType.Int}\\{bottomLeft}\\" +
                         $"{(uint) RpcDataType.Int}\\{bottomRight}\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        ExpectJrpcOptionalSuccess(command, response, "JRPC2 LED update failed");
    }

    public async Task ShowNotificationAsync(int logo, string? message, CancellationToken cancellationToken) {
        if (!NotificationTextPolicy.TryPrepareNotificationMessage(message, out string normalizedMessage, out string error))
            throw new ArgumentException(error, nameof(message));

        string msgHex = ConvertStringToHex(normalizedMessage);
        string command = $"consolefeatures ver=2 type=12 params=\"A\\0\\A\\2\\2/{normalizedMessage.Length}\\{msgHex}\\{(uint) RpcDataType.Int}\\{logo}\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        ExpectJrpcOptionalSuccess(command, response, "JRPC2 notification failed");
    }

    public async Task SetNotificationPositionAsync(int position, CancellationToken cancellationToken) {
        await DispatchAsync(
            RpcDataType.Void,
            null,
            "xam.xex",
            0x28C,
            false,
            false,
            new[] { new RpcArgument(RpcArgType.Int, position) },
            cancellationToken);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken) {
        const string command = "consolefeatures ver=2 type=11 params=\"A\\0\\A\\0\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        ExpectJrpcOptionalSuccess(command, response, "JRPC2 shutdown failed");
    }

    public async Task<uint> ResolveFunctionAsync(string moduleName, uint ordinal, CancellationToken cancellationToken) {
        string command = $"consolefeatures ver=2 type=9 params=\"A\\0\\A\\2\\" +
                         $"{(int) RpcDataType.String}/{moduleName.Length}\\" +
                         $"{ConvertStringToHex(moduleName)}\\" +
                         $"{(int) RpcDataType.Int}\\{ordinal}\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        if (response.StatusCode != 200 || response.ResponseType != XbdmResponseType.SingleResponse)
            throw new IOException($"JRPC2 resolve failed: {response.RawMessage}");

        string message = response.ExpectMessage(command, 200, XbdmResponseType.SingleResponse);

        if (!TryParseResolveAddress(message, out uint address))
            throw XbdmProtocolViolationException.ForInvalidBody(
                command,
                response,
                new[] { 200 },
                XbdmResponseType.SingleResponse,
                "hexadecimal resolved address");

        return address;
    }

    public Task<string> CallAsync(RpcDataType returnType, uint? address, string? module, int? ordinal, bool systemThread, bool vm, IReadOnlyList<RpcArgument> args, CancellationToken cancellationToken) {
        return CallAsync(returnType, address, module, ordinal, systemThread, vm, args, maxLoopCount: 10, loopDelayMs: 0, cancellationToken);
    }

    public async Task<string> CallAsync(
        RpcDataType returnType,
        uint? address,
        string? module,
        int? ordinal,
        bool systemThread,
        bool vm,
        IReadOnlyList<RpcArgument> args,
        int maxLoopCount,
        int loopDelayMs,
        CancellationToken cancellationToken) {
        uint argc = 0;
        string paramsText = CreateParams(vm, args, ref argc);
        string cmd = $"consolefeatures ver=2 type={(uint) returnType}" +
                     $"{(systemThread ? " system" : "")}" +
                     $"{(module != null ? $" module=\"{module}\" ord={ordinal}" : "")}" +
                     $"{(vm ? " VM" : "")} " +
                     $"as=0 params=\"A\\{(address ?? 0):X}\\A\\{argc}\\{paramsText}\"";

        string response = (await SendConsoleFeaturesLoopAsync(cmd, allowEmptyResponse: false, maxLoopCount, loopDelayMs, cancellationToken)).Trim();
        int split = response.IndexOf(' ');
        if (split <= 0) {
            if (returnType == RpcDataType.Void)
                return response;
            if (IsValidBareScalarResponse(returnType, response))
                return response;
            throw XbdmProtocolViolationException.ForInvalidBody(
                cmd,
                new XbdmResponse(200, XbdmResponseType.SingleResponse, response, response),
                new[] { 200 },
                XbdmResponseType.SingleResponse,
                "type-valid bare JRPC2 scalar or return value prefixed by a status token");
        }
        if (split == response.Length - 1) {
            throw XbdmProtocolViolationException.ForInvalidBody(
                cmd,
                new XbdmResponse(200, XbdmResponseType.SingleResponse, response, response),
                new[] { 200 },
                XbdmResponseType.SingleResponse,
                "JRPC2 return value after status token");
        }

        if (returnType == RpcDataType.Void)
            return response;

        return response.Substring(split + 1).Trim();
    }

    private static bool IsValidBareScalarResponse(RpcDataType returnType, string response) {
        ReadOnlySpan<char> value = response.AsSpan().Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value[2..];
        if (value.IsEmpty)
            return false;

        return returnType switch {
            RpcDataType.Int => uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _),
            RpcDataType.Byte => byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _),
            RpcDataType.Uint64 => ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _),
            RpcDataType.Float => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
                                 uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _),
            _ => false
        };
    }

    private async Task<string> SendConsoleFeaturesLoopAsync(string command, bool allowEmptyResponse, int maxLoopCount, int loopDelayMs, CancellationToken cancellationToken) {
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        string text = response.ExpectMessage(
            command,
            200,
            XbdmResponseType.SingleResponse,
            allowEmptyResponse ? XbdmResponseBodyRequirement.Optional : XbdmResponseBodyRequirement.RequiredNonEmpty);

        const string findText = "buf_addr=";
        for (int i = 0; text.Contains(findText, StringComparison.OrdinalIgnoreCase); i++) {
            if (loopDelayMs > 0)
                await Task.Delay(loopDelayMs, cancellationToken);
            int idx = text.IndexOf(findText, StringComparison.OrdinalIgnoreCase);
            if (!uint.TryParse(text.AsSpan(idx + findText.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint addr))
                break;
            string loopCommand = "consolefeatures ver=2 " + findText + "0x" + addr.ToString("X");
            XbdmResponse loop = await xbdm.SendCommandAsync(loopCommand, cancellationToken);
            text = loop.ExpectMessage(loopCommand, 200, XbdmResponseType.SingleResponse);
            if (i >= maxLoopCount)
                throw new IOException("JRPC2 error: potential infinite loop.");
        }

        return text;
    }

    public async Task DispatchAsync(
        RpcDataType returnType,
        uint? address,
        string? module,
        int? ordinal,
        bool systemThread,
        bool vm,
        IReadOnlyList<RpcArgument> args,
        CancellationToken cancellationToken) {
        uint argc = 0;
        string paramsText = CreateParams(vm, args, ref argc);
        string cmd = $"consolefeatures ver=2 type={(uint) returnType}" +
                     $"{(systemThread ? " system" : "")}" +
                     $"{(module != null ? $" module=\"{module}\" ord={ordinal}" : "")}" +
                     $"{(vm ? " VM" : "")} " +
                     $"as=0 params=\"A\\{(address ?? 0):X}\\A\\{argc}\\{paramsText}\"";

        XbdmResponse response = await xbdm.SendCommandAsync(cmd, cancellationToken);
        ExpectJrpcOptionalSuccess(cmd, response, "JRPC2 dispatch failed");
    }

    private static void ExpectJrpcOptionalSuccess(string command, XbdmResponse response, string failureMessage) {
        if (response.StatusCode != 200 || response.ResponseType != XbdmResponseType.SingleResponse)
            throw new IOException($"{failureMessage}: {response.RawMessage}");

        response.Expect(command, 200, XbdmResponseType.SingleResponse, XbdmResponseBodyRequirement.RequiredNonEmpty);
    }

    private static string CreateParams(bool vm, IReadOnlyList<RpcArgument> arguments, ref uint argc) {
        StringBuilder sb = new StringBuilder(128);
        foreach (RpcArgument arg in arguments) {
            switch (arg.Type) {
                case RpcArgType.Int:
                    sb.Append((uint) RpcDataType.Int).Append('\\').Append(((int) arg.Value).ToString(CultureInfo.InvariantCulture)).Append('\\');
                    argc++;
                    break;
                case RpcArgType.UInt:
                    sb.Append((uint) RpcDataType.Int).Append('\\').Append(((uint) arg.Value).ToString(CultureInfo.InvariantCulture)).Append('\\');
                    argc++;
                    break;
                case RpcArgType.Bool:
                    sb.Append((uint) RpcDataType.Int).Append('\\').Append(((bool) arg.Value) ? "1" : "0").Append('\\');
                    argc++;
                    break;
                case RpcArgType.Byte:
                    sb.Append((uint) RpcDataType.Byte).Append('\\').Append(((byte) arg.Value).ToString(CultureInfo.InvariantCulture)).Append('\\');
                    argc++;
                    break;
                case RpcArgType.Float:
                    sb.Append((uint) RpcDataType.Float).Append('\\').Append(((float) arg.Value).ToString(CultureInfo.InvariantCulture)).Append('\\');
                    argc++;
                    break;
                case RpcArgType.Double:
                    sb.Append((uint) RpcDataType.Float).Append('\\').Append(((double) arg.Value).ToString(CultureInfo.InvariantCulture)).Append('\\');
                    argc++;
                    break;
                case RpcArgType.String: {
                    string str = (string) arg.Value;
                    sb.Append((uint) RpcDataType.ByteArray).Append('/').Append(str.Length).Append('\\').Append(ConvertStringToHex(str)).Append('\\');
                    argc++;
                    break;
                }
                case RpcArgType.UInt64:
                    sb.Append((uint) RpcDataType.Uint64).Append('\\').Append(((ulong) arg.Value).ToString(CultureInfo.InvariantCulture)).Append('\\');
                    argc++;
                    break;
                case RpcArgType.Int64:
                    sb.Append((uint) RpcDataType.Uint64).Append('\\').Append(((long) arg.Value).ToString(CultureInfo.InvariantCulture)).Append('\\');
                    argc++;
                    break;
                case RpcArgType.Bytes: {
                    byte[] bytes = (byte[]) arg.Value;
                    sb.Append((uint) RpcDataType.ByteArray).Append('/').Append(bytes.Length).Append('\\').Append(ConvertToHex(bytes)).Append('\\');
                    argc++;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(arguments), "Unsupported RPC argument type.");
            }
        }

        return sb.ToString();
    }

    private static string ConvertStringToHex(string value) {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        return ConvertToHex(bytes);
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

    private static string ExtractHex(string text, int length) {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        StringBuilder sb = new StringBuilder(length);
        foreach (char ch in text) {
            bool isHex = (ch >= '0' && ch <= '9') ||
                         (ch >= 'a' && ch <= 'f') ||
                         (ch >= 'A' && ch <= 'F');
            if (!isHex)
                continue;
            sb.Append(ch);
            if (sb.Length == length)
                break;
        }

        return sb.ToString();
    }

    private static bool TryExtractCpuKey(string text, out string key) {
        key = ExtractHex(text, 32);
        return key.Length == 32;
    }

    private static bool TryParseDecimalUInt32(string text, out uint value) {
        return uint.TryParse(NormalizeJrpcScalar(text), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseHexUInt32(string text, out uint value) {
        return uint.TryParse(NormalizeJrpcScalar(text), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeJrpcScalar(string text) {
        return text.Trim();
    }

    private static bool TryParseResolveAddress(string text, out uint address) {
        address = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }
}
