using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using System.Data.Common;
using A2A;
using Azure.AI.Extensions.OpenAI;

#pragma warning disable OPENAI001

const string AgentResponseIdHeader = "x-agent-response-id";

string port = Environment.GetEnvironmentVariable("DEFAULT_AD_PORT") ?? "8088";

var projectEndpoint = GetFoundryProjectEndpoint();
var deploymentName = GetModelDeploymentName();

if (!Uri.TryCreate(projectEndpoint, UriKind.Absolute, out var projectUri) || projectUri is null)
{
    throw new InvalidOperationException("The configured Foundry project endpoint is invalid.");
}

TokenCredential credential = new ChainedTokenCredential(
    new DevTemporaryTokenCredential(),
    new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeManagedIdentityCredential = string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase)
    }));

// Helper to resolve a remote agent via A2A
AIAgent ResolveA2AAgent(string envVar, string cardPath = "/.well-known/agent-card.json", string? endpointPath = null)
{
    var url = Environment.GetEnvironmentVariable(envVar)
        ?? throw new InvalidOperationException($"{envVar} not configured.");
    var httpClient = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
    var resolver = new A2ACardResolver(httpClient.BaseAddress!, httpClient, agentCardPath: cardPath);
    var agentCard = resolver.GetAgentCardAsync().Result;
    if (!string.IsNullOrWhiteSpace(endpointPath))
    {
        foreach (var supportedInterface in agentCard.SupportedInterfaces)
            supportedInterface.Url = AppendPath(supportedInterface.Url, endpointPath);
    }

    return agentCard.AsAIAgent(httpClient);
}

static string AppendPath(string url, string path)
    => $"{url.TrimEnd('/')}/{path.TrimStart('/')}";

// Connect to specialist agents via A2A
var weatherAgent = ResolveA2AAgent(Environment.GetEnvironmentVariable("services__weatheragent__https__0") != null 
    ? "services__weatheragent__https__0" 
    : "services__weatheragent__http__0");
var liftAgent = ResolveA2AAgent(Environment.GetEnvironmentVariable("services__lifttrafficagent__https__0") != null
        ? "services__lifttrafficagent__https__0"
        : "services__lifttrafficagent__http__0");
var safetyAgent = ResolveA2AAgent(Environment.GetEnvironmentVariable("services__safetyagent__https__0") != null
        ? "services__safetyagent__https__0"
        : "services__safetyagent__http__0");
var coachAgent = ResolveA2AAgent(Environment.GetEnvironmentVariable("services__skicoachagent__https__0") != null
        ? "services__skicoachagent__https__0"
        : "services__skicoachagent__http__0");
var foundryProjectClient = new AIProjectClient(projectUri, credential);

// var skiResearcherAgent = await foundryProjectClient.AgentAdministrationClient.GetAgentAsync("ski-researcher");
var skiResearcherAgentName = Environment.GetEnvironmentVariable("SKIRESEARCHER_AGENTNAME")
    ?? throw new InvalidOperationException("SKIRESEARCHER_AGENTNAME is not set.");
var skiResearcherAgentReference = new AgentReference(name: skiResearcherAgentName);
var responseClient = foundryProjectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(skiResearcherAgentReference);
var skiResearcherAgent = responseClient
    .AsIChatClient(deploymentName)
    .AsAIAgent(skiResearcherAgentName, description: "I can search the web. Use me for any generic question about skiing.");

