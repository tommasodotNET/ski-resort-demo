"""
Pydantic models for ski resort telemetry data.
"""
from datetime import datetime
from typing import Literal, List
from pydantic import BaseModel, Field


class WeatherData(BaseModel):
    """Current weather conditions at the resort."""
    temperature: float = Field(..., description="Temperature in Celsius, range -15 to 5")
    wind_speed: float = Field(..., description="Wind speed in km/h, range 0 to 80")
    snow_intensity: float = Field(..., description="Snowfall rate in cm/h, range 0 to 5")
    visibility: float = Field(..., description="Visibility in meters, range 50 to 10000")
    timestamp: datetime


class LiftData(BaseModel):
    """Data for a single ski lift."""
    lift_id: str = Field(..., description="Unique lift identifier")
    name: str = Field(..., description="Display name of the lift")
    status: Literal["open", "closed", "maintenance"] = Field(..., description="Current operational status")
    queue_length: int = Field(..., description="Number of people in queue, 0-200")
    wait_time_minutes: float = Field(..., description="Estimated wait time in minutes")
    throughput_rate: int = Field(..., description="People per hour capacity")
    timestamp: datetime


class IncidentReport(BaseModel):
    """Safety incident report."""
    incident_type: Literal["minor_injury", "collision", "lost_person", "equipment_failure", "avalanche_warning"]
    location: str = Field(..., description="Location where incident occurred")
    severity: Literal["low", "medium", "high", "critical"]
    timestamp: datetime


class SafetyData(BaseModel):
    """Safety and risk assessment data."""
    avalanche_risk_index: float = Field(..., description="Avalanche risk from 0.0 (safe) to 1.0 (extreme)")
    incident_reports: List[IncidentReport] = Field(default_factory=list, description="Recent incident reports")
    timestamp: datetime


class SlopeData(BaseModel):
    """Data for a single ski slope."""
    slope_id: str = Field(..., description="Unique slope identifier")
    name: str = Field(..., description="Display name of the slope")
    difficulty: Literal["green", "blue", "red", "black"] = Field(..., description="Difficulty rating")
    is_open: bool = Field(..., description="Whether the slope is currently open")
    groomed: bool = Field(..., description="Whether the slope has been recently groomed")
    snow_depth_cm: float = Field(..., description="Snow depth in centimeters")


class ResortState(BaseModel):
    """Complete state of the ski resort at a point in time."""
    weather: WeatherData
    lifts: List[LiftData]
    safety: SafetyData
    slopes: List[SlopeData]
    timestamp: datetime
