using A2A;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using System.Data.Common;
using OpenTelemetry.Trace;
using SharedServices;
using VoiceAdvisorAgent;

#pragma warning disable MAAI001

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddKeyedAzureCosmosContainer("conversations",
    configureClientOptions: (option) =>
    {
        option.Serializer = new CosmosSystemTextJsonSerializer();
    });

builder.Services.AddSingleton(sp => sp.GetRequiredKeyedService<Container>("conversations"));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(VoiceSessionTraceEmitter.ActivitySourceName));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Parse the Voice Live endpoint from the Foundry connection string
// Voice Live needs the cognitiveservices.azure.com endpoint (the "Endpoint" key)
var endpoint = ParseVoiceLiveEndpoint(builder.Configuration.GetConnectionString("gptrealtime") ?? "");

var model = builder.Configuration["VoiceLive:Model"] ?? "gpt-realtime";
var voice = builder.Configuration["VoiceLive:Voice"] ?? "en-US-Ava:DragonHDLatestNeural";
var resourceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");
var architecture = Environment.GetEnvironmentVariable("VOICE_ADVISOR_ARCHITECTURE")
    ?? resourceName switch
    {
        "voiceadvisora2a" => "a2a",
        "voiceadvisorskill" => "skill",
        _ => null
    }
    ?? throw new InvalidOperationException(
        "VOICE_ADVISOR_ARCHITECTURE must be set to 'a2a' or 'skill'.");

if (architecture is not ("a2a" or "skill"))
{
    throw new InvalidOperationException(
        $"Unsupported voice advisor architecture '{architecture}'. Expected 'a2a' or 'skill'.");
}

// Connect to downstream agents via A2A
var agents = new Dictionary<string, AIAgent>(StringComparer.Ordinal);
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    // Managed identity is the production credential. Locally it fails as an
    // authentication error (rather than "unavailable"), which prevents the
    // chain from reaching the authenticated Azure CLI credential.
    ExcludeManagedIdentityCredential = builder.Environment.IsDevelopment()
});

if (architecture == "a2a")
{
    var agentConfigs = new Dictionary<string, (string EnvVar, string CardPath)>
    {
        ["weather_agent"] = ("services__weatheragenta2a__https__0", "/.well-known/agent-card.json"),
        ["lift_traffic_agent"] = ("services__lifttrafficagenta2a__https__0", "/.well-known/agent-card.json"),
        ["safety_agent"] = ("services__safetyagenta2a__https__0", "/.well-known/agent-card.json"),
        ["ski_coach_agent"] = ("services__skicoachagenta2a__https__0", "/.well-known/agent-card.json"),
    };

    foreach (var (agentName, config) in agentConfigs)
    {
        agents[agentName] = await ResolveA2AAgentAsync(config.EnvVar, config.CardPath);
    }

    var projectConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__projvoiceskiresort")
        ?? throw new InvalidOperationException("ConnectionStrings__projvoiceskiresort is not set.");
    var chatConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__gpt41")
        ?? throw new InvalidOperationException("ConnectionStrings__gpt41 is not set.");

    DbConnectionStringBuilder projectConnectionBuilder = new() { ConnectionString = projectConnectionString };
    DbConnectionStringBuilder chatConnectionBuilder = new() { ConnectionString = chatConnectionString };

    var projectEndpoint = GetRequiredConnectionValue(projectConnectionBuilder, "Endpoint");
    var deploymentName = GetRequiredConnectionValue(chatConnectionBuilder, "Deployment");

    if (!Uri.TryCreate(projectEndpoint, UriKind.Absolute, out var projectUri) || projectUri is null)
    {
        throw new InvalidOperationException(
            "ConnectionStrings__projvoiceskiresort contains an invalid Endpoint value.");
    }

    var skiResearcherAgentName = Environment.GetEnvironmentVariable("SKIRESEARCHER_AGENTNAME")
        ?? throw new InvalidOperationException("SKIRESEARCHER_AGENTNAME is not set.");
    var foundryProjectClient = new AIProjectClient(projectUri, credential);
    var skiResearcherAgentReference = new AgentReference(name: skiResearcherAgentName);
    var responseClient = foundryProjectClient.ProjectOpenAIClient
        .GetProjectResponsesClientForAgent(skiResearcherAgentReference);
    agents["ski_researcher_agent"] = responseClient
        .AsIChatClientWithStoredOutputDisabled(deploymentName, includeReasoningEncryptedContent: false)
        .AsAIAgent(
            "ski_researcher_agent",
            description: "I can search the web. Use me for any generic question about skiing.");
}
else
{
    agents["ski_advisor_skill"] = await ResolveA2AAgentAsync(
        "services__skiadvisorskilla2a__https__0",
        "/.well-known/agent-card.json");
}

