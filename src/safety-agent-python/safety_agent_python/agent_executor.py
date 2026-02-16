"""
Safety Agent Executor for A2A SDK.
"""
import logging
from typing import override

from a2a.server.agent_execution import AgentExecutor, RequestContext
from a2a.server.events import EventQueue
from a2a.utils import new_agent_text_message

from agent_framework.azure import AzureOpenAIChatClient
from azure.identity import AzureCliCredential

from tools.safety_tools import evaluate_risk, is_slope_safe, get_closed_slopes

logger = logging.getLogger(__name__)


class SafetyAgentExecutor(AgentExecutor):

    def __init__(self):
        self.agent = AzureOpenAIChatClient(credential=AzureCliCredential()).as_agent(
            name="safety-agent",
            instructions="""You are the Safety Agent for AlpineAI ski resort. Your role is to evaluate risk across slopes using weather, avalanche, and visibility data. 

Safety is your top priority. Always err on the side of caution.

Risk levels:
- Low (< 0.3): Normal skiing conditions
- Moderate (0.3-0.5): Exercise caution
- High (0.5-0.7): Dangerous for some slopes
- Critical (>= 0.7): Recommend resort closure

When in doubt, recommend caution.""",
            tools=[evaluate_risk, is_slope_safe, get_closed_slopes],
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
        await event_queue.enqueue_event(new_agent_text_message("Safety assessment cancelled"))