var agent = foundryProjectClient.AsAIAgent(
    model: deploymentName,
    name: Environment.GetEnvironmentVariable("AGENT_NAME") ?? "advisor-agent",
    description: "AlpineAI ski resort advisor orchestrator with local specialist-agent tools.",
    instructions: @"You are the Ski Resort Advisor, the main AI concierge for AlpineAI ski resort.

You have access to four specialist agents as tools:
- Weather Agent: current conditions, forecasts, storm alerts
- Lift Traffic Agent: lift status, wait times, congestion
- Safety Agent: risk evaluation, slope safety, closures
- Ski Coach Agent: personalized slope recommendations, day plans
- Ski Researcher Agent: general ski-related questions

IMPORTANT: Only call the agents that are relevant to the user's question. Do NOT call all agents for every question.
Never use your internal knowledge to answer questions. For generic ski questions, use the Ski Researcher Agent tool to search the web for up-to-date information.

Examples:
- ""What's the weather like?"" → call Weather Agent only
- ""Which lifts are open?"" → call Lift Traffic Agent only
- ""Is it safe to ski today?"" → call Safety Agent (and Weather Agent if you need conditions context)
- ""I'm a beginner, where should I ski?"" → call Ski Coach Agent
- ""Plan my full day"" → call multiple agents as needed
- ""I have a general question about skiing"" → call Ski Researcher Agent
- ""Hi"" or ""Thanks"" → respond directly, no agent calls needed

When you DO call agents, synthesize their responses into one clear answer. Mention any safety concerns prominently. Be friendly, concise, and helpful.",
    tools: [
        weatherAgent.AsAIFunction(),
        liftAgent.AsAIFunction(),
        safetyAgent.AsAIFunction(),
        coachAgent.AsAIFunction(),
        skiResearcherAgent.AsAIFunction()
    ],
    clientFactory: chatClient => chatClient
        .AsBuilder()
        .ConfigureOptions(options => options.AllowMultipleToolCalls = true)
        .UseOpenTelemetry(sourceName: "Foundry.Agents", configure: cfg => cfg.EnableSensitiveData = true)
        .Build());

var ficc = agent.GetService<FunctionInvokingChatClient>();
ficc?.AllowConcurrentInvocation = true;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("URLS")))
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

builder.AddServiceDefaults();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddFoundryResponses(agent);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevTemporaryLocalContributorSetup();
}

var app = builder.Build();

// Enable CORS
app.UseCors();

// Work around hosted storage conflicts caused by replayed platform response IDs.
app.Use(UseSdkGeneratedResponseIdsForResponses);

// Map Foundry Responses API endpoint at /responses.
app.MapFoundryResponses();
app.MapDevTemporaryLocalAgentEndpoint();
app.MapGet("/liveness", () => Results.Ok("Healthy"));
app.MapGet("/readiness", () => Results.Ok("Ready"));

// app.MapDefaultEndpoints();
app.Run();

static string GetFoundryProjectEndpoint()
{
    var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
    if (!string.IsNullOrWhiteSpace(endpoint))
    {
        return endpoint;
    }

    var projectConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__projvoiceskiresort")
        ?? throw new InvalidOperationException("Set FOUNDRY_PROJECT_ENDPOINT or ConnectionStrings__projvoiceskiresort.");

    DbConnectionStringBuilder projectConnectionBuilder = new() { ConnectionString = projectConnectionString };
    return GetRequiredConnectionValue(projectConnectionBuilder, "Endpoint");
}

static string GetModelDeploymentName()
{
    var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL");
    if (!string.IsNullOrWhiteSpace(model))
    {
        return model;
    }

    var chatConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__gpt41")
        ?? throw new InvalidOperationException("Set FOUNDRY_MODEL or ConnectionStrings__gpt41.");

    DbConnectionStringBuilder chatConnectionBuilder = new() { ConnectionString = chatConnectionString };
    return GetRequiredConnectionValue(chatConnectionBuilder, "Deployment");
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

static async Task UseSdkGeneratedResponseIdsForResponses(HttpContext context, Func<Task> next)
{
    if (HttpMethods.IsPost(context.Request.Method)
        && IsFoundryResponsesPath(context.Request.Path.Value)
        && context.Request.Headers.ContainsKey(AgentResponseIdHeader))
    {
        context.Request.Headers.Remove(AgentResponseIdHeader);
    }

    await next();
}

static bool IsFoundryResponsesPath(string? path)
    => string.Equals(path, "/responses", StringComparison.OrdinalIgnoreCase)
       || (path?.EndsWith("/endpoint/protocols/openai/responses", StringComparison.OrdinalIgnoreCase) ?? false);

sealed class DevTemporaryTokenCredential : TokenCredential
{
    private const string EnvironmentVariable = "AZURE_BEARER_TOKEN";
    private readonly string? token = Environment.GetEnvironmentVariable(EnvironmentVariable);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => GetAccessToken();

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new(GetAccessToken());

    private AccessToken GetAccessToken()
    {
        if (string.IsNullOrWhiteSpace(token) || string.Equals(token, nameof(DefaultAzureCredential), StringComparison.Ordinal))
        {
            throw new CredentialUnavailableException($"{EnvironmentVariable} environment variable is not set.");
        }

        return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
    }
}