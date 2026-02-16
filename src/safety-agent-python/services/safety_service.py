"""
Safety Service - Data fetching and risk evaluation logic.
"""
import os
import logging
from typing import Dict, Any, List
import httpx

logger = logging.getLogger(__name__)


class SafetyService:
    """
    Safety service that fetches data from data-generator and applies risk evaluation rules.
    """

    def __init__(self):
        """Initialize the safety service with data-generator endpoint."""
        self.data_generator_url = os.getenv("services__data-generator__http__0")
        if not self.data_generator_url:
            logger.warning("services__data-generator__http__0 not set, using default")
            self.data_generator_url = "http://localhost:8080"
        
        logger.info(f"SafetyService initialized with data-generator at: {self.data_generator_url}")

    async def _fetch_weather(self) -> Dict[str, Any]:
        """Fetch weather data from data-generator."""
        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                response = await client.get(f"{self.data_generator_url}/api/weather")
                response.raise_for_status()
                return response.json()
        except Exception as e:
            logger.error(f"Error fetching weather data: {e}")
            return {"temperature": 0, "wind_speed": 0, "snow_intensity": 0, "visibility": 5000}

    async def _fetch_safety(self) -> Dict[str, Any]:
        """Fetch safety data from data-generator."""
        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                response = await client.get(f"{self.data_generator_url}/api/safety")
                response.raise_for_status()
                return response.json()
        except Exception as e:
            logger.error(f"Error fetching safety data: {e}")
            return {"avalanche_risk_index": 0.0, "incident_reports": []}

    async def _fetch_slopes(self) -> List[Dict[str, Any]]:
        """Fetch slopes data from data-generator."""
        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                response = await client.get(f"{self.data_generator_url}/api/slopes")
                response.raise_for_status()
                return response.json()
        except Exception as e:
            logger.error(f"Error fetching slopes data: {e}")
            return []

    def _calculate_risk_score(self, weather: Dict[str, Any], safety: Dict[str, Any]) -> tuple[float, List[str]]:
        """
        Calculate risk score based on weather and safety data using rule engine.
        
        Returns:
            tuple: (risk_score, factors)
        """
        # Base risk from avalanche index
        risk = safety.get("avalanche_risk_index", 0.0)
        factors = []
        
        # Wind speed rules
        wind_speed = weather.get("wind_speed", 0)
        if wind_speed > 50:
            risk += 0.2
            factors.append(f"Extreme wind speed: {wind_speed} km/h")
        elif wind_speed > 30:
            risk += 0.1
            factors.append(f"High wind speed: {wind_speed} km/h")
        
        # Visibility rules
        visibility = weather.get("visibility", 5000)
        if visibility < 500:
            risk += 0.15
            factors.append(f"Very low visibility: {visibility}m")
        elif visibility < 1000:
            risk += 0.05
            factors.append(f"Low visibility: {visibility}m")
        
        # Snow intensity rules
        snow_intensity = weather.get("snow_intensity", 0)
        if snow_intensity > 3:
            risk += 0.1
            factors.append(f"Heavy snowfall: intensity {snow_intensity}")
        
        # Add avalanche risk factor
        if safety.get("avalanche_risk_index", 0) > 0:
            factors.append(f"Avalanche risk index: {safety['avalanche_risk_index']:.2f}")
        
        # Clamp to [0, 1]
        risk = max(0.0, min(1.0, risk))
        
        return risk, factors

    def _get_risk_level(self, risk_score: float) -> str:
        """Convert risk score to risk level string."""
        if risk_score < 0.3:
            return "low"
        elif risk_score < 0.5:
            return "moderate"
        elif risk_score < 0.7:
            return "high"
        else:
            return "critical"

    async def evaluate_risk(self, area: str) -> Dict[str, Any]:
        """
        Evaluate risk for a specific area or resort-wide.
        
        Args:
            area: Area or zone name to evaluate. Use 'all' for resort-wide.
            
        Returns:
            dict: Risk evaluation with risk_level, risk_score, factors, and affected_slopes
        """
        try:
            # Fetch all data
            weather = await self._fetch_weather()
            safety = await self._fetch_safety()
            slopes = await self._fetch_slopes()
            
            # Calculate risk
            risk_score, factors = self._calculate_risk_score(weather, safety)
            risk_level = self._get_risk_level(risk_score)
            
            # Filter slopes by area
            if area and area.lower() != "all":
                affected_slopes = [
                    s for s in slopes 
                    if area.lower() in s.get("name", "").lower()
                ]
            else:
                affected_slopes = slopes
            
            return {
                "area": area if area else "all",
                "risk_level": risk_level,
                "risk_score": round(risk_score, 2),
                "factors": factors,
                "affected_slopes": [
                    {
                        "slope_id": s.get("slope_id"),
                        "name": s.get("name"),
                        "difficulty": s.get("difficulty"),
                        "is_open": s.get("is_open"),
                    }
                    for s in affected_slopes
                ],
                "weather": weather,
                "incident_reports": safety.get("incident_reports", [])
            }
            
        except Exception as e:
            logger.error(f"Error evaluating risk: {e}")
            return {
                "area": area,
                "risk_level": "unknown",
                "risk_score": 0.0,
                "factors": [f"Error: {str(e)}"],
                "affected_slopes": []
            }

    async def is_slope_safe(self, slope_id: str) -> Dict[str, Any]:
        """
        Check if a specific slope is safe to ski on.
        
        Args:
            slope_id: The slope ID to check
            
        Returns:
            dict: Safety assessment with is_safe, risk_score, and reasons
        """
        try:
            # Fetch all data
            weather = await self._fetch_weather()
            safety = await self._fetch_safety()
            slopes = await self._fetch_slopes()
            
            # Find the slope
            slope = None
            for s in slopes:
                if s.get("slope_id") == slope_id:
                    slope = s
                    break
            
            if not slope:
                return {
                    "slope_id": slope_id,
                    "is_safe": False,
                    "risk_score": 1.0,
                    "reasons": [f"Slope {slope_id} not found"]
                }
            
            # Calculate overall risk
            risk_score, factors = self._calculate_risk_score(weather, safety)
            
            reasons = []
            is_safe = True
            
            # Check if slope is open
            if not slope.get("is_open", False):
                is_safe = False
                reasons.append(f"Slope is currently closed")
            
            # Check risk based on difficulty level
            difficulty = slope.get("difficulty", "").lower()
            difficulty_thresholds = {
                "black": 0.5,
                "red": 0.6,
                "blue": 0.7,
                "green": 0.8
            }
            
            threshold = difficulty_thresholds.get(difficulty, 0.7)
            
            if risk_score > threshold:
                is_safe = False
                reasons.append(
                    f"Risk level too high for {difficulty} slope: "
                    f"{risk_score:.2f} (threshold: {threshold})"
                )
            
            # Add risk factors
            if factors:
                reasons.extend(factors)
            
            return {
                "slope_id": slope_id,
                "slope_name": slope.get("name"),
                "difficulty": slope.get("difficulty"),
                "is_safe": is_safe,
                "risk_score": round(risk_score, 2),
                "reasons": reasons if reasons else ["Slope is safe for skiing"]
            }
            
        except Exception as e:
            logger.error(f"Error checking slope safety: {e}")
            return {
                "slope_id": slope_id,
                "is_safe": False,
                "risk_score": 1.0,
                "reasons": [f"Error: {str(e)}"]
            }

    async def get_closed_slopes(self) -> Dict[str, Any]:
        """
        Get all closed slopes with reasons.
        
        Returns:
            dict: List of closed slopes with reasons
        """
        try:
            slopes = await self._fetch_slopes()
            
            closed_slopes = [
                {
                    "slope_id": s.get("slope_id"),
                    "name": s.get("name"),
                    "difficulty": s.get("difficulty"),
                    "reasons": ["Slope is closed by resort management"]
                }
                for s in slopes
                if not s.get("is_open", False)
            ]
            
            return {
                "closed_slopes": closed_slopes,
                "total_closed": len(closed_slopes)
            }
            
        except Exception as e:
            logger.error(f"Error getting closed slopes: {e}")
            return {
                "closed_slopes": [],
                "total_closed": 0,
                "error": str(e)
            }
