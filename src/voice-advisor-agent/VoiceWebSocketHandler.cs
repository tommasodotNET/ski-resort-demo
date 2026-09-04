using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Azure.AI.VoiceLive;
using Azure.Core;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;

namespace VoiceAdvisorAgent;

/// <summary>
/// Handles a single voice session, bridging a browser WebSocket to a Voice Live session.
/// Delegates conversation persistence to <see cref="VoiceConversationStore"/> and
/// telemetry emission to <see cref="VoiceSessionTraceEmitter"/>.
/// </summary>
public sealed class VoiceWebSocketHandler
{
    private static readonly TimeSpan s_initializationTimeout = TimeSpan.FromSeconds(30);

    private readonly WebSocket _clientSocket;
    private readonly TokenCredential _credential;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string _voice;
    private readonly string _instructions;
    private readonly Dictionary<string, AIAgent> _a2aAgents;
    private readonly ILogger<VoiceWebSocketHandler> _logger;
    private readonly string? _conversationId;
    private readonly VoiceConversationStore? _conversationStore;
    private readonly VoiceSessionTraceEmitter _traceEmitter;

    private const string SkillsOrchestratorToolName = "ski_advisor_skill";
    private AgentSession? _skillsOrchestratorSession;

    // Conversation tracking for post-hoc telemetry and persistence
    private readonly List<ConversationMessage> _messages = new();
    private readonly List<ToolExecution> _toolExecutions = new();
    private readonly List<ToolDefinitionInfo> _toolDefinitions = new();
    private DateTimeOffset _sessionStartTime;
    private DateTimeOffset _sessionEndTime;
    private string? _errorType;

