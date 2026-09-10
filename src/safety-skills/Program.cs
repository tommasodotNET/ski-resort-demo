using SafetySkill.Dotnet.Services;
using SafetySkill.Dotnet.Skills;
using SafetySkill.Dotnet.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register HttpClientFactory for SafetyDataService (calls the data-generator via Aspire service discovery).
builder.Services.AddHttpClient();

// Register the reusable domain service behind the typed MCP tools.
builder.Services.AddSingleton<SafetyDataService>();

// Standalone MCP provider, separate from the safety A2A agent.
// Resources carry only the discovery index and SKILL.md; typed tools provide every operation.
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "safetyskills", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithResources<SafetySkillResources>()
    .WithTools<SafetyTools>();

var app = builder.Build();

// Native MAF progressive disclosure discovers and loads the typed tools over streamable HTTP.
app.MapMcp("/skillsmcp");

app.MapDefaultEndpoints();
app.Run();
