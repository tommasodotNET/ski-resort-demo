using SkiCoachSkill.Dotnet.Services;
using SkiCoachSkill.Dotnet.Skills;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register HttpClientFactory for CoachDataService (calls the data-generator via Aspire service discovery).
builder.Services.AddHttpClient();

// Register the domain service used directly by the MCP resources.
builder.Services.AddSingleton<CoachDataService>();

// This project is a standalone MCP skill-provider server: it does NOT host an A2A agent. The existing
// ski-coach-agent-a2a A2A agent (resource "skicoachagenta2a" in apphost.cs) remains separate; this
// server is an additive counterpart exposing the same capability as an SEP-2640 skill (discovery index +
// SKILL.md) plus live sibling resources that a skills-based orchestrator reads on demand.
// See Skills/CoachSkillCatalog.cs and Skills/CoachSkillResources.cs.
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "skicoachskills", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithResources<CoachSkillResources>();

var app = builder.Build();

// Map the MCP skill endpoint (streamable HTTP). A skills-based orchestrator discovers SKILL.md here and
// reads the skill's live sibling resources through read_skill_resource.
app.MapMcp("/skillsmcp");

app.MapDefaultEndpoints();
app.Run();
