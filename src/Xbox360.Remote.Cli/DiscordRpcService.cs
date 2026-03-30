using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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

	private readonly string clientId;

	private readonly System.Threading.Timer timer;

	private readonly object syncRoot = new object();

	private NamedPipeClientStream? pipe;

	private Stream? stream;

	private bool handshakeComplete;

	private bool disposed;

	private DiscordRpcService(string clientId)
	{
		this.clientId = clientId;
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
			clientId = TrimOrNull(CliConfig.Load().DiscordClientId);
		}
		if (string.IsNullOrWhiteSpace(clientId))
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
		await Task.Yield();
		lock (syncRoot)
		{
			if (handshakeComplete && stream != null && pipe != null && pipe.IsConnected)
			{
				return true;
			}
			ResetConnection();
			for (int i = 0; i < 10; i++)
			{
				try
				{
					NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
					namedPipeClientStream.Connect(200);
					pipe = namedPipeClientStream;
					stream = namedPipeClientStream;
					break;
				}
				catch
				{
				}
			}
			if (stream == null)
			{
				return false;
			}
		}
		await SendHandshakeAsync().ConfigureAwait(false);
		lock (syncRoot)
		{
			handshakeComplete = true;
		}
		return true;
	}

	private async Task SendHandshakeAsync()
	{
		object rpcEnvelope = new
		{
			v = 1,
			client_id = clientId
		};
		await WriteFrameAsync(0, rpcEnvelope).ConfigureAwait(false);
		await ReadFrameAsync().ConfigureAwait(false);
	}

	private async Task SendPresenceAsync(RuntimePresenceSnapshot snapshot)
	{
		string details = "XeCLI";
		string state = snapshot.Connected ? "Connected to RGH" : "Waiting for RGH";
		if (!string.IsNullOrWhiteSpace(snapshot.TitleName))
		{
			state = state + " · " + snapshot.TitleName;
		}
		if (!string.IsNullOrWhiteSpace(snapshot.Gamertag))
		{
			state = state + " · " + snapshot.Gamertag;
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
					Timestamps = snapshot.Connected ? new { start = DateTimeOffset.UtcNow.ToUnixTimeSeconds() } : null
				}
			},
			Nonce = Guid.NewGuid().ToString("N")
		};
		await WriteFrameAsync(1, rpcEnvelope).ConfigureAwait(false);
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

	private async Task ReadFrameAsync()
	{
		Stream? localStream;
		lock (syncRoot)
		{
			localStream = stream;
		}
		if (localStream == null)
		{
			return;
		}
		byte[] header = new byte[8];
		await ReadExactAsync(localStream, header, 0, header.Length).ConfigureAwait(false);
		int payloadLength = BitConverter.ToInt32(header, 4);
		if (payloadLength <= 0)
		{
			return;
		}
		byte[] payload = new byte[payloadLength];
		await ReadExactAsync(localStream, payload, 0, payloadLength).ConfigureAwait(false);
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
	}

	private static string? TrimOrNull(string? value)
	{
		string text = value?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return null;
	}
}