    public VoiceWebSocketHandler(
        WebSocket clientSocket,
        TokenCredential credential,
        string endpoint,
        string model,
        string voice,
        string instructions,
        Dictionary<string, AIAgent> a2aAgents,
        ILogger<VoiceWebSocketHandler> logger,
        string? conversationId = null,
        Container? cosmosContainer = null)
    {
        _clientSocket = clientSocket;
        _credential = credential;
        _endpoint = endpoint;
        _model = model;
        _voice = voice;
        _instructions = instructions;
        _a2aAgents = a2aAgents;
        _logger = logger;
        _conversationId = conversationId;
        _conversationStore = cosmosContainer is not null ? new VoiceConversationStore(cosmosContainer, logger) : null;
        _traceEmitter = new VoiceSessionTraceEmitter(logger);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Voice Live session with endpoint {Endpoint}, model {Model}", _endpoint, _model);
        _sessionStartTime = DateTimeOffset.UtcNow;

        try
        {
            using var initializationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            initializationCts.CancelAfter(s_initializationTimeout);
            var initializationToken = initializationCts.Token;

            var client = new VoiceLiveClient(new Uri(_endpoint), _credential);
            await using var session = await client.StartSessionAsync(_model, initializationToken);

            await ConfigureSessionAsync(session, _instructions, initializationToken);
            await InjectConversationHistoryAsync(session, initializationToken);
            await SendToClient(new { type = "ready", conversationId = _conversationId }, initializationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var clientToVoiceLive = Task.Run(() => ProcessClientMessages(session, cts.Token), cts.Token);
            var voiceLiveToClient = Task.Run(() => ProcessVoiceLiveEvents(session, cts.Token), cts.Token);

            await Task.WhenAny(clientToVoiceLive, voiceLiveToClient);
            await cts.CancelAsync();

            _logger.LogInformation("Voice Live session ended");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _errorType = "voice_live_initialization_timeout";
            _logger.LogError(ex, "Voice Live session initialization timed out after {Timeout}", s_initializationTimeout);
            await NotifyClientOfFailureAsync("Voice session initialization timed out.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Voice Live session was cancelled");
        }
        catch (Exception ex)
        {
            _errorType = ex.GetType().FullName;
            _logger.LogError(ex, "Voice Live session failed");
            await NotifyClientOfFailureAsync("Voice session could not be started.");
        }
        finally
        {
            _sessionEndTime = DateTimeOffset.UtcNow;

            _traceEmitter.Emit(_endpoint, _model, _instructions,
                _messages, _toolExecutions, _toolDefinitions,
                _sessionStartTime, _sessionEndTime, _errorType);

            if (_conversationId is not null && _conversationStore is not null)
                await _conversationStore.SaveAsync(_conversationId, _messages);
        }
    }

    private async Task NotifyClientOfFailureAsync(string message)
    {
        await SendToClient(new { type = "error", message }, CancellationToken.None);

        if (_clientSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _clientSocket.CloseOutputAsync(
                    WebSocketCloseStatus.InternalServerError,
                    "Voice session failed",
                    closeCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not close the client WebSocket after a voice session failure");
            }
        }
    }

    private async Task ConfigureSessionAsync(
        VoiceLiveSession session,
        string instructions,
        CancellationToken cancellationToken)
    {
        // Convert A2A agents to Voice Live tool definitions (analogous to agent.AsAIFunction() in MAF)
        var functionTools = _a2aAgents
            .Select(agentEntry => agentEntry.Value.AsVoiceLiveTool(agentEntry.Key))
            .ToList();

        // Collect tool definitions for telemetry
        foreach (var tool in functionTools)
        {
            _toolDefinitions.Add(new ToolDefinitionInfo(
                tool.Name,
                tool.Description ?? "",
                tool.Parameters?.ToString() ?? "{}"));
        }

        var options = new VoiceLiveSessionOptions
        {
            Model = _model,
            Instructions = instructions,
            Voice = new AzureStandardVoice(_voice),
            InputAudioFormat = InputAudioFormat.Pcm16,
            OutputAudioFormat = OutputAudioFormat.Pcm16,
            TurnDetection = new AzureSemanticVadTurnDetection
            {
                Threshold = 0.5f,
                PrefixPadding = TimeSpan.FromMilliseconds(300),
                SilenceDuration = TimeSpan.FromMilliseconds(500),
            },
            InputAudioEchoCancellation = new AudioEchoCancellation(),
            InputAudioNoiseReduction = new AudioNoiseReduction(AudioNoiseReductionType.AzureDeepNoiseSuppression),
            ToolChoice = ToolChoiceLiteral.Auto,
            InputAudioTranscription = new AudioInputTranscriptionOptions(AudioInputTranscriptionOptionsModel.Whisper1)
        };

        options.Modalities.Clear();
        options.Modalities.Add(InteractionModality.Text);
        options.Modalities.Add(InteractionModality.Audio);

        foreach (var tool in functionTools)
            options.Tools.Add(tool);

        await session.ConfigureSessionAsync(options, cancellationToken);
        _logger.LogInformation("Voice Live session configured with {ToolCount} tools", functionTools.Count);
    }

    /// <summary>
    /// Injects previous conversation history as native conversation items.
    /// </summary>
    private async Task InjectConversationHistoryAsync(VoiceLiveSession session, CancellationToken cancellationToken)
    {
        if (_conversationId is null || _conversationStore is null) return;

        try
        {
            var messages = await _conversationStore.LoadAsync(_conversationId, cancellationToken);
            if (messages.Count == 0) return;

            foreach (var (role, text) in messages)
            {
                ConversationRequestItem item = role switch
                {
                    "user" => new UserMessageItem(text),
                    "assistant" => new AssistantMessageItem(text),
                    _ => new UserMessageItem(text)
                };

                await session.AddItemAsync(item, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error injecting conversation history for {ConversationId}", _conversationId);
        }
    }

    private async Task ProcessClientMessages(VoiceLiveSession session, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 64];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _clientSocket.State == WebSocketState.Open)
            {
                var result = await _clientSocket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Client WebSocket closed");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleClientMessage(session, message, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogInformation("Client WebSocket closed prematurely");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing client messages");
        }
    }

    private async Task HandleClientMessage(VoiceLiveSession session, string message, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var type = doc.RootElement.GetProperty("type").GetString();

            if (type == "audio" && doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                var base64Audio = dataElement.GetString();
                if (!string.IsNullOrEmpty(base64Audio))
                {
                    var audioBytes = Convert.FromBase64String(base64Audio);
                    await session.SendInputAudioAsync(audioBytes, cancellationToken);
                }
            }
            else if (type == "stop")
            {
                _logger.LogInformation("Client requested stop");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client message");
        }
    }

