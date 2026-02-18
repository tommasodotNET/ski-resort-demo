"""
Main A2A server application for the Ski Coach Python agent.
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
from .agent_executor import SkiCoachAgentExecutor

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
    """
    Create and return the AgentCard for the ski coach agent.
    
    Args:
        host: The hostname where the agent is running
        port: The port number where the agent is running
        
    Returns:
        AgentCard: The agent card describing capabilities and skills
    """
    return AgentCard(
        name="ski-coach-agent",
        description="Personalized ski slope recommendation and day planning agent for AlpineAI ski resort",
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
                id="slope-recommendations",
                name="Slope Recommendations",
                description="Provides personalized ski slope recommendations based on skill level, preferences, weather, crowd levels, and safety conditions",
                examples=[
                    "I'm intermediate and hate crowds, where should I ski?",
                    "Build me a day plan, I'm an advanced skier",
                    "What's the best slope for a beginner right now?",
                    "I want groomed slopes only, I'm a beginner",
                    "Recommend slopes for an expert on a powder day",
                ],
                tags=["skiing", "recommendations", "slopes", "safety", "weather"]
            ),
            AgentSkill(
                id="day-planning",
                name="Ski Day Planning",
                description="Creates comprehensive ski day plans with morning warm-up, midday, and afternoon recommendations tailored to skill level",
                examples=[
                    "Plan my ski day, I'm intermediate",
                    "I'm a beginner, what should my day look like?",
                    "Create a full day plan for an expert skier",
                ],
                tags=["planning", "schedule", "coaching", "skiing"]
            )
        ]
    )


def create_app():
    """Create and configure the FastAPI A2A application."""
    _configure_from_aspire_connection_string()

    port = int(os.environ.get("PORT", 8083))
    host = os.environ.get("HOST", "0.0.0.0")

    configure_otel_providers()

    agent_card = get_agent_card(host, port)
    agent_executor = SkiCoachAgentExecutor()
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
        return {"status": "healthy", "service": "ski-coach-agent"}

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
    port = int(os.environ.get("PORT", 8083))
    host = os.environ.get("HOST", "0.0.0.0")

    logger.info(f"Ski Coach Agent starting on http://{host}:{port}")
    uvicorn.run(app, host=host, port=port, log_level="info")


if __name__ == "__main__":
    main()
