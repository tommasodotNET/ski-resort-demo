namespace SharedServices.Models;

public record AIChatMessage(string Content, string Role, string? Context = null);

public record AIChatRequest(List<AIChatMessage> Messages, string? SessionState = null, string? Context = null);

public record AIChatMessageDelta(string? Content = null, string? Role = null, string? Context = null);

public record AIChatCompletionDelta(AIChatMessageDelta Delta, string? SessionState = null, string? Context = null)
{
    public AIChatCompletionDelta(AIChatMessageDelta delta) : this(delta, null, null) { }
}
