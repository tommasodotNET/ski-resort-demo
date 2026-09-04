using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Extensions.AI;
using Azure.Identity;
using A2A;
using A2A.AspNetCore;
using LiftTrafficAgent.Dotnet.Services;
using LiftTrafficAgent.Dotnet.Skills;
using LiftTrafficAgent.Dotnet.Tools;
using Microsoft.Agents.AI.OpenAI;

const string A2AAgentBaseUrlEnvironmentVariable = "A2A_AGENT_BASE_URL";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure Azure chat client
builder.AddAzureChatCompletionsClient(connectionName: "gpt41",
    configureSettings: settings =>
    {
        settings.TokenCredential = new DefaultAzureCredential();
        settings.EnableSensitiveTelemetryData = true;
    })
    .AddChatClient();

// Register HttpClientFactory for LiftDataService
builder.Services.AddHttpClient();

// Register services
builder.Services.AddSingleton<LiftDataService>();
builder.Services.AddSingleton<LiftTrafficTools>();

// Register the agent
var liftAgentBuilder = builder.AddAIAgent("lifttrafficagent", (sp, key) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    var tools = sp.GetRequiredService<LiftTrafficTools>().GetFunctions();

    var agent = chatClient.AsAIAgent(
        instructions: LiftTrafficSkillCatalog.Instructions,
        name: key,
        description: LiftTrafficSkillCatalog.Description,
        tools: tools.ToArray()
    );

    return agent;
}).AddA2AServer();

// Configure CORS for frontend access
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Enable CORS
app.UseCors();

var agentBaseUrl = Environment.GetEnvironmentVariable(A2AAgentBaseUrlEnvironmentVariable)
    ?? app.Configuration["ASPNETCORE_URLS"]?.Split(';')[0]
    ?? "http://localhost:5196";
var agentUrl = $"{agentBaseUrl.TrimEnd('/')}/agenta2a";
var hostA2AAgentCard = new AgentCard
{
    Name = LiftTrafficSkillCatalog.AgentName,
    Description = LiftTrafficSkillCatalog.Description,
    Version = "1.0.0",
    SupportedInterfaces = [
        new AgentInterface
        {
            Url = agentUrl,
            ProtocolBinding = "HTTP+JSON",
            ProtocolVersion = "1.0"
        }
    ],
    DefaultInputModes = ["text"],
    DefaultOutputModes = ["text"],
    Capabilities = new AgentCapabilities
    {
        Streaming = true,
        PushNotifications = false
    },
    Skills = [
        new A2A.AgentSkill
        {
            Id = "lift-traffic-analysis",
            Name = "Lift Traffic Analysis",
            Description = "Real-time lift status, wait times, and congestion analysis",
            Examples = [
                "What's the current wait time for Lift 1?",
                "Show me all lift wait times",
                "Which area of the resort is least crowded?",
                "Where should I ski to avoid long lines?"
            ],
            Tags = ["lifts", "traffic", "wait-times", "congestion"]
        }
    ]
};

app.MapWellKnownAgentCard(hostA2AAgentCard);

// Map A2A endpoint
app.MapA2AHttpJson(liftAgentBuilder, "/agenta2a");

app.MapDefaultEndpoints();
app.Run();
