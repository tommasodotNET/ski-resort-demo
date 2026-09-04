"""
Weather Agent Executor for A2A SDK.
"""
import logging
import os

from agent_framework.foundry import FoundryChatClient
from agent_framework_a2a import A2AExecutor
from azure.identity import DefaultAzureCredential

from tools.weather_tools import get_current_conditions, get_forecast, is_storm_incoming

logger = logging.getLogger(__name__)


class WeatherAgentExecutor(A2AExecutor):

    def __init__(self):
        agent = FoundryChatClient(project_endpoint=os.getenv("GPT41_URI"), credential=DefaultAzureCredential(), model="gpt41").as_agent(
            name="weatheragenta2a",
            instructions="""You are the Weather Intelligence Agent for AlpineAI ski resort. 
Your role is to help skiers, staff, and resort operators understand current weather conditions, 
upcoming forecasts, and potential storm threats.

When users ask questions, always provide specific numbers and actionable recommendations.
Be concise but thorough. Safety is the top priority.""",
            tools=[get_current_conditions, get_forecast, is_storm_incoming],
        )
        super().__init__(agent, stream=True)
