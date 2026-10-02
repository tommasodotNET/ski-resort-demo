#:sdk Aspire.AppHost.Sdk@13.5.3
#:package Aspire.Hosting.Azure.AppContainers@13.5.3
#:package Aspire.Hosting.Foundry@13.5.3-preview.1.26425.3
#:package Aspire.Hosting.Azure.CosmosDB@13.5.3
#:package Aspire.Hosting.Python@13.5.3
#:package Aspire.Hosting.JavaScript@13.5.3
#:package CommunityToolkit.Aspire.Hosting.Golang@13.3.0

#:project ./ski-advisor-a2a/AdvisorAgent.Dotnet.csproj
#:project ./a2a/lift-traffic-agent-a2a/LiftTrafficAgent.Dotnet.csproj
#:project ./skills/lift-traffic-skills/LiftTrafficSkill.Dotnet.csproj
#:project ./responses-gateway/ResponsesGateway.csproj
#:project ./skills/safety-skills/SafetySkill.Dotnet.csproj
#:project ./skills/ski-coach-skills/SkiCoachSkill.Dotnet.csproj
#:project ./voice-advisor-agent/VoiceAdvisorAgent.csproj
#:project ./skills/weather-skills/WeatherSkill.Dotnet.csproj

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Foundry;
using Azure.Provisioning.CosmosDB;

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

var skiResearcher = project.AddPromptAgent("skiresearcher", deployment,
    instructions: """You are a ski researcher agent. Your job is to research and provide information about ski.""")
    .WithTool(webSearch);

#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmosdb")
    .ConfigureInfrastructure(infra =>
    {
        var account = infra.GetProvisionableResources().OfType<CosmosDBAccount>().Single();
        // Approved non-production policy exemption; Foundry connects over the public endpoint.
        account.Tags["SecurityControl"] = "Ignore";
        account.PublicNetworkAccess = CosmosDBPublicNetworkAccess.Enabled;
    })
    .RunAsPreviewEmulator(
        emulator =>
        {
            emulator.WithDataExplorer();
            emulator.WithLifetime(ContainerLifetime.Persistent);
        });
var db = cosmos.AddCosmosDatabase("db");
var conversations = db.AddContainer("conversations", "/conversationId");
var sessions = db.AddContainer("sessions", "/conversationId");
var skillHistory = db.AddContainer("skillhistory", "/session_id");

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
var weatherAgent = builder.AddUvicornApp("weatheragenta2a", "./a2a/weather-agent-a2a", "weather_agent_python.main:app")
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
var safetyAgent = builder.AddUvicornApp("safetyagenta2a", "./a2a/safety-agent-a2a", "safety_agent_python.main:app")
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
var coachAgent = builder.AddUvicornApp("skicoachagenta2a", "./a2a/ski-coach-agent-a2a", "ski_coach_agent_python.main:app")
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
var liftAgent = builder.AddProject("lifttrafficagenta2a", "./a2a/lift-traffic-agent-a2a/LiftTrafficAgent.Dotnet.csproj")
    .WithExternalHttpEndpoints()
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithComputeEnvironment(aca);
liftAgent.WithEnvironment(A2AAgentBaseUrlEnvironmentVariable, liftAgent.GetEndpoint("http"));

// ---------------------------------------------------------------------------
// MCP-hosted Agent Skills (.NET)
// ---------------------------------------------------------------------------
var weatherSkill = builder.AddProject("weatherskills", "./skills/weather-skills/WeatherSkill.Dotnet.csproj")
    .WithHttpEndpoint()
    .WithExternalHttpEndpoints()
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithComputeEnvironment(aca);

var safetySkill = builder.AddProject("safetyskills", "./skills/safety-skills/SafetySkill.Dotnet.csproj")
    .WithHttpEndpoint()
    .WithExternalHttpEndpoints()
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithComputeEnvironment(aca);

var coachSkill = builder.AddProject("skicoachskills", "./skills/ski-coach-skills/SkiCoachSkill.Dotnet.csproj")
    .WithHttpEndpoint()
    .WithExternalHttpEndpoints()
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithComputeEnvironment(aca);

var liftSkill = builder.AddProject("lifttrafficskills", "./skills/lift-traffic-skills/LiftTrafficSkill.Dotnet.csproj")
    .WithHttpEndpoint()
    .WithExternalHttpEndpoints()
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithComputeEnvironment(aca);

// ---------------------------------------------------------------------------
// Skills Orchestrator A2A adapter (Python, used by Voice Live)
// ---------------------------------------------------------------------------
var skillsAdvisorA2A = builder.AddUvicornApp(
        "skiadvisorskilla2a",
        "./ski-advisor-skill",
        "skills_orchestrator_python.main:app")
    .WithUv()
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(weatherSkill).WaitFor(weatherSkill)
    .WithReference(safetySkill).WaitFor(safetySkill)
    .WithReference(coachSkill).WaitFor(coachSkill)
    .WithReference(liftSkill).WaitFor(liftSkill)
    .WithReference(skiResearcher).WaitFor(skiResearcher)
    .WithReference(skillHistory).WaitFor(skillHistory)
    .WithComputeEnvironment(aca);
