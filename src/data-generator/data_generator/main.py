"""
FastAPI application for ski resort data generation service.
Continuously generates and serves synthetic ski resort telemetry data.
"""
import asyncio
import logging
import os
import random
from contextlib import asynccontextmanager

import uvicorn
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

# OpenTelemetry imports
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor

from .generator import DataGenerator
from .models import ResortState, WeatherData, LiftData, SafetyData, SlopeData
from typing import List

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Global generator instance
generator: DataGenerator = None
update_task: asyncio.Task = None


async def data_update_loop():
    """Background task that continuously updates telemetry data."""
    global generator
    
    logger.info("Starting data generation loop")
    
    while True:
        try:
            # Update data
            generator.update()
            
            # Random interval between 1-3 seconds
            interval = random.uniform(1.0, 3.0)
            await asyncio.sleep(interval)
            
        except Exception as e:
            logger.error(f"Error in data update loop: {e}", exc_info=True)
            await asyncio.sleep(2)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Manage application lifespan - startup and shutdown."""
    global generator, update_task
    
    # Startup
    logger.info("Initializing data generator service")
    generator = DataGenerator()
    
    # Start background update task
    update_task = asyncio.create_task(data_update_loop())
    
    yield
    
    # Shutdown
    logger.info("Shutting down data generator service")
    if update_task:
        update_task.cancel()
        try:
            await update_task
        except asyncio.CancelledError:
            pass


def create_app() -> FastAPI:
    """Create and configure the FastAPI application."""
    
    app = FastAPI(
        title="AlpineAI Data Generator",
        description="Real-time synthetic telemetry data for ski resort operations",
        version="0.1.0",
        lifespan=lifespan,
    )
    
    # Add CORS middleware
    app.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )
    
    # Setup OpenTelemetry
    otel_endpoint = os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT")
    if otel_endpoint:
        trace.set_tracer_provider(TracerProvider())
        otlp_exporter = OTLPSpanExporter(endpoint=otel_endpoint)
        processor = BatchSpanProcessor(otlp_exporter)
        trace.get_tracer_provider().add_span_processor(processor)
        FastAPIInstrumentor().instrument_app(app)
        logger.info(f"OpenTelemetry configured with endpoint: {otel_endpoint}")
    else:
        logger.warning("OTEL_EXPORTER_OTLP_ENDPOINT not set, telemetry disabled")
    
    return app


app = create_app()


@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return {"status": "healthy", "service": "data-generator"}


@app.get("/api/current-state", response_model=ResortState)
async def get_current_state():
    """Get the complete current state of the resort."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    
    return generator.get_state()


@app.get("/api/current-state/weather", response_model=WeatherData)
async def get_current_state_weather():
    """Get current weather conditions."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    return generator.weather


@app.get("/api/current-state/lifts", response_model=List[LiftData])
async def get_current_state_lifts():
    """Get data for all lifts."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    return generator.lifts


@app.get("/api/current-state/safety", response_model=SafetyData)
async def get_current_state_safety():
    """Get current safety data."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    return generator.safety


@app.get("/api/current-state/slopes", response_model=List[SlopeData])
async def get_current_state_slopes():
    """Get data for all slopes."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    return generator.slopes


@app.get("/api/weather", response_model=WeatherData)
async def get_weather():
    """Get current weather conditions."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    
    return generator.weather


@app.get("/api/lifts", response_model=List[LiftData])
async def get_lifts():
    """Get data for all lifts."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    
    return generator.lifts


@app.get("/api/lifts/{lift_id}", response_model=LiftData)
async def get_lift(lift_id: str):
    """Get data for a specific lift."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    
    lift = next((l for l in generator.lifts if l.lift_id == lift_id), None)
    if lift is None:
        raise HTTPException(status_code=404, detail=f"Lift '{lift_id}' not found")
    
    return lift


@app.get("/api/safety", response_model=SafetyData)
async def get_safety():
    """Get current safety and risk assessment data."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    
    return generator.safety


@app.get("/api/slopes", response_model=List[SlopeData])
async def get_slopes():
    """Get data for all slopes."""
    if generator is None:
        raise HTTPException(status_code=503, detail="Generator not initialized")
    
    return generator.slopes


def main():
    """Main entry point for the application."""
    port = int(os.environ.get("PORT", 8080))
    host = os.environ.get("HOST", "0.0.0.0")
    
    logger.info(f"Starting AlpineAI Data Generator on http://{host}:{port}")
    logger.info(f"API endpoints available at http://{host}:{port}/api/")
    logger.info(f"Health check: http://{host}:{port}/health")
    logger.info(f"API docs: http://{host}:{port}/docs")
    
    uvicorn.run(
        app,
        host=host,
        port=port,
        log_level="info",
    )


if __name__ == "__main__":
    main()
