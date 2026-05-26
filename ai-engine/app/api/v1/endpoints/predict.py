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
    rider_speed_kmh: Optional[float] = None
    osrm_pickup_duration_seconds: Optional[float] = None

class PredictEtaResponse(BaseModel):
    eta_minutes: float
    eta_datetime: str
    confidence: float
    factors: dict

@router.post("/predict-eta", response_model=PredictEtaResponse)
async def predict_eta(req: PredictEtaRequest):
    """
    Predicts the Estimated Time of Arrival (ETA) based on route details, 
    time of day, weather, traffic conditions, rider velocity, and OSRM pickup duration.
    """
    try:
        # Base ETA from routing engine (OSRM provides optimal driving time without delays)
        base_seconds = req.route_duration_seconds
        
        # Dispatch + Pickup time: ใช้ OSRM pickup duration ถ้ามี หรือ fallback 10 นาที
        if req.osrm_pickup_duration_seconds is not None and req.osrm_pickup_duration_seconds > 0:
            dispatch_pickup_seconds = req.osrm_pickup_duration_seconds + 120  # +2 นาที (dispatch processing + rider acceptance)
        else:
            dispatch_pickup_seconds = 600  # fallback 10 นาที

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

        # Rider Velocity Adjustment Factor
        # OSRM สมมติ speed limit (~40-60 km/h ในเมือง)
        # ถ้า rider ขับจริงช้ากว่า → velocity_factor > 1.0 → เพิ่มเวลา
        # ถ้า rider ขับจริงเร็วกว่า → velocity_factor < 1.0 → ลดเวลา
        velocity_factor = 1.0
        if req.rider_speed_kmh is not None and req.rider_speed_kmh > 0:
            # คำนวณ OSRM assumed speed จาก distance/duration
            if req.route_duration_seconds > 0:
                osrm_assumed_speed = (req.route_distance_meters / 1000.0) / (req.route_duration_seconds / 3600.0)
            else:
                osrm_assumed_speed = 40.0  # default เมือง

            # Clamp velocity_factor ระหว่าง 0.5x - 3.0x เพื่อป้องกัน outlier
            velocity_factor = max(0.5, min(osrm_assumed_speed / req.rider_speed_kmh, 3.0))

        # Calculate final adjusted time
        adjusted_travel_seconds = base_seconds * traffic_multiplier * weather_multiplier * velocity_factor
        total_seconds = adjusted_travel_seconds + dispatch_pickup_seconds + dropoff_seconds
        
        eta_minutes = total_seconds / 60.0
        eta_datetime = current_dt + timedelta(seconds=total_seconds)

        # Confidence drops if weather/traffic is bad or velocity data is uncertain
        confidence = 0.95
        if weather_multiplier > 1.2:
            confidence -= 0.15
        if traffic_multiplier > 1.2:
            confidence -= 0.1
        if velocity_factor > 1.5 or velocity_factor < 0.7:
            confidence -= 0.05  # ความเร็วจริงห่างจาก OSRM มาก → ความมั่นใจลดลง

        return PredictEtaResponse(
            eta_minutes=round(eta_minutes, 1),
            eta_datetime=eta_datetime.isoformat() + "Z",
            confidence=round(max(confidence, 0.3), 2),  # Minimum confidence 30%
            factors={
                "base_travel_mins": round(base_seconds / 60.0, 1),
                "adjusted_travel_mins": round(adjusted_travel_seconds / 60.0, 1),
                "dispatch_pickup_mins": round(dispatch_pickup_seconds / 60.0, 1),
                "traffic_multiplier": traffic_multiplier,
                "weather_multiplier": weather_multiplier,
                "velocity_factor": round(velocity_factor, 2),
                "rider_speed_kmh": req.rider_speed_kmh,
                "osrm_pickup_duration_seconds": req.osrm_pickup_duration_seconds
            }
        )
    except Exception as e:
        logger.error(f"Error predicting ETA: {str(e)}")
        raise HTTPException(status_code=500, detail="Internal server error during ETA prediction")
