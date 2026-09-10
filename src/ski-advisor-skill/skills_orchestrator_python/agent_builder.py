"""Shared `agent_framework.Agent` construction for the Skills Orchestrator.

Both hosting surfaces this project exposes build the *same* underlying agent --
Native MCP-discovered Agent Skills and skill-selected tools for weather/safety/
ski-coach/lift-traffic, the Foundry ski researcher as a static direct tool, and
(when configured) a Cosmos DB-backed `CosmosHistoryProvider` for durable
conversation history. Only the surrounding transport/host differs:

- The A2A surface (`agent_executor.SkillsOrchestratorExecutor`, served by
  `main.py` via FastAPI/uvicorn as the `skiadvisorskill` Aspire resource).
- The Microsoft Foundry hosted-agent Responses-protocol surface
  (`foundry_responses_main.py`, served by
  `agent_framework_foundry_hosting.ResponsesHostServer`).

This module factors the construction logic (originally private to
`agent_executor.SkillsOrchestratorExecutor._build_agent`) out into a single,
reusable, host-agnostic function -- `build_orchestrator_agent` -- so neither
surface has to duplicate the MCP-session, skills-discovery, researcher-tool, or
Cosmos-history-provider wiring.
"""
from __future__ import annotations

import logging
import os
from contextlib import AsyncExitStack
from dataclasses import dataclass, field
from typing import Any

from agent_framework import (
    SkillsProvider,
)
from agent_framework.azure import CosmosHistoryProvider
from agent_framework.foundry import FoundryAgent, FoundryChatClient
from azure.ai.projects.aio import AIProjectClient
from azure.identity import DefaultAzureCredential
from azure.identity.aio import DefaultAzureCredential as AsyncDefaultAzureCredential
from mcp.client.session import ClientSession
from mcp.client.streamable_http import streamable_http_client

from .config import (
    COSMOS_HISTORY_SOURCE_ID,
    DEFAULT_SKILL_PROVIDERS,
    SKI_RESEARCHER_AGENT_NAME_ENV,
    SKI_RESEARCHER_PROJECT_ENDPOINT_ENV,
    SkillProviderConfig,
    get_cosmos_history_config,
    get_foundry_config,
    resolve_skill_provider_url,
)
from .native_mcp import SkillToolsMiddleware, SkillConnection

logger = logging.getLogger(__name__)

INSTRUCTIONS = """You are the AlpineAI Skills Orchestrator, the main ski resort advisor.
Use `ski_researcher_agent` for general skiing questions that need web-backed research.
Never invent operational resort data. Answer concisely and concretely, and prioritize safety.
Load the relevant skill's canonical instructions with load_skill. After a successful
load, all operations from that provider are registered for the next model iteration.
Choose and directly call operations according to their descriptions and parameter schemas.
Resource reads are for skill documentation only, never operational data.
Each new turn starts with skill metadata and load helpers only; reload needed skills on followups.
Loading is progressive disclosure, not authorization."""


@dataclass
class BuiltOrchestratorAgent:
    """The constructed agent plus metadata about how it was assembled."""

    agent: Any
    connected_providers: list[str] = field(default_factory=list)
    """Keys of the skill providers successfully connected."""
    skipped_providers: list[str] = field(default_factory=list)
    """Keys of the skill providers that were unconfigured or unreachable."""
    history_backend: str = "none"
    """``"cosmos"`` once a durable Cosmos-backed history provider is attached, else ``"none"``."""


async def _connect_skill_provider(url: str, exit_stack: AsyncExitStack) -> ClientSession:
    """Open a streamable-HTTP MCP connection.

    On success, the connection's resources are adopted into `exit_stack`. On
    failure, any partially opened resources for this attempt are torn down
    immediately, in a local stack, so a single unreachable provider cannot
    surface a deferred connection error later -- e.g. `streamable_http_client`'s
    background request task only raises its connection error when its task
    group is exited, which otherwise would happen during `exit_stack.aclose()`
    long after the failure was already handled and the provider marked as
    skipped.
    """
    local_stack = AsyncExitStack()
    try:
        read, write, _ = await local_stack.enter_async_context(streamable_http_client(url=url))
        session = await local_stack.enter_async_context(ClientSession(read, write))
        await session.initialize()
    except BaseException:
        await local_stack.aclose()
        raise
    else:
        exit_stack.push_async_callback(local_stack.aclose)
        return session


