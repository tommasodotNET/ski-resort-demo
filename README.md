# 🏔️ AlpineAI – Multi-Agent Ski Resort Demo

A distributed ski resort system built with **Microsoft Agent Framework (MAF)**, **Azure AI Foundry**, **A2A**, **Agent Skills over MCP**, **Voice Live**, and **Aspire**.

An AI-powered ski resort concierge that coordinates weather intelligence, lift traffic, safety evaluation, personalized coaching, web-backed ski research, and voice conversations. Compare remote specialist agents with remote skill instructions and typed MCP tools on the same dashboard.

**Compatibility:** the skills path uses a pinned historical draft profile of
SEP-2640, not the latest discovery contract. As of September 10, 2026, the
upstream text is marked Accepted while its PR remains unmerged. Core MCP
resources/tools and MAF's experimental progressive-loading API are distinct
from that evolving profile. See the [profile details](src/ski-advisor-skill/README.md#skill-transport-compatibility).

## Architecture

| Component | Language | Role |
|---|---|---|
| **`skiadvisora2a`** | .NET | Existing Foundry-hosted orchestrator using specialists as A2A tools |
| **`skiadvisorskill`** | Python | Native MAF skills and MCP tools advisor; its A2A hosting surface uses Cosmos-backed history |
| **`voiceadvisora2a`** | .NET | Voice Live bridge exposing the four specialist A2A agents plus the ski researcher |
| **`voiceadvisorskill`** | .NET | Voice Live bridge exposing only the `skiadvisorskilla2a` orchestrator |
| **Compact A2A resources** | Python/.NET | `weatheragenta2a`, `safetyagenta2a`, `skicoachagenta2a`, and `lifttrafficagenta2a` |
| **Compact skill resources** | .NET | `weatherskills`, `safetyskills`, `skicoachskills`, and `lifttrafficskills`, exposing instructional skills and twelve typed MCP tools |
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

### Skills advisor

The skills advisor composes MAF's `SkillsProvider` / `MCPSkillsSource` with
native MCP functions and request-local `FunctionInvocationContext.add_tools`.
After a skill loads successfully, a small host hook registers every tool from
that skill's configured provider for the next model iteration. The model sees
their descriptions and schemas before choosing which to invoke. This is the
skills architecture's single execution path; no mode switch or Foundry Toolbox
is required.

Both Responses and A2A hosting surfaces use the same builder. The frontend's
separate A2A-specialist architecture selection remains unchanged. See the
[skills advisor README](src/ski-advisor-skill/README.md#native-progressive-disclosure)
for tool loading, approval, lifetime, and local testing.

This small demo illustrates a larger-tool-catalog pattern. For genuinely small
catalogs, load the configured providers' MCP tools upfront with the standard SDK instead.
That simpler composition does not need this demo's skill-to-provider registration
hook; it can still load skill instructions on demand.

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

2. Weather, lift, safety, and coach each have an **A2A** resource and a paired .NET **MCP Agent Skill** resource. Skill instructions and typed tools are published remotely rather than embedding domain operations in the skills orchestrator.

3. **Ski Researcher Agent** remains an Azure AI Foundry prompt agent with web search. Both orchestrators register it directly as an agent tool, demonstrating that one agent can combine tools and skills.

4. **`skiadvisora2a`** preserves the agent-as-tool architecture. **`skiadvisorskill`** uses native MAF skill loading and progressive MCP tool registration. Its model calls registered provider tools directly over MCP. The Foundry ski researcher remains a separate direct agent tool for both orchestrators.

5. **`voiceadvisora2a`** and **`voiceadvisorskill`** run the same Voice Live bridge with architecture-specific configuration. The A2A resource registers only the four specialist A2A agents plus the ski researcher; the skill resource registers only `skiadvisorskilla2a`. The voice conversation ID is preserved through either route and becomes the remote skills-orchestrator session ID when that tool is used.

6. **Frontend** displays real-time data panels, provides an AI chat panel, and sends voice sessions to `/ws/voice/a2a` or `/ws/voice/skill` according to the selected architecture.

## Agent as a Skill over MCP

This sample implements the same four specialist domains as A2A agents and MCP skills:

| Agent as a tool | Agent as a skill |
|---|---|
| The advisor sees one function per remote A2A agent | The advisor initially sees skill summaries and skill-loading functions |
| Invoking the function starts a second specialist model run | Successful `load_skill` supplies instructions and triggers registration of the provider's tools |
| The specialist model chooses and calls its own tools | The advisor follows `SKILL.md` and chooses from the group's full tool descriptions and schemas |
| A2A returns the specialist's synthesized answer | MCP `tools/call` returns the tool handler's data |

Calling this pattern **agent as a skill** is an architectural mapping: the skill
does not contain another agent. The Agent Card's name/description become skill
metadata, and the former system prompt becomes an enriched `SKILL.md`.
Specialist tools stay tools, now exposed by MCP handlers over the existing
remote domain services. Authentication and transport fields do not become
prose. There is no separate specialist model loop, although the advisor uses
multiple model iterations.

### What the MCP provider publishes

Each .NET provider exposes instructional resources plus typed MCP tools:

```text
skill://index.json
skill://weather/SKILL.md
MCP tools/list: weather_current_conditions, weather_forecast, weather_storm_status
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

`SKILL.md` contains domain instructions and explains that the provider's tools
become available after the skill loads. Resources carry only
discovery and instructions; operational data is returned by tool handlers
backed by the existing domain services:

```csharp
builder.Services.AddMcpServer(options =>
    options.ServerInfo = new() { Name = "weatherskills", Version = "1.0.0" })
    .WithHttpTransport()
    .WithResources<WeatherSkillResources>()
    .WithTools<WeatherTools>();

app.MapMcp("/skillsmcp");
```

### What the Python advisor consumes

`SkillsProvider` and `MCPSkillsSource` discover and load the skill documents.
`MCPStreamableHTTPTool` discovers authoritative native functions host-side.
A small function-invocation hook associates a successfully loaded skill with its
configured provider and registers that provider's whole catalog using MAF's
experimental `FunctionInvocationContext.add_tools` API. The functions retain
their descriptions, schemas, native MCP transport, and approval behavior;
there is no custom operation dispatcher.

### Native progressive-disclosure flow

1. The SDK reads discovery metadata and the paginated MCP tool catalogs
   host-side. The initial model context contains skill summaries and generic
   skill-loading functions, not domain operation schemas or per-tool loaders.
2. The model calls `load_skill({"skill_name":"weather"})`; MAF reads the weather `SKILL.md`.
3. After the skill read succeeds, the host registers all three weather tools
   for this run through `add_tools`. Other providers' tools remain hidden.
4. On the next model iteration, the model chooses from the weather tools'
   descriptions and input schemas, for example
   `weather_weather_forecast({"hours":6})`.
5. Native MCP `tools/call` executes the .NET handler, which reads resort data.
   The same advisor model uses the result to answer.

The model selects the skill; the host determines the tool group. This
skill-to-provider association is application glue, not a feature that
`SkillsProvider` automatically supplies and not a replacement for authorization.
Each current provider hosts one skill. Loading it exposes every operation in
that provider's catalog, but does not execute them. There are no model-facing
per-provider list/load/unload helpers. See the
[advisor documentation](src/ski-advisor-skill/README.md#native-progressive-disclosure)
for lifetime and approval details.

The configured providers' tool catalogs are the source of truth; the advisor
does not maintain a duplicate tool-name allowlist. A selected skill exposes its
provider's full catalog, not only name-matched operations. The current trusted
providers expose read-only operations, which retain unattended execution.
Native `approval_mode="never_require"` and the SDK function-calling loop handle
that execution automatically. Native `SkillsProvider` settings disable approval
prompts for instruction reads too; there is no approval-resumption bookkeeping.

Registrations belong to the current invocation, not the shared agent.
They last through the response stream and disappear when that run ends,
including failure or cancellation. Followups start without operation tools and
load their skills again; conversation history is separate. MCP connections stay
open until app shutdown. The shared native MCP objects do not maintain mutable
per-tool-loader state; only their registrations are invocation-local.

## Key Technologies

- **[Microsoft Agent Framework (MAF)](https://github.com/microsoft/agents)** — Agent creation, tool registration, and orchestration
- **[Azure AI Voice Live](https://learn.microsoft.com/azure/ai-services/speech-service/voice-live)** — Realtime speech-to-speech voice conversations
- **[A2A Protocol](https://github.com/google/A2A)** — Agent-to-agent communication over JSON-RPC + SSE streaming
- **[Aspire](https://aspire.dev)** — Distributed app orchestration, service discovery, observability
- **[Azure AI Foundry](https://ai.azure.com)** — LLM backend, hosted Responses agent, prompt agent, web search, and realtime deployment
- **[Vite](https://vitejs.dev) + [React](https://react.dev)** — Frontend dashboard
- **[Azure Cosmos DB](https://learn.microsoft.com/azure/cosmos-db/)** — Conversation thread persistence

## Further Reading

The article and deck compare the two advisor paths using three fresh-conversation
pairs at `56f453a`: mean elapsed **15.480 s A2A versus 6.348 s skills**, with
**11,134 versus 13,533** whole-system tokens across the three runs. Skills used
three model calls per request; A2A used six, six, and seven including remote
specialists. Reused processes, prompt-cache variation, first-use credential
probes, live data, and different model-selected work matter. These observations
are not a controlled architectural speedup, quality study, or billable-cost ratio.

- [Architecture](ARCHITECTURE.md): component and protocol diagrams.
- [Native MAF migration article](BLOG_DISTRIBUTED_AGENT_SKILLS.md): implementation
  walkthrough and paired measurements using
  `considering weather and waiting time, where should i start?`.
- [Presentation source](slides/index.html): English Reveal.js deck. Aspire serves
  it as `slides`; run `npm run build` from `slides/` to generate the Vite build.
