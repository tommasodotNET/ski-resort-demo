using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AlpineAI.Core;

public enum AdvisorArchitecture { A2A, Skill }
public sealed record AdvisorUpdate(string? Content, string? ConversationId);

public sealed class AdvisorClient(HttpClient http)
{
    private readonly Dictionary<string, (string Response, string Session)> continuations = [];

    public void Reset() => continuations.Clear();
    public static string Route(AdvisorArchitecture architecture) => architecture == AdvisorArchitecture.A2A ? "a2a" : "skill";

    public async IAsyncEnumerable<AdvisorUpdate> SendAsync(string text, AdvisorArchitecture architecture,
        string? conversationId, [EnumeratorCancellation] CancellationToken token)
    {
        var name = architecture == AdvisorArchitecture.A2A ? "skiadvisora2a-ha" : "skiadvisorskill-ha";
        conversationId ??= architecture == AdvisorArchitecture.Skill ? Guid.NewGuid().ToString() : null;
        var body = new Dictionary<string, object>
        {
            ["model"] = name,
            ["agent_reference"] = new { type = "agent_reference", name },
            ["input"] = new[] { new { type = "message", role = "user", content = new[] { new { type = "input_text", text } } } },
            ["stream"] = true,
            ["metadata"] = new { entity_id = name }
        };
        if (architecture == AdvisorArchitecture.Skill && conversationId is not null
            && continuations.TryGetValue(conversationId, out var continuation))
        {
            body["previous_response_id"] = continuation.Response;
            body["agent_session_id"] = continuation.Session;
        }
        else if (conversationId is not null)
            body["conversation"] = conversationId;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"responses/{Route(architecture)}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Responses API failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(token)}");

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        string? serverConversation = null;
        await foreach (var payload in ReadEventsAsync(stream, token))
        {
            var type = String(payload, "type");
            var result = Property(payload, "response");
            if (type is "error" or "response.failed" or "response.incomplete")
                throw new InvalidOperationException(String(Property(payload, "error"), "message")
                    ?? String(Property(result, "error"), "message") ?? "The advisor did not complete its response.");
            serverConversation = String(payload, "conversation_id") ?? String(result, "conversation_id")
                ?? String(Property(result, "conversation"), "id") ?? serverConversation;
            conversationId = serverConversation ?? conversationId;
            var responseId = String(result, "id") ?? String(result, "response_id");
            var sessionId = String(result, "agent_session_id");
            if (architecture == AdvisorArchitecture.Skill && type == "response.completed"
                && serverConversation is null && conversationId is not null && responseId is not null && sessionId is not null)
                continuations[conversationId] = (responseId, sessionId);
            yield return new AdvisorUpdate(type == "response.output_text.delta" ? String(payload, "delta") : null, conversationId);
        }
    }

    public static async IAsyncEnumerable<JsonElement> ReadEventsAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var data = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is null || line.Length == 0)
            {
                if (data.Length > 0)
                {
                    var json = data.ToString().TrimEnd('\n');
                    data.Clear();
                    if (json == "[DONE]") yield break;
                    using var document = JsonDocument.Parse(json);
                    yield return document.RootElement.Clone();
                }
                if (line is null) yield break;
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = line[5..];
                data.Append(value.StartsWith(' ') ? value[1..] : value).Append('\n');
            }
        }
    }

    internal static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) ? result : default;
    internal static string? String(JsonElement value, string name) =>
        Property(value, name) is { ValueKind: JsonValueKind.String } result ? result.GetString() : null;
}
