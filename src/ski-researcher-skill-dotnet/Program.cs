using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.AspNetCore;
using SkiResearcherSkill.Dotnet.Skills;
using SkiResearcherSkill.Dotnet.Tools;
using System.Data.Common;

#pragma warning disable OPENAI001

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// The ski researcher has no local business logic of its own: it is the existing `ski-researcher-agent-a2a`
// Foundry prompt agent (see apphost.cs's `project.AddPromptAgent("ski-researcher-agent-a2a", ...)`), wrapped
// here purely so its capability can also be discovered/invoked as an MCP skill, without touching the agent's
// own definition or apphost.cs.
var projectConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__projvoiceskiresort")
    ?? throw new InvalidOperationException("ConnectionStrings__projvoiceskiresort is not set.");
var chatConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__gpt41")
    ?? throw new InvalidOperationException("ConnectionStrings__gpt41 is not set.");

DbConnectionStringBuilder projectBuilder = new() { ConnectionString = projectConnectionString };
DbConnectionStringBuilder chatBuilder = new() { ConnectionString = chatConnectionString };
var projectUri = new Uri(projectBuilder["Endpoint"]!.ToString()!);
var deploymentName = chatBuilder["Deployment"]!.ToString()!;

var foundryProjectClient = new AIProjectClient(projectUri, new DefaultAzureCredential());
var skiResearcherAgentName = Environment.GetEnvironmentVariable("SKIRESEARCHER_AGENTNAME")
    ?? throw new InvalidOperationException("SKIRESEARCHER_AGENTNAME is not set.");

var responseClient = foundryProjectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForAgent(new AgentReference(name: skiResearcherAgentName));

var skiResearcherAgent = responseClient
    .AsIChatClient(deploymentName)
    .AsAIAgent(skiResearcherAgentName, description: SkiResearcherSkillCatalog.Description);

builder.Services.AddSingleton(skiResearcherAgent);
builder.Services.AddSingleton<SkiResearcherTools>();
builder.Services.AddSingleton<SkiResearcherSkillResources>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<SkiResearcherTools>()
    .WithResources<SkiResearcherSkillResources>();

var app = builder.Build();

app.MapMcp("/skillsmcp");
app.MapDefaultEndpoints();

app.Run();
