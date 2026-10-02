"""
Ski Coach Agent Executor for A2A SDK.
"""
import logging
import os

from agent_framework.foundry import FoundryChatClient
from agent_framework_a2a import A2AExecutor
from azure.identity import DefaultAzureCredential

from tools.coach_tools import recommend_slope, build_day_plan

logger = logging.getLogger(__name__)


class SkiCoachAgentExecutor(A2AExecutor):

    def __init__(self):
        agent = FoundryChatClient(project_endpoint=os.getenv("GPT41_URI"), credential=DefaultAzureCredential(), model="gpt41").as_agent(
            name="skicoachagenta2a",
            instructions="""You are the Ski Coach Agent for AlpineAI ski resort. You help skiers find the best slopes based on their skill level, preferences, and current conditions.

When users ask for recommendations, always ask about their skill level if not provided (beginner, intermediate, advanced, expert).
Use the recommend_slope tool to get current conditions and recommendations.
Use the build_day_plan tool to create a structured day schedule.

Always be encouraging and helpful. Skiing should be fun and safe!""",
            tools=[recommend_slope, build_day_plan],
        )
        super().__init__(agent, stream=True)
