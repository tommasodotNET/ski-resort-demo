using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Extensions.AI;
using Azure.Identity;
using A2A;
using LiftTrafficAgent.Dotnet.Services;
using LiftTrafficAgent.Dotnet.Tools;
using Microsoft.Agents.AI.OpenAI;

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

// Register HttpClientFactory for LiftDataService
builder.Services.AddHttpClient();

// Register services
builder.Services.AddSingleton<LiftDataService>();
builder.Services.AddSingleton<LiftTrafficTools>();

// Register the agent
builder.AddAIAgent("lift-traffic-agent", (sp, key) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    var tools = sp.GetRequiredService<LiftTrafficTools>().GetFunctions();

    var agent = chatClient.CreateAIAgent(
        instructions: @"You are the Lift Traffic Agent for AlpineAI ski resort. You provide real-time lift status, wait times, and congestion analysis. Help skiers find the least crowded areas and plan efficient lift usage.",
        name: key,
        description: "Lift congestion and traffic intelligence agent",
        tools: tools.ToArray()
    );

    return agent;
});

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

// Map A2A endpoint
app.MapA2A("lift-traffic-agent", "/agenta2a", new AgentCard
{
    Name = "lift-traffic-agent",
    Url = app.Configuration["ASPNETCORE_URLS"]?.Split(';')[0] + "/agenta2a" ?? "http://localhost:5196/agenta2a",
    Description = "Lift congestion and traffic intelligence agent",
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
            Name = "Lift Traffic Analysis",
            Description = "Real-time lift status, wait times, and congestion analysis",
            Examples = [
                "What's the current wait time for Lift 1?",
                "Show me all lift wait times",
                "Which area of the resort is least crowded?",
                "Where should I ski to avoid long lines?"
            ]
        }
    ]
});

app.MapDefaultEndpoints();
app.Run();