async def _discover_providers(
    providers: tuple[SkillProviderConfig, ...],
    exit_stack: AsyncExitStack,
) -> tuple[list[SkillConnection], list[str], list[str]]:
    """Connect to every configured provider for native skills and tools.

    Returns:
        A tuple of ``(connections, connected_providers, skipped_providers)``
        for every provider that was successfully connected. Native skills and
        app-lifetime native tool instances share the host-owned session. Unconfigured or
        unreachable providers are recorded in `skipped_providers` and otherwise
        skipped.
    """
    connections: list[SkillConnection] = []
    connected_providers: list[str] = []
    skipped_providers: list[str] = []

    for provider in providers:
        url = resolve_skill_provider_url(provider)
        if not url:
            logger.warning(
                "Skill provider '%s' has no configured MCP URL (set %s, or the "
                "Aspire service-discovery vars for resource '%s'); skipping.",
                provider.key,
                provider.url_env_var,
                provider.resource_name,
            )
            skipped_providers.append(provider.key)
            continue

        try:
            session = await _connect_skill_provider(url, exit_stack)
        except Exception:
            logger.exception(
                "Failed to connect to skill provider '%s' at %s; skipping.", provider.key, url
            )
            skipped_providers.append(provider.key)
            continue

        connections.append(SkillConnection(provider, url, session))
        connected_providers.append(provider.key)
        logger.info("Connected MCP Agent Skills provider '%s' at %s", provider.key, url)

    return connections, connected_providers, skipped_providers


async def _build_history_provider(exit_stack: AsyncExitStack) -> tuple[CosmosHistoryProvider | None, str]:
    """Build and connect the durable Cosmos DB conversation-history provider, if configured.

    Returns ``(None, "none")`` when `AZURE_COSMOS_ENDPOINT` is not set -- conversation
    history is then only conversation-aware for the current process's in-memory turn,
    not durable across restarts. See `config.get_cosmos_history_config` for the
    resolution rules and rationale for using a dedicated container.
    """
    cosmos_config = get_cosmos_history_config()
    if cosmos_config is None:
        logger.warning(
            "No Cosmos DB endpoint configured (set %s); conversation history will not be "
            "durable across process restarts.",
            "AZURE_COSMOS_ENDPOINT",
        )
        return None, "none"

    credential: str | Any
    if cosmos_config.key:
        credential = cosmos_config.key
    else:
        # Own this credential's lifecycle explicitly (entered/closed via the shared exit
        # stack), matching the reference sample's `async with AzureCliCredential() as
        # credential:` -- CosmosHistoryProvider itself does not take ownership of a
        # credential instance passed in, only of the Cosmos client/container it builds
        # from it.
        credential = await exit_stack.enter_async_context(AsyncDefaultAzureCredential())

    history_provider = CosmosHistoryProvider(
        COSMOS_HISTORY_SOURCE_ID,
        endpoint=cosmos_config.endpoint,
        database_name=cosmos_config.database_name,
        container_name=cosmos_config.container_name,
        credential=credential,
    )
    await exit_stack.enter_async_context(history_provider)
    logger.info(
        "Connected durable conversation history to Cosmos DB database '%s', container '%s'.",
        cosmos_config.database_name,
        cosmos_config.container_name,
    )
    return history_provider, "cosmos"


