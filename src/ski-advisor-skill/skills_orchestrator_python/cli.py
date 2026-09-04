"""Standalone CLI entrypoint, closely mirroring the Microsoft Agent Framework
MCP-based skills sample this orchestrator is based on:

    python/samples/02-agents/skills/mcp_based_skill/mcp_based_skill.py
    https://github.com/microsoft/agent-framework

Unlike the sample (a single hardcoded ``MCP_SKILLS_SERVER_URL``), this CLI
connects to every configured skill provider from `config.py` and runs a
one-shot or interactive chat loop against the resulting agent. Useful for
manually exercising the orchestrator without the A2A/FastAPI server.

Run with ``uv run cli`` from this project's directory.
"""
from __future__ import annotations

import asyncio
import logging

from dotenv import load_dotenv

from .agent_executor import SkillsOrchestratorExecutor
from .config import DEFAULT_SKILL_PROVIDERS

logging.basicConfig(level=logging.WARNING)


async def _run() -> None:
    load_dotenv()

    executor = SkillsOrchestratorExecutor(DEFAULT_SKILL_PROVIDERS)
    print("Discovering MCP-based skills")
    print("-" * 60)

    try:
        await executor.ensure_ready()
        print(f"Connected skill providers: {executor.connected_providers or '(none)'}")
        print(f"Skipped skill providers:   {executor.skipped_providers or '(none)'}")

        agent = executor.agent
        assert agent is not None
        session = agent.create_session()

        while True:
            try:
                query = input("User: ").strip()  # noqa: ASYNC250
            except (EOFError, KeyboardInterrupt):
                break
            if not query:
                break
            response = await agent.run(query, session=session)
            print(f"Agent: {response}\n")
    finally:
        await executor.aclose()


def run() -> None:
    """Entry point for the ``cli`` project script."""
    asyncio.run(_run())


if __name__ == "__main__":
    run()
