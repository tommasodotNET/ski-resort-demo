"""
Safety Agent Executor for A2A SDK.
"""
import logging
import os

from agent_framework.foundry import FoundryChatClient
from agent_framework_a2a import A2AExecutor
from azure.identity import DefaultAzureCredential

from tools.safety_tools import evaluate_risk, is_slope_safe, get_closed_slopes

logger = logging.getLogger(__name__)


class SafetyAgentExecutor(A2AExecutor):

    def __init__(self):
        agent = FoundryChatClient(project_endpoint=os.getenv("GPT41_URI"), credential=DefaultAzureCredential(), model="gpt41",).as_agent(
            name="safetyagenta2a",
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
        super().__init__(agent, stream=True)