async def build_orchestrator_agent(
    providers: tuple[SkillProviderConfig, ...] = DEFAULT_SKILL_PROVIDERS,
    exit_stack: AsyncExitStack | None = None,
    *,
    enter_agent_context: bool = True,
) -> BuiltOrchestratorAgent:
    """Build the orchestrator's `agent_framework.Agent`.

    All MCP client sessions, the Cosmos history provider, and the Foundry
    ski-researcher prompt agent are entered into `exit_stack`, which the caller
    owns and must close (`await exit_stack.aclose()`) on shutdown. When requested,
    the constructed agent is entered there as well.

    Args:
        providers: The skill providers to discover. Defaults to the four
            operational specialists (weather, safety, ski-coach, lift-traffic).
        exit_stack: The `AsyncExitStack` that owns every resource this function
            opens. Required -- a fresh one is *not* created internally, so the
            caller controls exactly how long these connections live.
        enter_agent_context: When `True` (the default -- used by the A2A surface
            and the CLI), also enters the constructed agent's own async context
            into `exit_stack`, matching `agent_framework.Agent`'s usual
            `async with agent:` usage. Set to `False` when the returned agent
            will be handed to `agent_framework_foundry_hosting.ResponsesHostServer`:
            that host owns and lazily enters the agent's async context itself (on
            the first request, so MCP-consent failures surface as an
            `oauth_consent_request` stream event instead of crashing the server)
            and must not have it pre-entered here.

    Returns:
        The constructed `BuiltOrchestratorAgent`.
    """
    if exit_stack is None:
        raise ValueError("exit_stack is required")

    if len({p.key for p in providers}) != len(providers):
        raise ValueError("Duplicate configured skill provider keys")
    connections, connected_providers, skipped_providers = await _discover_providers(providers, exit_stack)

    skills_provider: SkillsProvider | None = None
    context_providers: list[Any] = []
    middleware: list[Any] = []
    skill_tools: SkillToolsMiddleware | None = None
    if connections:
        skill_tools = SkillToolsMiddleware(connections)
        # Native trusted read-only execution must stay in one function loop:
        # approval/resume starts a new loop and discards add_tools registrations.
        # Script approvals retain the native SkillsProvider default.
        skills_provider = SkillsProvider(
            skill_tools.source,
            disable_load_skill_approval=True,
            disable_read_skill_resource_approval=True,
        )
        context_providers.append(skills_provider)
        middleware = [skill_tools]
    else:
        logger.warning("No skill providers connected; agent will run with no discoverable skills.")

    history_provider, history_backend = await _build_history_provider(exit_stack)
    default_options: dict[str, Any] = {}
    if history_provider is not None:
        context_providers.append(history_provider)
        # Disable the chat client's own server-managed thread/store: CosmosHistoryProvider
        # is now the single source of truth for conversation history, matching the
        # reference sample (`default_options={"store": False}`). Without this, the Foundry
        # service's own thread persistence and our Cosmos-backed history could both try to
        # own conversation state for the same session.
        default_options["store"] = False

    endpoint, model = get_foundry_config()
    researcher_name = os.environ.get(SKI_RESEARCHER_AGENT_NAME_ENV)
    if not researcher_name:
        raise ValueError(f"{SKI_RESEARCHER_AGENT_NAME_ENV} is not configured")
    researcher_endpoint = os.environ.get(SKI_RESEARCHER_PROJECT_ENDPOINT_ENV)
    if not researcher_endpoint:
        raise ValueError(f"{SKI_RESEARCHER_PROJECT_ENDPOINT_ENV} is not configured")

    researcher_credential = await exit_stack.enter_async_context(AsyncDefaultAzureCredential())
    researcher_project_client = await exit_stack.enter_async_context(
        AIProjectClient(endpoint=researcher_endpoint, credential=researcher_credential)
    )
    researcher_versions = researcher_project_client.agents.list_versions(
        agent_name=researcher_name,
        limit=1,
        order="desc",
    )
    try:
        researcher_version = await anext(researcher_versions)
    except StopAsyncIteration as exc:
        raise RuntimeError(f"Foundry prompt agent '{researcher_name}' has no deployed versions") from exc

    researcher_agent = FoundryAgent(
        project_client=researcher_project_client,
        agent_name=researcher_name,
        agent_version=researcher_version.version,
        name="ski_researcher_agent",
        description="Web-backed research for general skiing questions.",
    )
    await exit_stack.enter_async_context(researcher_agent)
    researcher_tool = researcher_agent.as_tool(
        name="ski_researcher_agent",
        description="Research general skiing questions using the existing Foundry prompt agent.",
        arg_name="query",
        arg_description="The skiing research question.",
    )

    client = FoundryChatClient(project_endpoint=endpoint, credential=DefaultAzureCredential(), model=model)

    agent = client.as_agent(
        name="skiadvisorskill",
        instructions=INSTRUCTIONS,
        tools=[researcher_tool],
        context_providers=context_providers,
        middleware=middleware,
        default_options=default_options or None,
    )

    if skill_tools is not None:
        await skill_tools.initialize(agent, exit_stack)

    if enter_agent_context:
        await exit_stack.enter_async_context(agent)

    return BuiltOrchestratorAgent(
        agent=agent,
        connected_providers=connected_providers,
        skipped_providers=skipped_providers,
        history_backend=history_backend,
    )
