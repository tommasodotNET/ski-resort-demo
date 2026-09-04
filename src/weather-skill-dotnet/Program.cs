using WeatherSkill.Dotnet.Services;
using WeatherSkill.Dotnet.Skills;
using WeatherSkill.Dotnet.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register HttpClientFactory for WeatherDataService (calls the data-generator via Aspire service discovery).
builder.Services.AddHttpClient();

// Register services/tools
builder.Services.AddSingleton<WeatherDataService>();
builder.Services.AddSingleton<WeatherTools>();

// This project is a standalone MCP skill-provider server: it does NOT host an A2A agent. The existing
// weather-agent-python A2A agent (resource "weatheragent" in apphost.cs) remains completely unchanged; this
// server is an additive counterpart exposing the same capability as an SEP-2640 skill (discovery index +
// SKILL.md) plus literal MCP tools ("scripts") that a skills-based orchestrator can call directly.
// See Skills/WeatherSkillCatalog.cs and Skills/WeatherSkillResources.cs.
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "weather-skill", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithTools<WeatherTools>()
    .WithResources<WeatherSkillResources>();

var app = builder.Build();

// Map the MCP skill endpoint (streamable HTTP). A skills-based orchestrator connects an McpClient here to
// discover this skill (SKILL.md) and invoke its tools ("scripts") directly.
app.MapMcp("/skillsmcp");

app.MapDefaultEndpoints();
app.Run();
