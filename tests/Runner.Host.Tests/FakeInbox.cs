using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Runner.Abstractions;

namespace Runner.Host.Tests;

/// <summary>
/// An in-process inbox with the contract A endpoints: <c>/listen</c>, <c>GET /api/events</c>
/// and <c>POST /api/consume</c>. It keeps the events and the consume calls in memory.
/// </summary>
public sealed class FakeInbox : IAsyncDisposable
{
    public const string Token = "test-token";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<long, InboxEvent> _events = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _consumed = new();
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private int _consumeFailures;

    public ConcurrentQueue<long[]> ConsumeCalls { get; } = new();
    public int CatchUpCalls => _catchUpCalls;
    public int ListenRequests => _listenRequests;
    public int OpenSockets => _sockets.Count;
    public Uri BaseUrl { get; private set; } = null!;

    private int _catchUpCalls;
    private int _listenRequests;

    private FakeInbox(WebApplication app) => _app = app;

    public static async Task<FakeInbox> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var inbox = new FakeInbox(app);
        inbox.Map(app);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        inbox.BaseUrl = new Uri(address);
        return inbox;
    }

    /// <summary>Makes the next <paramref name="count"/> consume calls return 500.</summary>
    public void FailConsume(int count) => Interlocked.Exchange(ref _consumeFailures, count);

    /// <summary>Stores an event. If <paramref name="push"/> is true, sends it to each open socket.</summary>
    public async Task AddAsync(InboxEvent e, bool push = true)
    {
        _events[e.Id] = e;
        if (push) await PushAsync(e);
    }

    /// <summary>Sends an event to each open socket, also if the event is not stored.</summary>
    public async Task PushAsync(InboxEvent e)
    {
        var json = JsonSerializer.Serialize(new { kind = "event", @event = e });
        foreach (var socket in _sockets.Values) await SendAsync(socket, json);
    }

    /// <summary>Aborts each open socket, as a network failure does.</summary>
    public void DropSockets()
    {
        foreach (var (key, socket) in _sockets)
        {
            socket.Abort();
            _sockets.TryRemove(key, out _);
        }
    }

    public bool IsConsumed(string consumer, long id) =>
        _consumed.TryGetValue(consumer, out var set) && set.ContainsKey(id);

    private void Map(WebApplication app)
    {
        app.UseWebSockets();

        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.Authorization != $"Bearer {Token}")
            {
                context.Response.StatusCode = 401;
                return;
            }
            await next();
        });

        app.Map("/listen", async context =>
        {
            Interlocked.Increment(ref _listenRequests);
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }
            var consumer = context.Request.Query["consumer"].ToString();
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var key = Guid.NewGuid();
            await SendAsync(socket, JsonSerializer.Serialize(new { kind = "hello", consumer }));
            _sockets[key] = socket;

            var buffer = new byte[1024];
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (Exception)
            {
                // The test aborts the socket.
            }
            finally
            {
                _sockets.TryRemove(key, out _);
            }
        });

        app.MapGet("/api/events", (HttpRequest request) =>
        {
            Interlocked.Increment(ref _catchUpCalls);
            var consumer = request.Query["consumer"].ToString();
            var after = long.TryParse(request.Query["after"], out var a) ? a : 0;
            var limit = int.TryParse(request.Query["limit"], out var l) ? l : 50;
            var consumed = _consumed.GetValueOrDefault(consumer);
            var events = _events.Values
                .Where(e => e.Id > after && (consumed is null || !consumed.ContainsKey(e.Id)))
                .OrderBy(e => e.Id)
                .Take(limit)
                .ToList();
            return Results.Json(new { events, next_after = events.Count > 0 ? events[^1].Id : after });
        });

        app.MapPost("/api/consume", async (HttpRequest request) =>
        {
            if (Interlocked.Decrement(ref _consumeFailures) >= 0) return Results.StatusCode(500);
            Interlocked.Exchange(ref _consumeFailures, 0);

            using var doc = await JsonDocument.ParseAsync(request.Body);
            var consumer = doc.RootElement.GetProperty("consumer").GetString()!;
            var ids = doc.RootElement.GetProperty("ids").EnumerateArray().Select(x => x.GetInt64()).ToArray();
            ConsumeCalls.Enqueue(ids);
            var set = _consumed.GetOrAdd(consumer, _ => new());
            foreach (var id in ids) set[id] = 0;
            return Results.Json(new { consumed = ids, already = Array.Empty<long>(), unknown = Array.Empty<long>() });
        });
    }

    private async Task SendAsync(WebSocket socket, string json)
    {
        await _sendGate.WaitAsync();
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        DropSockets();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
