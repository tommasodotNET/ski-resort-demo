"""
Main A2A server application for the weather agent.
Uses the official A2A Python SDK with FastAPI and JSON-RPC support.
"""
import os
import logging

import uvicorn

# A2A SDK imports
from a2a.server.apps import A2AFastAPIApplication
from a2a.server.request_handlers import DefaultRequestHandler
from a2a.server.tasks import InMemoryTaskStore
from a2a.types import (
    AgentCapabilities,
    AgentCard,
    AgentSkill,
    TransportProtocol,
)

# OpenTelemetry imports
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor

# Microsoft Agent Framework
from agent_framework.observability import configure_otel_providers

# Local imports
from .agent_executor import WeatherAgentExecutor

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


def _configure_from_aspire_connection_string():
    """Parse Aspire-injected connection string and set Azure OpenAI env vars."""
    conn_str = os.environ.get("ConnectionStrings__gpt41", "")
    if not conn_str:
        return
    for part in conn_str.split(";"):
        if "=" in part:
            key, _, value = part.partition("=")
            key = key.strip()
            value = value.strip()
            if key == "Endpoint":
                os.environ.setdefault("AZURE_OPENAI_ENDPOINT", value)
            elif key == "Deployment":
                os.environ.setdefault("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME", value)


def get_agent_card(host: str, port: int) -> AgentCard:
    """Create and return the AgentCard for the weather agent."""
    return AgentCard(
        name="weather-agent",
        description="Weather intelligence agent providing real-time conditions, forecasts, and storm alerts for the ski resort",
        url=f"https://localhost:{port}/",
        version="1.0.0",
        default_input_modes=["text"],
        default_output_modes=["text"],
        capabilities=AgentCapabilities(
            streaming=True,
            push_notifications=False
        ),
        preferred_transport=TransportProtocol.http_json,
        skills=[
            AgentSkill(
                id="weather-intelligence",
                name="Weather Intelligence",
                description="Provides real-time weather conditions, forecasts, and storm alerts for the ski resort",
                examples=[
                    "What are current weather conditions?",
                    "Give me a 6 hour forecast",
                    "Is there a storm coming?",
                    "What's the temperature and wind speed?",
                    "Should we expect snow in the next 12 hours?",
                    "Is it safe to keep the upper lifts open?"
                ],
                tags=["weather", "forecast", "storm", "conditions", "safety"]
            )
        ]
    )


def create_app():
    """Create and configure the FastAPI A2A application."""
    _configure_from_aspire_connection_string()

    port = int(os.environ.get("PORT", 8081))
    host = os.environ.get("HOST", "0.0.0.0")

    configure_otel_providers()

    agent_card = get_agent_card(host, port)
    agent_executor = WeatherAgentExecutor()
    task_store = InMemoryTaskStore()

    http_handler = DefaultRequestHandler(
        agent_executor=agent_executor,
        task_store=task_store,
    )

    server = A2AFastAPIApplication(
        agent_card=agent_card,
        http_handler=http_handler
    )

    app_instance = server.build()

    from fastapi.middleware.cors import CORSMiddleware
    app_instance.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    @app_instance.get("/health")
    async def health():
        return {"status": "healthy", "service": "weather-agent"}

    otel_endpoint = os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT")
    if otel_endpoint:
        trace.set_tracer_provider(TracerProvider())
        otlp_exporter = OTLPSpanExporter(endpoint=otel_endpoint)
        processor = BatchSpanProcessor(otlp_exporter)
        trace.get_tracer_provider().add_span_processor(processor)
        FastAPIInstrumentor().instrument_app(app_instance)

    return app_instance


app = create_app()


def main():
    """Main entry point for the application."""
    port = int(os.environ.get("PORT", 8081))
    host = os.environ.get("HOST", "0.0.0.0")

    logger.info(f"Weather Agent starting on http://{host}:{port}")
    uvicorn.run(app, host=host, port=port, log_level="info")


if __name__ == "__main__":
    main()
