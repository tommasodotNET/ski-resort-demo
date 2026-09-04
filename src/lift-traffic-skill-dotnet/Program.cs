using LiftTrafficSkill.Dotnet.Services;
using LiftTrafficSkill.Dotnet.Skills;
using LiftTrafficSkill.Dotnet.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register HttpClientFactory for LiftDataService (calls the data-generator via Aspire service discovery).
builder.Services.AddHttpClient();

// Register services/tools
builder.Services.AddSingleton<LiftDataService>();
builder.Services.AddSingleton<LiftTrafficTools>();

// This project is a standalone MCP skill-provider server: it does NOT host an A2A agent. The existing
// lift-traffic-agent-dotnet A2A agent (Aspire resource "lift-traffic-agent-a2a") remains completely unchanged;
// this server is a separate, dedicated additive counterpart (Aspire resource "lift-traffic-agent-skill")
// exposing the same capability as an SEP-2640 skill (discovery index + SKILL.md) plus literal MCP tools
// ("scripts") that a skills-based orchestrator can call directly.
// See Skills/LiftTrafficSkillCatalog.cs and Skills/LiftTrafficSkillResources.cs.
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "lift-traffic-skill", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithTools<LiftTrafficTools>()
    .WithResources<LiftTrafficSkillResources>();

var app = builder.Build();

// Map the MCP skill endpoint (streamable HTTP). A skills-based orchestrator connects an McpClient here to
// discover this skill (SKILL.md) and invoke its tools ("scripts") directly.
app.MapMcp("/skillsmcp");

app.MapDefaultEndpoints();
app.Run();
