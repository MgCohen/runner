using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace Listener.Infrastructure;

// The /listen socket. ReceiveAsync returns the next text message, or null when the socket closes.
public interface IInboxConnection : IAsyncDisposable {
    Task<string?> ReceiveAsync(CancellationToken ct);
}

public interface IInboxSocket {
    // Throws InboxRefusedException for 401 or 403.
    Task<IInboxConnection> ConnectAsync(CancellationToken ct);
}

public interface INetworkState {
    bool IsUp { get; }
}

public sealed class InboxRefusedException(string message) : Exception(message) {
    public InboxRefusedException() : this("The inbox refused the token.") { }

    public InboxRefusedException(string message, Exception inner) : this(message) => _ = inner;
}

public sealed class SystemNetwork : INetworkState {
    public bool IsUp => NetworkInterface.GetIsNetworkAvailable();
}

// The real socket: wss://<inbox>/listen?consumer=<name> with the bearer token on the upgrade request.
public sealed class WebSocketInbox(IOptions<ListenerOptions> options) : IInboxSocket {
    public static Uri ListenUri(ListenerOptions o) {
        var builder = new UriBuilder(o.BaseUrl) {
            Scheme = o.BaseUrl.Scheme == Uri.UriSchemeHttp ? "ws" : "wss",
            Path = o.BaseUrl.AbsolutePath.TrimEnd('/') + "/listen",
            Query = "consumer=" + Uri.EscapeDataString(ConsumerName.Resolve(o.Consumer)),
        };
        if (builder.Uri.IsDefaultPort) {
            builder.Port = -1;
        }
        return builder.Uri;
    }

    public async Task<IInboxConnection> ConnectAsync(CancellationToken ct) {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Token)) {
            throw new InboxRefusedException("The inbox token is missing. Set INBOX_TOKEN (op run).");
        }
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + o.Token);
        socket.Options.KeepAliveInterval = o.KeepAliveInterval;
        socket.Options.KeepAliveTimeout = o.KeepAliveTimeout;
        socket.Options.CollectHttpResponseDetails = true;
        try {
            await socket.ConnectAsync(ListenUri(o), ct);
        } catch (WebSocketException e) when (socket.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
            socket.Dispose();
            throw new InboxRefusedException("The inbox refused the token.", e);
        } catch {
            socket.Dispose();
            throw;
        }
        return new Connection(socket);
    }

    sealed class Connection(ClientWebSocket socket) : IInboxConnection {
        readonly byte[] buffer = new byte[16 * 1024];

        public async Task<string?> ReceiveAsync(CancellationToken ct) {
            using var message = new MemoryStream();
            while (socket.State == WebSocketState.Open) {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) {
                    return null;
                }
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) {
                    return result.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(message.ToArray()) : "";
                }
            }
            return null;
        }

        public ValueTask DisposeAsync() {
            socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
