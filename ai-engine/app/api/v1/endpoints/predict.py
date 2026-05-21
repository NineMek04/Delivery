import logging
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel
from typing import Optional
from datetime import datetime, timedelta

router = APIRouter()
logger = logging.getLogger(__name__)

class PredictEtaRequest(BaseModel):
    pickup_lat: float
    pickup_lng: float
    dropoff_lat: float
    dropoff_lng: float
    route_distance_meters: float
    route_duration_seconds: float
    current_time: Optional[str] = None
    weather_condition: Optional[str] = "clear"
    traffic_level: Optional[str] = "normal"

class PredictEtaResponse(BaseModel):
    eta_minutes: float
    eta_datetime: str
    confidence: float
    factors: dict

@router.post("/predict-eta", response_model=PredictEtaResponse)
async def predict_eta(req: PredictEtaRequest):
    """
    Predicts the Estimated Time of Arrival (ETA) based on route details, 
    time of day, weather, and traffic conditions.
    """
    try:
        # Base ETA from routing engine (OSRM usually provides optimal driving time without delays)
        base_seconds = req.route_duration_seconds
        
        # Add typical dispatch time (finding a rider + rider traveling to pickup)
        # Average dispatch and pickup time = 10 minutes
        dispatch_pickup_seconds = 600 

        # Add typical dropoff transaction time (parking, walking to customer) = 3 minutes
        dropoff_seconds = 180

        # Feature Engineering: Time of Day
        current_dt = datetime.fromisoformat(req.current_time.replace('Z', '+00:00')) if req.current_time else datetime.utcnow()
        hour = current_dt.hour
        
        # Traffic Multiplier
        traffic_multiplier = 1.0
        if req.traffic_level == "heavy":
            traffic_multiplier = 1.5
        elif req.traffic_level == "light":
            traffic_multiplier = 0.9

        # Rush hour penalty (7-9 AM, 5-7 PM)
        if (7 <= hour <= 9) or (17 <= hour <= 19):
            traffic_multiplier = max(traffic_multiplier, 1.3)
            
        # Weather Multiplier
        weather_multiplier = 1.0
        if req.weather_condition == "rain":
            weather_multiplier = 1.4
        elif req.weather_condition == "storm":
            weather_multiplier = 1.8

        # Calculate final adjusted time
        adjusted_travel_seconds = base_seconds * traffic_multiplier * weather_multiplier
        total_seconds = adjusted_travel_seconds + dispatch_pickup_seconds + dropoff_seconds
        
        eta_minutes = total_seconds / 60.0
        eta_datetime = current_dt + timedelta(seconds=total_seconds)

        # Confidence drops if weather/traffic is bad
        confidence = 0.95
        if weather_multiplier > 1.2:
            confidence -= 0.15
        if traffic_multiplier > 1.2:
            confidence -= 0.1

        return PredictEtaResponse(
            eta_minutes=round(eta_minutes, 1),
            eta_datetime=eta_datetime.isoformat() + "Z",
            confidence=round(confidence, 2),
            factors={
                "base_travel_mins": round(base_seconds / 60.0, 1),
                "adjusted_travel_mins": round(adjusted_travel_seconds / 60.0, 1),
                "dispatch_pickup_mins": round(dispatch_pickup_seconds / 60.0, 1),
                "traffic_multiplier": traffic_multiplier,
                "weather_multiplier": weather_multiplier
            }
        )
    except Exception as e:
        logger.error(f"Error predicting ETA: {str(e)}")
        raise HTTPException(status_code=500, detail="Internal server error during ETA prediction")
