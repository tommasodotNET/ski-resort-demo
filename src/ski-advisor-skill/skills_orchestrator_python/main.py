"""
Main A2A server application for the Skills Orchestrator (Aspire resource "skiadvisorskilla2a").

Mirrors the FastAPI + A2A SDK conventions used by the existing Python
specialist agents (weather-agent-a2a, safety-agent-a2a,
ski-coach-agent-a2a), so it can be consumed the same way -- via the A2A
protocol (agent card + JSON-RPC) -- by .NET orchestrators, the frontend, or
the voice bridge, once wired into the AppHost (see README.md).

Paired with the existing .NET A2A orchestrator ("skiadvisora2a") under the
shared "ski-advisor-*" naming convention: both are alternate front doors onto
the resort's specialists (one hosts them directly as A2A agents, this one
discovers them as MCP-published Agent Skills), so voice/frontend clients can
route to either without caring which one answers.

This orchestrator discovers four operational specialists as remote MCP skill
providers and keeps the existing Foundry ski researcher as a direct agent tool.

Unlike those agents, this orchestrator's underlying `Agent` cannot be built
synchronously at import time: it must open MCP connections to the remote
skill-provider resources first. Construction is therefore deferred to the
first request (see `agent_executor.SkillsOrchestratorExecutor`), so the
server can still start and answer `/health` immediately.
"""
import logging
import os
from contextlib import asynccontextmanager

import uvicorn
from fastapi import FastAPI

# A2A SDK imports
from a2a.server.request_handlers import DefaultRequestHandler
from a2a.server.routes import create_agent_card_routes, create_jsonrpc_routes
from a2a.server.tasks import InMemoryTaskStore
from a2a.types import (
    AgentCapabilities,
    AgentCard,
    AgentSkill,
    AgentInterface,
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
from .agent_executor import SkillsOrchestratorExecutor
from .config import DEFAULT_SKILL_PROVIDERS

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)
A2A_AGENT_BASE_URL_ENV = "A2A_AGENT_BASE_URL"


def get_agent_card(agent_url: str) -> AgentCard:
    """Create and return the AgentCard for the skills orchestrator."""
    return AgentCard(
        name="skiadvisorskilla2a",
        description=(
            "Skills orchestrator that discovers AlpineAI specialist capabilities "
            "(weather, safety, ski-coach, lift-traffic) as SEP-2640 Agent Skills "
            "over MCP while retaining the Foundry ski researcher as a direct tool."
        ),
        version="1.0.0",
        default_input_modes=["text"],
        default_output_modes=["text"],
        supported_interfaces=[
            AgentInterface(
                url=agent_url,
                protocol_binding="JSONRPC",
                protocol_version="1.0",
            )
        ],
        capabilities=AgentCapabilities(
            streaming=True,
            push_notifications=False
        ),
        skills=[
            AgentSkill(
                id="skills-orchestration",
                name="Skills Orchestration",
                description=(
                    "Routes to remote weather, safety, ski-coach, and lift-traffic "
                    "skills plus the Foundry ski researcher agent tool."
                ),
                examples=[
                    "What are current weather conditions?",
                    "Is it safe to ski the upper slopes right now?",
                    "Recommend a beginner-friendly run for this afternoon.",
                ],
                tags=[
                    "skills",
                    "mcp",
                    "orchestrator",
                    "weather",
                    "safety",
                    "ski-coach",
                    "lift-traffic",
                    "researcher-tool",
                ]
            )
        ]
    )


def get_agent_url(port: int) -> str:
    base_url = os.environ.get(A2A_AGENT_BASE_URL_ENV, f"http://localhost:{port}")
    return f"{base_url.rstrip('/')}/"


def create_app():
    """Create and configure the FastAPI A2A application."""
    port = int(os.environ.get("PORT", 8084))

    configure_otel_providers(enable_sensitive_data=True)

    agent_card = get_agent_card(get_agent_url(port))
    agent_executor = SkillsOrchestratorExecutor()
    task_store = InMemoryTaskStore()

    http_handler = DefaultRequestHandler(
        agent_executor=agent_executor,
        task_store=task_store,
        agent_card=agent_card,
    )

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        try:
            yield
        finally:
            await agent_executor.aclose()

    app_instance = FastAPI(
        routes=[
            *create_agent_card_routes(agent_card),
            *create_jsonrpc_routes(http_handler, "/"),
        ],
        lifespan=lifespan,
    )

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
        return {
            "status": "healthy",
            "service": "skiadvisorskilla2a",
            "agent_ready": agent_executor.is_ready,
            "connected_skill_providers": agent_executor.connected_providers,
            "skipped_skill_providers": agent_executor.skipped_providers,
            "configured_skill_providers": [p.key for p in DEFAULT_SKILL_PROVIDERS],
            "conversation_history_backend": agent_executor.history_backend,
        }

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
    port = int(os.environ.get("PORT", 8084))
    host = os.environ.get("HOST", "0.0.0.0")

    logger.info(f"Skills Orchestrator starting on http://{host}:{port}")
    uvicorn.run(app, host=host, port=port, log_level="info")


if __name__ == "__main__":
    main()
