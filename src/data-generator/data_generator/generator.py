"""
Data generation logic for ski resort telemetry.
"""
import json
import random
from datetime import datetime
from pathlib import Path
from typing import List

from .models import (
    WeatherData,
    LiftData,
    SafetyData,
    SlopeData,
    ResortState,
    IncidentReport,
)


def _load_config() -> dict:
    """Load configuration from config.json."""
    config_path = Path(__file__).parent / "config.json"
    with open(config_path) as f:
        return json.load(f)


class DataGenerator:
    """Generates and evolves synthetic ski resort telemetry data."""

    def __init__(self):
        """Initialize the generator with realistic starting values."""
        self.config = _load_config()
        self.current_time = datetime.now()
        
        # Initialize lifts
        self.lifts = self._create_initial_lifts()
        
        # Initialize slopes
        self.slopes = self._create_initial_slopes()
        
        # Initialize weather
        self.weather = WeatherData(
            temperature=random.uniform(-10, 0),
            wind_speed=random.uniform(5, 25),
            snow_intensity=random.uniform(0, 2),
            visibility=random.uniform(5000, 10000),
            timestamp=self.current_time,
        )
        
        # Initialize safety
        self.safety = SafetyData(
            avalanche_risk_index=random.uniform(0.1, 0.4),
            incident_reports=[],
            timestamp=self.current_time,
        )
        
        # Keep track of recent incidents (last 20)
        self._incident_history: List[IncidentReport] = []

    def _create_initial_lifts(self) -> List[LiftData]:
        """Create initial lift configurations."""
        # Each lift serves specific slopes (defined in _create_initial_slopes)
        lift_configs = [
            ("gondola-1", "Summit Gondola", 2400, "open", ["summit-chute", "avalanche-alley"]),
            ("chairlift-alpha", "Alpine Express", 1800, "open", ["alpine-meadow", "north-face"]),
            ("chairlift-bravo", "Eagle Chair", 1600, "open", ["eagle-ridge", "timber-bowl"]),
            ("t-bar-1", "Beginner T-Bar", 800, "open", ["valley-run"]),
            ("magic-carpet-1", "Kids Magic Carpet", 400, "open", ["sunrise-trail"]),
        ]
        
        lifts = []
        for lift_id, name, throughput, status, serves_slopes in lift_configs:
            queue = random.randint(10, 80)
            wait_time = (queue / throughput) * 60 if throughput > 0 else 0
            
            lifts.append(LiftData(
                lift_id=lift_id,
                name=name,
                status=status,
                queue_length=queue,
                wait_time_minutes=round(wait_time, 1),
                throughput_rate=throughput,
                serves_slopes=serves_slopes,
                timestamp=self.current_time,
            ))
        
        return lifts

    def _create_initial_slopes(self) -> List[SlopeData]:
        """Create initial slope configurations."""
        slope_configs = [
            ("valley-run", "Valley Run", "green", True, True, 85, "t-bar-1"),
            ("sunrise-trail", "Sunrise Trail", "green", True, True, 90, "magic-carpet-1"),
            ("alpine-meadow", "Alpine Meadow", "blue", True, True, 105, "chairlift-alpha"),
            ("eagle-ridge", "Eagle Ridge", "blue", True, False, 95, "chairlift-bravo"),
            ("timber-bowl", "Timber Bowl", "blue", True, False, 110, "chairlift-bravo"),
            ("north-face", "North Face", "red", True, False, 120, "chairlift-alpha"),
            ("summit-chute", "Summit Chute", "black", True, False, 130, "gondola-1"),
            ("avalanche-alley", "Avalanche Alley", "black", True, False, 125, "gondola-1"),
        ]
        
        slopes = []
        for slope_id, name, difficulty, is_open, groomed, base_depth, lift_id in slope_configs:
            depth_variance = random.uniform(-10, 10)
            
            slopes.append(SlopeData(
                slope_id=slope_id,
                name=name,
                difficulty=difficulty,
                is_open=is_open,
                groomed=groomed,
                snow_depth_cm=round(base_depth + depth_variance, 1),
                served_by_lift_id=lift_id,
            ))
        
        return slopes

    def update(self) -> None:
        """
        Update all telemetry data with realistic changes.
        Called every 1-3 seconds to simulate real-time evolution.
        """
        self.current_time = datetime.now()
        
        # Update weather with gradual changes
        self._update_weather()
        
        # Update lifts
        self._update_lifts()
        
        # Update safety data
        self._update_safety()
        
        # Update slopes based on conditions
        self._update_slopes()

    def _update_weather(self) -> None:
        """Update weather conditions with gradual random walks."""
        cfg = self.config["weather"]
        d = cfg["temperature_drift"]
        temp_delta = random.uniform(-d, d)
        new_temp = self.weather.temperature + temp_delta
        self.weather.temperature = max(-15, min(5, new_temp))
        
        d = cfg["wind_speed_drift"]
        wind_delta = random.uniform(-d, d)
        new_wind = self.weather.wind_speed + wind_delta
        self.weather.wind_speed = max(0, min(80, new_wind))
        
        d = cfg["snow_intensity_drift"]
        snow_delta = random.uniform(-d, d)
        new_snow = self.weather.snow_intensity + snow_delta
        self.weather.snow_intensity = max(0, min(5, new_snow))
        
        d = cfg["visibility_drift"]
        vis_delta = random.uniform(-d, d)
        if self.weather.snow_intensity > 2:
            vis_delta -= d * 2
        if self.weather.wind_speed > 40:
            vis_delta -= d * 1.5
        new_vis = self.weather.visibility + vis_delta
        self.weather.visibility = max(50, min(10000, new_vis))
        
        self.weather.timestamp = self.current_time

    def _update_lifts(self) -> None:
        """Update lift operations."""
        cfg = self.config["lifts"]
        for lift in self.lifts:
            d = cfg["queue_drift"]
            queue_delta = random.randint(-d, d)
            new_queue = lift.queue_length + queue_delta
            lift.queue_length = max(0, min(200, new_queue))
            
            if random.random() < cfg["status_change_probability"]:
                if lift.status == "open":
                    lift.status = random.choice(["closed", "maintenance"])
                else:
                    lift.status = "open"
            
            if lift.status == "open" and lift.throughput_rate > 0:
                lift.wait_time_minutes = round((lift.queue_length / lift.throughput_rate) * 60, 1)
            else:
                lift.wait_time_minutes = 0
            
            lift.timestamp = self.current_time

    def _update_safety(self) -> None:
        """Update safety metrics and generate occasional incidents."""
        cfg = self.config["safety"]
        d = cfg["risk_drift"]
        risk_delta = random.uniform(-d, d)
        
        if self.weather.wind_speed > 50:
            risk_delta += d * 0.5
        if self.weather.snow_intensity > 3:
            risk_delta += d * 0.5
        
        new_risk = self.safety.avalanche_risk_index + risk_delta
        self.safety.avalanche_risk_index = max(0, min(1, new_risk))
        
        if random.random() < cfg["incident_probability"]:
            incident = self._generate_incident()
            self._incident_history.append(incident)
            # Keep only last 20 incidents
            self._incident_history = self._incident_history[-20:]
        
        self.safety.incident_reports = self._incident_history.copy()
        self.safety.timestamp = self.current_time

    def _generate_incident(self) -> IncidentReport:
        """Generate a random incident report."""
        incident_types = ["minor_injury", "collision", "lost_person", "equipment_failure"]
        
        # Higher avalanche risk increases avalanche warnings
        if self.safety.avalanche_risk_index > 0.7:
            incident_types.append("avalanche_warning")
            incident_types.append("avalanche_warning")  # Higher probability
        
        incident_type = random.choice(incident_types)
        
        # Determine severity based on type
        severity_map = {
            "minor_injury": ["low", "medium"],
            "collision": ["low", "medium", "high"],
            "lost_person": ["medium", "high"],
            "equipment_failure": ["low", "medium", "high"],
            "avalanche_warning": ["high", "critical"],
        }
        
        severity = random.choice(severity_map[incident_type])
        
        # Random location from slopes and lifts
        locations = [slope.name for slope in self.slopes] + [lift.name for lift in self.lifts]
        location = random.choice(locations)
        
        return IncidentReport(
            incident_type=incident_type,
            location=location,
            severity=severity,
            timestamp=self.current_time,
        )

    def _update_slopes(self) -> None:
        """Update slope conditions based on weather and safety."""
        cfg = self.config["slopes"]
        for slope in self.slopes:
            d = cfg["depth_drift"]
            depth_delta = random.uniform(-d, d)
            if self.weather.snow_intensity > 1:
                depth_delta += self.weather.snow_intensity * 0.1
            
            new_depth = slope.snow_depth_cm + depth_delta
            slope.snow_depth_cm = round(max(0, new_depth), 1)
            
            if slope.difficulty == "black" and self.safety.avalanche_risk_index > 0.8:
                slope.is_open = False
            
            if slope.difficulty in ["black", "red"] and self.weather.wind_speed > 60:
                slope.is_open = False
            
            if not slope.is_open and random.random() < cfg["reopen_probability"]:
                if not (slope.difficulty == "black" and self.safety.avalanche_risk_index > 0.8):
                    if not (slope.difficulty in ["black", "red"] and self.weather.wind_speed > 60):
                        slope.is_open = True
            
            if slope.difficulty in ["green", "blue"] and random.random() < cfg["groom_probability"]:
                slope.groomed = True
            elif slope.groomed and random.random() < cfg["ungroom_probability"]:
                slope.groomed = False

    def get_state(self) -> ResortState:
        """Get the current complete resort state."""
        return ResortState(
            weather=self.weather,
            lifts=self.lifts,
            safety=self.safety,
            slopes=self.slopes,
            timestamp=self.current_time,
        )
