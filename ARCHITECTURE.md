# 🏔️ AlpineAI – Multi-Agent Ski Resort System

## Overview

AlpineAI is a distributed, cloud-native, multi-agent system representing an intelligent ski resort platform.

The system is built using:

* **Microsoft Agent Framework (MAF)** as the agent orchestration layer
* **Agent-to-Agent (A2A)** for the existing agent-as-tool path
* **SEP-2640 Agent Skills over MCP** for the parallel skills path
* **[Aspire](https://aspire.dev)** as the local development orchestrator
* Polyglot microservices (.NET + Python + Go)
* Real-time fake telemetry generator
* Event-driven communication
* A real-time frontend dashboard

The app deliberately exposes two equivalent orchestration paths:

* `skiadvisora2a`: the existing .NET orchestrator consuming A2A agents as tools.
* `skiadvisorskill`: a Python orchestrator discovering .NET-hosted skills over MCP.

Weather, lift traffic, safety, and ski coach have paired compact Aspire
resources (`weatheragenta2a`/`weatherskills`, `safetyagenta2a`/`safetyskills`,
`skicoachagenta2a`/`skicoachskills`, and
`lifttrafficagenta2a`/`lifttrafficskills`). The existing Foundry ski researcher
remains a direct agent tool available to both orchestrators.

---

# 1. High-Level Architecture

## Core Components

| Component                | Language                       | Role                              |
| ------------------------ | ------------------------------ | --------------------------------- |
| Ski Advisor A2A           | .NET                           | A2A agent-as-tool orchestrator    |
| Ski Advisor Skill         | Python                         | MCP Agent Skills orchestrator     |
| Voice Advisor A2A         | .NET                           | Voice Live bridge to A2A specialists |
| Voice Advisor Skill       | .NET                           | Voice Live bridge to the skills orchestrator |
| Weather Agent            | Python                         | Weather intelligence              |
| Lift Traffic Agent       | .NET                           | Lift congestion analysis          |
| Safety Agent             | Python                         | Risk & slope safety validation    |
| Ski Coach Agent          | Python                         | Skill-based slope recommendation  |
| Real-Time Data Generator | Go                             | Fake telemetry + weather + events |
| Event Bus                | Cloud-native (Dapr or similar) | Pub/Sub event backbone            |
| Frontend Dashboard       | React / Next.js                | Visualization UI                  |
| API Gateway              | .NET                           | Unified access point              |

---

# 2. Agent Framework Layer

## Microsoft Agent Framework (MAF)

The A2A path exposes specialist Agent Cards and invokes them as remote tools.
The skills path exposes `skill://index.json`, `skill://<name>/SKILL.md`, and
dynamic sibling resources from independent .NET MCP services. The Python
orchestrator combines those skills with the Foundry researcher agent as a normal tool and uses
`MCPSkillsSource` for progressive disclosure and `CosmosHistoryProvider` with a
dedicated `/session_id`-partitioned `skillhistory` container.

## Agent as a Tool vs Agent as a Skill

The term **agent as a skill** describes how this sample maps an existing
specialist-agent boundary onto Agent Skills. The skill is not itself an agent.
It carries the same bounded domain context—description, instructions, references,
and operations—but lets the advisor model execute that context directly.

```mermaid
flowchart LR
    subgraph A2A["Agent as a tool"]
        OA[Advisor model] -->|A2A agent function| SA[Specialist model]
        SA -->|function call| ST[Specialist tool]
        ST --> SA --> OA
    end

    subgraph Skills["Agent as a skill"]
        OS[Advisor model] -->|load_skill| MD[Remote SKILL.md]
        MD -->|instructions name a resource| OS
        OS -->|read_skill_resource| DR[Dynamic MCP resource]
        DR -->|service/API call| DATA[Live resort data]
        DATA --> DR --> OS
    end
```

| Concern | Agent as a tool | Agent as a skill |
|---|---|---|
| Initial discovery | A2A Agent Card becomes an advisor function | `skill://index.json` advertises name and description |
| Domain instructions | Owned by the specialist model | Loaded into the advisor run from `SKILL.md` |
| Operation selection | Specialist model selects a function | Advisor model selects a sibling resource |
| Remote execution | Specialist tool executes behind the A2A agent | MCP resource handler executes on the skill provider |
| Model calls | Advisor + specialist | Advisor only |

## MCP Skill Contract

Each provider exposes the same three layers:

```text
skill://index.json                       # L1 discovery metadata
skill://weather/SKILL.md                 # L2 domain instructions
skill://weather/forecast/{hours}         # L3 dynamic sibling resource
```

The server-side resource handler is executable application code:

```csharp
[McpServerResourceType]
public sealed class WeatherSkillResources(WeatherDataService weather)
{
    [McpServerResource(
        UriTemplate = "skill://weather/forecast/{hours}",
        Name = "Weather Forecast",
        MimeType = "application/json")]
    public Task<string> GetForecast(int hours) =>
        weather.GetForecastAsync(hours);
}
```

`McpServerResource` describes how the method is reached over MCP. When the
advisor reads `skill://weather/forecast/24`, the MCP server binds `24` to
`hours`, executes the method remotely, and returns its result as resource
content. The handler may call `datagenerator`, a database, or another downstream
service.

The providers intentionally register no MCP domain tools:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithResources<WeatherSkillResources>();

app.MapMcp("/skillsmcp");
```

The Python advisor likewise registers no direct provider tools:

```python
sources = [
    MCPSkillsSource(client=weather_session),
    MCPSkillsSource(client=safety_session),
    MCPSkillsSource(client=coach_session),
    MCPSkillsSource(client=lift_session),
]

skills = SkillsProvider(AggregatingSkillsSource(sources))

agent = client.as_agent(
    name="skiadvisorskill",
    context_providers=[skills, history],
    tools=[ski_researcher_agent.as_tool(...)],
)
```

Agent Framework advertises the generic `load_skill` and
`read_skill_resource` functions. A typical weather request proceeds as follows:

```mermaid
sequenceDiagram
    participant O as skiadvisorskill
    participant L as Advisor model
    participant M as weatherskills MCP
    participant D as datagenerator

    O->>M: resources/read skill://index.json
    M-->>O: weather name + description
    O->>L: prompt + advertised skill
    L->>O: load_skill("weather")
    O->>M: resources/read skill://weather/SKILL.md
    M-->>O: instructions + resource templates
    O->>L: loaded weather instructions
    L->>O: read_skill_resource("weather", "forecast/24")
    O->>M: resources/read skill://weather/forecast/24
    M->>D: GET current weather data
    D-->>M: telemetry JSON
    M-->>O: dynamic resource content
    O->>L: current forecast
    L-->>O: final answer
```

Dynamic resources are tool-like because reading them can execute arbitrary
server-side logic. They remain resources semantically: the model chooses a
relative URI described by `SKILL.md`, rather than invoking a domain function
with a JSON Schema. This keeps the provider behind the Agent Skills abstraction
and preserves progressive disclosure.

---

# 3. Ski Resort Advisor Orchestrators

## Role

`skiadvisora2a` remains the default Responses-based chat orchestrator.
`skiadvisorskill` provides the alternative skills-based architecture and is
also exposed over A2A so the Voice Live bridge can call it as a remote tool.

Voice uses two Aspire resources backed by the same .NET project:
`voiceadvisora2a` registers the weather, lift traffic, safety, and ski coach
A2A agents plus the Foundry ski researcher, while `voiceadvisorskill`
registers only the `skiadvisorskilla2a` adapter. The frontend selects the
matching `/ws/voice/a2a` or `/ws/voice/skill` proxy route and preserves the
conversation ID in either architecture.

It:

* Receives user input
* Performs intent decomposition
* Invokes specialist agents via A2A
* Synthesizes final response
* Applies safety overrides

## Responsibilities

* Register other agents as tools
* Manage conversation memory
* Enforce priority rules (Safety > Weather > Coach)
* Aggregate distributed responses

## Tools It Consumes

| Tool                   | Provided By        |
| ---------------------- | ------------------ |
| get_current_conditions | Weather Agent      |
| get_forecast           | Weather Agent      |
| get_lift_status        | Lift Traffic Agent |
| get_wait_times         | Lift Traffic Agent |
| evaluate_risk          | Safety Agent       |
| recommend_slope        | Ski Coach Agent    |

## Technology

* .NET 8+
* Microsoft Agent Framework
* ASP.NET Core
* A2A client
* SignalR (for live frontend updates)

---

# 4. Weather Agent (Python)

## Role

Provides real-time weather and snow conditions.

## Data Source

Consumes data from:

* Real-Time Fake Data Generator

## Tools Exposed

* `get_current_conditions()`
* `get_forecast(hours: int)`
* `is_storm_incoming()`

## Responsibilities

* Aggregate telemetry
* Detect storm thresholds
* Provide structured condition summaries

## Technology

* Python 3.11+
* FastAPI
* Microsoft Agent Framework (Python SDK)
* A2A endpoint exposure

---

# 5. Lift Traffic Agent (.NET)

## Role

Manages lift telemetry and congestion intelligence.

## Data Source

Consumes:

* Lift queue events
* Lift operational status
* Telemetry stream

## Tools Exposed

* `get_lift_status(lift_id)`
* `get_wait_times()`
* `suggest_less_busy_area()`

## Responsibilities

* Compute congestion score
* Identify overload scenarios
* Emit congestion events

## Technology

* .NET 8
* Microsoft Agent Framework
* Background hosted service for event consumption

---

# 6. Safety Agent (Python)

## Role

Evaluates risk across slopes.

## Data Inputs

* Wind speed
* Snow intensity
* Avalanche risk index
* Lift failures
* Visibility metrics

## Tools Exposed

* `evaluate_risk(area)`
* `is_slope_safe(slope_id)`
* `get_closed_slopes()`

## Rules

* Wind > threshold → risk level increase
* Avalanche index high → slope closure
* Visibility low + steep slope → unsafe

## Technology

* Python
* FastAPI
* Microsoft Agent Framework
* Rule engine logic

---

# 7. Ski Coach Agent (Python)

## Role

Recommends slopes based on:

* Skill level
* Weather
* Congestion
* Safety

## Tools Exposed

* `recommend_slope(skill_level, preferences)`
* `build_day_plan(skill_level)`

## Data Source

* Static slope metadata
* Conditions from Weather Agent
* Congestion from Lift Traffic Agent

Note: It may call other agents via A2A if needed.

---

# 8. Real-Time Fake Data Generator (Go)

## Purpose

Continuously emits synthetic but realistic data so system behaves as live.

## Generates

### Weather Data

* Temperature
* Wind speed
* Snow intensity
* Visibility

### Lift Telemetry

* Queue length
* Lift status (open/closed)
* Throughput rate

### Safety Signals

* Avalanche index
* Incident reports

## Implementation

* Go HTTP service
* Updates telemetry every 5–10 seconds
* Publishes to Event Bus
* Also exposes REST endpoint for latest state

---

# 9. Event-Driven Backbone

Use:

* Dapr Pub/Sub OR
* Azure Service Bus OR
* Kafka (for demo simplicity optional)

Events:

* `WeatherUpdated`
* `LiftStatusChanged`
* `QueueUpdated`
* `SafetyAlertRaised`
* `SlopeClosed`

All agents subscribe to relevant topics.

---

# 10. Frontend Dashboard

## Technology

* React or Next.js
* SignalR client
* Real-time charts (Recharts or similar)

## Features

### Live Panels

* Weather dashboard
* Lift wait times
* Risk heatmap
* Open/Closed slopes
* Agent decision trace

### AI Chat Panel

* User interacts with Ski Resort Advisor
* Displays:

  * Tool calls
  * Agent responses
  * Final synthesized output

---

# 11. Observability

## Required

* Distributed tracing
* Correlation IDs
* Structured logging
* Metrics per agent

## Recommended Stack

* OpenTelemetry
* Azure Monitor OR Prometheus + Grafana

---

# 12. Deployment Target

* Azure Container Apps
* Container per agent
* Internal Dapr sidecar
* Horizontal scaling enabled
* Managed identity for secure agent communication

---

# 13. Local Development Orchestration (Aspire)

## Overview

[Aspire](https://aspire.dev) is used as the local development orchestrator for AlpineAI.

Aspire provides:

* Service discovery and orchestration across all agents and services
* Built-in dashboard for logs, traces, and metrics
* Simplified local environment setup (no need for manual Docker Compose wiring)
* Native OpenTelemetry integration

## Version

* Aspire **13.1.1** (latest)

## How It Works

The Aspire **AppHost** project defines the entire distributed application graph:

* Each agent (.NET and Python) is registered as a project or executable resource
* The Real-Time Data Generator runs as a background resource
* The Frontend Dashboard is registered as a Node.js app resource
* Service references and environment variables are wired automatically
* All dotnet projects must reference the [service-default project](./src/service-defaults/service-defaults.csproj) to implement common configuration calling `builder.AddServiceDefaults()` and `app.MapDfeltEndpoints()` in their startup

## Benefits for This System

* Single `F5` experience to launch all agents, the data generator, and the frontend
* Centralized dashboard showing health, logs, and distributed traces across all agents
* Automatic port management and service endpoint injection
* No Docker Compose required for local development

## Documentation

* Official site: [https://aspire.dev](https://aspire.dev)

---

# 14. System Interaction Flow

## Example Request

User:
"I am intermediate, I dislike crowds, and wind is strong. Where should I ski?"

### Flow

1. Ski Resort Advisor receives request
2. Calls Weather Agent
3. Calls Lift Traffic Agent
4. Calls Safety Agent
5. Calls Ski Coach Agent
6. Applies safety override if needed
7. Synthesizes response
8. Emits decision trace to frontend

---

# 15. Non-Functional Requirements

* All agents stateless
* Horizontal scalability
* Resilient to agent downtime
* Safety agent always has highest priority
* Real-time update latency < 2 seconds
* System must run locally with Aspire (see section 13)

---

# 16. Folder Structure (Suggested)

```
/alpine-ai
  /ski-advisor-a2a
  /ski-advisor-skill
  /voice-advisor-agent
  /lift-traffic-agent-a2a
  /weather-agent-a2a
  /safety-agent-a2a
  /ski-coach-agent-a2a
  /weather-skills
  /safety-skills
  /ski-coach-skills
  /lift-traffic-skills
  /data-generator
  /frontend
  /infrastructure
```

---

# 17. Key Architectural Principles

* Agent-as-a-Tool
* Single responsibility per agent
* Event-driven reactivity
* Orchestrated intelligence
* Safety-first overrides
* Real-time observability

---
