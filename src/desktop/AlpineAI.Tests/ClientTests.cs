using System.Net;
using System.Text;
using System.Text.Json;
using AlpineAI.Core;
using Xunit;

namespace AlpineAI.Tests;

public sealed class ClientTests
{
    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://frontend.dev.localhost:8000")]
    [InlineData("https://resort.example")]
    public void AcceptsServerOrigins(string value) => Assert.NotNull(ResortClient.ParseServerUri(value));

    [Theory]
    [InlineData("http://resort.example")]
    [InlineData("https://user:password@resort.example")]
    [InlineData("file:///tmp/index.html")]
    [InlineData("https://resort.example/other")]
    [InlineData("https://resort.example/?key=value")]
    [InlineData("https://resort.example/#fragment")]
    [InlineData("")]
    public void RejectsInvalidOrigins(string value) => Assert.Throws<ArgumentException>(() => ResortClient.ParseServerUri(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingBackendDirectsUserToAspire(string? value)
    {
        var error = Assert.Throws<ArgumentException>(() => ResortClient.ParseServerUri(value));
        Assert.Contains("ALPINEAI_SERVER_URL", error.Message);
        Assert.Contains("Aspire", error.Message);
    }

    [Theory]
    [InlineData(0.19, "Low")]
    [InlineData(0.2, "Moderate")]
    [InlineData(0.4, "Considerable")]
    [InlineData(0.6, "High")]
    [InlineData(0.8, "Extreme")]
    public void RiskThresholdsMatchWeb(double risk, string expected) =>
        Assert.Equal(expected, new Safety(risk, []).RiskLevel);

    [Fact]
    public async Task ReadsAllDashboardContracts()
    {
        using var http = Client(new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/current-state/weather" => """{"temperature":-7.5,"wind_speed":12,"snow_intensity":1.2,"visibility":900}""",
            "/api/current-state/safety" => """{"avalanche_risk_index":0.45,"incident_reports":[{"incident_type":"Fall","location":"North","severity":"minor"}]}""",
            "/api/current-state/lifts" => """[{"lift_id":"l1","name":"Gondola","status":"open","queue_length":20,"wait_time_minutes":3.5,"serves_slopes":["s1"]}]""",
            "/api/current-state/slopes" => """{"slopes":[{"slope_id":"s1","name":"North","difficulty":"red","is_open":true,"snow_depth_cm":125,"groomed":true,"served_by_lift_id":"l1"}]}""",
            "/config.json" => """{"pollingIntervalMs":5000}""",
            _ => throw new InvalidOperationException("Unexpected route")
        }));
        var client = new ResortClient(http);
        Assert.Equal(-7.5, (await client.GetWeatherAsync(default)).Temperature);
        var safety = await client.GetSafetyAsync(default);
        Assert.Equal("Considerable", safety.RiskLevel);
        Assert.Equal("Fall", Assert.Single(safety.IncidentReports).IncidentType);
        var lift = Assert.Single(await client.GetLiftsAsync(default));
        Assert.Equal(3.5, lift.WaitTimeMinutes);
        var slope = Assert.Single(await client.GetSlopesAsync(default));
        Assert.Equal(lift.LiftId, slope.ServedByLiftId);
        Assert.Equal(125, slope.SnowDepthCm);
        Assert.True(slope.Groomed && slope.IsOpen);
        Assert.Equal(5000, (await client.GetConfigAsync(default)).PollingIntervalMs);
    }

    [Fact]
    public async Task SupportsWrappedLiftsAndBareSlopes()
    {
        using var http = Client(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("lifts")
            ? """{"lifts":[]}""" : "[]"));
        var client = new ResortClient(http);
        Assert.Empty(await client.GetLiftsAsync(default));
        Assert.Empty(await client.GetSlopesAsync(default));
    }

    [Fact]
    public async Task RejectsInvalidPollingIntervalAndHttpFailures()
    {
        using var http = Client(new Handler(_ => """{"pollingIntervalMs":0}"""));
        await Assert.ThrowsAsync<JsonException>(() => new ResortClient(http).GetConfigAsync(default));
        using var failed = Client(new Handler(_ => "Unavailable", HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ResortClient(failed).GetWeatherAsync(default));
    }

    [Fact]
    public async Task ParsesFragmentedUtf8CrLfMultilineAndFinalEvents()
    {
        var text = ": heartbeat\r\nevent: update\r\ndata: {\"type\":\"response.output_text.delta\",\r\ndata: \"delta\":\"Café 🏔️\"}\r\n\r\n"
            + "data: {\"type\":\"response.completed\"}";
        await using var stream = new FragmentedStream(Encoding.UTF8.GetBytes(text));
        var events = new List<JsonElement>();
        await foreach (var item in AdvisorClient.ReadEventsAsync(stream, default)) events.Add(item);
        Assert.Equal(2, events.Count);
        Assert.Equal("Café 🏔️", events[0].GetProperty("delta").GetString());
    }

    [Fact]
    public async Task DoneTerminatesStreamAndCancellationIsObserved()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data: [DONE]\n\ndata: not json\n\n"));
        await foreach (var _ in AdvisorClient.ReadEventsAsync(stream, default)) Assert.Fail("Unexpected SSE event");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var cancelled = new MemoryStream(Encoding.UTF8.GetBytes("data: {}\n\n"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in AdvisorClient.ReadEventsAsync(cancelled, cancellation.Token)) { }
        });
    }

    [Fact]
    public async Task SkillsPreserveHostedContinuationAndReset()
    {
        var handler = new Handler(_ => Event("""{"type":"response.output_text.delta","delta":"Hello"}""")
            + Event("""{"type":"response.completed","response":{"id":"r1","agent_session_id":"s1"}}"""));
        using var http = Client(handler);
        var client = new AdvisorClient(http);
        var updates = await Send(client, AdvisorArchitecture.Skill, null);
        var conversation = updates.Last().ConversationId;
        Assert.True(Guid.TryParse(conversation, out _));
        Assert.Equal("Hello", updates[0].Content);
        await Send(client, AdvisorArchitecture.Skill, conversation);
        var first = handler.Bodies[0];
        Assert.Equal(conversation, first.GetProperty("conversation").GetString());
        Assert.Equal("skiadvisorskill-ha", first.GetProperty("model").GetString());
        Assert.Equal("agent_reference", first.GetProperty("agent_reference").GetProperty("type").GetString());
        Assert.True(first.GetProperty("stream").GetBoolean());
        Assert.Equal("skiadvisorskill-ha", first.GetProperty("metadata").GetProperty("entity_id").GetString());
        Assert.Equal("hello", first.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("/responses/skill", handler.Paths[0]);
        var next = handler.Bodies[1];
        Assert.Equal("r1", next.GetProperty("previous_response_id").GetString());
        Assert.Equal("s1", next.GetProperty("agent_session_id").GetString());
        Assert.False(next.TryGetProperty("conversation", out _));
        client.Reset();
        await Send(client, AdvisorArchitecture.Skill, conversation);
        Assert.False(handler.Bodies[2].TryGetProperty("previous_response_id", out _));
        Assert.Equal(conversation, handler.Bodies[2].GetProperty("conversation").GetString());
    }

    [Theory]
    [InlineData(AdvisorArchitecture.A2A)]
    [InlineData(AdvisorArchitecture.Skill)]
    public async Task ServerConversationTakesPrecedence(AdvisorArchitecture architecture)
    {
        var handler = new Handler(_ => Event("""{"type":"response.created","response":{"conversation":{"id":"server-conversation"}}}""")
            + Event("""{"type":"response.completed","response":{"id":"r1","agent_session_id":"s1"}}"""));
        using var http = Client(handler);
        var client = new AdvisorClient(http);
        var updates = await Send(client, architecture, null);
        Assert.Equal("server-conversation", updates.Last().ConversationId);
        await Send(client, architecture, updates.Last().ConversationId);
        Assert.Equal("server-conversation", handler.Bodies[1].GetProperty("conversation").GetString());
        Assert.False(handler.Bodies[1].TryGetProperty("previous_response_id", out _));
        Assert.Equal($"/responses/{AdvisorClient.Route(architecture)}", handler.Paths[0]);
        if (architecture == AdvisorArchitecture.A2A)
        {
            Assert.False(handler.Bodies[0].TryGetProperty("conversation", out _));
            Assert.Equal("skiadvisora2a-ha", handler.Bodies[0].GetProperty("model").GetString());
        }
    }

    [Theory]
    [InlineData("error")]
    [InlineData("response.failed")]
    [InlineData("response.incomplete")]
    public async Task AdvisorErrorsAreNotSuccessfulResponses(string type)
    {
        using var http = Client(new Handler(_ => Event(JsonSerializer.Serialize(new { type, error = new { message = "Model unavailable" } }))));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Send(new AdvisorClient(http), AdvisorArchitecture.A2A, null));
        Assert.Equal("Model unavailable", error.Message);
    }

    [Fact]
    public async Task AdvisorHttpFailureIncludesStatus()
    {
        using var http = Client(new Handler(_ => "Gateway failed", HttpStatusCode.BadGateway));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Send(new AdvisorClient(http), AdvisorArchitecture.A2A, null));
        Assert.Contains("502", error.Message);
    }

    private static async Task<List<AdvisorUpdate>> Send(AdvisorClient client, AdvisorArchitecture architecture, string? conversation)
    {
        var updates = new List<AdvisorUpdate>();
        await foreach (var update in client.SendAsync("hello", architecture, conversation, default)) updates.Add(update);
        return updates;
    }

    private static string Event(string json) => $"data: {json}\n\n";
    private static HttpClient Client(Handler handler) => new(handler) { BaseAddress = new Uri("http://localhost:12345") };

    private sealed class Handler(Func<HttpRequestMessage, string> response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<JsonElement> Bodies { get; } = [];
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.Content is not null)
            {
                using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
                Bodies.Add(document.RootElement.Clone());
                Assert.Contains(request.Headers.Accept, header => header.MediaType == "text/event-stream");
            }
            return new HttpResponseMessage(status) { Content = new StringContent(response(request)) };
        }
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], token);
    }
}