builder.Services.AddSingleton(agents);
builder.Services.AddSingleton(credential);

var promptFileName = architecture == "a2a"
    ? "system-prompt.txt"
    : "skill-system-prompt.txt";
var systemPrompt = File.ReadAllText(
    Path.Combine(builder.Environment.ContentRootPath, "Prompts", promptFileName));

var app = builder.Build();

app.Logger.LogInformation(
    "Voice advisor resource {ResourceName} is using {Architecture} architecture with tools [{Tools}]",
    resourceName ?? "unknown",
    architecture,
    string.Join(", ", agents.Keys));

app.UseCors();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok("healthy"));

app.Map("/ws/voice", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket connection expected");
        return;
    }

    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger<VoiceWebSocketHandler>();

    var credential = context.RequestServices.GetRequiredService<DefaultAzureCredential>();
    var a2aAgents = context.RequestServices.GetRequiredService<Dictionary<string, AIAgent>>();
    var cosmosContainer = context.RequestServices.GetRequiredService<Container>();

    // Use conversationId directly (no suffix) so voice and chat histories are merged
    // Generate one if not provided so voice-only sessions are still saved
    var conversationId = context.Request.Query["conversationId"].FirstOrDefault()
        ?? Guid.NewGuid().ToString();

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

    var handler = new VoiceWebSocketHandler(
        webSocket,
        credential,
        endpoint,
        model,
        voice,
        systemPrompt,
        a2aAgents,
        logger,
        conversationId,
        cosmosContainer);

    await handler.RunAsync(context.RequestAborted);
});

app.MapDefaultEndpoints();
app.Run();

static string ParseVoiceLiveEndpoint(string connectionString)
{
    // Voice Live requires the cognitiveservices.azure.com endpoint (the "Endpoint" key)
    string? endpoint = null;

    foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        var kv = part.Split('=', 2);
        if (kv.Length != 2) continue;

        var key = kv[0].Trim();
        var value = kv[1].Trim();

        if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = value.TrimEnd('/');
        }
    }

    if (endpoint is not null)
        return endpoint;

    // If no key=value format, treat the whole string as a URL
    if (connectionString.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        return connectionString.TrimEnd('/');

    return "https://localhost";
}

static string GetRequiredConnectionValue(DbConnectionStringBuilder connectionBuilder, string key)
{
    if (!connectionBuilder.TryGetValue(key, out var rawValue) || rawValue is null)
    {
        throw new InvalidOperationException($"Connection string is missing '{key}'.");
    }

    var value = rawValue.ToString();
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Connection string has an empty '{key}' value.");
    }

    return value;
}

static async Task<AIAgent> ResolveA2AAgentAsync(string httpsEnvironmentVariable, string cardPath)
{
    var url = Environment.GetEnvironmentVariable(httpsEnvironmentVariable)
        ?? Environment.GetEnvironmentVariable(
            httpsEnvironmentVariable.Replace("__https__", "__http__", StringComparison.Ordinal));

    if (string.IsNullOrWhiteSpace(url))
    {
        throw new InvalidOperationException(
            $"Neither '{httpsEnvironmentVariable}' nor its HTTP equivalent is set.");
    }

    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(url),
        Timeout = TimeSpan.FromSeconds(60)
    };

    var cardResolver = new A2ACardResolver(
        httpClient.BaseAddress,
        httpClient,
        agentCardPath: cardPath);

    return await cardResolver.GetAIAgentAsync();
}