skillsAdvisorA2A.WithEnvironment(A2AAgentBaseUrlEnvironmentVariable, skillsAdvisorA2A.GetEndpoint("http"));

// ---------------------------------------------------------------------------
// Skills Orchestrator (Python) — Foundry hosted Responses agent
// ---------------------------------------------------------------------------
var skillsAdvisor = builder.AddPythonExecutable(
        "skiadvisorskill",
        "./ski-advisor-skill",
        "start-responses")
    .WithUv()
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(weatherSkill).WaitFor(weatherSkill)
    .WithReference(safetySkill).WaitFor(safetySkill)
    .WithReference(coachSkill).WaitFor(coachSkill)
    .WithReference(liftSkill).WaitFor(liftSkill)
    .WithReference(skiResearcher).WaitFor(skiResearcher)
    .WithReference(skillHistory).WaitFor(skillHistory)
    .WithComputeEnvironment(aca)
    .WithHttpEndpoint(targetPort: 8089)
    .AsHostedAgent(project, HostedAgentProtocol.Responses, "2.0.0");

// ---------------------------------------------------------------------------
// A2A Orchestrator (.NET)
// ---------------------------------------------------------------------------
var advisorAgent = builder.AddProject("skiadvisora2a", "./ski-advisor-a2a/AdvisorAgent.Dotnet.csproj")
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(weatherAgent).WaitFor(weatherAgent)
    .WithReference(liftAgent).WaitFor(liftAgent)
    .WithReference(safetyAgent).WaitFor(safetyAgent)
    .WithReference(coachAgent).WaitFor(coachAgent)
    .WithReference(skiResearcher).WaitFor(skiResearcher)
    .AsHostedAgent(project, HostedAgentProtocol.Responses, "2.0.0" );

// ---------------------------------------------------------------------------
// Voice Advisor Agents (.NET) — architecture-specific Voice Live bridges
// ---------------------------------------------------------------------------
var voiceAdvisorA2A = builder.AddProject("voiceadvisora2a", "./voice-advisor-agent/VoiceAdvisorAgent.csproj")
    .WithReference(project).WaitFor(project)
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(voiceDeployment).WaitFor(voiceDeployment)
    .WithReference(conversations).WaitFor(conversations)
    .WithReference(weatherAgent).WaitFor(weatherAgent)
    .WithReference(liftAgent).WaitFor(liftAgent)
    .WithReference(safetyAgent).WaitFor(safetyAgent)
    .WithReference(coachAgent).WaitFor(coachAgent)
    .WithReference(skiResearcher).WaitFor(skiResearcher)
    .WithEnvironment("VOICE_ADVISOR_ARCHITECTURE", "a2a")
    .WithComputeEnvironment(aca);

var voiceAdvisorSkill = builder.AddProject("voiceadvisorskill", "./voice-advisor-agent/VoiceAdvisorAgent.csproj")
    .WithReference(voiceDeployment).WaitFor(voiceDeployment)
    .WithReference(conversations).WaitFor(conversations)
    .WithReference(skillsAdvisorA2A).WaitFor(skillsAdvisorA2A)
    .WithEnvironment("VOICE_ADVISOR_ARCHITECTURE", "skill")
    .WithComputeEnvironment(aca);

// ---------------------------------------------------------------------------
// Frontend Dashboard (Vite + React)
// ---------------------------------------------------------------------------
var frontend = builder.AddViteApp("frontend", "./frontend", "dev")
    .WithReference(voiceAdvisorA2A).WaitFor(voiceAdvisorA2A)
    .WithReference(voiceAdvisorSkill).WaitFor(voiceAdvisorSkill)
    .WithReference(dataGenerator).WaitFor(dataGenerator)
    .WithReference(advisorAgent).WaitFor(advisorAgent)
    .WithReference(skillsAdvisor).WaitFor(skillsAdvisor)
    .WithUrls((e) =>
    {
        e.Urls.Clear();
        e.Urls.Add(new() { Url = "/", DisplayText = "⛷️ Ski Resort Dashboard", Endpoint = e.GetEndpoint("http") });
    })
    .WithComputeEnvironment(aca);

if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddProject("frontendgateway", "./responses-gateway/ResponsesGateway.csproj")
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints()
        .WithReference(advisorAgent).WaitFor(advisorAgent)
        .WithReference(skillsAdvisor).WaitFor(skillsAdvisor)
        .WithReference(dataGenerator).WaitFor(dataGenerator)
        .WithReference(voiceAdvisorA2A).WaitFor(voiceAdvisorA2A)
        .WithReference(voiceAdvisorSkill).WaitFor(voiceAdvisorSkill)
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
