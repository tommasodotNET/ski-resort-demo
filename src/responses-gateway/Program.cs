using System.Data.Common;
using System.Net.WebSockets;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;

#pragma warning disable OPENAI001

const string DataGeneratorClientName = "datagenerator";
const string VoiceAdvisorPath = "/ws/voice";
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://+:{port}");

builder.AddServiceDefaults();
builder.Services.AddHttpClient(DataGeneratorClientName, client =>
{
    client.BaseAddress = new Uri("https+http://datagenerator/");
});
builder.Services.AddSingleton<TokenCredential>(_ => new ChainedTokenCredential(
    CreateManagedIdentityCredential(),
    new AzureCliCredential()));
builder.Services.AddSingleton(sp => new AIProjectClient(
    endpoint: new Uri(GetFoundryProjectEndpoint()),
    tokenProvider: sp.GetRequiredService<TokenCredential>()));
builder.Services.AddSingleton<FoundryResponsesClientProvider>();

var app = builder.Build();

app.UseFileServer();
app.UseWebSockets();

app.MapGet("/liveness", () => Results.Ok("Healthy"));
app.MapGet("/readiness", () => Results.Ok("Ready"));

app.Map("/api/{**catchAll}", (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
    ProxyHttpAsync(context, httpClientFactory, DataGeneratorClientName, cancellationToken));
app.MapPost("/responses/{architecture}", ProxyFoundryResponsesAsync);
app.Map("/ws/voice/{architecture}", (string architecture, HttpContext context) =>
    ProxyWebSocketAsync(context, GetVoiceAdvisorEndpoint(architecture), VoiceAdvisorPath));
app.Map("/ws/live/{architecture}", (string architecture, HttpContext context) =>
    ProxyWebSocketAsync(context, GetVoiceAdvisorEndpoint(architecture), VoiceAdvisorPath));

app.MapFallbackToFile("index.html");

app.Run();

static async Task ProxyHttpAsync(
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    string clientName,
    CancellationToken cancellationToken)
{
    using var requestMessage = CreateProxyRequest(context.Request, BuildTargetPath(context.Request));

    var httpClient = httpClientFactory.CreateClient(clientName);
    using var responseMessage = await httpClient.SendAsync(
        requestMessage,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);

    context.Response.StatusCode = (int)responseMessage.StatusCode;
    CopyResponseHeaders(responseMessage, context.Response);

    await responseMessage.Content.CopyToAsync(context.Response.Body, cancellationToken);
}

static async Task ProxyFoundryResponsesAsync(
    string architecture,
    HttpContext context,
    FoundryResponsesClientProvider clientProvider,
    CancellationToken cancellationToken)
{
    var responseClient = await clientProvider.GetClientAsync(architecture, cancellationToken);
    using var requestContent = await CreateFoundryRequestContentAsync(context.Request, cancellationToken);
    var requestOptions = CreateFoundryRequestOptions(context.Request, cancellationToken);
    using var responseMessage = (await responseClient.CreateResponseAsync(requestContent, requestOptions)).GetRawResponse();

    context.Response.StatusCode = responseMessage.Status;
    CopySdkResponseHeaders(responseMessage, context.Response);

    if (responseMessage.ContentStream is not null)
    {
        await responseMessage.ContentStream.CopyToAsync(context.Response.Body, cancellationToken);
    }
}

static async Task<BinaryContent> CreateFoundryRequestContentAsync(HttpRequest request, CancellationToken cancellationToken)
{
    var requestBody = await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken);
    if (requestBody is not JsonObject requestObject)
    {
        throw new BadHttpRequestException("Responses request body must be a JSON object.");
    }

    if (requestObject.TryGetPropertyValue("conversation", out var conversationNode))
    {
        requestObject.Remove("conversation");
        var conversationId = conversationNode?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(conversationId) && !Guid.TryParse(conversationId, out _))
        {
            requestObject["conversation_id"] = conversationId;
        }
    }

    return BinaryContent.Create(BinaryData.FromString(requestObject.ToJsonString()));
}

static string BuildTargetPath(HttpRequest request)
    => $"{request.Path.Value?.TrimStart('/')}{request.QueryString}";

static string GetFoundryProjectEndpoint()
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__projvoiceskiresort")
        ?? throw new InvalidOperationException("ConnectionStrings__projvoiceskiresort is not set.");

    DbConnectionStringBuilder connectionBuilder = new() { ConnectionString = connectionString };
    if (!connectionBuilder.TryGetValue("Endpoint", out var rawEndpoint) || rawEndpoint is null)
    {
        throw new InvalidOperationException("ConnectionStrings__projvoiceskiresort is missing Endpoint.");
    }

    var endpoint = rawEndpoint.ToString();
    if (string.IsNullOrWhiteSpace(endpoint))
    {
        throw new InvalidOperationException("ConnectionStrings__projvoiceskiresort has an empty Endpoint value.");
    }

    return endpoint.EndsWith('/') ? endpoint : $"{endpoint}/";
}

