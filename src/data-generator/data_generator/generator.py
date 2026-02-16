"""
Data generation logic for ski resort telemetry.
"""
import random
from datetime import datetime
from typing import List

from .models import (
    WeatherData,
    LiftData,
    SafetyData,
    SlopeData,
    ResortState,
    IncidentReport,
)


class DataGenerator:
    """Generates and evolves synthetic ski resort telemetry data."""

    def __init__(self):
        """Initialize the generator with realistic starting values."""
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
        lift_configs = [
            ("gondola-1", "Summit Gondola", 2400, "open"),
            ("chairlift-alpha", "Alpine Express", 1800, "open"),
            ("chairlift-bravo", "Eagle Chair", 1600, "open"),
            ("t-bar-1", "Beginner T-Bar", 800, "open"),
            ("magic-carpet-1", "Kids Magic Carpet", 400, "open"),
        ]
        
        lifts = []
        for lift_id, name, throughput, status in lift_configs:
            queue = random.randint(10, 80)
            wait_time = (queue / throughput) * 60 if throughput > 0 else 0
            
            lifts.append(LiftData(
                lift_id=lift_id,
                name=name,
                status=status,
                queue_length=queue,
                wait_time_minutes=round(wait_time, 1),
                throughput_rate=throughput,
                timestamp=self.current_time,
            ))
        
        return lifts

    def _create_initial_slopes(self) -> List[SlopeData]:
        """Create initial slope configurations."""
        slope_configs = [
            ("valley-run", "Valley Run", "green", True, True, 85),
            ("sunrise-trail", "Sunrise Trail", "green", True, True, 90),
            ("alpine-meadow", "Alpine Meadow", "blue", True, True, 105),
            ("eagle-ridge", "Eagle Ridge", "blue", True, False, 95),
            ("timber-bowl", "Timber Bowl", "blue", True, False, 110),
            ("north-face", "North Face", "red", True, False, 120),
            ("summit-chute", "Summit Chute", "black", True, False, 130),
            ("avalanche-alley", "Avalanche Alley", "black", True, False, 125),
        ]
        
        slopes = []
        for slope_id, name, difficulty, is_open, groomed, base_depth in slope_configs:
            depth_variance = random.uniform(-10, 10)
            
            slopes.append(SlopeData(
                slope_id=slope_id,
                name=name,
                difficulty=difficulty,
                is_open=is_open,
                groomed=groomed,
                snow_depth_cm=round(base_depth + depth_variance, 1),
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
        # Temperature drift
        temp_delta = random.uniform(-0.3, 0.3)
        new_temp = self.weather.temperature + temp_delta
        self.weather.temperature = max(-15, min(5, new_temp))
        
        # Wind speed drift
        wind_delta = random.uniform(-2, 2)
        new_wind = self.weather.wind_speed + wind_delta
        self.weather.wind_speed = max(0, min(80, new_wind))
        
        # Snow intensity drift
        snow_delta = random.uniform(-0.2, 0.2)
        new_snow = self.weather.snow_intensity + snow_delta
        self.weather.snow_intensity = max(0, min(5, new_snow))
        
        # Visibility drift (inverse relationship with snow/wind)
        vis_delta = random.uniform(-100, 100)
        if self.weather.snow_intensity > 2:
            vis_delta -= 200
        if self.weather.wind_speed > 40:
            vis_delta -= 150
        new_vis = self.weather.visibility + vis_delta
        self.weather.visibility = max(50, min(10000, new_vis))
        
        self.weather.timestamp = self.current_time

    def _update_lifts(self) -> None:
        """Update lift operations."""
        for lift in self.lifts:
            # Queue length fluctuates
            queue_delta = random.randint(-10, 10)
            new_queue = lift.queue_length + queue_delta
            lift.queue_length = max(0, min(200, new_queue))
            
            # Occasionally change status (1% chance)
            if random.random() < 0.01:
                if lift.status == "open":
                    lift.status = random.choice(["closed", "maintenance"])
                else:
                    lift.status = "open"
            
            # Recalculate wait time from queue and throughput
            if lift.status == "open" and lift.throughput_rate > 0:
                lift.wait_time_minutes = round((lift.queue_length / lift.throughput_rate) * 60, 1)
            else:
                lift.wait_time_minutes = 0
            
            lift.timestamp = self.current_time

    def _update_safety(self) -> None:
        """Update safety metrics and generate occasional incidents."""
        # Avalanche risk drifts slowly
        risk_delta = random.uniform(-0.02, 0.02)
        
        # Risk increases with wind and snow
        if self.weather.wind_speed > 50:
            risk_delta += 0.01
        if self.weather.snow_intensity > 3:
            risk_delta += 0.01
        
        new_risk = self.safety.avalanche_risk_index + risk_delta
        self.safety.avalanche_risk_index = max(0, min(1, new_risk))
        
        # Occasionally generate incidents (0.5% chance per tick)
        if random.random() < 0.005:
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
        for slope in self.slopes:
            # Snow depth drifts (accumulates during snowfall)
            depth_delta = random.uniform(-0.5, 0.5)
            if self.weather.snow_intensity > 1:
                depth_delta += self.weather.snow_intensity * 0.3
            
            new_depth = slope.snow_depth_cm + depth_delta
            slope.snow_depth_cm = round(max(0, new_depth), 1)
            
            # Close black slopes if avalanche risk is very high
            if slope.difficulty == "black" and self.safety.avalanche_risk_index > 0.8:
                slope.is_open = False
            
            # Close black and red slopes if wind is too high
            if slope.difficulty in ["black", "red"] and self.weather.wind_speed > 60:
                slope.is_open = False
            
            # Reopen slopes if conditions improve (10% chance per tick if closed)
            if not slope.is_open and random.random() < 0.1:
                if not (slope.difficulty == "black" and self.safety.avalanche_risk_index > 0.8):
                    if not (slope.difficulty in ["black", "red"] and self.weather.wind_speed > 60):
                        slope.is_open = True
            
            # Occasionally groom green and blue slopes (0.5% chance)
            if slope.difficulty in ["green", "blue"] and random.random() < 0.005:
                slope.groomed = True
            # Ungroomed after time (1% chance if currently groomed)
            elif slope.groomed and random.random() < 0.01:
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
