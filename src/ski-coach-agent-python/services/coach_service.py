"""
Ski Coach Service - Core business logic for slope recommendations and day planning.
"""
import os
import logging
from typing import Optional, Dict, Any, List
import httpx

logger = logging.getLogger(__name__)


class CoachService:
    """Service for ski slope recommendations and day planning."""
    
    # Metadata for all slopes in the resort
    SLOPE_METADATA = {
        "valley-run": {
            "difficulty": "green",
            "suitable_levels": ["beginner"],
            "vertical_drop_m": 150,
            "length_m": 1200,
            "features": ["wide", "gentle", "perfect-for-learning"],
        },
        "sunrise-trail": {
            "difficulty": "green",
            "suitable_levels": ["beginner"],
            "vertical_drop_m": 180,
            "length_m": 1400,
            "features": ["scenic", "wide", "groomed"],
        },
        "alpine-meadow": {
            "difficulty": "blue",
            "suitable_levels": ["intermediate", "beginner"],
            "vertical_drop_m": 300,
            "length_m": 2200,
            "features": ["cruising", "groomed", "family-friendly"],
        },
        "eagle-ridge": {
            "difficulty": "blue",
            "suitable_levels": ["intermediate"],
            "vertical_drop_m": 350,
            "length_m": 2500,
            "features": ["scenic-views", "varied-terrain", "groomed"],
        },
        "timber-bowl": {
            "difficulty": "blue",
            "suitable_levels": ["intermediate", "advanced"],
            "vertical_drop_m": 400,
            "length_m": 2800,
            "features": ["tree-skiing", "powder-stashes", "challenging"],
        },
        "north-face": {
            "difficulty": "red",
            "suitable_levels": ["advanced"],
            "vertical_drop_m": 500,
            "length_m": 2400,
            "features": ["steep", "moguls", "expert-territory"],
        },
        "summit-chute": {
            "difficulty": "black",
            "suitable_levels": ["expert"],
            "vertical_drop_m": 600,
            "length_m": 1800,
            "features": ["extreme-steep", "narrow", "experts-only"],
        },
        "avalanche-alley": {
            "difficulty": "black",
            "suitable_levels": ["expert"],
            "vertical_drop_m": 650,
            "length_m": 2000,
            "features": ["off-piste", "challenging", "backcountry-style"],
        },
    }
    
    # Map skill levels to suitable difficulties
    SKILL_TO_DIFFICULTY = {
        "beginner": ["green", "blue"],
        "intermediate": ["blue", "red"],
        "advanced": ["red", "black"],
        "expert": ["black", "red"],
    }
    
    def __init__(self):
        """Initialize the coach service."""
        self.data_generator_url = os.environ.get("services__data-generator__http__0", "http://localhost:8080")
        logger.info(f"CoachService initialized with data generator at: {self.data_generator_url}")
    
    async def _fetch_current_state(self) -> Dict[str, Any]:
        """Fetch current resort state from data generator."""
        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                response = await client.get(f"{self.data_generator_url}/api/current-state")
                response.raise_for_status()
                return response.json()
        except Exception as e:
            logger.error(f"Error fetching resort state: {e}")
            raise Exception(f"Failed to fetch resort state: {str(e)}")
    
    def _parse_preferences(self, preferences: Optional[str]) -> Dict[str, bool]:
        """Parse preferences string into a dictionary."""
        if not preferences:
            return {}
        
        prefs = {}
        for pref in preferences.lower().split(','):
            pref = pref.strip()
            if pref:
                prefs[pref] = True
        return prefs
    
    def _find_slope_lift(self, slope_id: str, lifts: List[Dict[str, Any]]) -> Optional[Dict[str, Any]]:
        """Find the lift that serves this slope."""
        for lift in lifts:
            if slope_id in lift.get("serves_slopes", []):
                return lift
        return None
    
    def _score_slope(
        self,
        slope: Dict[str, Any],
        weather: Dict[str, Any],
        lifts: List[Dict[str, Any]],
        safety: Dict[str, Any],
        preferences: Dict[str, bool],
        metadata: Dict[str, Any],
    ) -> tuple[float, List[str]]:
        """Score a slope based on current conditions and preferences."""
        score = 100.0
        reasons = []
        
        # Weather scoring
        wind_speed = weather.get("wind_speed_kmh", 0)
        visibility = weather.get("visibility_km", 10)
        
        if wind_speed > 40:
            penalty = (wind_speed - 40) * 0.5
            score -= penalty
            reasons.append(f"High wind ({wind_speed} km/h)")
        elif wind_speed < 20:
            score += 5
            reasons.append("Calm winds")
        
        if visibility > 8:
            score += 10
            reasons.append("Excellent visibility")
        elif visibility < 3:
            score -= 15
            reasons.append("Poor visibility")
        
        # Congestion scoring
        lift = self._find_slope_lift(slope["slope_id"], lifts)
        if lift:
            queue_length = lift.get("queue_length", 0)
            if preferences.get("avoid_crowds"):
                penalty = queue_length * 3  # Heavy penalty for crowds if preferred
                score -= penalty
                if queue_length > 20:
                    reasons.append(f"Long wait at lift ({queue_length} people)")
            else:
                penalty = queue_length * 0.5
                score -= penalty
                if queue_length > 30:
                    reasons.append(f"Very crowded ({queue_length} people)")
            
            if queue_length < 10:
                reasons.append("Short lift lines")
        
        # Safety scoring
        avalanche_risk = safety.get("avalanche_risk_index", 3)
        difficulty = metadata.get("difficulty", "blue")
        
        if difficulty in ["black", "red"] and avalanche_risk > 6:
            penalty = (avalanche_risk - 6) * 5
            score -= penalty
            reasons.append(f"Elevated avalanche risk (level {avalanche_risk})")
        
        # Grooming preference
        if preferences.get("groomed_only") and not slope.get("groomed", False):
            score -= 30
            reasons.append("Not groomed")
        elif slope.get("groomed", False):
            score += 5
            reasons.append("Freshly groomed")
        
        # Snow conditions bonus
        if slope.get("snow_quality") == "powder":
            score += 15
            reasons.append("Powder conditions")
        elif slope.get("snow_quality") == "packed":
            score += 5
            reasons.append("Good packed snow")
        
        # Features bonus
        features = metadata.get("features", [])
        if "scenic-views" in features or "scenic" in features:
            score += 3
        
        return score, reasons
    
    async def recommend_slope(
        self,
        skill_level: str,
        preferences: Optional[Dict[str, bool]] = None
    ) -> Dict[str, Any]:
        """
        Recommend slopes based on skill level and preferences.
        
        Args:
            skill_level: Skier skill level (beginner, intermediate, advanced, expert)
            preferences: Optional dict of preferences (avoid_crowds, groomed_only, etc.)
        
        Returns:
            Dict with top 3 slope recommendations
        """
        # Normalize skill level
        skill_level = skill_level.lower()
        if skill_level not in self.SKILL_TO_DIFFICULTY:
            raise ValueError(f"Invalid skill level: {skill_level}. Must be one of: beginner, intermediate, advanced, expert")
        
        # Fetch current state
        state = await self._fetch_current_state()
        slopes = state.get("slopes", [])
        weather = state.get("weather", {})
        lifts = state.get("lifts", [])
        safety = state.get("safety", {})
        
        # Get suitable difficulties for this skill level
        suitable_difficulties = self.SKILL_TO_DIFFICULTY[skill_level]
        
        # Filter slopes
        candidates = []
        for slope in slopes:
            slope_id = slope.get("slope_id")
            metadata = self.SLOPE_METADATA.get(slope_id, {})
            
            # Must be open
            if not slope.get("is_open", False):
                continue
            
            # Must match skill level
            difficulty = metadata.get("difficulty", "blue")
            if difficulty not in suitable_difficulties:
                continue
            
            # Apply groomed_only filter
            if preferences and preferences.get("groomed_only") and not slope.get("groomed", False):
                continue
            
            # Score the slope
            score, reasons = self._score_slope(slope, weather, lifts, safety, preferences or {}, metadata)
            
            candidates.append({
                "slope_id": slope_id,
                "slope_name": slope.get("slope_name", slope_id),
                "difficulty": difficulty,
                "score": score,
                "reasons": reasons,
                "metadata": metadata,
                "current_conditions": {
                    "is_open": slope.get("is_open"),
                    "groomed": slope.get("groomed"),
                    "snow_quality": slope.get("snow_quality"),
                }
            })
        
        # Sort by score and take top 3
        candidates.sort(key=lambda x: x["score"], reverse=True)
        top_recommendations = candidates[:3]
        
        return {
            "skill_level": skill_level,
            "preferences": preferences or {},
            "current_weather": {
                "condition": weather.get("condition"),
                "temperature_c": weather.get("temperature_c"),
                "wind_speed_kmh": weather.get("wind_speed_kmh"),
                "visibility_km": weather.get("visibility_km"),
            },
            "recommendations": top_recommendations,
        }
    
    async def build_day_plan(self, skill_level: str) -> Dict[str, Any]:
        """
        Build a full day ski plan based on skill level.
        
        Args:
            skill_level: Skier skill level (beginner, intermediate, advanced, expert)
        
        Returns:
            Dict with morning, midday, and afternoon recommendations
        """
        # Normalize skill level
        skill_level = skill_level.lower()
        if skill_level not in self.SKILL_TO_DIFFICULTY:
            raise ValueError(f"Invalid skill level: {skill_level}. Must be one of: beginner, intermediate, advanced, expert")
        
        # Fetch current state
        state = await self._fetch_current_state()
        slopes = state.get("slopes", [])
        weather = state.get("weather", {})
        lifts = state.get("lifts", [])
        safety = state.get("safety", {})
        
        # Get suitable difficulties
        suitable_difficulties = self.SKILL_TO_DIFFICULTY[skill_level]
        
        # Prepare slope data
        slope_data = []
        for slope in slopes:
            if not slope.get("is_open", False):
                continue
            
            slope_id = slope.get("slope_id")
            metadata = self.SLOPE_METADATA.get(slope_id, {})
            difficulty = metadata.get("difficulty", "blue")
            
            if difficulty not in suitable_difficulties:
                continue
            
            score, reasons = self._score_slope(slope, weather, lifts, safety, {}, metadata)
            
            slope_data.append({
                "slope_id": slope_id,
                "slope_name": slope.get("slope_name", slope_id),
                "difficulty": difficulty,
                "score": score,
                "reasons": reasons,
                "metadata": metadata,
            })
        
        slope_data.sort(key=lambda x: x["score"], reverse=True)
        
        # Build the plan
        plan = []
        
        # Morning: Warm-up on easier slopes
        morning_slopes = [s for s in slope_data if s["difficulty"] in ["green", "blue"]][:2]
        if not morning_slopes:
            morning_slopes = slope_data[:2]
        
        plan.append({
            "time_slot": "Morning (9:00 - 12:00)",
            "recommendation": "Warm-up session - Start with easier slopes to get your legs ready",
            "slopes": [
                {
                    "name": s["slope_name"],
                    "difficulty": s["difficulty"],
                    "reasons": s["reasons"][:2],  # Top 2 reasons
                }
                for s in morning_slopes
            ],
            "tips": "Take it easy and focus on technique. Check your equipment and get comfortable."
        })
        
        # Midday: Break and less crowded slopes
        midday_slopes = [s for s in slope_data if any("Short lift lines" in r or "calm" in r.lower() for r in s["reasons"])][:2]
        if not midday_slopes:
            midday_slopes = slope_data[2:4] if len(slope_data) > 2 else slope_data[:2]
        
        plan.append({
            "time_slot": "Midday (12:00 - 14:00)",
            "recommendation": "Lunch break and light skiing - Avoid peak crowds",
            "slopes": [
                {
                    "name": s["slope_name"],
                    "difficulty": s["difficulty"],
                    "reasons": s["reasons"][:2],
                }
                for s in midday_slopes
            ],
            "tips": "Stay hydrated and take a proper lunch break. Ski a few lighter runs to stay loose."
        })
        
        # Afternoon: Best conditions
        afternoon_slopes = slope_data[:3]
        
        plan.append({
            "time_slot": "Afternoon (14:00 - 16:00)",
            "recommendation": "Prime time - Best conditions and your peak performance",
            "slopes": [
                {
                    "name": s["slope_name"],
                    "difficulty": s["difficulty"],
                    "reasons": s["reasons"][:3],
                }
                for s in afternoon_slopes
            ],
            "tips": "You're warmed up and conditions are optimal. Push yourself but know your limits!"
        })
        
        return {
            "skill_level": skill_level,
            "plan": plan,
            "weather_summary": {
                "condition": weather.get("condition"),
                "temperature_c": weather.get("temperature_c"),
                "wind_speed_kmh": weather.get("wind_speed_kmh"),
            },
            "safety_notes": f"Avalanche risk: {safety.get('avalanche_risk_index', 3)}/10. Always ski within your ability and follow resort safety guidelines."
        }
