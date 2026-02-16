"""
Weather service for fetching data from the data-generator service.
"""
import os
import logging
import random
from typing import Dict, Any, List
import httpx

logger = logging.getLogger(__name__)


class WeatherService:
    """Service for fetching and processing weather data from the data-generator."""
    
    def __init__(self):
        """Initialize the weather service."""
        self.data_generator_url = os.environ.get("services__data-generator__http__0")
        if not self.data_generator_url:
            logger.warning("services__data-generator__http__0 environment variable not set")
            self.data_generator_url = "http://localhost:8080"  # Fallback
        
        logger.info(f"WeatherService initialized with data-generator URL: {self.data_generator_url}")
    
    async def get_current_conditions(self) -> Dict[str, Any]:
        """
        Get current weather conditions from the data-generator.
        
        Returns:
            dict: Current weather data with temperature, wind_speed, snow_intensity, visibility, timestamp
        """
        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                response = await client.get(f"{self.data_generator_url}/api/weather")
                response.raise_for_status()
                data = response.json()
                logger.info(f"Retrieved current weather conditions: {data}")
                return data
        except Exception as e:
            logger.error(f"Error fetching current weather conditions: {e}")
            # Return fallback data
            return {
                "temperature": -5.0,
                "wind_speed": 15.0,
                "snow_intensity": 1,
                "visibility": 5000,
                "timestamp": "unavailable",
                "error": str(e)
            }
    
    async def get_forecast(self, hours: int) -> Dict[str, Any]:
        """
        Generate a weather forecast by projecting current conditions forward.
        
        Args:
            hours: Number of hours to forecast (1-24)
            
        Returns:
            dict: Forecast data with hourly projections
        """
        # Clamp hours to valid range
        hours = max(1, min(24, hours))
        
        try:
            # Get current conditions as baseline
            current = await self.get_current_conditions()
            
            # Generate hourly forecast with small random variations
            forecast_hours: List[Dict[str, Any]] = []
            
            base_temp = current.get("temperature", -5.0)
            base_wind = current.get("wind_speed", 15.0)
            base_snow = current.get("snow_intensity", 1)
            base_visibility = current.get("visibility", 5000)
            
            for hour in range(1, hours + 1):
                # Add random variations to simulate forecast
                temp_variation = random.uniform(-2, 2)
                wind_variation = random.uniform(-5, 5)
                snow_variation = random.randint(-1, 1)
                visibility_variation = random.randint(-500, 500)
                
                forecast_hours.append({
                    "hour": hour,
                    "temperature": round(base_temp + temp_variation, 1),
                    "wind_speed": round(max(0, base_wind + wind_variation), 1),
                    "snow_intensity": max(0, min(5, base_snow + snow_variation)),
                    "visibility": max(100, base_visibility + visibility_variation)
                })
            
            return {
                "current_conditions": current,
                "forecast_hours": hours,
                "hourly_forecast": forecast_hours
            }
            
        except Exception as e:
            logger.error(f"Error generating forecast: {e}")
            return {
                "error": str(e),
                "forecast_hours": hours,
                "hourly_forecast": []
            }
    
    async def is_storm_incoming(self) -> Dict[str, Any]:
        """
        Assess if a storm is incoming based on current conditions.
        
        Returns:
            dict: Storm assessment with storm_incoming (bool) and reason (str)
        """
        try:
            current = await self.get_current_conditions()
            
            wind_speed = current.get("wind_speed", 0)
            snow_intensity = current.get("snow_intensity", 0)
            visibility = current.get("visibility", 10000)
            
            reasons = []
            storm_incoming = False
            
            # Check storm conditions
            if wind_speed > 50:
                reasons.append(f"High wind speed detected: {wind_speed} km/h")
                storm_incoming = True
            
            if snow_intensity > 3:
                reasons.append(f"Heavy snow intensity: {snow_intensity}/5")
                storm_incoming = True
            
            if visibility < 500:
                reasons.append(f"Low visibility: {visibility}m")
                storm_incoming = True
            
            # Also check for warning signs (not quite storm level but concerning)
            if not storm_incoming:
                if wind_speed > 40:
                    reasons.append(f"Elevated wind speed: {wind_speed} km/h")
                if snow_intensity >= 3:
                    reasons.append(f"Moderate to heavy snow: {snow_intensity}/5")
                if visibility < 1000:
                    reasons.append(f"Reduced visibility: {visibility}m")
            
            if storm_incoming:
                reason = "Storm conditions detected: " + "; ".join(reasons)
            elif reasons:
                reason = "Monitoring conditions: " + "; ".join(reasons)
            else:
                reason = f"Conditions are good (Wind: {wind_speed} km/h, Snow: {snow_intensity}/5, Visibility: {visibility}m)"
            
            return {
                "storm_incoming": storm_incoming,
                "reason": reason,
                "current_conditions": {
                    "wind_speed": wind_speed,
                    "snow_intensity": snow_intensity,
                    "visibility": visibility
                }
            }
            
        except Exception as e:
            logger.error(f"Error assessing storm conditions: {e}")
            return {
                "storm_incoming": False,
                "reason": f"Unable to assess storm conditions: {str(e)}",
                "error": str(e)
            }
