"""
Ski Coach Agent Executor for A2A SDK.
"""
import logging
from typing import override

from a2a.server.agent_execution import AgentExecutor, RequestContext
from a2a.server.events import EventQueue
from a2a.utils import new_agent_text_message

from agent_framework.azure import AzureOpenAIChatClient
from azure.identity import AzureCliCredential

from tools.coach_tools import recommend_slope, build_day_plan

logger = logging.getLogger(__name__)


class SkiCoachAgentExecutor(AgentExecutor):

    def __init__(self):
        self.agent = AzureOpenAIChatClient(credential=AzureCliCredential()).as_agent(
            name="ski-coach-agent",
            instructions="""You are the Ski Coach Agent for AlpineAI ski resort. You help skiers find the best slopes based on their skill level, preferences, and current conditions.

When users ask for recommendations, always ask about their skill level if not provided (beginner, intermediate, advanced, expert).
Use the recommend_slope tool to get current conditions and recommendations.
Use the build_day_plan tool to create a structured day schedule.

Always be encouraging and helpful. Skiing should be fun and safe!""",
            tools=[recommend_slope, build_day_plan],
        )

    @override
    async def execute(self, context: RequestContext, event_queue: EventQueue) -> None:
        query = context.get_user_input()
        if not context.message:
            raise Exception('No message provided')

        try:
            response = await self.agent.run(query)
            await event_queue.enqueue_event(new_agent_text_message(response.text))
        except Exception as e:
            logger.error(f"Error during execution: {e}", exc_info=True)
            await event_queue.enqueue_event(new_agent_text_message(f"Error: {str(e)}"))

    @override
    async def cancel(self, context: RequestContext, event_queue: EventQueue) -> None:
        await event_queue.enqueue_event(new_agent_text_message("Operation cancelled"))
