# Skills Orchestrator (`skiadvisorskill`)

Python Microsoft Agent Framework (MAF) advisor combining remote Agent Skills
with native progressive MCP tool loading. Weather, safety, ski-coach, and
lift-traffic run as .NET MCP providers with twelve typed tools, not specialist
model loops. The existing Foundry ski researcher remains a separate direct
agent tool.

Both hosting surfaces use `build_orchestrator_agent` in
`skills_orchestrator_python/agent_builder.py`:

| Aspire resource | Entry point | Consumer |
| --- | --- | --- |
| `skiadvisorskill` | `uv run start-responses` | Frontend, using the Responses protocol |
| `skiadvisorskilla2a` | `uv run start` | `voiceadvisorskill`, using A2A |

The separate .NET `skiadvisora2a` orchestrator continues to call the original
A2A specialists as agent tools. Selecting that architecture in the frontend
does not change the skills advisor's implementation.

## Skill transport compatibility

The implemented `skill://index.json` convention follows the
[historical SEP-2640 Draft revision](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/b3f015a7929041dada4b0eaf5a657b30d4f5d6d1/seps/2640-skills-extension.md)
and installed `MCPSkillsSource`, not a current core MCP skill-discovery standard.
As checked September 10, 2026, the
[newer proposal text](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/d6b31a03504c15677d49b922b6b6ace0ef65728d/seps/2640-skills-extension.md)
is marked Accepted while [its PR](https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2640)
remains open and unmerged. It specifies `skills/list` and `skills/get`, which
this demo does not implement. Pin the profile and SDK together when upgrading.

Core MCP `resources/read`, `tools/list`, and `tools/call` are separate protocol
primitives. The native MAF progressive-loading API is independently experimental.
The Python advisor and .NET providers are implementation choices, not a required
language migration from the original Python/.NET A2A specialists.

## Native progressive disclosure

The composition uses existing SDK capabilities:

- `MCPSkillsSource` and `SkillsProvider` discover skill summaries and implement
  `load_skill`.
- `MCPStreamableHTTPTool(use_progressive_disclosure=True)` implements each
  provider's `list_mcp_tools`, `load_tool`, and `unload_tool` functions.
- Native `load_tool` registers selected remote `FunctionTool` definitions via
  `FunctionInvocationContext.add_tools` for the **next model iteration**.
  Their invocation, argument binding, MCP transport, and approval behavior
  remain SDK-managed.

The model-mediated flow is:

```text
load_skill({"skill_name": "weather"})
weather_load_tool({"tool": "weather_forecast"})
weather_weather_forecast({"hours": 6})  # next model iteration
```

The last call reaches MCP `tools/call` with the original remote name
`weather_forecast`. The repeated `weather_` is intentional: MAF's configured
provider prefix is added to the provider's already domain-prefixed tool name.
The loader's `tool` argument accepts one raw remote name or an array of names.

There is no custom operation dispatcher, Foundry Toolbox, semantic search
service, or second specialist model call.

### What is disclosed, and when

The SDK retrieves MCP tool catalogs **host-side** using paginated `tools/list`
when each invocation's native tool objects connect.
That network discovery is not deferred until a model selects a skill.
Progressive disclosure defers *model exposure*: initial context contains
skill summaries and native management functions, not the twelve operations'
full schemas. Loading a skill supplies its instructions; calling the native
loader is a separate model step that registers the requested functions.

Canonical `SKILL.md` documents name their relevant tools and loader directly.
The model therefore need not call `list_mcp_tools` to choose a known operation.
If it does call that native function, it receives descriptions and parameters
for the provider's allowed catalog. This is normal SDK behavior, not a hidden
all-tools prohibition.

Skill-to-tool association is **instructional guidance**, not an authorization
boundary or an atomic SDK guarantee. The model can call a loader without
first calling `load_skill`. Approval and configured tool access are separate
from instruction loading.

The progressive MCP API is experimental in MAF core 1.17.0. Rerun the
framework-level tests when upgrading. Reference samples:

