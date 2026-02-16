"""
Weather tools - AI agent tools for weather-related functions.
"""
import json
import logging
from typing import Annotated

from pydantic import Field
from agent_framework import tool

from services.weather_service import WeatherService

logger = logging.getLogger(__name__)

_weather_service = WeatherService()


@tool(name="get_current_conditions", description="Get current weather conditions at the ski resort including temperature, wind speed, snow intensity, and visibility")
async def get_current_conditions() -> str:
    try:
        conditions = await _weather_service.get_current_conditions()
        return json.dumps(conditions, indent=2)
    except Exception as e:
        logger.error(f"Error getting current conditions: {e}")
        return json.dumps({"error": str(e)})


@tool(name="get_forecast", description="Get a weather forecast for the specified number of hours ahead (1-24)")
async def get_forecast(
    hours: Annotated[int, Field(description="Number of hours to forecast, 1-24")] = 6,
) -> str:
    try:
        forecast = await _weather_service.get_forecast(hours)
        return json.dumps(forecast, indent=2)
    except Exception as e:
        logger.error(f"Error getting forecast: {e}")
        return json.dumps({"error": str(e)})


@tool(name="is_storm_incoming", description="Assess whether a storm is incoming based on current weather conditions")
async def is_storm_incoming() -> str:
    try:
        assessment = await _weather_service.is_storm_incoming()
        return json.dumps(assessment, indent=2)
    except Exception as e:
        logger.error(f"Error assessing storm conditions: {e}")
        return json.dumps({"error": str(e)})

