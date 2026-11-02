using System.Runtime.CompilerServices;
using System.Text.Json;
using AlpineAI.Core;
using Xunit;

namespace AlpineAI.Tests;

public sealed class VoiceTests
{
    [Theory]
    [InlineData("https://resort.example", AdvisorArchitecture.Skill, "wss://resort.example/ws/voice/skill")]
    [InlineData("http://localhost:8000", AdvisorArchitecture.A2A, "ws://localhost:8000/ws/voice/a2a")]
    public void UsesMatchingArchitectureAndSecureWebsocket(string origin, AdvisorArchitecture architecture, string expected) =>
        Assert.Equal(expected, VoiceSession.CreateUri(new Uri(origin), architecture, null).AbsoluteUri);

    [Fact]
    public void EscapesConversationId() =>
        Assert.Equal("?conversationId=a%26b%3Dc", VoiceSession.CreateUri(new Uri("http://localhost"),
            AdvisorArchitecture.A2A, "a&b=c").Query);

    [Fact]
    public async Task HandlesEveryBridgeMessageAndBothFinalFlags()
    {
        var audio = new Audio();
        await using var voice = new VoiceSession(audio);
        var transcripts = new List<VoiceTranscript>();
        string? status = null;
        string? conversation = null;
        var cleared = false;
        voice.Status += value => status = value;
        voice.Conversation += value => conversation = value;
        voice.Transcript += transcripts.Add;
        voice.ClearAudio += () => cleared = true;
        Message(voice, """{"type":"ready","conversationId":"c1"}""");
        Assert.Equal("Voice active", status);
        Assert.Equal("c1", conversation);
        Message(voice, """{"type":"audio","data":"AAABAA=="}""");
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, Assert.Single(audio.Played));
        Message(voice, """{"type":"transcript","role":"assistant","text":"Hel","final_":false}""");
        Message(voice, """{"type":"transcript","role":"assistant","text":"Hello","final_":true}""");
        Message(voice, """{"type":"transcript","role":"user","text":"Hi","final":true}""");
        Assert.False(transcripts[0].IsFinal);
        Assert.True(transcripts[1].IsFinal && transcripts[2].IsFinal);
        Message(voice, """{"type":"status","status":"function_calling"}""");
        Assert.Equal("Searching...", status);
        Message(voice, """{"type":"clear_audio"}""");
        Assert.True(cleared && audio.Cleared);
        var error = Assert.Throws<InvalidOperationException>(() => Message(voice, """{"type":"error","message":"Voice unavailable"}"""));
        Assert.Equal("Voice unavailable", error.Message);
    }

    [Fact]
    public async Task DisposalBeforeStartingReleasesAudio()
    {
        var audio = new Audio();
        await new VoiceSession(audio).DisposeAsync();
        Assert.True(audio.Disposed);
    }

    private static void Message(VoiceSession voice, string json)
    {
        using var document = JsonDocument.Parse(json);
        voice.HandleMessage(document.RootElement);
    }

    private sealed class Audio : IVoiceAudio
    {
        public List<byte[]> Played { get; } = [];
        public bool Cleared { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() { }
        public void Play(byte[] pcm) => Played.Add(pcm);
        public void Clear() => Cleared = true;
        public void Dispose() => Disposed = true;
        public async IAsyncEnumerable<byte[]> CaptureAsync([EnumeratorCancellation] CancellationToken token)
        {
            await Task.Delay(Timeout.Infinite, token);
            yield break;
        }
    }
}
