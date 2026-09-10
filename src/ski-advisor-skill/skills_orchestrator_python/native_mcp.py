"""Run-scoped lifetime for native MAF progressive MCP tools.

The native tool owns mutable loaded-tool names, so it must not live on the
shared host agent. This middleware supplies fresh native instances as runtime
tools, without replacing native discovery, loading, invocation or approval.
The connected MCP sessions themselves remain owned by the host's exit stack.
"""
from __future__ import annotations

from contextlib import AsyncExitStack, asynccontextmanager
from dataclasses import dataclass
from typing import Any, Literal

from agent_framework import (
    AgentMiddleware,
    AgentResponse,
    MCPStreamableHTTPTool,
    ResponseStream,
)
from mcp import ClientSession

from .config import SkillProviderConfig


@dataclass(frozen=True)
class SkillConnection:
    config: SkillProviderConfig
    url: str
    session: ClientSession


class NativeMCPToolsMiddleware(AgentMiddleware):
    """Add native progressive tools for one invocation, including lazy streams.

    Install AFTER the standard ToolApprovalMiddleware. That middleware binds
    incoming approvals to pending calls before invoking us. Approved direct MCP
    calls are made available through native ``always_load`` on continuation;
    this changes exposure only, never grants approval. Ordinary followups start
    with loader tools again and can reload from canonical skill instructions.
    """

    def __init__(
        self,
        connections: list[SkillConnection],
        *,
        approval_mode: Literal["never_require", "always_require"] = "never_require",
    ) -> None:
        self.connections = tuple(connections)
        self.approval_mode = approval_mode

    @asynccontextmanager
    async def _tools_for_run(self, context: Any):
        approved_names = {
            content.function_call.name
            for message in context.messages
            for content in message.contents
            if content.type == "function_approval_response"
            and content.approved is True
            and content.function_call is not None
            and content.function_call.name is not None
        }
        original_tools = context.tools
        async with AsyncExitStack() as stack:
            tools = []
            for connection in self.connections:
                prefix = connection.config.key
                native = MCPStreamableHTTPTool(
                    name=prefix,
                    url=connection.url,
                    session=connection.session,
                    tool_name_prefix=prefix,
                    load_prompts=False,
                    allowed_tools=connection.config.allowed_tools,
                    use_progressive_disclosure=True,
                    always_load=[name for name in approved_names if name.startswith(f"{prefix}_")],
                    approval_mode=self.approval_mode,
                )
                tools.append(await stack.enter_async_context(native))
            context.tools = [*(original_tools or []), *tools]
            try:
                yield
            finally:
                context.tools = original_tools

    async def process(self, context: Any, call_next: Any) -> None:
        if not context.stream:
            async with self._tools_for_run(context):
                await call_next()
            return

        async def stream():
            async with self._tools_for_run(context):
                await call_next()
                inner = context.result
                async for update in inner:
                    yield update

        context.result = ResponseStream(stream(), finalizer=AgentResponse.from_updates)