- [MCP progressive disclosure](https://github.com/microsoft/agent-framework/blob/4507512f95effaae4518d658e86e9afc0ccb4514/python/samples/02-agents/mcp/mcp_progressive_disclosure.py)
- [MCP-based skills](https://github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/skills/mcp_based_skill/mcp_based_skill.py)
- [Cosmos history provider](https://github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/conversations/cosmos_history_provider.py)

## Provider contract

Each configured provider exposes `/skillsmcp` over streamable HTTP.

| MCP capability | Content |
| --- | --- |
| `resources/read skill://index.json` | Skill names, descriptions, and canonical document locations |
| `resources/read skill://<name>/SKILL.md` | Domain instructions and native named-tool loading guidance |
| `tools/list` | Typed tool names, descriptions, input/output schemas, and annotations |
| `tools/call` | Execution by the existing .NET domain services |

MCP resources carry discovery and instructional documents only. All business
operations are MCP tools:

| Provider key | Skill | Remote tools |
| --- | --- | --- |
| `weather` | `weather` | `weather_current_conditions`, `weather_forecast`, `weather_storm_status` |
| `safety` | `safety` | `safety_risk`, `safety_slope_safety`, `safety_closed_slopes` |
| `skicoach` | `ski-coach` | `ski_coach_recommendations`, `ski_coach_day_plan` |
| `lifttraffic` | `lift-traffic` | `lift_traffic_lifts`, `lift_traffic_lift_status`, `lift_traffic_wait_times`, `lift_traffic_least_busy_area` |

Provider prefixes keep model-visible tools and loaders distinct. Configured
connections determine where tools run; skill instructions do not configure
new endpoints. Tool results are structured, with errors and cancellation
handled through the MCP/MAF stack rather than fabricated operational data.

| Native loader | Example registered callable |
| --- | --- |
| `weather_load_tool` | `weather_weather_forecast` |
| `safety_load_tool` | `safety_safety_risk` |
| `skicoach_load_tool` | `skicoach_ski_coach_recommendations` |
| `lifttraffic_load_tool` | `lifttraffic_lift_traffic_lifts` |

Each prefix also has native `list_mcp_tools` and `unload_tool` functions.

The Agent Card's descriptive identity maps to skill metadata; its authentication
and transport configuration do not. The former system prompt supplies domain
instructions. Tools remain MCP tools backed by remote domain services. The
generated documents add MAF-specific loader guidance, so they are not presented
as host-neutral examples of the general `SKILL.md` format.

## Tool lifetime and approval

`skills_orchestrator_python/native_mcp.py` contains 98 lines including imports,
documentation, a connection dataclass, and the `NativeMCPToolsMiddleware`
lifecycle adapter. Native progressive tool objects
retain mutable loaded-name state, so sharing them on either host's singleton
agent would leak tool exposure between users. The adapter creates fresh native
objects per invocation while reusing host-owned MCP sessions. It closes those
objects after completion, failure, or cancellation, including lazy streams.
It supplies native runtime tool objects through public middleware APIs, not a
skill-to-tool resolver. Discovery, schema generation, loading, dispatch, and
approval remain SDK code.

The reusable MCP connections stay open for the application's lifetime.
`AsyncExitStack` is the cleanup manager that closes them on shutdown and cleans
up failed connection setup. It is separate from the invocation-local tool
objects that live until their response stream finishes.

Ordinary followups start with loaders again, including restored conversations.
The standard `ToolApprovalMiddleware` runs before the adapter. On an approval
continuation, the adapter exposes verified pending direct calls using native
`always_load`; this restores their availability without granting approval.
The tests also exercise native `always_require`, denial, and streaming approval
continuations.

`SkillProviderConfig.allowed_tools` lists the twelve trusted read-only demo
operations by provider. They use native `approval_mode="never_require"`;
unlisted operations cannot be loaded. This is application-controlled policy,
not trust derived from skill text or an MCP annotation. Adding write operations
requires revisiting that policy rather than adding them to the read-only list.
Instruction reads use the SDK's standard skills read-only auto-approval rule.

### Simpler option for small catalogs

Four skills and twelve tools are a small example of a larger-catalog problem.
For a genuinely small catalog, the standard SDK can expose all allowed MCP
tools upfront: set `use_progressive_disclosure=False` and register the MCP
integration directly in the agent's `tools`. Keep ordinary connection lifetime,
access policy, and optional skill instruction loading, but omit this demo's
progressive-state lifecycle middleware. This is an alternative SDK composition,
not another runtime mode implemented by this repository.

## Configuration

| Variable | Default | Description |
| --- | --- | --- |
| `GPT41_URI` | Required | Foundry project endpoint, normally injected by Aspire's model reference |
| `GPT41_MODEL` | `gpt41` | Model deployment |
| `SKIRESEARCHER_AGENTNAME` | Required | Existing Foundry researcher agent name |
| `SKIRESEARCHER_PROJECTENDPOINT` | Required | Researcher project endpoint |
| `DEFAULT_AD_PORT` | `PORT`, then `8088` | Responses host port, injected by Aspire |
| `PORT` | `8084` for A2A | A2A server port; also Responses fallback |
| `HOST` | `0.0.0.0` | Responses host binding |
| `A2A_AGENT_BASE_URL` | Local A2A URL | Base URL advertised in the A2A Agent Card |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Unset | Enables telemetry export |

Provider endpoints are resolved in order:

| Configuration | Meaning |
| --- | --- |
| `<PROVIDER>_SKILLS_MCP_URL` | Explicit full endpoint, e.g. `WEATHER_SKILLS_MCP_URL` |
| `services__<resource>__https__0` / `services__<resource>__http__0` | Aspire base URL, HTTPS preferred |
| `<PROVIDER>_SKILLS_MCP_PATH` | Path appended to a discovered base URL; default `/skillsmcp` |

Provider resource names are `weatherskills`, `safetyskills`, `skicoachskills`,
and `lifttrafficskills`. Unconfigured or unreachable providers are logged
and reported as skipped.

## Conversation history

When Cosmos is configured, the shared builder attaches
`agent_framework.azure.CosmosHistoryProvider` and sets `store=False` on the
chat client to avoid competing server-managed history. The provider uses
the A2A context/conversation ID as its session ID.

| Variable | Default | Description |
| --- | --- | --- |
| `AZURE_COSMOS_ENDPOINT` | Unset | Cosmos account endpoint |
| `AZURE_COSMOS_DATABASE_NAME` | `db` | Database |
| `AZURE_COSMOS_CONTAINER_NAME` | `skillhistory` | Dedicated history container |
| `AZURE_COSMOS_KEY` | Unset | Optional key; otherwise async Azure credential |
| `ConnectionStrings__skillhistory` | Unset | Aspire-injected endpoint/key, used when explicit endpoint is absent |

The `skillhistory` container is partitioned on `/session_id`, as required by
the native history provider. The voice and .NET components' existing
`conversations` and `sessions` containers use `/conversationId` and are not
interchangeable with it.

Aspire gives the A2A adapter a `skillHistory` reference. Without Cosmos, its
history is not durable across process restarts. The Responses host instead
uses `history_source="agent_server"` when Cosmos is absent, allowing the
Foundry Agent Server session store to own history when deployed. Standalone
local hosting does not itself provide that managed session store.

## Running

```bash
# From repository root, with your configured Azure environment:
aspire start --apphost src/apphost.cs

# For an additional worktree instance:
aspire start --isolated --apphost src/apphost.cs

# Standalone, from src/ski-advisor-skill:
uv sync
uv run start
uv run start-responses
uv run cli
```

Isolated Aspire runs use private local ports and user-secrets stores. Existing
authorized Azure configuration/cache can be copied into that private store
without copying local API keys or committing settings. Starting the full
AppHost includes Azure resources and the declared Foundry researcher.

The A2A host builds the agent lazily. `/health` reports `agent_ready`,
connected/skipped/configured providers, and `conversation_history_backend`.
The Responses host builds eagerly in one async lifetime and exposes the
standard Responses protocol plus host readiness/liveness endpoints. It owns
the agent's async context; the shared builder uses `enter_agent_context=False`
to avoid double entry.

Aspire publishes the Python project using its `Dockerfile`; the Responses
host is named `skiadvisorskill-ha` in Foundry. The Docker entrypoint also
recognizes Aspire's Uvicorn arguments for the A2A adapter. The frontend uses
the configured local Responses endpoint during development.

## Local coverage

Run the stdlib unittest suite with the actual native MAF tool loop and
scripted model/MCP fixtures:

```bash
cd src/ski-advisor-skill
uv run python -m unittest discover -s tests -v
```

For real local .NET MCP integration, build the providers first:

```bash
dotnet build src/weather-skills/WeatherSkill.Dotnet.csproj
dotnet build src/safety-skills/SafetySkill.Dotnet.csproj
dotnet build src/ski-coach-skills/SkiCoachSkill.Dotnet.csproj
dotnet build src/lift-traffic-skills/LiftTrafficSkill.Dotnet.csproj
cd src/ski-advisor-skill
RUN_LIVE_MCP_TESTS=1 uv run python -m unittest discover -s tests -p test_live_mcp.py -v
```

The opt-in suite uses ephemeral loopback provider processes and deterministic
HTTP telemetry fixtures. It does not start Aspire or deploy Azure resources.

The [migration article](../../BLOG_DISTRIBUTED_AGENT_SKILLS.md#what-changed-in-latency-and-tokens)
compares the running A2A and native-skills paths using fresh conversations and
the exact prompt `considering weather and waiting time, where should i start?`.
Its token accounting includes every A2A specialist's leaf model spans, rather
than only the top-level Responses usage.
