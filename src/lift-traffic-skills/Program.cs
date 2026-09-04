using LiftTrafficSkill.Dotnet.Services;
using LiftTrafficSkill.Dotnet.Skills;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register HttpClientFactory for LiftDataService (calls the data-generator via Aspire service discovery).
builder.Services.AddHttpClient();

// Register the domain service used directly by the MCP resources.
builder.Services.AddSingleton<LiftDataService>();

// This project is a standalone MCP skill-provider server: it does NOT host an A2A agent. The existing
// lift-traffic-agent-a2a A2A agent (Aspire resource "lifttrafficagenta2a") remains separate;
// this server is a separate, dedicated additive counterpart (Aspire resource "lifttrafficskills")
// exposing the same capability as an SEP-2640 skill (discovery index + SKILL.md) plus live sibling
// resources that a skills-based orchestrator reads on demand.
// See Skills/LiftTrafficSkillCatalog.cs and Skills/LiftTrafficSkillResources.cs.
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "lifttrafficskills", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithResources<LiftTrafficSkillResources>();

var app = builder.Build();

// Map the MCP skill endpoint (streamable HTTP). A skills-based orchestrator discovers SKILL.md here and
// reads the skill's live sibling resources through read_skill_resource.
app.MapMcp("/skillsmcp");

app.MapDefaultEndpoints();
app.Run();
