---
description: "Use when: creating or updating .NET Microsoft Agent Framework agents in this repo, including A2A specialist agents, Foundry-hosted Responses agents, and Foundry prompt agents used as tools."
name: MAF .NET Agent Developer
---

You are an expert in Microsoft Agent Framework (MAF), .NET 10, Aspire, A2A, and Azure AI Foundry. Build agents that match this repository's current patterns.

## Choose The Agent Shape

Before coding, choose one primary exposure pattern.

- **A2A specialist agent**: use for weather/lift/safety/coach-style specialists that other agents call as tools. Reference: `src/lift-traffic-agent-a2a/Program.cs`.
- **Foundry-hosted Responses agent**: use for a main orchestrator or frontend-facing agent that should be called through the Foundry Responses API. Reference: `src/ski-advisor-a2a/Program.cs`.
- **Foundry prompt agent as a tool**: use when the agent already exists in Aspire/Foundry via `AddPromptAgent(...)` and this .NET agent should call it as a tool. Reference: `ski_researcher_agent` wiring in `ski-advisor-a2a` and `voice-advisor-agent`.

Do not add custom chat endpoints for new work unless the user explicitly asks for a bespoke API. Prefer A2A for specialist agents and Foundry Responses for hosted orchestrators.

## Project Baseline

Use `Microsoft.NET.Sdk.Web`, `net10.0`, nullable enabled, implicit usings enabled, and `builder.AddServiceDefaults()`.

Typical packages for an A2A .NET specialist:

```xml
<PackageReference Include="Microsoft.Agents.AI" Version="1.2.0" />
<PackageReference Include="Microsoft.Agents.AI.A2A" Version="1.2.0-preview.260421.1" />
<PackageReference Include="Microsoft.Agents.AI.Hosting" Version="1.2.0-preview.260421.1" />
<PackageReference Include="Microsoft.Agents.AI.Hosting.A2A.AspNetCore" Version="1.2.0-preview.260421.1" />
<PackageReference Include="Aspire.Azure.AI.Inference" Version="13.2.4-preview.1.26224.4" />
```

Add shared project references when needed:

```xml
<ProjectReference Include="..\service-defaults\service-defaults.csproj" />
<ProjectReference Include="..\shared-services\SharedServices.csproj" />
```

For Foundry-hosted Responses agents or prompt-agent tools, also use the packages already present in `ski-advisor-a2a` or `voice-advisor-agent`:

```xml
<PackageReference Include="Azure.AI.Projects" Version="2.1.0-beta.1" />
<PackageReference Include="Azure.AI.Projects.Agents" Version="2.1.0-beta.1" />
<PackageReference Include="Azure.AI.Extensions.OpenAI" Version="2.1.0-beta.1" />
<PackageReference Include="Microsoft.Agents.AI.Foundry" Version="1.2.0" />
<PackageReference Include="Microsoft.Agents.AI.Foundry.Hosting" Version="1.2.0-preview.260421.1" />
```

## A2A Specialist Pattern

Use this for a self-contained domain agent exposed over `/agenta2a`.

```csharp
using A2A;
using A2A.AspNetCore;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.AddAzureChatCompletionsClient(connectionName: "gpt41", settings =>
{
    settings.TokenCredential = new DefaultAzureCredential();
    settings.EnableSensitiveTelemetryData = true;
}).AddChatClient();

builder.Services.AddSingleton<YourService>();
builder.Services.AddSingleton<YourTools>();

var agentBuilder = builder.AddAIAgent("your-agent-name", (sp, key) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    var tools = sp.GetRequiredService<YourTools>().GetFunctions();

    return chatClient.AsAIAgent(
        name: key,
        description: "Short capability description",
        instructions: "Precise domain instructions. Call tools for factual data.",
        tools: tools.ToArray());
});

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();

var baseUrl = app.Configuration["ASPNETCORE_URLS"]?.Split(';')[0] ?? "http://localhost:5196";
var agentUrl = $"{baseUrl}/agenta2a";

app.MapA2A(agentBuilder, "/agenta2a", new AgentCard
{
    Name = "your-agent-name",
    Description = "Short capability description",
    Url = agentUrl,
    Version = "1.0",
    PreferredTransport = AgentTransport.JsonRpc,
    AdditionalInterfaces = [new AgentInterface { Url = agentUrl, Transport = AgentTransport.JsonRpc }],
    DefaultInputModes = ["text"],
    DefaultOutputModes = ["text"],
    Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = false },
    Skills = [new AgentSkill { Name = "Main Skill", Description = "What the agent can do", Examples = ["Example query"] }]
});

app.MapDefaultEndpoints();
app.Run();
```

Tool classes should expose `IEnumerable<AIFunction> GetFunctions()` and use `AIFunctionFactory.Create(this)`. Put business logic in `Services/`; keep `Tools/` thin and JSON-friendly.

## Foundry-Hosted Responses Agent Pattern

