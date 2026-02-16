"""
Weather Agent Executor for A2A SDK.
"""
import logging
from typing import override

from a2a.server.agent_execution import AgentExecutor, RequestContext
from a2a.server.events import EventQueue
from a2a.utils import new_agent_text_message

from agent_framework.azure import AzureOpenAIChatClient
from azure.identity import AzureCliCredential

from tools.weather_tools import get_current_conditions, get_forecast, is_storm_incoming

logger = logging.getLogger(__name__)


class WeatherAgentExecutor(AgentExecutor):

    def __init__(self):
        self.agent = AzureOpenAIChatClient(credential=AzureCliCredential()).as_agent(
            name="weather-agent",
            instructions="""You are the Weather Intelligence Agent for AlpineAI ski resort. 
Your role is to help skiers, staff, and resort operators understand current weather conditions, 
upcoming forecasts, and potential storm threats.

When users ask questions, always provide specific numbers and actionable recommendations.
Be concise but thorough. Safety is the top priority.""",
            tools=[get_current_conditions, get_forecast, is_storm_incoming],
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
        await event_queue.enqueue_event(new_agent_text_message("Weather query cancelled"))
