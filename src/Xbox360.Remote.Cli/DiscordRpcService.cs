using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Xbox360.Remote.Cli;

internal sealed class DiscordRpcService : IDisposable
{
	private sealed class RpcActivity
	{
		[JsonPropertyName("details")]
		public string? Details { get; init; }

		[JsonPropertyName("state")]
		public string? State { get; init; }

		[JsonPropertyName("timestamps")]
		public object? Timestamps { get; init; }
	}

	private sealed class RpcPresenceArgs
	{
		[JsonPropertyName("pid")]
		public int Pid { get; init; }

		[JsonPropertyName("activity")]
		public RpcActivity Activity { get; init; } = new RpcActivity();
	}

	private const int RpcIntervalMs = 45000;

	private const int PipeConnectTimeoutMs = 500;

	private const int MaxDetailsLength = 64;

	private const int MaxStateLength = 128;

	private readonly string clientId;

	private readonly System.Threading.Timer timer;

	private readonly object syncRoot = new object();

	private NamedPipeClientStream? pipe;

	private Stream? stream;

	private bool handshakeComplete;

	private bool disposed;

	private int tickInFlight;

	private long? connectedSinceUnixSeconds;

	private string? lastPresenceKey;

	private DiscordRpcService(string clientId)
	{
		this.clientId = clientId;
		RuntimePresenceState.Changed += HandlePresenceChanged;
		timer = new System.Threading.Timer(delegate
		{
			_ = TickAsync();
		}, null, Timeout.Infinite, Timeout.Infinite);
	}

	public static DiscordRpcService? CreateIfConfigured()
	{
		string? clientId = TrimOrNull(Environment.GetEnvironmentVariable("XECLI_DISCORD_CLIENT_ID"));
		if (string.IsNullOrWhiteSpace(clientId))
		{
			CliConfig.TryLoad(out CliConfig config);
			clientId = TrimOrNull(config.DiscordClientId);
		}
		if (string.IsNullOrWhiteSpace(clientId))
		{
			return null;
		}
		if (!clientId.All(char.IsDigit))
		{
			return null;
		}
		return new DiscordRpcService(clientId);
	}

