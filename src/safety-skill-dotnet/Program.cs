using SafetySkill.Dotnet.Services;
using SafetySkill.Dotnet.Skills;
using SafetySkill.Dotnet.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register HttpClientFactory for SafetyDataService (calls the data-generator via Aspire service discovery).
builder.Services.AddHttpClient();

// Register services/tools
builder.Services.AddSingleton<SafetyDataService>();
builder.Services.AddSingleton<SafetyTools>();

// This project is a standalone MCP skill-provider server: it does NOT host an A2A agent. The existing
// safety-agent-python A2A agent (resource "safetyagent" in apphost.cs) remains completely unchanged; this
// server is an additive counterpart exposing the same capability as an SEP-2640 skill (discovery index +
// SKILL.md) plus literal MCP tools ("scripts") that a skills-based orchestrator can call directly.
// See Skills/SafetySkillCatalog.cs and Skills/SafetySkillResources.cs.
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "safety-skill", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithTools<SafetyTools>()
    .WithResources<SafetySkillResources>();

var app = builder.Build();

// Map the MCP skill endpoint (streamable HTTP). A skills-based orchestrator connects an McpClient here to
// discover this skill (SKILL.md) and invoke its tools ("scripts") directly.
app.MapMcp("/skillsmcp");

app.MapDefaultEndpoints();
app.Run();
