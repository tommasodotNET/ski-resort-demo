using System.Net.WebSockets;
using System.Text.Json;

namespace AlpineAI.Core;

public interface IVoiceAudio : IDisposable
{
    IAsyncEnumerable<byte[]> CaptureAsync(CancellationToken token);
    void Start();
    void Play(byte[] pcm);
    void Clear();
}

public sealed record VoiceTranscript(string Role, string Text, bool IsFinal);

public sealed class VoiceSession(IVoiceAudio audio) : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? running;
    public event Action<string>? Status;
    public event Action<string>? Conversation;
    public event Action<VoiceTranscript>? Transcript;
    public event Action? ClearAudio;
    public event Action<string>? Error;

    public static Uri CreateUri(Uri origin, AdvisorArchitecture architecture, string? conversation)
    {
        var uri = new UriBuilder(new Uri(origin, $"ws/voice/{AdvisorClient.Route(architecture)}"))
        {
            Scheme = origin.Scheme == "https" ? "wss" : "ws",
            Query = conversation is null ? "" : $"conversationId={Uri.EscapeDataString(conversation)}"
        };
        return uri.Uri;
    }

    public void Start(Uri origin, AdvisorArchitecture architecture, string? conversation)
    {
        if (running is not null) throw new InvalidOperationException("Voice session already started.");
        running = RunAsync(CreateUri(origin, architecture, conversation));
    }

    private async Task RunAsync(Uri uri)
    {
        Task? sending = null;
        try
        {
            Status?.Invoke("Connecting...");
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(uri, connect.Token);
            }
            audio.Start();
            sending = SendAudioAsync(lifetime.Token);
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            ready.CancelAfter(TimeSpan.FromSeconds(35));
            var buffer = new byte[16384];
            var receivedReady = false;
            while (!lifetime.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ready.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (!receivedReady || result.CloseStatus != WebSocketCloseStatus.NormalClosure)
                            throw new IOException($"Voice service disconnected: {result.CloseStatusDescription ?? "session was not ready or closed unexpectedly"}");
                        return;
                    }
                    if (result.MessageType != WebSocketMessageType.Text)
                        throw new InvalidDataException("Voice server sent a non-text message.");
                    if (message.Length + result.Count > 4 * 1024 * 1024)
                        throw new InvalidDataException("Voice message exceeded 4 MiB.");
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                using var document = JsonDocument.Parse(message.ToArray());
                if (AdvisorClient.String(document.RootElement, "type") == "ready")
                {
                    receivedReady = true;
                    ready.CancelAfter(Timeout.InfiniteTimeSpan);
                }
                HandleMessage(document.RootElement);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is WebSocketException or IOException or JsonException or InvalidOperationException
            or OperationCanceledException or DllNotFoundException or EntryPointNotFoundException or FormatException)
        {
            Error?.Invoke(ex is OperationCanceledException ? "Voice connection or readiness timed out." : ex.Message);
        }
        finally
        {
            await lifetime.CancelAsync();
            socket.Abort();
            if (sending is not null) await sending;
            audio.Dispose();
            Status?.Invoke("Disconnected");
        }
    }

    private async Task SendAudioAsync(CancellationToken token)
    {
        try
        {
            await foreach (var pcm in audio.CaptureAsync(token))
            {
                var data = JsonSerializer.SerializeToUtf8Bytes(new { type = "audio", data = Convert.ToBase64String(pcm) });
                await socket.SendAsync(data, WebSocketMessageType.Text, true, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            if (!token.IsCancellationRequested) Error?.Invoke(ex.Message);
            await lifetime.CancelAsync();
        }
    }

    public void HandleMessage(JsonElement message)
    {
        switch (AdvisorClient.String(message, "type"))
        {
            case "ready":
                Status?.Invoke("Voice active");
                if (AdvisorClient.String(message, "conversationId") is { } id) Conversation?.Invoke(id);
                break;
            case "audio":
                audio.Play(Convert.FromBase64String(message.GetProperty("data").GetString()!));
                break;
            case "clear_audio":
                audio.Clear();
                ClearAudio?.Invoke();
                break;
            case "transcript":
                var final = AdvisorClient.Property(message, "final_");
                if (final.ValueKind == JsonValueKind.Undefined) final = AdvisorClient.Property(message, "final");
                Transcript?.Invoke(new(message.GetProperty("role").GetString()!, message.GetProperty("text").GetString()!,
                    final.ValueKind == JsonValueKind.True));
                break;
            case "status":
                Status?.Invoke(AdvisorClient.String(message, "status") switch
                {
                    "listening" => "Listening...",
                    "processing" => "Processing...",
                    "function_calling" => "Searching...",
                    "ready" => "Voice active",
                    var status => status ?? "Voice active"
                });
                break;
            case "error":
                throw new InvalidOperationException(AdvisorClient.String(message, "message") ?? "Voice service failed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        socket.Abort();
        if (running is not null) await running;
        else audio.Dispose();
        socket.Dispose();
        lifetime.Dispose();
    }
}
