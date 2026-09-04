# 🏔️ AlpineAI – Multi-Agent Ski Resort Demo

A distributed ski resort system built with **Microsoft Agent Framework (MAF)**, **Azure AI Foundry**, **A2A**, **Agent Skills over MCP**, **Voice Live**, and **Aspire**.

An AI-powered ski resort concierge that coordinates weather intelligence, lift traffic, safety evaluation, personalized coaching, web-backed ski research, and voice conversations through a network of specialist agents — all orchestrated by hosted advisor experiences and displayed on a real-time dashboard.

## Architecture

| Component | Language | Role |
|---|---|---|
| **`skiadvisora2a`** | .NET | Existing Foundry-hosted orchestrator using specialists as A2A tools |
| **`skiadvisorskill`** | Python | Parallel orchestrator discovering remote SEP-2640 skills over MCP, with Cosmos-backed conversation history |
| **`voiceadvisora2a`** | .NET | Voice Live bridge exposing the four specialist A2A agents plus the ski researcher |
| **`voiceadvisorskill`** | .NET | Voice Live bridge exposing only the `skiadvisorskilla2a` orchestrator |
| **Compact A2A resources** | Python/.NET | `weatheragenta2a`, `safetyagenta2a`, `skicoachagenta2a`, and `lifttrafficagenta2a` |
| **Compact skill resources** | .NET | `weatherskills`, `safetyskills`, `skicoachskills`, and `lifttrafficskills`, exposing `skill://index.json`, `SKILL.md`, and live resources |
| **Ski Researcher** | Foundry | Existing web-backed prompt agent used directly as a tool by both orchestrators |
| **Data Generator** | Go | Continuously generates synthetic resort telemetry |
| **Frontend** | React/Vite | Real-time dashboard with AI chat and voice controls |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Python 3.11+](https://www.python.org/downloads/)
- [uv](https://docs.astral.sh/uv/) (Python package manager)
- [Go 1.23+](https://go.dev/doc/install)
- [Node.js 20+](https://nodejs.org/)
- [Aspire CLI](https://aspire.dev/get-started/install-cli/)
- An **Azure AI Foundry** resource with a `gpt-4.1` (or similar) deployment
- **Azure CLI** authenticated (`az login`)

### Install Aspire CLI

Refer to the [official Aspire documentation](https://aspire.dev/get-started/install-cli/) for installation instructions.

## Setup

### 1. Clone the repository

```bash
git clone https://github.com/tommasodotNET/ski-resort-demo.git
cd ski-resort-demo
```

### 2. Configure Azure settings

Edit `src/apphost.settings.Development.json` with your Azure details:

```json
{
    "Azure": {
        "TenantId": "<your-tenant-id>",
        "SubscriptionId": "<your-subscription-id>",
        "AllowResourceGroupCreation": true,
        "ResourceGroup": "<your-resource-group>",
        "Location": "<your-azure-region>",
        "CredentialSource": "AzureCli"
    }
}
```

> **Note:** The Azure AI Foundry resource must have a chat completion model deployed (e.g., `gpt-4.1`). The deployment name is configured in the Aspire AppHost.

### 3. Run the application

From the `src/` directory:

```bash
cd src
aspire start
```

This starts both orchestrators, all A2A specialists, their paired .NET MCP skill
providers, the shared researcher tool, both voice bridges, frontend, data generator, Foundry resources, and
Cosmos DB emulator.

Open the **Aspire dashboard** (URL shown in terminal output) to see all services, logs, and distributed traces.

The **frontend** will be available at the URL assigned by Aspire (shown in the dashboard).

## Project Structure

```
src/
├── apphost.cs                      # Aspire orchestration (all services wired here)
├── apphost.settings.Development.json  # Azure configuration
├── ski-advisor-a2a/              # skiadvisora2a orchestrator
├── ski-advisor-skill/            # skiadvisorskill + skiadvisorskilla2a surfaces
├── voice-advisor-agent/            # Shared .NET project for both Voice Live resources
├── lift-traffic-agent-a2a/       # .NET lift traffic A2A agent
├── {weather,safety,ski-coach,lift-traffic}-skills/ # .NET MCP providers
├── weather-agent-a2a/            # Python weather A2A agent
├── safety-agent-a2a/             # Python safety A2A agent
├── ski-coach-agent-a2a/          # Python ski coach A2A agent
├── data-generator/                 # Go data generator
├── frontend/                       # Vite + React + Tailwind dashboard
├── shared-services/                # .NET shared library (Cosmos, thread store)
└── service-defaults/               # Aspire service defaults
```

## Configuration

### Data Generator

The data generation speed and drift magnitudes are configurable via `src/data-generator/config.json`:

```json
{
  "update_interval_seconds": { "min": 5, "max": 10 },
  "weather": { "temperature_drift": 0.1, "wind_speed_drift": 0.5, ... },
  "lifts": { "queue_drift": 3, "status_change_probability": 0.002 },
  ...
}
```

### Frontend

The dashboard polling interval is configurable via `src/frontend/public/config.json`:

```json
{
  "pollingIntervalMs": 10000
}
```

Changes are picked up automatically without restarting.

## How It Works

1. **Data Generator** continuously produces synthetic weather, lift, slope, and safety telemetry via a REST API.

2. Weather, lift, safety, and coach each have an existing **A2A** resource and a paired .NET **MCP Agent Skill** resource. Skill instructions and live data resources are published remotely rather than defined in the skills orchestrator.

3. **Ski Researcher Agent** remains an Azure AI Foundry prompt agent with web search. Both orchestrators register it directly as an agent tool, demonstrating that one agent can combine tools and skills.

4. **`skiadvisora2a`** preserves the existing agent-as-tool architecture. **`skiadvisorskill`** is a Python MAF agent that discovers the remote skills and reads their MCP resources through `SkillsProvider`; its only explicit non-skill tool is the Foundry ski researcher.

5. **`voiceadvisora2a`** and **`voiceadvisorskill`** run the same Voice Live bridge with architecture-specific configuration. The A2A resource registers only the four specialist A2A agents plus the ski researcher; the skill resource registers only `skiadvisorskilla2a`. The voice conversation ID is preserved through either route and becomes the remote skills-orchestrator session ID when that tool is used.

6. **Frontend** displays real-time data panels, provides an AI chat panel, and sends voice sessions to `/ws/voice/a2a` or `/ws/voice/skill` according to the selected architecture.

## Agent as a Skill over MCP

This sample deliberately implements the same four specialist domains in two
different ways:

| Agent as a tool | Agent as a skill |
|---|---|
| The advisor sees one function per remote A2A agent | The advisor initially sees only skill names and descriptions |
| Invoking the function starts a second specialist model run | `load_skill` adds the selected specialist context to the existing model run |
| The specialist model chooses and calls its own tools | The advisor follows `SKILL.md` and chooses a sibling skill resource |
| A2A returns the specialist's synthesized answer | MCP `resources/read` returns the resource handler's data |

Calling this pattern **agent as a skill** is an architectural mapping: the skill
does not contain another agent. Instead, it packages the bounded context that
belonged to the specialist agent—its description, operating instructions, and
available operations—without introducing a second model invocation.

### What the MCP provider publishes

Each .NET provider exposes a SEP-2640 discovery index, a `SKILL.md`, and dynamic
sibling resources:

```text
skill://index.json
skill://weather/SKILL.md
skill://weather/current-conditions
skill://weather/forecast/{hours}
skill://weather/storm-status
```

The index is the lightweight discovery layer:

```json
{
  "$schema": "https://schemas.agentskills.io/discovery/0.2.0/schema.json",
  "skills": [{
    "name": "weather",
    "type": "skill-md",
    "description": "Weather intelligence for the ski resort",
    "url": "skill://weather/SKILL.md"
  }]
}
```

`SKILL.md` contains the detailed instructions and tells the model which relative
resource to read:

```markdown
---
name: weather
description: Weather intelligence for the ski resort
---

For current weather, read `current-conditions`.
For a forecast, read `forecast/{hours}`, replacing `{hours}` with the requested horizon.
For storm information, read `storm-status`.
```

The resources are dynamic: reading one executes its .NET handler on the remote
MCP server. The handler can call databases, APIs, or other services before
returning the current content:

```csharp
[McpServerResourceType]
public sealed class WeatherSkillResources(WeatherDataService weather)
{
    [McpServerResource(
        UriTemplate = "skill://weather/current-conditions",
        Name = "Current Conditions",
        MimeType = "application/json")]
    public Task<string> GetCurrentConditions() =>
        weather.GetCurrentConditionsJsonAsync();

    [McpServerResource(
        UriTemplate = "skill://weather/forecast/{hours}",
        Name = "Weather Forecast",
        MimeType = "application/json")]
    public Task<string> GetForecast(int hours) =>
        weather.GetForecastAsync(hours);
}
```

The server registers resources only—domain operations are not exposed as MCP
tools:

```csharp
builder.Services.AddMcpServer(options =>
    options.ServerInfo = new() { Name = "weatherskills", Version = "1.0.0" })
    .WithHttpTransport()
    .WithResources<WeatherSkillResources>();

app.MapMcp("/skillsmcp");
```

### What the Python advisor consumes

The advisor maintains one MCP session per provider and supplies only
`MCPSkillsSource` instances to `SkillsProvider`:

```python
read, write, _ = await exit_stack.enter_async_context(
    streamable_http_client(url=provider_url)
)
session = await exit_stack.enter_async_context(ClientSession(read, write))
await session.initialize()

skill_sources.append(MCPSkillsSource(client=session))

skills_provider = SkillsProvider(
    AggregatingSkillsSource(skill_sources)
)

agent = client.as_agent(
    name="skiadvisorskill",
    instructions=INSTRUCTIONS,
    tools=[ski_researcher_agent.as_tool(...)],
    context_providers=[skills_provider, history_provider],
)
```

Agent Framework contributes the generic `load_skill` and
`read_skill_resource` operations. It does not add the domain resource handlers
as direct tools.

### Progressive-disclosure network flow

1. At the beginning of a run, `MCPSkillsSource` reads `skill://index.json` from
   each provider. Only skill names and descriptions are advertised to the model.
2. The model chooses the relevant domain and calls `load_skill("weather")`.
3. Agent Framework reads `skill://weather/SKILL.md` over MCP and adds its
   instructions to the current model run.
4. The model follows those instructions and calls
   `read_skill_resource("weather", "forecast/24")`.
5. Agent Framework resolves the relative name to
   `skill://weather/forecast/24` and sends MCP `resources/read`.
6. The .NET resource handler receives `hours = 24`, calls the data generator,
   and returns current JSON.
7. The original advisor model—not a second specialist model—uses that result to
   compose the answer.

Dynamic resources are therefore **tool-like**, but their contract is different:
an MCP tool exposes a function name and JSON argument schema, while a dynamic
resource exposes a URI template whose path or query values are described in
`SKILL.md`.

## Key Technologies

- **[Microsoft Agent Framework (MAF)](https://github.com/microsoft/agents)** — Agent creation, tool registration, and orchestration
- **[Azure AI Voice Live](https://learn.microsoft.com/azure/ai-services/speech-service/voice-live)** — Realtime speech-to-speech voice conversations
- **[A2A Protocol](https://github.com/google/A2A)** — Agent-to-agent communication over JSON-RPC + SSE streaming
- **[Aspire](https://aspire.dev)** — Distributed app orchestration, service discovery, observability
- **[Azure AI Foundry](https://ai.azure.com)** — LLM backend, hosted Responses agent, prompt agent, web search, and realtime deployment
- **[Vite](https://vitejs.dev) + [React](https://react.dev)** — Frontend dashboard
- **[Azure Cosmos DB](https://learn.microsoft.com/azure/cosmos-db/)** — Conversation thread persistence

## Further Reading

See [ARCHITECTURE.md](ARCHITECTURE.md) for the detailed system architecture document.