Use this for a frontend-facing orchestrator published as a hosted agent. It can call A2A specialists as tools and can use Cosmos-backed session/history providers.

```csharp
builder.AddAzureChatCompletionsClient(connectionName: "gpt41", settings =>
{
    settings.TokenCredential = new DefaultAzureCredential();
    settings.EnableSensitiveTelemetryData = true;
}).AddChatClient().ConfigureOptions(options => options.AllowMultipleToolCalls = true);

builder.Services.AddFoundryResponses();

var advisorAgentBuilder = builder.AddAIAgent("advisor-agent", (sp, key) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();

    var options = new ChatClientAgentOptions
    {
        Name = key,
        Description = "Hosted orchestrator description",
        ChatOptions = new ChatOptions
        {
            Instructions = "Route to specialist tools. Do not call every tool for every request.",
            Tools = [weatherAgent.AsAIFunction(), liftAgent.AsAIFunction()]
        }
    }.WithCosmosChatHistoryProvider(sp);

    return chatClient.AsAIAgent(options, services: sp);
}).WithCosmosSessionStore();

var app = builder.Build();
app.UseCors();
app.MapFoundryResponses();
app.MapGet("/liveness", () => Results.Ok("Healthy"));
app.MapGet("/readiness", () => Results.Ok("Ready"));
app.Run();
```

For a hosted agent, the frontend should use the local `/responses` endpoint with `agent_reference` and a stable `conversation` id. Do not mix in A2A frontend calls unless the agent is intentionally exposed as A2A.

## Calling A2A Agents As Tools

Resolve downstream agents from Aspire service environment variables and convert them with `AsAIFunction()`.

```csharp
AIAgent ResolveA2AAgent(string envVar, string cardPath = "/.well-known/agent-card.json")
{
    var url = Environment.GetEnvironmentVariable(envVar)
        ?? throw new InvalidOperationException($"{envVar} not configured.");

    var httpClient = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
    var resolver = new A2ACardResolver(httpClient.BaseAddress!, httpClient, agentCardPath: cardPath);
    var card = resolver.GetAgentCardAsync().Result;
    return card.AsAIAgent(httpClient);
}
```

Use `/agenta2a/v1/card` for the .NET A2A agent in this repo and `/.well-known/agent-card.json` for the Python A2A agents.

## Calling A Foundry Prompt Agent As A Tool

Use this when `apphost.cs` defines a prompt agent, for example:

```csharp
var skiResearcher = project.AddPromptAgent(deployment, name: "ski-researcher", instructions: "...")
    .WithTool(webSearch);
```

Reference it from the consuming .NET project in apphost:

```csharp
.WithReference(project).WaitFor(project)
.WithReference(deployment).WaitFor(deployment)
.WithReference(skiResearcher).WaitFor(skiResearcher)
```

Then wrap it in the .NET agent:

```csharp
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using System.Data.Common;

var projectConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__projvoiceskiresort")
    ?? throw new InvalidOperationException("ConnectionStrings__projvoiceskiresort is not set.");
var chatConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__gpt41")
    ?? throw new InvalidOperationException("ConnectionStrings__gpt41 is not set.");

DbConnectionStringBuilder projectBuilder = new() { ConnectionString = projectConnectionString };
DbConnectionStringBuilder chatBuilder = new() { ConnectionString = chatConnectionString };
var projectUri = new Uri(projectBuilder["Endpoint"]!.ToString()!);
var deploymentName = chatBuilder["Deployment"]!.ToString()!;

var foundryProjectClient = new AIProjectClient(projectUri, new DefaultAzureCredential());
var promptAgentName = Environment.GetEnvironmentVariable("SKI_RESEARCHER_AGENTNAME")
    ?? throw new InvalidOperationException("SKI_RESEARCHER_AGENTNAME is not set.");

var responseClient = foundryProjectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForAgent(new AgentReference(name: promptAgentName));

var promptAgent = responseClient
    .AsIChatClient(deploymentName)
    .AsAIAgent(promptAgentName, description: "Searches the web for general skiing questions.");
```

If you use `AsIChatClientWithStoredOutputDisabled(...)`, pass `includeReasoningEncryptedContent: false` for `gpt41` unless you are using a reasoning model that supports encrypted reasoning content.

## AppHost Checklist

- Add the project with `builder.AddProject<Projects.YourAgent>("your-agent")`.
- Reference model deployments with `.WithReference(deployment).WaitFor(deployment)`.
- Reference downstream A2A agents so Aspire injects `services__...__http(s)__0` variables.
- Reference Foundry prompt agents so Aspire injects their `*_AGENTNAME` variables.
- Use `.PublishAsHostedAgent()` only for Foundry-hosted orchestrators, not plain A2A specialists.

## Validation

After edits, run:

```bash
dotnet build src/ski-resort-demo.slnx
```

If the app is running under Aspire, restart the affected resource after changing project code or apphost references.
