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
    private readonly XbdmClient xbdm;

    public Jrpc2Client(XbdmClient xbdm) {
        this.xbdm = xbdm;
    }

    public async Task<string> GetCpuKeyAsync(CancellationToken cancellationToken) {
        const string command = "consolefeatures ver=2 type=10 params=\"A\\0\\A\\0\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        if (response.StatusCode != 200)
            throw new IOException("JRPC2 did not respond correctly.");
        string key = ExtractHex(response.Message, 32);
        if (key.Length != 32)
            throw new IOException("JRPC2 did not respond correctly.");
        return key;
    }

    public async Task<uint> GetDashboardVersionAsync(CancellationToken cancellationToken) {
        const string command = "consolefeatures ver=2 type=13 params=\"A\\0\\A\\4\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        if (response.StatusCode != 200 || !uint.TryParse(response.Message, out uint version))
            throw new IOException("JRPC2 did not respond correctly.");
        return version;
    }

    public async Task<uint> GetTemperatureAsync(SensorType sensorType, CancellationToken cancellationToken) {
        string command = $"consolefeatures ver=2 type=15 params=\"A\\0\\A\\1\\{(uint) RpcDataType.Int}\\{(int) sensorType}\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        if (response.StatusCode == 200 && uint.TryParse(response.Message, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint temp))
            return temp;
        throw new IOException("JRPC2 did not respond correctly.");
    }

    public async Task<uint> GetTitleIdAsync(CancellationToken cancellationToken) {
        const string command = "consolefeatures ver=2 type=16 params=\"A\\0\\A\\0\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        if (response.StatusCode == 200 && uint.TryParse(response.Message, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint tid))
            return tid;
        throw new IOException("JRPC2 did not respond correctly.");
    }

    public async Task<string> GetMotherboardTypeAsync(CancellationToken cancellationToken) {
        const string command = "consolefeatures ver=2 type=17 params=\"A\\0\\A\\0\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        if (response.StatusCode == 200)
            return response.Message;
        throw new IOException("JRPC2 did not respond correctly.");
    }

    public Task ShowNotificationAsync(int logo, string? message, CancellationToken cancellationToken) {
        string msgHex = message != null ? ConvertStringToHex(message) : "";
        string command = $"consolefeatures ver=2 type=12 params=\"A\\0\\A\\2\\2/{message?.Length ?? 0}\\{msgHex}\\{(uint) RpcDataType.Int}\\{logo}\\\"";
        return xbdm.SendCommandAsync(command, cancellationToken);
    }

    public async Task<uint> ResolveFunctionAsync(string moduleName, uint ordinal, CancellationToken cancellationToken) {
        string command = $"consolefeatures ver=2 type=9 params=\"A\\0\\A\\2\\" +
                         $"{(int) RpcDataType.String}/{moduleName.Length}\\" +
                         $"{ConvertStringToHex(moduleName)}\\" +
                         $"{(int) RpcDataType.Int}\\{ordinal}\\\"";
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        if (response.StatusCode != 200)
            return 0;
        int index = response.Message.IndexOf(' ');
        if (index == -1)
            return 0;
        if (!uint.TryParse(response.Message.AsSpan(index + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address))
            return 0;
        return address;
    }

    public async Task<string> CallAsync(RpcDataType returnType, uint? address, string? module, int? ordinal, bool systemThread, bool vm, IReadOnlyList<RpcArgument> args, CancellationToken cancellationToken) {
        uint argc = 0;
        string paramsText = CreateParams(vm, args, ref argc);
        string cmd = $"consolefeatures ver=2 type={(uint) returnType}" +
                     $"{(systemThread ? " system" : "")}" +
                     $"{(module != null ? $" module=\\\"{module}\\\" ord={ordinal}" : "")}" +
                     $"{(vm ? " VM" : "")} " +
                     $"as=0 params=\\\"A\\{(address ?? 0):X}\\A\\{argc}\\{paramsText}";

        string response = await SendConsoleFeaturesLoopAsync(cmd, cancellationToken);
        int split = response.IndexOf(' ');
        if (split <= 0)
            return response;
        return response.Substring(split + 1);
    }

    private async Task<string> SendConsoleFeaturesLoopAsync(string command, CancellationToken cancellationToken) {
        XbdmResponse response = await xbdm.SendCommandAsync(command, cancellationToken);
        if (response.StatusCode != 200)
            throw new IOException($"JRPC2 call failed: {response.RawMessage}");

        string text = response.Message;
        const string findText = "buf_addr=";
        for (int i = 0; text.Contains(findText, StringComparison.OrdinalIgnoreCase); i++) {
            int idx = text.IndexOf(findText, StringComparison.OrdinalIgnoreCase);
            if (!uint.TryParse(text.AsSpan(idx + findText.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint addr))
                break;
            XbdmResponse loop = await xbdm.SendCommandAsync("consolefeatures " + findText + "0x" + addr.ToString("X"), cancellationToken);
            if (loop.StatusCode != 200)
                throw new IOException($"JRPC2 loop failed: {loop.RawMessage}");
            text = loop.Message;
            if (i > 10)
                throw new IOException("JRPC2 error: potential infinite loop.");
        }

        return text;
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
}