	public void Start()
	{
		timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(RpcIntervalMs));
	}

	public void Dispose()
	{
		disposed = true;
		RuntimePresenceState.Changed -= HandlePresenceChanged;
		timer.Dispose();
		lock (syncRoot)
		{
			stream?.Dispose();
			stream = null;
			pipe?.Dispose();
			pipe = null;
			handshakeComplete = false;
		}
	}

	private async Task TickAsync()
	{
		if (disposed)
		{
			return;
		}
		if (Interlocked.Exchange(ref tickInFlight, 1) != 0)
		{
			return;
		}
		try
		{
			RuntimePresenceSnapshot snapshot = RuntimePresenceState.Current;
			if (!await EnsureConnectedAsync().ConfigureAwait(false))
			{
				return;
			}
			await SendPresenceAsync(snapshot).ConfigureAwait(false);
		}
		catch
		{
			ResetConnection();
		}
		finally
		{
			Interlocked.Exchange(ref tickInFlight, 0);
		}
	}

	private async Task<bool> EnsureConnectedAsync()
	{
		lock (syncRoot)
		{
			if (handshakeComplete && stream != null && pipe != null && pipe.IsConnected)
			{
				return true;
			}
		}
		ResetConnection();
		NamedPipeClientStream? namedPipeClientStream = null;
		for (int i = 0; i < 10; i++)
		{
			try
			{
				NamedPipeClientStream candidate = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
				using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(PipeConnectTimeoutMs);
				await candidate.ConnectAsync(cancellationTokenSource.Token).ConfigureAwait(false);
				namedPipeClientStream = candidate;
				break;
			}
			catch
			{
				namedPipeClientStream?.Dispose();
				namedPipeClientStream = null;
			}
		}
		if (namedPipeClientStream == null)
		{
			return false;
		}
		lock (syncRoot)
		{
			if (disposed)
			{
				namedPipeClientStream.Dispose();
				return false;
			}
			pipe = namedPipeClientStream;
			stream = namedPipeClientStream;
		}
		RpcFrame rpcFrame = await SendHandshakeAsync().ConfigureAwait(false);
		ValidateDiscordFrame(rpcFrame);
		lock (syncRoot)
		{
			handshakeComplete = true;
		}
		return true;
	}

	private async Task<RpcFrame> SendHandshakeAsync()
	{
		object rpcEnvelope = new
		{
			v = 1,
			client_id = clientId
		};
		await WriteFrameAsync(0, rpcEnvelope).ConfigureAwait(false);
		return await ReadFrameAsync().ConfigureAwait(false);
	}

	private async Task SendPresenceAsync(RuntimePresenceSnapshot snapshot)
	{
		string details = "XeCLI";
		string state = BuildPresenceState(snapshot);
		details = ClampText(details, MaxDetailsLength);
		state = ClampText(state, MaxStateLength);
		if (!snapshot.Connected)
		{
			connectedSinceUnixSeconds = null;
		}
		else if (!connectedSinceUnixSeconds.HasValue)
		{
			connectedSinceUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		}
		string text = (snapshot.Connected ? "1" : "0") + "|" + details + "|" + state + "|" + (connectedSinceUnixSeconds?.ToString() ?? "0");
		if (string.Equals(lastPresenceKey, text, StringComparison.Ordinal))
		{
			return;
		}
		RpcEnvelope rpcEnvelope = new RpcEnvelope
		{
			Cmd = "SET_ACTIVITY",
			Args = new RpcPresenceArgs
			{
				Pid = Process.GetCurrentProcess().Id,
				Activity = new RpcActivity
				{
					Details = details,
					State = state,
					Timestamps = snapshot.Connected && connectedSinceUnixSeconds.HasValue ? new { start = connectedSinceUnixSeconds.Value } : null
				}
			},
			Nonce = Guid.NewGuid().ToString("N")
		};
		await WriteFrameAsync(1, rpcEnvelope).ConfigureAwait(false);
		ValidateDiscordFrame(await ReadFrameAsync().ConfigureAwait(false));
		lastPresenceKey = text;
	}

	private sealed class RpcEnvelope
	{
		[JsonPropertyName("cmd")]
		public string? Cmd { get; init; }

		[JsonPropertyName("nonce")]
		public string? Nonce { get; init; }

		[JsonPropertyName("args")]
		public object? Args { get; init; }
	}

	private async Task WriteFrameAsync(int opcode, object payloadObject)
	{
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(payloadObject, new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		});
		byte[] header = new byte[8];
		Array.Copy(BitConverter.GetBytes(opcode), 0, header, 0, 4);
		Array.Copy(BitConverter.GetBytes(payload.Length), 0, header, 4, 4);
		Stream? localStream;
		lock (syncRoot)
		{
			localStream = stream;
		}
		if (localStream == null)
		{
			return;
		}
		await localStream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
		await localStream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
		await localStream.FlushAsync().ConfigureAwait(false);
	}

	private sealed class RpcFrame
	{
		public int Opcode { get; init; }

		public byte[] Payload { get; init; } = Array.Empty<byte>();

		public JsonDocument? Json { get; init; }
	}

	private async Task<RpcFrame> ReadFrameAsync()
	{
		Stream? localStream;
		lock (syncRoot)
		{
			localStream = stream;
		}
		if (localStream == null)
		{
			return new RpcFrame();
		}
		byte[] header = new byte[8];
		await ReadExactAsync(localStream, header, 0, header.Length).ConfigureAwait(false);
		int payloadLength = BitConverter.ToInt32(header, 4);
		if (payloadLength <= 0)
		{
			return new RpcFrame
			{
				Opcode = BitConverter.ToInt32(header, 0)
			};
		}
		byte[] payload = new byte[payloadLength];
		await ReadExactAsync(localStream, payload, 0, payloadLength).ConfigureAwait(false);
		return new RpcFrame
		{
			Opcode = BitConverter.ToInt32(header, 0),
			Payload = payload,
			Json = TryParseJson(payload)
		};
	}

	private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int length)
	{
		int read = 0;
		while (read < length)
		{
			int num = await stream.ReadAsync(buffer, offset + read, length - read).ConfigureAwait(false);
			if (num <= 0)
			{
				throw new EndOfStreamException();
			}
			read += num;
		}
	}

	private void ResetConnection()
	{
		lock (syncRoot)
		{
			handshakeComplete = false;
			stream?.Dispose();
			stream = null;
			pipe?.Dispose();
			pipe = null;
		}
		lastPresenceKey = null;
	}

	private void HandlePresenceChanged()
	{
		if (!disposed)
		{
			_ = TickAsync();
		}
	}

	private static JsonDocument? TryParseJson(byte[] payload)
	{
		try
		{
			return JsonDocument.Parse(payload);
		}
		catch
		{
			return null;
		}
	}

	private static void ValidateDiscordFrame(RpcFrame frame)
	{
		if (frame.Opcode == 2)
		{
			throw new IOException("Discord RPC closed the pipe.");
		}
		if (frame.Opcode != 1)
		{
			throw new IOException("Unexpected Discord RPC opcode " + frame.Opcode + ".");
		}
		if (frame.Json == null)
		{
			return;
		}
		JsonElement rootElement = frame.Json.RootElement;
		if (rootElement.TryGetProperty("evt", out JsonElement jsonElement) && string.Equals(jsonElement.GetString(), "ERROR", StringComparison.OrdinalIgnoreCase))
		{
			throw new IOException(ReadDiscordError(rootElement) ?? "Discord RPC rejected the payload.");
		}
		if (rootElement.TryGetProperty("data", out JsonElement jsonElement2) && jsonElement2.ValueKind == JsonValueKind.Object && jsonElement2.TryGetProperty("message", out JsonElement jsonElement3))
		{
			string? stringValue = jsonElement3.GetString();
			if (!string.IsNullOrWhiteSpace(stringValue) && rootElement.TryGetProperty("evt", out JsonElement jsonElement4) && string.Equals(jsonElement4.GetString(), "ERROR", StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException(stringValue);
			}
		}
	}

	private static string? ReadDiscordError(JsonElement rootElement)
	{
		if (!rootElement.TryGetProperty("data", out JsonElement jsonElement) || jsonElement.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		if (jsonElement.TryGetProperty("message", out JsonElement jsonElement2))
		{
			return jsonElement2.GetString();
		}
		return null;
	}

	private static string ClampText(string value, int maxLength)
	{
		string text = TrimOrNull(value) ?? string.Empty;
		if (text.Length <= maxLength)
		{
			return text;
		}
		return text.Substring(0, Math.Max(0, maxLength - 3)) + "...";
	}

	private static string BuildPresenceState(RuntimePresenceSnapshot snapshot)
	{
		if (!snapshot.Connected)
		{
			return "Not Connected To A Console";
		}
		string motherboard = NormalizePresenceValue(snapshot.Motherboard) ?? string.Empty;
		string dashboard = snapshot.DashboardVersion.HasValue ? snapshot.DashboardVersion.Value.ToString() : string.Empty;
		string titleSegment = NormalizePresenceValue(snapshot.TitleName) ?? string.Empty;
		string connectedSegment = string.IsNullOrWhiteSpace(motherboard) ? "Connected To RGH" : ("Connected To RGH " + motherboard);
		string[] fullSegments = new string[3]
		{
			connectedSegment,
			dashboard,
			titleSegment
		};
		string state = JoinPresenceSegments(fullSegments);
		if (state.Length <= MaxStateLength)
		{
			return state;
		}
		string[] compactSegments = string.IsNullOrWhiteSpace(dashboard) ? new string[1] { connectedSegment } : new string[2]
		{
			connectedSegment,
			dashboard
		};
		state = JoinPresenceSegments(compactSegments);
		if (state.Length <= MaxStateLength)
		{
			return state;
		}
		return connectedSegment;
	}

	private static string JoinPresenceSegments(params string[] segments)
	{
		return string.Join(" | ", segments.Where(static segment => !string.IsNullOrWhiteSpace(segment)));
	}

	private static string? NormalizePresenceValue(string? value)
	{
		string? text = TrimOrNull(value);
		if (text == null || string.Equals(text, "unknown", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "--", StringComparison.Ordinal))
		{
			return null;
		}
		return text;
	}

	private static string? TrimOrNull(string? value)
	{
		string? text = value?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return null;
	}
}
