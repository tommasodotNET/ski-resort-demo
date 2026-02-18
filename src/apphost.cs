#:sdk Aspire.AppHost.Sdk@13.3.0-pr.14149.gd67070ad
#:package Aspire.Hosting.Azure.AIFoundry@13.3.0-pr.14149.gd67070ad
#:package Aspire.Hosting.Azure.CosmosDB@13.3.0-pr.14149.gd67070ad
#:package Aspire.Hosting.Python@13.3.0-pr.14149.gd67070ad
#:package Aspire.Hosting.JavaScript@13.3.0-pr.14149.gd67070ad

#:project ./advisor-agent-dotnet/AdvisorAgent.Dotnet.csproj
#:project ./lift-traffic-agent-dotnet/LiftTrafficAgent.Dotnet.csproj

using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);

var tenantId = builder.AddParameterFromConfiguration("tenant", "Azure:TenantId");

var foundry = builder.AddAzureAIFoundry("foundry-ski-resort");
var project = foundry.AddProject("project-ski-resort");
var deployment = foundry.AddDeployment("gpt41", AIFoundryModel.OpenAI.Gpt41);

tenantId.WithParentRelationship(foundry);
// existingFoundryName.WithParentRelationship(foundry);
// existingFoundryResourceGroup.WithParentRelationship(foundry);

#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmos-db")
    .RunAsPreviewEmulator(
        emulator =>
        {
            emulator.WithDataExplorer();
            emulator.WithLifetime(ContainerLifetime.Persistent);
        });
var db = cosmos.AddCosmosDatabase("db");
var conversations = db.AddContainer("conversations", "/conversationId");

// ---------------------------------------------------------------------------
// Data Generator (Python)
// ---------------------------------------------------------------------------
var dataGenerator = builder.AddUvicornApp("data-generator", "./data-generator", "data_generator.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health");

// ---------------------------------------------------------------------------
// Weather Agent (Python)
// ---------------------------------------------------------------------------
var weatherAgent = builder.AddUvicornApp("weather-agent-python", "./weather-agent-python", "weather_agent_python.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health")
    .WithReference(deployment).WaitFor(deployment)
    .WithEnvironment("AZURE_TENANT_ID", tenantId)
    .WithReference(dataGenerator).WaitFor(dataGenerator);

// ---------------------------------------------------------------------------
// Safety Agent (Python)
// ---------------------------------------------------------------------------
var safetyAgent = builder.AddUvicornApp("safety-agent-python", "./safety-agent-python", "safety_agent_python.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health")
    .WithReference(deployment).WaitFor(deployment)
    .WithEnvironment("AZURE_TENANT_ID", tenantId)
    .WithReference(dataGenerator).WaitFor(dataGenerator);

// ---------------------------------------------------------------------------
// Ski Coach Agent (Python)
// ---------------------------------------------------------------------------
var coachAgent = builder.AddUvicornApp("ski-coach-agent-python", "./ski-coach-agent-python", "ski_coach_agent_python.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health")
    .WithReference(deployment).WaitFor(deployment)
    .WithEnvironment("AZURE_TENANT_ID", tenantId)
    .WithReference(dataGenerator).WaitFor(dataGenerator);

// ---------------------------------------------------------------------------
// Lift Traffic Agent (.NET)
// ---------------------------------------------------------------------------
var liftAgent = builder.AddProject<Projects.LiftTrafficAgent_Dotnet>("lift-traffic-agent-dotnet")
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(dataGenerator).WaitFor(dataGenerator);

// ---------------------------------------------------------------------------
// Advisor Agent (.NET) — Orchestrator
// ---------------------------------------------------------------------------
var advisorAgent = builder.AddProject<Projects.AdvisorAgent_Dotnet>("advisor-agent-dotnet")
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(conversations).WaitFor(conversations)
    .WithReference(weatherAgent).WaitFor(weatherAgent)
    .WithReference(liftAgent).WaitFor(liftAgent)
    .WithReference(safetyAgent).WaitFor(safetyAgent)
    .WithReference(coachAgent).WaitFor(coachAgent);

// ---------------------------------------------------------------------------
// Frontend Dashboard (Vite + React)
// ---------------------------------------------------------------------------
builder.AddViteApp("frontend", "./frontend", "dev")
    .WithReference(advisorAgent).WaitFor(advisorAgent)
    .WithReference(dataGenerator).WaitFor(dataGenerator);

builder.Build().Run();