    private async Task ProcessVoiceLiveEvents(VoiceLiveSession session, CancellationToken cancellationToken)
    {
        Dictionary<string, object>? pendingFunctionCall = null;

        try
        {
            await foreach (var update in session.GetUpdatesAsync(cancellationToken))
            {
                switch (update)
                {
                    case SessionUpdateSessionUpdated:
                        _logger.LogInformation("Voice Live session updated and ready");
                        await session.StartResponseAsync(cancellationToken);
                        await SendToClient(new { type = "status", status = "ready" }, cancellationToken);
                        break;

                    case SessionUpdateInputAudioBufferSpeechStarted:
                        _logger.LogDebug("User started speaking (barge-in)");
                        await SendToClient(new { type = "clear_audio" }, cancellationToken);
                        await SendToClient(new { type = "status", status = "listening" }, cancellationToken);
                        break;

                    case SessionUpdateInputAudioBufferSpeechStopped:
                        _logger.LogDebug("User stopped speaking");
                        await SendToClient(new { type = "status", status = "processing" }, cancellationToken);
                        break;

                    case SessionUpdateResponseCreated:
                        _logger.LogDebug("Response created");
                        break;

                    case SessionUpdateResponseAudioDelta audioDelta:
                        if (audioDelta.Delta is { Length: > 0 })
                        {
                            var base64Audio = Convert.ToBase64String(audioDelta.Delta.ToArray());
                            await SendToClient(new { type = "audio", data = base64Audio }, cancellationToken);
                        }
                        break;

                    case SessionUpdateResponseAudioTranscriptDelta transcriptDelta:
                        if (!string.IsNullOrEmpty(transcriptDelta.Delta))
                        {
                            await SendToClient(new
                            {
                                type = "transcript",
                                role = "assistant",
                                text = transcriptDelta.Delta,
                                final_ = false
                            }, cancellationToken);
                        }
                        break;

                    case SessionUpdateResponseAudioTranscriptDone transcriptDone:
                        if (!string.IsNullOrEmpty(transcriptDone.Transcript))
                        {
                            _messages.Add(new ConversationMessage(
                                DateTimeOffset.UtcNow, "assistant", "text", transcriptDone.Transcript));

                            await SendToClient(new
                            {
                                type = "transcript",
                                role = "assistant",
                                text = transcriptDone.Transcript,
                                final_ = true
                            }, cancellationToken);
                        }
                        break;

                    case SessionUpdateConversationItemInputAudioTranscriptionCompleted inputTranscript:
                        if (!string.IsNullOrEmpty(inputTranscript.Transcript))
                        {
                            _messages.Add(new ConversationMessage(
                                DateTimeOffset.UtcNow, "user", "text", inputTranscript.Transcript));

                            await SendToClient(new
                            {
                                type = "transcript",
                                role = "user",
                                text = inputTranscript.Transcript,
                                final_ = true
                            }, cancellationToken);
                        }
                        break;

                    case SessionUpdateResponseFunctionCallArgumentsDone functionCallFinished:
                        pendingFunctionCall = new Dictionary<string, object>
                        {
                            ["name"] = functionCallFinished.Name,
                            ["call_id"] = functionCallFinished.CallId,
                            ["item_id"] = functionCallFinished.ItemId,
                            ["arguments"] = functionCallFinished.Arguments
                        };

                        _messages.Add(new ConversationMessage(
                            DateTimeOffset.UtcNow, "assistant", "tool_call",
                            Content: null,
                            ToolCallId: functionCallFinished.CallId,
                            ToolName: functionCallFinished.Name,
                            ToolArguments: functionCallFinished.Arguments));

                        _logger.LogInformation("Function call: {FunctionName}", functionCallFinished.Name);
                        await SendToClient(new
                        {
                            type = "status",
                            status = "function_calling",
                            function_name = functionCallFinished.Name
                        }, cancellationToken);
                        break;

                    case SessionUpdateResponseDone:
                        _logger.LogDebug("Response done");
                        if (pendingFunctionCall != null)
                        {
                            await ExecuteFunctionCall(session, pendingFunctionCall, cancellationToken);
                            pendingFunctionCall = null;
                        }
                        await SendToClient(new { type = "status", status = "ready" }, cancellationToken);
                        break;

                    case SessionUpdateError errorUpdate:
                        _errorType = "voice_live_error";
                        _logger.LogError("Voice Live error: {Error}", errorUpdate.Error.Message);
                        await SendToClient(new { type = "error", message = errorUpdate.Error.Message }, cancellationToken);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _errorType ??= ex.GetType().FullName;
            _logger.LogError(ex, "Error processing Voice Live events");
            await SendToClient(new { type = "error", message = ex.Message }, cancellationToken);
        }
    }

    private async Task ExecuteFunctionCall(VoiceLiveSession session, Dictionary<string, object> callInfo, CancellationToken cancellationToken)
    {
        var functionName = (string)callInfo["name"];
        var callId = (string)callInfo["call_id"];
        var arguments = (string)callInfo["arguments"];

        _logger.LogInformation("Executing function {FunctionName} with args: {Args}", functionName, arguments);

        var toolExecution = new ToolExecution(functionName, callId, arguments, StartTime: DateTimeOffset.UtcNow);

        string resultJson;
        try
        {
            if (_a2aAgents.TryGetValue(functionName, out var agent))
            {
                var args = JsonDocument.Parse(arguments).RootElement;
                var query = args.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";

                var messages = new List<ChatMessage> { new(ChatRole.User, query) };
                var agentSession = await GetOrCreateAgentSessionAsync(functionName, agent);
                var agentResponse = agentSession is not null
                    ? await agent.RunAsync(messages, agentSession, cancellationToken: cancellationToken)
                    : await agent.RunAsync(messages, cancellationToken: cancellationToken);

                var responseText = agentResponse.Text;
                if (string.IsNullOrEmpty(responseText) && agentResponse.Messages is { Count: > 0 })
                {
                    responseText = string.Join("\n", agentResponse.Messages
                        .Where(m => m.Role == ChatRole.Assistant)
                        .SelectMany(m => m.Contents.OfType<TextContent>())
                        .Select(tc => tc.Text));
                }

                resultJson = responseText ?? "No response from agent";
            }
            else
            {
                _logger.LogWarning("Unknown function: {FunctionName}", functionName);
                resultJson = JsonSerializer.Serialize(new { error = $"Unknown function: {functionName}" });
            }

            _messages.Add(new ConversationMessage(
                DateTimeOffset.UtcNow, "tool", "tool_call_response",
                Content: null, ToolCallId: callId, ToolName: functionName, ToolResult: resultJson));

            toolExecution = toolExecution with { Result = resultJson, EndTime = DateTimeOffset.UtcNow };
            _toolExecutions.Add(toolExecution);

            await session.AddItemAsync(new FunctionCallOutputItem(callId, resultJson), cancellationToken);
            _logger.LogInformation("Function {FunctionName} result sent", functionName);
            await session.StartResponseAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing function {FunctionName}", functionName);
            var errorResult = JsonSerializer.Serialize(new { error = ex.Message });

            _messages.Add(new ConversationMessage(
                DateTimeOffset.UtcNow, "tool", "tool_call_response",
                Content: null, ToolCallId: callId, ToolName: functionName, ToolResult: errorResult));

            toolExecution = toolExecution with { Result = errorResult, EndTime = DateTimeOffset.UtcNow, ErrorType = ex.GetType().FullName };
            _toolExecutions.Add(toolExecution);

            await session.AddItemAsync(new FunctionCallOutputItem(callId, errorResult), cancellationToken);
            await session.StartResponseAsync(cancellationToken);
        }
    }

    private async ValueTask<AgentSession?> GetOrCreateAgentSessionAsync(string functionName, AIAgent agent)
    {
        if (_conversationId is null
            || functionName != SkillsOrchestratorToolName
            || agent is not A2AAgent a2aAgent)
        {
            return null;
        }

        if (_skillsOrchestratorSession is not null)
        {
            return _skillsOrchestratorSession;
        }

        try
        {
            _skillsOrchestratorSession = await a2aAgent.CreateSessionAsync(_conversationId);
            return _skillsOrchestratorSession;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not create an A2A session for tool {FunctionName}; falling back to a stateless call",
                functionName);
            return null;
        }
    }

    private async Task SendToClient(object message, CancellationToken cancellationToken)
    {
        if (_clientSocket.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await _clientSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to client WebSocket");
        }
    }
}
