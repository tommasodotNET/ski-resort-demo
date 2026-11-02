using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AlpineAI.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AlpineAI.Tests;

public sealed class VoiceConnectionTests
{
    [Fact]
    public async Task StreamsPcmAndTranscriptsOverRealWebsocketAndReleasesDevices()
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = CreateServer();
        server.Map("/ws/voice/skill", async context =>
        {
            Assert.Equal("conversation-1", context.Request.Query["conversationId"]);
            using var socket = (await context.WebSockets.AcceptWebSocketAsync());
            await Send(socket, """{"type":"ready","conversationId":"conversation-1"}""");
            var buffer = new byte[8192];
            using var frame = new MemoryStream();
            WebSocketReceiveResult message;
            do
            {
                message = await socket.ReceiveAsync(buffer, context.RequestAborted);
                frame.Write(buffer, 0, message.Count);
            } while (!message.EndOfMessage);
            using var document = JsonDocument.Parse(frame.ToArray());
            received.TrySetResult(document.RootElement.Clone());
            await Send(socket, """{"type":"audio","data":"AAABAA=="}""");
            await Send(socket, """{"type":"transcript","role":"assistant","text":"Welcome","final_":true}""");
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Finished", context.RequestAborted);
        });
        await server.StartAsync();
        var audio = new Audio();
        await using var voice = new VoiceSession(audio);
        var transcript = new TaskCompletionSource<VoiceTranscript>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<string>();
        voice.Transcript += value => transcript.TrySetResult(value);
        voice.Error += errors.Add;
        voice.Status += value => { if (value == "Disconnected") disconnected.TrySetResult(); };
        voice.Start(new Uri(server.Urls.Single()), AdvisorArchitecture.Skill, "conversation-1");
        var frameReceived = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("audio", frameReceived.GetProperty("type").GetString());
        Assert.Equal(audio.Frame, Convert.FromBase64String(frameReceived.GetProperty("data").GetString()!));
        Assert.Equal("Welcome", (await transcript.Task.WaitAsync(TimeSpan.FromSeconds(10))).Text);
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, Assert.Single(audio.Played));
        Assert.True(audio.Started && audio.Disposed);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AudioFailureCancelsConnectionAndReleasesDevices(bool failOnStart)
    {
        await using var server = CreateServer();
        server.Map("/ws/voice/a2a", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            try
            {
                await Send(socket, """{"type":"ready"}""");
                await socket.ReceiveAsync(new byte[8192], context.RequestAborted);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        });
        await server.StartAsync();
        var audio = new Audio { FailOnStart = failOnStart, FailOnCapture = !failOnStart };
        await using var voice = new VoiceSession(audio);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        voice.Error += value => error.TrySetResult(value);
        voice.Status += value => { if (value == "Disconnected") disconnected.TrySetResult(); };
        voice.Start(new Uri(server.Urls.Single()), AdvisorArchitecture.A2A, null);
        Assert.Contains("Audio failed", await error.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(audio.Disposed);
    }

    [Fact]
    public async Task StopDuringConnectionReleasesAudio()
    {
        await using var server = CreateServer();
        server.Map("/ws/voice/a2a", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            try { await socket.ReceiveAsync(new byte[8192], context.RequestAborted); }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        });
        await server.StartAsync();
        var audio = new Audio();
        var voice = new VoiceSession(audio);
        voice.Start(new Uri(server.Urls.Single()), AdvisorArchitecture.A2A, null);
        await voice.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(audio.Disposed);
    }

    private static WebApplication CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var server = builder.Build();
        server.UseWebSockets();
        return server;
    }

    private static Task Send(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    private sealed class Audio : IVoiceAudio
    {
        public readonly byte[] Frame = new byte[2400];
        public List<byte[]> Played { get; } = [];
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailOnStart { get; init; }
        public bool FailOnCapture { get; init; }
        public void Start()
        {
            if (FailOnStart) throw new IOException("Audio failed to start");
            Started = true;
        }
        public void Play(byte[] pcm) => Played.Add(pcm);
        public void Clear() => Played.Clear();
        public void Dispose() => Disposed = true;
        public async IAsyncEnumerable<byte[]> CaptureAsync([EnumeratorCancellation] CancellationToken token)
        {
            if (FailOnCapture) throw new IOException("Audio failed during capture");
            yield return Frame;
            await Task.Delay(Timeout.Infinite, token);
        }
    }
}
