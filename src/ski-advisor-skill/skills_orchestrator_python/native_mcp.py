"""Bridge native skill selection to invocation-local native MCP catalogs."""
from __future__ import annotations

from collections.abc import Awaitable, Callable, Mapping
from contextlib import AsyncExitStack
from dataclasses import dataclass

from agent_framework import (
    AggregatingSkillsSource,
    CachingSkillsSource,
    Content,
    FunctionInvocationContext,
    FunctionMiddleware,
    MCPSkillsSource,
    MCPStreamableHTTPTool,
    Skill,
    SkillsSourceContext,
    SupportsAgentRun,
)
from mcp import ClientSession

from .config import SkillProviderConfig


@dataclass(frozen=True)
class SkillConnection:
    config: SkillProviderConfig
    url: str
    session: ClientSession


class SkillToolsMiddleware(FunctionMiddleware):
    """Expose a configured provider's entire catalog after native skill loading.

    Sources and eager wrappers belong to the host, not individual runs. Only
    ``context.add_tools`` changes visibility; MAF owns that run-local list,
    next-iteration timing and same-object deduplication (including parallel loads).
    """

    def __init__(self, connections: list[SkillConnection]) -> None:
        self.connections = tuple(connections)
        self.sources = [
            CachingSkillsSource(MCPSkillsSource(client=connection.session))
            for connection in connections
        ]
        self.source = AggregatingSkillsSource(self.sources)
        self.catalogs: dict[str, tuple[Skill, MCPStreamableHTTPTool]] = {}

    async def initialize(self, agent: SupportsAgentRun, exit_stack: AsyncExitStack) -> None:
        """Discover identities through native metadata, without reading SKILL.md.

        The same cached Skill instances serve SkillsProvider and the observer.
        Reject ambiguous names instead of silently selecting the first provider.
        """
        for connection, source in zip(self.connections, self.sources, strict=True):
            skills = await source.get_skills(SkillsSourceContext(agent=agent))
            native = await exit_stack.enter_async_context(MCPStreamableHTTPTool(
                name=connection.config.key,
                url=connection.url,
                session=connection.session,
                tool_name_prefix=connection.config.key,
                load_prompts=False,
                use_progressive_disclosure=False,
                approval_mode="never_require",
            ))
            for skill in skills:
                name = skill.frontmatter.name.lower()
                if name in self.catalogs:
                    raise ValueError(f"Ambiguous configured provider skill identity: {name!r}")
                self.catalogs[name] = (skill, native)

    async def process(
        self, context: FunctionInvocationContext, call_next: Callable[[], Awaitable[None]]
    ) -> None:
        await call_next()  # Preserve native failures, cancellation and approval results.
        if context.function.name != "load_skill":
            return
        result = context.result
        # FunctionTool normalizes the native string result into text Content.
        # Approval requests and all other non-text results are not successful loads.
        if not (isinstance(result, list) and len(result) == 1
                and isinstance(result[0], Content) and result[0].type == "text"):
            return
        arguments = context.arguments
        name = (arguments.get("skill_name") if isinstance(arguments, Mapping)
                else getattr(arguments, "skill_name", None))
        catalog = self.catalogs.get(name.lower()) if isinstance(name, str) else None
        if catalog is not None:
            skill, native = catalog
            # MAF 1.17 returns raw SKILL.md, not a typed success envelope. Compare
            # against the native cached content via its public API, never error
            # prefixes or instruction wording. A known name alone grants nothing.
            if result[0].text == await skill.get_content():
                context.add_tools(native.functions)
