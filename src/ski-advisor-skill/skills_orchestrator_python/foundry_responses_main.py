"""Entry point that hosts the Skills Orchestrator as a Microsoft Foundry hosted
agent via the Foundry **Responses** protocol (OpenAI-compatible ``/responses``
endpoint), using `agent_framework_foundry_hosting.ResponsesHostServer`.

Modeled on the Microsoft Agent Framework Foundry-hosting sample:
``python/samples/04-hosting/foundry-hosted-agents/responses/tools/main.py``
(https://github.com/microsoft/agent-framework), adapted so the hosted agent is
the *same* MCP-skills-aware orchestrator as the A2A surface (`main.py`) --
weather/safety/ski-coach/lift-traffic skills discovered over MCP, the Foundry
ski researcher as a direct agent tool, and Cosmos-backed conversation history
when configured -- instead of the sample's local-function tools.

Run locally with ``uv run start-responses`` from this project's directory
(reads the same env vars as `main.py`/`cli.py` -- see README.md's
"Configuration" section -- plus `DEFAULT_AD_PORT`/`PORT`/`HOST` for the
Responses listener, with port precedence in that order and a final default of
8088/0.0.0.0 to match the reference sample and
`azure-ai-agentserver-core`'s own default). See README.md's "AppHost wiring"
section for how an Aspire resource can wrap this with
``.AsHostedAgent(project, HostedAgentProtocol.Responses, "2.0.0")`` -- this
project does not own (and does not edit) `apphost.cs`.

Why this needs its own entry point instead of reusing `agent_executor.py`
directly: `ResponsesHostServer` takes an already-built `agent_framework.Agent`
at construction time (unlike the A2A surface, which defers construction to the
first request via `SkillsOrchestratorExecutor._build_agent`). Building this
agent is inherently async (it opens MCP client sessions before any tool/skill
can be attached), so this module builds it once, up front, and then serves
requests -- all inside the *same* `asyncio.run()` call, because the MCP
sessions opened via `mcp.client.streamable_http.streamable_http_client` are
tied to the event loop that opened them and cannot be handed to a second
`asyncio.run()` (which `ResponsesHostServer.run()`'s synchronous entry point
would otherwise start internally). `ResponsesHostServer.run_async()` -- the
awaitable counterpart -- lets both halves share one loop.

Conversation history: when Cosmos DB is configured (see
`config.get_cosmos_history_config`), `agent_builder.build_orchestrator_agent`
attaches a `CosmosHistoryProvider` (``load_messages=True``) as a context
provider on the agent -- the same durable, session-keyed conversation history
the A2A surface uses. `ResponsesHostServer`'s default
``history_source="agent_server"`` is incompatible with that: it requires *no*
history-loading context provider, since the Foundry Agent Server's own
response store becomes the model's history source instead. So this module
selects ``history_source="agent"`` whenever Cosmos is configured, which tells
the host to pass through only the current turn's input and leave history
entirely up to the agent's own context provider(s) -- preserving the exact
same Cosmos-backed durability as the A2A surface. Without Cosmos configured,
there is no history-loading context provider on the agent, so the default
``history_source="agent_server"`` is used instead, letting the Foundry Agent
Server's own session store durably own conversation history for this hosted
agent -- an even better fallback than the A2A surface's in-memory-only default
(at the cost of only being meaningful once actually deployed as a Foundry
hosted agent, not when run standalone).
"""
from __future__ import annotations

import asyncio
import logging
import os
from contextlib import AsyncExitStack

from agent_framework_foundry_hosting import ResponsesHostServer

from .agent_builder import build_orchestrator_agent
from .config import DEFAULT_SKILL_PROVIDERS

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


async def _run() -> None:
    async with AsyncExitStack() as exit_stack:
        # `enter_agent_context=False`: ResponsesHostServer owns the agent's own
        # async context itself (entered lazily, on the first request -- see the
        # module docstring), so this exit stack must not pre-enter it.
        built = await build_orchestrator_agent(
            DEFAULT_SKILL_PROVIDERS, exit_stack, enter_agent_context=False
        )
        logger.info(
            "Skills Orchestrator (Foundry Responses host) ready: connected=%s skipped=%s "
            "history_backend=%s",
            built.connected_providers,
            built.skipped_providers,
            built.history_backend,
        )

        history_source = "agent" if built.history_backend == "cosmos" else "agent_server"
        server = ResponsesHostServer(built.agent, history_source=history_source)

        host = os.environ.get("HOST", "0.0.0.0")
        port = int(
            os.environ.get("DEFAULT_AD_PORT") or os.environ.get("PORT") or "8088"
        )
        await server.run_async(host=host, port=port)


def main() -> None:
    """Entry point for the ``start-responses`` project script."""
    asyncio.run(_run())


if __name__ == "__main__":
    main()
