# Skills Orchestrator (`skiadvisorskill`)

Python MAF agent that discovers weather, safety, ski-coach, and lift-traffic as
remote [SEP-2640](https://github.com/microsoft/agent-framework) Agent Skills
over MCP, while retaining the Foundry ski researcher as a direct agent tool.
Its A2A surface lets the voice bridge consume the same skill-backed
orchestrator used by the frontend's hosted Responses surface.

The *same* underlying agent can also be run as a **Microsoft Foundry hosted
agent** speaking the Responses protocol (`uv run start-responses`) — see
"Foundry hosted Responses agent" below.

Modeled on two Microsoft Agent Framework Python samples:

- Skill discovery/routing: [`python/samples/02-agents/skills/mcp_based_skill/mcp_based_skill.py`](https://github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/skills/mcp_based_skill/mcp_based_skill.py)
- Durable, conversation-aware sessions: [`python/samples/02-agents/conversations/cosmos_history_provider.py`](https://github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/conversations/cosmos_history_provider.py)

This project does **not** modify the existing Python specialist agents or their
A2A servers, and does not implement any MCP skill-*provider* server itself — it
only *consumes* remote skill providers over MCP. All four skill-provider
implementations are owned and built in .NET:

- `weather-skills`, `safety-skills`, and `ski-coach-skills`
  are standalone MCP server projects, one
  per specialist, each an additive server exposing its specialist's capability
  as an SEP-2640 skill (`skill://index.json` + `skill://<name>/SKILL.md`) with
  live operational sibling resources.
- `lift-traffic-skills` — a lightweight MCP host that reuses the existing
  lift data capability as live skill resources without starting its chat agent.

See "AppHost wiring" below for how to wire them together.

## Naming

The readable source folder `src/ski-advisor-skill` hosts two compact Aspire
resources:

- `skiadvisorskilla2a`: the A2A adapter used by `voiceadvisorskill`; its
  `AgentCard.name` matches the resource.
- `skiadvisorskill`: the Foundry-hosted Responses surface; Aspire names its
  generated hosted-agent deployment `skiadvisorskill-ha`.

The parallel .NET orchestrator is `skiadvisora2a`, with hosted-agent deployment
`skiadvisora2a-ha`.

## How it works

1. On the first request, connects to every configured skill-provider MCP
   endpoint over streamable HTTP.
2. Discovers each provider's SEP-2640 skills via `MCPSkillsSource` (reads
   `skill://index.json` / `skill://<name>/SKILL.md`) and combines them behind a
   single `SkillsProvider` (via `AggregatingSkillsSource` when there's more than
   one provider). The framework advertises and loads each skill and reads its
   live sibling resources on demand; provider operations are available only
   through those skill resources.
3. Maintains exactly one long-lived MCP `ClientSession` per connected provider.
   That session feeds only its `MCPSkillsSource` and is closed with the shared
   host-owned `AsyncExitStack`.
4. Registers the existing Foundry ski researcher with `FoundryAgent.as_tool()`
   as the agent's only explicit non-skill tool.
5. Auto-approves the `SkillsProvider`'s read-only skill operations via
   `ToolApprovalMiddleware`
   (`SkillsProvider.read_only_tools_auto_approval_rule`) so the orchestrator can
   run unattended behind either hosting surface.
6. Delegates the A2A task/session lifecycle to `agent_framework_a2a.A2AExecutor`,
   the same executor sibling agents in this repo build on. `A2AExecutor` creates
   an `AgentSession` keyed by the caller's `task.context_id` on *every* call — the
   same `conversationId` the voice bridge and frontend already track for their own
   conversation state — so this orchestrator's own history durability (below)
   naturally lines up with the rest of the app's conversation model.

A skill provider that is unconfigured or unreachable is skipped (logged, and
reported in `/health`); the orchestrator still starts and runs with whatever
subset of providers is available.

## Durable, conversation-aware sessions

This orchestrator is **not stateless**. When a Cosmos DB endpoint is configured,
it attaches an `agent_framework.azure.CosmosHistoryProvider` as an additional
`context_provider` on the agent — matching
`python/samples/02-agents/conversations/cosmos_history_provider.py` exactly:

- Conversation turns are loaded from Cosmos DB before every model call, and
  persisted after, keyed by `session_id` (== the A2A `context_id` /
  `conversationId` the caller already uses).
- `default_options={"store": False}` is set on the agent so the chat client's
  own server-managed thread/store is disabled — Cosmos DB is the single source
  of truth for conversation history, exactly like the reference sample.
- If Cosmos is not configured (`AZURE_COSMOS_ENDPOINT` unset), the orchestrator
  still starts and runs; conversation state simply doesn't survive a process
  restart (each session's history lives only as long as the underlying
  `AgentSession`'s own in-memory turn). This matches the project's existing
  graceful-degradation philosophy for every other optional dependency.
- `GET /health` reports the resolved backend as `conversation_history_backend`:
  `"cosmos"` once configured, else `"none"`.

### Why a new `skillhistory` container instead of reusing `conversations`/`sessions`

The apphost's existing Cosmos containers (`conversations`, `sessions`, both
partitioned on `/conversationId`) back the .NET orchestrator's/voice bridge's
own conversation persistence schema. `CosmosHistoryProvider` **hardcodes** its
partition key to `/session_id` (see
`agent_framework_azure_cosmos/_history_provider.py`) and expects to own the
document schema in whatever container it's given (via
`create_container_if_not_exists`), which is incompatible with reusing either of
those existing containers' partition key or documents.

The closest feasible interpretation of "match the app's Cosmos resources where
feasible" is therefore: reuse the **same Cosmos account + database**
(`cosmosdb` / `db`) that the rest of the app already uses, but add one new,
dedicated container — `skillhistory`, partitioned on `/session_id` — solely for
this orchestrator's own conversation-history schema. See "AppHost wiring" below
for the exact resource definition.

## Configuration

Foundry model (same convention as the sibling specialist agents):

| Variable | Default | Description |
| --- | --- | --- |
| `GPT41_URI` | *(required)* | Foundry project endpoint, normally injected by Aspire via `.WithReference(deployment)` on an `AddModelDeployment("gpt41", ...)` resource. |
| `GPT41_MODEL` | `gpt41` | Model deployment name. |
| `DEFAULT_AD_PORT` | `PORT`, then `8088` | Preferred port for the Foundry Responses host (`start-responses`, `foundry_responses_main.py`), injected by Aspire hosted-agent wiring. |
| `PORT` | `8084` (A2A) / `8088` (Responses fallback) | Port the A2A server (`start`, `main.py`) listens on; the Responses host reads it only when `DEFAULT_AD_PORT` is unset. |
| `HOST` | `0.0.0.0` | Only read by `start-responses`; the A2A server's host binding is fixed in `main.py`. |
| `A2A_AGENT_BASE_URL` | `http://localhost:<PORT>` | Base URL advertised in this agent's own `AgentCard`. Only used by the A2A surface. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | *(unset)* | Enables OpenTelemetry span export when set. |

Per skill provider (`weather`, `safety`, `skicoach`, `lifttraffic`), resolved in this order:

| Variable | Description |
| --- | --- |
| `<PROVIDER>_SKILLS_MCP_URL` (e.g. `WEATHER_SKILLS_MCP_URL`, `LIFTTRAFFIC_SKILLS_MCP_URL`) | Full MCP endpoint URL; takes precedence over everything else. |
| `<PROVIDER>_SKILLS_MCP_PATH` | MCP path appended to the Aspire-discovered base URL (default `/skillsmcp`, matching `app.MapMcp("/skillsmcp")` in every standalone .NET skill-provider project). |
| `services__<resource>__https__0` / `services__<resource>__http__0` | Aspire service-discovery base URL for `weatherskills`, `safetyskills`, `skicoachskills`, or `lifttrafficskills`. |

If none of the above resolve for a provider, it is skipped rather than
treated as an error.

Durable conversation history (Cosmos DB), matching the official sample's env
var names exactly:

| Variable | Default | Description |
| --- | --- | --- |
| `AZURE_COSMOS_ENDPOINT` | *(unset)* | Cosmos DB account endpoint. Unset ⇒ history is not durable (`conversation_history_backend: "none"`). |
| `AZURE_COSMOS_DATABASE_NAME` | `db` | Matches the apphost's existing `cosmos.AddCosmosDatabase("db")`. |
| `AZURE_COSMOS_CONTAINER_NAME` | `skillhistory` | Dedicated container for this provider's `/session_id`-partitioned schema (see rationale above). |
| `AZURE_COSMOS_KEY` | *(unset)* | Optional Cosmos account key. If unset, falls back to an async Azure AD credential (`azure.identity.aio.DefaultAzureCredential`), matching the local emulator / managed-identity conventions used elsewhere in this repo. |

## Running

```bash
# from src/ski-advisor-skill
uv sync
uv run start             # start the A2A server (FastAPI + uvicorn) on $PORT (default 8084)
uv run cli                # interactive CLI, mirrors the reference sample's chat loop
uv run start-responses  # Responses host: $DEFAULT_AD_PORT, then $PORT, then 8088
```

`GET /health` reports `agent_ready`, `connected_skill_providers`,
`skipped_skill_providers`, `configured_skill_providers`, and
`conversation_history_backend` without requiring a live Foundry connection (the
underlying agent, its MCP connections, and its Cosmos history provider are all
built lazily on the first A2A request).

## Foundry hosted Responses agent (`start-responses`)

In addition to the A2A surface above, this project can be deployed as a
**Microsoft Foundry hosted agent** speaking the OpenAI-compatible **Responses**
protocol (`POST /responses`), following the official Agent Framework sample
[`python/samples/04-hosting/foundry-hosted-agents/responses/tools/main.py`](https://github.com/microsoft/agent-framework/blob/main/python/samples/04-hosting/foundry-hosted-agents/responses/tools/main.py)
and its accompanying
[Learn doc](https://learn.microsoft.com/en-us/agent-framework/hosting/foundry-hosted-agent).
This is a second entry point (`skills_orchestrator_python/foundry_responses_main.py`,
`uv run start-responses`) alongside — not instead of — the existing A2A server
(`main.py`, `uv run start`); both build and expose the **same** underlying
agent (weather/safety/ski-coach/lift-traffic skills over MCP, the Foundry ski
researcher as a direct tool, Cosmos-backed history when configured), via
`skills_orchestrator_python/agent_builder.py`'s shared, host-agnostic
`build_orchestrator_agent(...)` — factored out of what was previously private
to `agent_executor.SkillsOrchestratorExecutor` so neither surface duplicates
the MCP-connection/skills-discovery/researcher-tool/Cosmos-history wiring.

- Served by `agent_framework_foundry_hosting.ResponsesHostServer`
  (`agent-framework-foundry-hosting>=1.0.0b260903`, pinned explicitly in
  `pyproject.toml` because the `history_source` parameter used below did not
  exist in earlier prereleases). Internally this wraps
  `azure.ai.agentserver.responses`'s ASGI app and serves it with `hypercorn`.
- Listens on `DEFAULT_AD_PORT`, then `PORT`, then **8088** (matching Aspire's
  hosted-agent port convention), and `HOST` (default `0.0.0.0`).
- Exposes **only** the Responses protocol routes (`POST /responses`,
  `GET/POST /responses/{id}`, `POST /responses/{id}/cancel`, `GET
  /responses/{id}/input_items`) — no A2A routes, no `/health`. This mirrors
  how the existing .NET `ski-advisor-a2a` project already exposes
  `MapFoundryResponses()` instead of A2A routes once wrapped in
  `.AsHostedAgent(...)`.
- **History source selection** (`history_source` on `ResponsesHostServer`):
  - The AppHost intentionally does not give the Foundry-hosted resource a
    `skillHistory` reference. With no Cosmos provider configured,
    `history_source="agent_server"` lets Foundry's own Agent Server session
    store durably own conversation history for this hosted agent. This is a
    *better*
    fallback than the A2A surface gets in the same unconfigured case (which
    only keeps history for the lifetime of a single in-memory `AgentSession`),
    though it's only meaningful once actually deployed as a Foundry hosted
    agent — running `uv run start-responses` standalone with no Foundry
    project behind it has no session store to persist to.
- Builds the agent **once**, eagerly, at process startup (unlike the A2A
  surface, which builds lazily on the first request) — `ResponsesHostServer`
  needs an already-constructed `agent_framework.Agent` at construction time.
  Because MCP's `streamable_http_client`/`ClientSession` objects are bound to
  the event loop that opened them, the build, the `ResponsesHostServer`
  construction, and `await server.run_async(...)` all run inside one
  `asyncio.run()` call, wrapped in a single `async with AsyncExitStack():` so
  every MCP session, the Cosmos provider, the researcher agent, and the chat
  client are released together right after `run_async()` returns.
- Unlike the sample's default `enter_agent_context=True` construction,
  `agent_builder.build_orchestrator_agent(..., enter_agent_context=False)` is
  used here: `ResponsesHostServer` lazily enters the agent's own async context
  itself (on the first inbound request) and registers its own
  `shutdown_handler` for exiting it — this project's own exit stack must not
  pre-enter that context or double-manage its teardown.
- A `Dockerfile` (and `.dockerignore`) exist at this project's root purely so
  Aspire's **publish**-mode wiring can containerize both Python resources that
  share this working directory. Its entrypoint keeps `uv run --no-sync
  start-responses` as the default while recognizing the command-only
  `skills_orchestrator_python.main:app --host ...` arguments generated for
  `skiadvisorskilla2a` and dispatching them through Uvicorn. It has no effect
  on local `aspire run`/dev-loop execution.

## AppHost wiring

`src/apphost.cs` registers every resource this orchestrator needs.

**New Cosmos container** (alongside the existing `conversations`/`sessions`,
both `/conversationId`-partitioned):

```csharp
var skillHistory = db.AddContainer("skillhistory", "/session_id");
```

**Four paired MCP skill-provider resources**:
distinct from that specialist's existing `<key>-agent-a2a` resource, all
mapping their MCP endpoint at `/skillsmcp`:

| Resource name | Project |
| --- | --- |
| `weatherskills` | `./weather-skills/WeatherSkill.Dotnet.csproj` |
| `safetyskills` | `./safety-skills/SafetySkill.Dotnet.csproj` |
| `skicoachskills` | `./ski-coach-skills/SkiCoachSkill.Dotnet.csproj` |
| `lifttrafficskills` | `./lift-traffic-skills/LiftTrafficSkill.Dotnet.csproj` |

**This orchestrator's A2A resource**:

```csharp
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
skillsAdvisorA2A.WithEnvironment(
    A2AAgentBaseUrlEnvironmentVariable,
    skillsAdvisorA2A.GetEndpoint("http"));
```

`.WithReference(skillHistory)` (a *container*-level reference, matching how
`voice-advisor-agent` references its own `conversations` container) is what
injects `ConnectionStrings__skillhistory` — parsed by `get_cosmos_history_config()`
into `AZURE_COSMOS_ENDPOINT`/`AZURE_COSMOS_KEY` (see Configuration above).

The paired .NET orchestrator resource is `skiadvisora2a`.
`voiceadvisorskill` references `skiadvisorskilla2a` so the voice bridge can
route to it under the Voice Live tool name `ski_advisor_skill`.

**Foundry-hosted Responses resource**:

```csharp
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
    .WithComputeEnvironment(aca)
    .WithHttpEndpoint(targetPort: 8089)
    .AsHostedAgent(project, HostedAgentProtocol.Responses, "2.0.0");
```

- **Publish mode**: for the `ExecutableResource` created by
  `AddPythonExecutable`,
  `AsHostedAgent` calls `PublishAsDockerFile()` automatically, building from
  the `Dockerfile` this project now ships at its root (`./ski-advisor-skill/Dockerfile`).
- **Hosted-agent deployment resource name**: Aspire's own convention names the
  generated Foundry-deployment child resource `"{resourceName}-ha"`:
  `skiadvisorskill-ha`. The frontend and responses gateway use this exact name.
- **Endpoint path**: `/responses` (`POST`), plus `/responses/{id}`
  (`GET`/`POST`), `/responses/{id}/cancel` (`POST`), and
  `/responses/{id}/input_items` (`GET`) — no path prefix is configured, so
  these are served at the resource's root, exactly like `ski-advisor-a2a`'s
  `MapFoundryResponses()`.
