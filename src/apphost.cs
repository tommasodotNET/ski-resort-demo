#:sdk Aspire.AppHost.Sdk@13.4.6
#:package Aspire.Hosting.Azure.AppContainers@13.4.6
#:package Aspire.Hosting.Foundry@13.4.6-preview.1.26319.6
#:package Aspire.Hosting.Azure.CosmosDB@13.4.6
#:package Aspire.Hosting.Python@13.4.6
#:package Aspire.Hosting.JavaScript@13.4.6
#:package CommunityToolkit.Aspire.Hosting.Golang@13.3.0

#:project ./advisor-agent-dotnet/AdvisorAgent.Dotnet.csproj
#:project ./lift-traffic-agent-dotnet/LiftTrafficAgent.Dotnet.csproj
#:project ./responses-gateway/ResponsesGateway.csproj
#:project ./voice-advisor-agent/VoiceAdvisorAgent.csproj

using Aspire.Hosting.Foundry;
using Azure.AI.Projects.Agents;

var builder = DistributedApplication.CreateBuilder(args);
const string A2AAgentBaseUrlEnvironmentVariable = "A2A_AGENT_BASE_URL";

var aca = builder.AddAzureContainerAppEnvironment("aca");

var foundry = builder.AddFoundry("aifskiresort");
var project = foundry.AddProject("projvoiceskiresort");
var deployment = project.AddModelDeployment("gpt41", FoundryModel.OpenAI.Gpt41)
    .WithProperties(configure => configure.SkuCapacity = 10);
var voiceDeployment = project.AddModelDeployment("gptrealtime", FoundryModel.OpenAI.GptRealtime)
    .WithProperties(configure => configure.SkuCapacity = 5);

var webSearch = project.AddWebSearchTool("websearch");

var skiResearcher =project.AddPromptAgent("skiresearcher", deployment,
    instructions: """You are a ski researcher agent. Your job is to research and provide information about ski.""")
    .WithTool(webSearch);

#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmosdb")
    .RunAsPreviewEmulator(
        emulator =>
        {
            emulator.WithDataExplorer();
            emulator.WithLifetime(ContainerLifetime.Persistent);
        });
var db = cosmos.AddCosmosDatabase("db");
var conversations = db.AddContainer("conversations", "/conversationId");
var sessions = db.AddContainer("sessions", "/conversationId");

// ---------------------------------------------------------------------------
// Data Generator (Go)
// ---------------------------------------------------------------------------
var dataGenerator = builder.AddGolangApp("datagenerator", "./data-generator")
    .WithGoModTidy()
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithComputeEnvironment(aca);

// ---------------------------------------------------------------------------
// Weather Agent (Python)
// ---------------------------------------------------------------------------
var weatherAgent = builder.AddUvicornApp("weatheragent", "./weather-agent-python", "weather_agent_python.main:app")
    .WithUv()
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithComputeEnvironment(aca);
weatherAgent.WithEnvironment(A2AAgentBaseUrlEnvironmentVariable, weatherAgent.GetEndpoint("http"));

// ---------------------------------------------------------------------------
// Safety Agent (Python)
// ---------------------------------------------------------------------------
var safetyAgent = builder.AddUvicornApp("safetyagent", "./safety-agent-python", "safety_agent_python.main:app")
    .WithUv()
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithComputeEnvironment(aca);
safetyAgent.WithEnvironment(A2AAgentBaseUrlEnvironmentVariable, safetyAgent.GetEndpoint("http"));

// ---------------------------------------------------------------------------
// Ski Coach Agent (Python)
// ---------------------------------------------------------------------------
var coachAgent = builder.AddUvicornApp("skicoachagent", "./ski-coach-agent-python", "ski_coach_agent_python.main:app")
    .WithUv()
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithComputeEnvironment(aca);
coachAgent.WithEnvironment(A2AAgentBaseUrlEnvironmentVariable, coachAgent.GetEndpoint("http"));

// ---------------------------------------------------------------------------
// Lift Traffic Agent (.NET)
// ---------------------------------------------------------------------------
var liftAgent = builder.AddProject<Projects.LiftTrafficAgent_Dotnet>("lifttrafficagent")
    .WithExternalHttpEndpoints()
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithComputeEnvironment(aca);
liftAgent.WithEnvironment(A2AAgentBaseUrlEnvironmentVariable, liftAgent.GetEndpoint("http"));

// ---------------------------------------------------------------------------
// Advisor Agent (.NET) — Orchestrator
// ---------------------------------------------------------------------------
var advisorAgent = builder.AddProject<Projects.AdvisorAgent_Dotnet>("advisoragent")
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(weatherAgent).WaitFor(weatherAgent)
    .WithReference(liftAgent).WaitFor(liftAgent)
    .WithReference(safetyAgent).WaitFor(safetyAgent)
    .WithReference(coachAgent).WaitFor(coachAgent)
    .WithReference(skiResearcher).WaitFor(skiResearcher)
    .AsHostedAgent(project, configure => configure.ContainerProtocolVersions.Add(new ProtocolVersionRecord("responses", "2.0.0")));

// ---------------------------------------------------------------------------
// Voice Advisor Agent (.NET) — Voice orchestrator via WebSocket + Voice Live
// ---------------------------------------------------------------------------
var voiceAdvisorAgent = builder.AddProject<Projects.VoiceAdvisorAgent>("voiceadvisoragent")
    .WithReference(project).WaitFor(project)
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(voiceDeployment).WaitFor(voiceDeployment)
    .WithReference(conversations).WaitFor(conversations)
    .WithReference(weatherAgent).WaitFor(weatherAgent)
    .WithReference(liftAgent).WaitFor(liftAgent)
    .WithReference(safetyAgent).WaitFor(safetyAgent)
    .WithReference(coachAgent).WaitFor(coachAgent)
    .WithReference(skiResearcher).WaitFor(skiResearcher)
    .WithComputeEnvironment(aca);

// ---------------------------------------------------------------------------
// Frontend Dashboard (Vite + React)
// ---------------------------------------------------------------------------
var frontend = builder.AddViteApp("frontend", "./frontend", "dev")
    .WithReference(voiceAdvisorAgent).WaitFor(voiceAdvisorAgent)
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithReference(advisorAgent).WaitFor(advisorAgent)
    .WithUrls((e) =>
    {
        e.Urls.Clear();
        e.Urls.Add(new() { Url = "/", DisplayText = "⛷️ Ski Resort Dashboard", Endpoint = e.GetEndpoint("http") });
    })
    .WithComputeEnvironment(aca);

if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddProject<Projects.ResponsesGateway>("frontendgateway")
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints()
        .WithReference(advisorAgent).WaitFor(advisorAgent)
        .WithReference(dataGenerator).WaitFor(dataGenerator)
        .WithReference(voiceAdvisorAgent).WaitFor(voiceAdvisorAgent)
        .WithReference(project).WaitFor(project)
        .WithHttpHealthCheck("/readiness")
        .PublishWithContainerFiles(frontend, "./wwwroot")
        .WithUrls((e) =>
        {
            e.Urls.Clear();
            e.Urls.Add(new() { Url = "/", DisplayText = "⛷️ Ski Resort Dashboard", Endpoint = e.GetEndpoint("http") });
        })
        .WithComputeEnvironment(aca);
}

if (builder.ExecutionContext.IsRunMode)
{

    builder.AddViteApp("slides", "../slides", "start")
        .WithUrls((e) =>
        {
            e.Urls.Clear();
            e.Urls.Add(new() { Url = "/", DisplayText = "Slides", Endpoint = e.GetEndpoint("http") });
        });
}

builder.Build().Run();
