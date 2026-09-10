using WeatherSkill.Dotnet.Services;
using WeatherSkill.Dotnet.Skills;
using WeatherSkill.Dotnet.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register HttpClientFactory for WeatherDataService (calls the data-generator via Aspire service discovery).
builder.Services.AddHttpClient();

// Register the reusable domain service behind the typed MCP tools.
builder.Services.AddSingleton<WeatherDataService>();

// Standalone MCP provider, separate from the weather A2A agent.
// Resources carry only the discovery index and SKILL.md; typed tools provide every operation.
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "weatherskills", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithResources<WeatherSkillResources>()
    .WithTools<WeatherTools>();

var app = builder.Build();

// Native MAF progressive disclosure discovers and loads the typed tools over streamable HTTP.
app.MapMcp("/skillsmcp");

app.MapDefaultEndpoints();
app.Run();