static RequestOptions CreateFoundryRequestOptions(HttpRequest request, CancellationToken cancellationToken)
{
    var requestOptions = new RequestOptions
    {
        BufferResponse = false,
        CancellationToken = cancellationToken,
        ErrorOptions = ClientErrorBehaviors.NoThrow
    };

    requestOptions.SetHeader("Content-Type", request.ContentType ?? "application/json");
    if (request.Headers.TryGetValue("Accept", out var acceptHeader))
    {
        requestOptions.SetHeader("Accept", acceptHeader.ToString());
    }

    return requestOptions;
}

static TokenCredential CreateManagedIdentityCredential()
{
    var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
#pragma warning disable CS0618
    return string.IsNullOrWhiteSpace(clientId)
        ? new ManagedIdentityCredential()
        : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
#pragma warning restore CS0618
}

static HttpRequestMessage CreateProxyRequest(HttpRequest request, string targetPath)
{
    var requestMessage = new HttpRequestMessage(new HttpMethod(request.Method), targetPath);

    if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
    {
        requestMessage.Content = new StreamContent(request.Body);
    }

    foreach (var header in request.Headers)
    {
        if (ShouldSkipRequestHeader(header.Key))
        {
            continue;
        }

        if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
        {
            requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    return requestMessage;
}

static async Task ProxyWebSocketAsync(HttpContext context, string targetEndpoint, string targetPath)
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket connection expected", context.RequestAborted);
        return;
    }

    var targetUri = BuildWebSocketUri(context.Request, targetEndpoint, targetPath);

    using var downstreamSocket = await context.WebSockets.AcceptWebSocketAsync();
    using var upstreamSocket = new ClientWebSocket();
    foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
    {
        upstreamSocket.Options.AddSubProtocol(protocol);
    }

    await upstreamSocket.ConnectAsync(targetUri, context.RequestAborted);

    var downstreamToUpstream = PumpWebSocketAsync(downstreamSocket, upstreamSocket, context.RequestAborted);
    var upstreamToDownstream = PumpWebSocketAsync(upstreamSocket, downstreamSocket, context.RequestAborted);

    await Task.WhenAny(downstreamToUpstream, upstreamToDownstream);
}

static Uri BuildWebSocketUri(HttpRequest request, string targetEndpoint, string targetPath)
{
    var builder = new UriBuilder(targetEndpoint)
    {
        Scheme = "ws",
        Path = targetPath,
        Query = request.QueryString.HasValue ? request.QueryString.Value![1..] : string.Empty
    };

    return builder.Uri;
}

static string GetVoiceAdvisorEndpoint(string architecture)
    => architecture switch
    {
        "a2a" => "http://voiceadvisora2a",
        "skill" => "http://voiceadvisorskill",
        _ => throw new BadHttpRequestException(
            $"Unknown voice advisor architecture '{architecture}'.")
    };

static async Task PumpWebSocketAsync(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
{
    var buffer = new byte[64 * 1024];

    while (!cancellationToken.IsCancellationRequested && source.State == WebSocketState.Open && destination.State == WebSocketState.Open)
    {
        var result = await source.ReceiveAsync(buffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            await destination.CloseOutputAsync(
                result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                result.CloseStatusDescription,
                cancellationToken);
            return;
        }

        await destination.SendAsync(
            buffer.AsMemory(0, result.Count),
            result.MessageType,
            result.EndOfMessage,
            cancellationToken);
    }
}

static void CopyResponseHeaders(HttpResponseMessage responseMessage, HttpResponse response)
{
    foreach (var header in responseMessage.Headers)
    {
        if (!ShouldSkipResponseHeader(header.Key))
        {
            response.Headers[header.Key] = header.Value.ToArray();
        }
    }

    foreach (var header in responseMessage.Content.Headers)
    {
        if (!ShouldSkipResponseHeader(header.Key))
        {
            response.Headers[header.Key] = header.Value.ToArray();
        }
    }
}

static void CopySdkResponseHeaders(PipelineResponse responseMessage, HttpResponse response)
{
    foreach (var header in responseMessage.Headers)
    {
        if (!ShouldSkipResponseHeader(header.Key))
        {
            response.Headers[header.Key] = header.Value;
        }
    }
}

static bool ShouldSkipRequestHeader(string headerName)
    => headerName.Equals("Host", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("TE", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);

static bool ShouldSkipResponseHeader(string headerName)
    => headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("TE", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
       || headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);

sealed class FoundryResponsesClientProvider(AIProjectClient projectClient)
{
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly Dictionary<string, ProjectResponsesClient> responseClients = [];

    public async Task<ProjectResponsesClient> GetClientAsync(
        string architecture,
        CancellationToken cancellationToken)
    {
        var agentName = architecture switch
        {
            "a2a" => "skiadvisora2a-ha",
            "skill" => "skiadvisorskill-ha",
            _ => throw new ArgumentException($"Unknown advisor architecture '{architecture}'.", nameof(architecture)),
        };

        if (responseClients.TryGetValue(agentName, out var existingClient))
        {
            return existingClient;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (!responseClients.TryGetValue(agentName, out var responseClient))
            {
                responseClient = projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgentEndpoint(agentName);
                responseClients[agentName] = responseClient;
            }

            return responseClient;
        }
        finally
        {
            initializationLock.Release();
        }
    }
}
