"""Agent executor for the Skills Orchestrator A2A surface (Aspire resource "skiadvisorskilla2a").

Adapts the shared agent built by `agent_builder.build_orchestrator_agent` (MCP
Agent Skill/resource discovery, the Foundry ski researcher tool, and
Cosmos-backed conversation history -- see that module for the full pipeline) to
the A2A protocol.

Connections are built lazily on first use (not at import/construction time),
so the FastAPI app can start and answer ``/health`` even before any skill
provider is reachable, and reused for the process lifetime.

**Durable, conversation-aware sessions**: this executor is *not* stateless.
Every A2A task's `agent_framework_a2a.A2AExecutor` already creates an
`AgentSession` keyed by `task.context_id` (the caller's conversation ID --
e.g. the same ``conversationId`` the voice bridge and frontend already use for
their own Cosmos-backed conversation persistence). When a Cosmos DB endpoint
is configured, `agent_builder` attaches an
:class:`agent_framework.azure.CosmosHistoryProvider` (see the official Agent
Framework Python conversations sample,
``python/samples/02-agents/conversations/cosmos_history_provider.py``) as an
additional context provider: it automatically loads prior turns for that
session from Cosmos DB before every run and persists new turns after, so a
conversation survives both individual A2A calls *and* process restarts,
without this executor needing to serialize/restore session state itself. If
Cosmos is not configured, the orchestrator still starts and runs -- sessions
just aren't durable across restarts (matching this project's existing
graceful-degradation philosophy for every other optional dependency).

This project also exposes the *same* underlying agent through Microsoft
Foundry's hosted-agent Responses protocol -- see `foundry_responses_main.py`
and the project README's "Foundry hosted Responses agent" section.
"""
from __future__ import annotations

import asyncio
import logging
from contextlib import AsyncExitStack
from typing import Any, override

from a2a.helpers import new_text_message
from a2a.server.agent_execution import AgentExecutor, RequestContext
from a2a.server.events import EventQueue
from agent_framework_a2a import A2AExecutor

from .agent_builder import build_orchestrator_agent
from .config import DEFAULT_SKILL_PROVIDERS, SkillProviderConfig

logger = logging.getLogger(__name__)


class SkillsOrchestratorExecutor(AgentExecutor):
    """A2A executor that lazily builds an MCP-skills-aware agent and delegates to it.

    Delegates actual A2A protocol handling (task/session lifecycle, streaming,
    event translation) to :class:`agent_framework_a2a.A2AExecutor`, once the
    underlying agent has been constructed.
    """

    def __init__(
        self,
        providers: tuple[SkillProviderConfig, ...] = DEFAULT_SKILL_PROVIDERS,
    ) -> None:
        self._providers = providers
        self._agent: Any | None = None
        self._delegate: A2AExecutor | None = None
        self._init_lock = asyncio.Lock()
        self._exit_stack = AsyncExitStack()
        self._connected_providers: list[str] = []
        self._skipped_providers: list[str] = []
        self._history_backend = "none"

    @property
    def is_ready(self) -> bool:
        """Whether the underlying agent has been built."""
        return self._delegate is not None

    @property
    def agent(self) -> Any | None:
        """The underlying `agent_framework.Agent`, once built (else `None`)."""
        return self._agent

    @property
    def connected_providers(self) -> list[str]:
        """Keys of the skill providers successfully connected so far."""
        return list(self._connected_providers)

    @property
    def skipped_providers(self) -> list[str]:
        """Keys of the skill providers that were unconfigured or unreachable."""
        return list(self._skipped_providers)

    @property
    def history_backend(self) -> str:
        """``"cosmos"`` once a durable Cosmos-backed history provider is attached, else ``"none"``.

        ``"none"`` means sessions are still conversation-aware for the lifetime of a single
        A2A task/context (the agent's own in-memory turn), but are not persisted -- a process
        restart loses history for any conversation in flight.
        """
        return self._history_backend

    async def _ensure_ready(self) -> None:
        if self._delegate is not None:
            return
        async with self._init_lock:
            if self._delegate is not None:
                return
            await self._build_agent()

    async def ensure_ready(self) -> None:
        """Build the underlying agent and MCP connections if not already built.

        Public wrapper around the lazy-initialization path, intended for
        callers (e.g. the CLI) that need the agent ready before their first
        `agent.run(...)` call, outside of the A2A `execute()` flow.
        """
        await self._ensure_ready()

    async def _build_agent(self) -> None:
        """Build the agent via the shared `agent_builder` pipeline and wrap it for A2A.

        `enter_agent_context=True` here: the A2A surface owns the agent's async
        context for the process lifetime (entered once, closed via `aclose()`),
        unlike the Foundry Responses surface, which lets `ResponsesHostServer`
        own that lifecycle instead (see `foundry_responses_main.py`).
        """
        built = await build_orchestrator_agent(
            self._providers, self._exit_stack, enter_agent_context=True
        )
        self._agent = built.agent
        self._connected_providers = built.connected_providers
        self._skipped_providers = built.skipped_providers
        self._history_backend = built.history_backend
        self._delegate = A2AExecutor(built.agent, stream=True)

    @override
    async def execute(self, context: RequestContext, event_queue: EventQueue) -> None:
        try:
            await self._ensure_ready()
        except Exception as exc:
            logger.error("Failed to initialize the skills orchestrator agent: %s", exc, exc_info=True)
            await event_queue.enqueue_event(
                new_text_message(f"Skills orchestrator failed to initialize: {exc}")
            )
            return

        assert self._delegate is not None
        await self._delegate.execute(context, event_queue)

    @override
    async def cancel(self, context: RequestContext, event_queue: EventQueue) -> None:
        if self._delegate is not None:
            await self._delegate.cancel(context, event_queue)
        else:
            await event_queue.enqueue_event(new_text_message("Operation cancelled by user"))

    async def aclose(self) -> None:
        """Release all MCP connections and agent resources. Call on application shutdown."""
        await self._exit_stack.aclose()
