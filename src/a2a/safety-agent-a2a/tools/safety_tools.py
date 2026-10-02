"""
Safety Tools - AI agent tools for risk evaluation and slope safety.
"""
import json
from typing import Annotated

from pydantic import Field
from agent_framework import tool

from services.safety_service import SafetyService

_safety_service = SafetyService()


@tool(name="evaluate_risk", description="Evaluate safety risk for a specific area or the entire resort")
async def evaluate_risk(
    area: Annotated[str, Field(description="Area or zone name to evaluate risk for. Use 'all' for resort-wide assessment.")] = "all",
) -> str:
    result = await _safety_service.evaluate_risk(area)
    return json.dumps(result, indent=2)


@tool(name="is_slope_safe", description="Check if a specific slope is safe to ski on based on current conditions")
async def is_slope_safe(
    slope_id: Annotated[str, Field(description="The slope ID to check safety for (e.g., 'valley-run', 'north-face')")],
) -> str:
    result = await _safety_service.is_slope_safe(slope_id)
    return json.dumps(result, indent=2)


@tool(name="get_closed_slopes", description="Get a list of all currently closed slopes with reasons for closure")
async def get_closed_slopes() -> str:
    result = await _safety_service.get_closed_slopes()
    return json.dumps(result, indent=2)

