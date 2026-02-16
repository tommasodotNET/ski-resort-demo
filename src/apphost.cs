#:sdk Aspire.AppHost.Sdk@13.1.1
#:package Aspire.Hosting.Azure.AIFoundry@13.1.1-preview.1.26105.8
#:package Aspire.Hosting.Azure.CosmosDB@13.1.1
#:package Aspire.Hosting.Python@13.1.1
#:package Aspire.Hosting.JavaScript@13.1.1

#:project ./advisor-agent-dotnet/AdvisorAgent.Dotnet.csproj
#:project ./lift-traffic-agent-dotnet/LiftTrafficAgent.Dotnet.csproj

var builder = DistributedApplication.CreateBuilder(args);

var tenantId = builder.AddParameterFromConfiguration("tenant", "Azure:TenantId");
var existingFoundryName = builder.AddParameter("existingFoundryName")
    .WithDescription("The name of the existing Azure Foundry resource.");
var existingFoundryResourceGroup = builder.AddParameter("existingFoundryResourceGroup")
    .WithDescription("The resource group of the existing Azure Foundry resource.");

var foundry = builder.AddAzureAIFoundry("foundry")
    .AsExisting(existingFoundryName, existingFoundryResourceGroup);

tenantId.WithParentRelationship(foundry);
existingFoundryName.WithParentRelationship(foundry);
existingFoundryResourceGroup.WithParentRelationship(foundry);

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
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", $"https://{existingFoundryName}.openai.azure.com/")
    .WithEnvironment("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME", "gpt-4.1")
    .WithEnvironment("AZURE_TENANT_ID", tenantId)
    .WithReference(dataGenerator).WaitFor(dataGenerator);

// ---------------------------------------------------------------------------
// Safety Agent (Python)
// ---------------------------------------------------------------------------
var safetyAgent = builder.AddUvicornApp("safety-agent-python", "./safety-agent-python", "safety_agent_python.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", $"https://{existingFoundryName}.openai.azure.com/")
    .WithEnvironment("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME", "gpt-4.1")
    .WithEnvironment("AZURE_TENANT_ID", tenantId)
    .WithReference(dataGenerator).WaitFor(dataGenerator);

// ---------------------------------------------------------------------------
// Ski Coach Agent (Python)
// ---------------------------------------------------------------------------
var coachAgent = builder.AddUvicornApp("ski-coach-agent-python", "./ski-coach-agent-python", "ski_coach_agent_python.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", $"https://{existingFoundryName}.openai.azure.com/")
    .WithEnvironment("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME", "gpt-4.1")
    .WithEnvironment("AZURE_TENANT_ID", tenantId)
    .WithReference(dataGenerator).WaitFor(dataGenerator);

// ---------------------------------------------------------------------------
// Lift Traffic Agent (.NET)
// ---------------------------------------------------------------------------
var liftAgent = builder.AddProject<Projects.LiftTrafficAgent_Dotnet>("lift-traffic-agent-dotnet")
    .WithReference(foundry).WaitFor(foundry)
    .WithReference(dataGenerator).WaitFor(dataGenerator);

// ---------------------------------------------------------------------------
// Advisor Agent (.NET) — Orchestrator
// ---------------------------------------------------------------------------
var advisorAgent = builder.AddProject<Projects.AdvisorAgent_Dotnet>("advisor-agent-dotnet")
    .WithReference(foundry).WaitFor(foundry)
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
