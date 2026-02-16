"""
Ski Coach Tools - AI agent tools for slope recommendations and day planning.
"""
import json
from typing import Optional, Annotated

from pydantic import Field
from agent_framework import tool

from services.coach_service import CoachService

_coach_service = CoachService()


@tool(name="recommend_slope", description="Get personalized slope recommendations based on skill level and preferences")
async def recommend_slope(
    skill_level: Annotated[str, Field(description="Skier skill level: 'beginner', 'intermediate', 'advanced', or 'expert'")],
    preferences: Annotated[Optional[str], Field(description="Optional comma-separated preferences like 'avoid_crowds,groomed_only'")] = None,
) -> str:
    prefs_dict = None
    if preferences:
        prefs_dict = {p.strip(): True for p in preferences.lower().split(',') if p.strip()}
    result = await _coach_service.recommend_slope(skill_level, prefs_dict)
    return json.dumps(result, indent=2)


@tool(name="build_day_plan", description="Build a full day ski plan with morning, midday, and afternoon recommendations")
async def build_day_plan(
    skill_level: Annotated[str, Field(description="Skier skill level: 'beginner', 'intermediate', 'advanced', or 'expert'")],
) -> str:
    result = await _coach_service.build_day_plan(skill_level)
    return json.dumps(result, indent=2)

