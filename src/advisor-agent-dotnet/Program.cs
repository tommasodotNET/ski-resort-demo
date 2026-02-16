using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Extensions.AI;
using Azure.Identity;
using A2A;
using SharedServices;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure Azure chat client
builder.AddAzureChatCompletionsClient(connectionName: "foundry",
    configureSettings: settings =>
    {
        settings.TokenCredential = new DefaultAzureCredential();
        settings.EnableSensitiveTelemetryData = true;
    })
    .AddChatClient("gpt-4.1");

// Register Cosmos for conversation storage
builder.AddKeyedAzureCosmosContainer("conversations",
    configureClientOptions: (option) => option.Serializer = new CosmosSystemTextJsonSerializer());
builder.Services.AddSingleton<ICosmosThreadRepository, CosmosThreadRepository>();
builder.Services.AddSingleton<CosmosAgentThreadStore>();

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

// Helper to resolve a remote agent via A2A
AIAgent ResolveA2AAgent(string envVar, string? cardPath = "/.well-known/agent-card.json")
{
    var url = Environment.GetEnvironmentVariable(envVar)
        ?? throw new InvalidOperationException($"{envVar} not configured.");
    var httpClient = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
    var resolver = new A2ACardResolver(httpClient.BaseAddress!, httpClient, agentCardPath: cardPath);
    return resolver.GetAIAgentAsync(httpClient).Result;
}

// Connect to specialist agents via A2A
var weatherAgent = ResolveA2AAgent("services__weather-agent-python__http__0");
var liftAgent = ResolveA2AAgent(Environment.GetEnvironmentVariable("services__lift-traffic-agent-dotnet__https__0") != null
        ? "services__lift-traffic-agent-dotnet__https__0"
        : "services__lift-traffic-agent-dotnet__http__0",
    "/agenta2a/v1/card");
var safetyAgent = ResolveA2AAgent("services__safety-agent-python__http__0");
var coachAgent = ResolveA2AAgent("services__ski-coach-agent-python__http__0");

// Register the orchestrator agent that uses all 4 remote agents as tools
builder.AddAIAgent("advisor-agent", (sp, key) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();

    var agent = chatClient.CreateAIAgent(
        instructions: @"You are the Ski Resort Advisor, the main AI concierge for AlpineAI ski resort.

You help skiers plan their perfect day by coordinating information from specialist agents:
- Weather Agent: current conditions, forecasts, storm alerts
- Lift Traffic Agent: lift status, wait times, congestion
- Safety Agent: risk evaluation, slope safety, closures
- Ski Coach Agent: personalized slope recommendations, day plans

DECISION PRIORITY (always follow this order):
1. SAFETY FIRST: Always check safety before making recommendations
2. WEATHER CHECK: Factor in current and forecasted weather
3. CONGESTION: Consider crowd levels and wait times
4. PERSONALIZATION: Match to user's skill level and preferences

When answering questions:
1. Gather relevant data from specialist agents
2. Synthesize information into a clear, actionable response
3. Always mention any safety concerns prominently
4. Provide specific slope and lift names
5. Be friendly, helpful, and encouraging",
        description: "AlpineAI Ski Resort Advisor - your intelligent ski concierge",
        name: key,
        tools: [
            weatherAgent.AsAIFunction(),
            liftAgent.AsAIFunction(),
            safetyAgent.AsAIFunction(),
            coachAgent.AsAIFunction()
        ]
    );

    return agent;
}).WithThreadStore((sp, key) => sp.GetRequiredService<CosmosAgentThreadStore>());

var app = builder.Build();

// Enable CORS
app.UseCors();

// Map A2A endpoint
app.MapA2A("advisor-agent", "/agenta2a", new AgentCard
{
    Name = "advisor-agent",
    Url = app.Configuration["ASPNETCORE_URLS"]?.Split(';')[0] + "/agenta2a" ?? "http://localhost:5200/agenta2a",
    Description = "AlpineAI Ski Resort Advisor - your intelligent ski concierge coordinating weather, lifts, safety, and coaching",
    Version = "1.0",
    DefaultInputModes = ["text"],
    DefaultOutputModes = ["text"],
    Capabilities = new AgentCapabilities
    {
        Streaming = true,
        PushNotifications = false
    },
    Skills = [
        new AgentSkill
        {
            Name = "Ski Resort Advisory",
            Description = "Coordinate weather, lift traffic, safety, and coaching information to provide personalized ski resort recommendations",
            Examples = [
                "I'm intermediate and hate crowds. Where should I ski?",
                "Is it safe to ski today?",
                "Plan my day - I'm an advanced skier",
                "What's the weather like and which lifts have short wait times?"
            ]
        }
    ]
});

app.MapDefaultEndpoints();
app.Run();
