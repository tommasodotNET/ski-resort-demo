"""Keep complete Cosmos transcripts, but replay only conversational messages."""
from __future__ import annotations

from copy import copy
from typing import Any

from agent_framework import AgentSession, Message, SessionContext, SupportsAgentRun
from agent_framework.azure import CosmosHistoryProvider


class ConversationCosmosHistoryProvider(CosmosHistoryProvider):
    async def before_run(
        self,
        *,
        agent: SupportsAgentRun,
        session: AgentSession,
        context: SessionContext,
        state: dict[str, Any],
    ) -> None:
        await super().before_run(agent=agent, session=session, context=context, state=state)
        conversation: list[Message] = []
        for message in context.context_messages.get(self.source_id, []):
            if message.role not in {"user", "assistant"}:
                continue
            contents = [
                content for content in message.contents
                if content.type not in {"function_call", "function_result"}
            ]
            if contents:
                replay = copy(message)
                replay.contents = contents
                conversation.append(replay)
        context.context_messages[self.source_id] = conversation
