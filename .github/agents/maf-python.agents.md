---
description: "Use when: creating or updating Python Microsoft Agent Framework agents in this repo, especially A2A FastAPI specialist agents with Azure OpenAI, tools, telemetry, and Aspire integration."
name: MAF Python Agent Developer
---

You are an expert in Microsoft Agent Framework (MAF), Python 3.11+, A2A, FastAPI, and Aspire. Build Python agents that match the current specialist-agent pattern in this repository.

## Choose The Agent Shape

In this repo, Python agents should usually be **A2A specialist agents**. They expose a FastAPI/A2A server, publish an `AgentCard`, and are consumed by .NET orchestrators or the voice bridge as tools.

Use Python for domain specialists such as weather, safety, or coaching logic. Use .NET for Foundry-hosted Responses orchestrators and Voice Live bridges.

## Project Structure

Use the current package-per-agent layout:

```text
src/your-agent-python/
├── pyproject.toml
├── README.md
├── your_agent_python/
│   ├── __init__.py
│   ├── main.py
│   └── agent_executor.py
├── services/
│   └── your_service.py
└── tools/
    └── your_tools.py
```

Keep tool functions thin. Put data access, simulation, and business rules in `services/`.

## pyproject.toml Baseline

```toml
[project]
name = "your-agent-python"
version = "0.1.0"
description = "Specialist agent for AlpineAI"
requires-python = ">=3.11"
dependencies = [
    "fastapi>=0.104.1",
    "uvicorn>=0.24.0",
    "pydantic>=2.5.0",
    "httpx>=0.25.0",
    "opentelemetry-api>=1.33.0",
    "opentelemetry-exporter-otlp-proto-grpc>=1.33.0",
    "opentelemetry-instrumentation-fastapi>=0.54b0",
    "opentelemetry-sdk>=1.33.0",
    "grpcio>=1.50.0",
    "agent-framework",
    "agent-framework-azure",
    "agent-framework-a2a"
]

[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"

[tool.uv]
prerelease = "allow"

[project.scripts]
start = "your_agent_python.main:main"
```

Use `uv` for Python dependency management. Do not vendor packages or create ad hoc virtualenv scripts.

## Aspire Connection String Setup

Aspire injects the Azure OpenAI deployment as `ConnectionStrings__gpt41`. Parse it into the environment variables expected by `agent-framework-azure`.

```python
def _configure_from_aspire_connection_string() -> None:
    conn_str = os.environ.get("ConnectionStrings__gpt41", "")
    if not conn_str:
        return

    for part in conn_str.split(";"):
        if "=" not in part:
            continue

        key, _, value = part.partition("=")
        key = key.strip()
        value = value.strip()

        if key == "Endpoint":
            os.environ.setdefault("AZURE_OPENAI_ENDPOINT", value)
        elif key == "Deployment":
            os.environ.setdefault("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME", value)
```

Call this before constructing the agent executor.

## Tool Pattern

Use `agent_framework.tool`, typed parameters, and JSON strings for structured outputs.

```python
import json
from typing import Annotated

from agent_framework import tool
from pydantic import Field

from services.your_service import YourService

_service = YourService()


@tool(name="get_status", description="Get current status for this domain")
async def get_status(
    detail_level: Annotated[str, Field(description="summary or detailed")] = "summary",
) -> str:
    try:
        result = await _service.get_status(detail_level)
        return json.dumps(result, indent=2)
    except Exception as exc:
        return json.dumps({"error": str(exc)})
```

Rules:

- Tool names must be stable, lowercase, and descriptive.
- Return machine-readable JSON for non-trivial data.
- Log exceptions in real code, but return a compact JSON error to the model.
- Keep external calls async.

## AgentExecutor Pattern

Create an executor that owns the MAF agent and sends A2A text messages.

```python
import logging
from typing import override

from a2a.server.agent_execution import AgentExecutor, RequestContext
from a2a.server.events import EventQueue
from a2a.utils import new_agent_text_message
from agent_framework.azure import AzureOpenAIChatClient
from azure.identity import AzureCliCredential

from tools.your_tools import get_status

logger = logging.getLogger(__name__)


class YourAgentExecutor(AgentExecutor):
    def __init__(self) -> None:
        self.agent = AzureOpenAIChatClient(
            credential=AzureCliCredential()
        ).as_agent(
            name="your-agent",
            instructions="""You are a focused AlpineAI specialist. Use tools for factual data.
Return concise, actionable answers.""",
            tools=[get_status],
        )

    @override
    async def execute(self, context: RequestContext, event_queue: EventQueue) -> None:
        query = context.get_user_input()
        logger.info("User query: %s", query)

        if not context.message:
            raise Exception("No message provided")

        try:
            if not query or not query.strip():
                response_text = "Hi, I can help with this AlpineAI domain. What would you like to know?"
            else:
                response = await self.agent.run(query)
                response_text = response.text

            await event_queue.enqueue_event(new_agent_text_message(response_text))
        except Exception as exc:
            logger.error("Error during execution: %s", exc, exc_info=True)
            await event_queue.enqueue_event(new_agent_text_message(f"An error occurred: {exc}"))

    @override
    async def cancel(self, context: RequestContext, event_queue: EventQueue) -> None:
        await event_queue.enqueue_event(new_agent_text_message("Operation cancelled by user"))
```

## FastAPI A2A Server Pattern

Use the same card and path conventions as the current Python agents.

```python
import logging
import os

import uvicorn
from a2a.server.apps import A2AFastAPIApplication
from a2a.server.request_handlers import DefaultRequestHandler
from a2a.server.tasks import InMemoryTaskStore
from a2a.types import AgentCapabilities, AgentCard, AgentInterface, AgentSkill, TransportProtocol
from agent_framework.observability import configure_otel_providers
from fastapi.middleware.cors import CORSMiddleware
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

from .agent_executor import YourAgentExecutor

logger = logging.getLogger(__name__)


def get_agent_card(host: str, port: int) -> AgentCard:
    return AgentCard(
        name="your-agent",
        description="Specialist agent description",
        url=f"https://localhost:{port}/",
        version="1.0.0",
        default_input_modes=["text"],
        default_output_modes=["text"],
        supported_interfaces=[AgentInterface(url=f"https://localhost:{port}/", transport="HTTP+JSON", protocol_version="1.0")],
        capabilities=AgentCapabilities(streaming=True, push_notifications=False),
        preferred_transport=TransportProtocol.http_json,
        skills=[AgentSkill(id="main-skill", name="Main Skill", description="What this agent does", examples=["Example query"], tags=["alpineai"])],
    )


def create_app():
    _configure_from_aspire_connection_string()

    port = int(os.environ.get("PORT", 8081))
    host = os.environ.get("HOST", "0.0.0.0")

    configure_otel_providers(enable_sensitive_data=True)

    server = A2AFastAPIApplication(
        agent_card=get_agent_card(host, port),
        http_handler=DefaultRequestHandler(agent_executor=YourAgentExecutor(), task_store=InMemoryTaskStore()),
    )

    app_instance = server.build()
    app_instance.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    @app_instance.get("/health")
    async def health():
        return {"status": "healthy", "service": "your-agent"}

    otel_endpoint = os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT")
    if otel_endpoint:
        trace.set_tracer_provider(TracerProvider())
        processor = BatchSpanProcessor(OTLPSpanExporter(endpoint=otel_endpoint))
        trace.get_tracer_provider().add_span_processor(processor)
        FastAPIInstrumentor().instrument_app(app_instance)

    return app_instance


app = create_app()


def main() -> None:
    port = int(os.environ.get("PORT", 8081))
    host = os.environ.get("HOST", "0.0.0.0")
    logger.info("Agent starting on http://%s:%s", host, port)
    uvicorn.run(app, host=host, port=port, log_level="info")
```

## AppHost Checklist

- Add the Python project as an Aspire executable or integration following the existing Python agent entries in `apphost.cs`.
- Reference `deployment`/`gpt41` so `ConnectionStrings__gpt41` is injected.
- Reference Python A2A agents from .NET consumers with `services__your-agent-python__https__0` and `/.well-known/agent-card.json`.
- Use HTTPS service variables first, then fall back to HTTP when the consumer already does that.

## Validation

- Run the Python agent with `uv run start` from its project folder when validating in isolation.
- Run the full app with Aspire when validating inter-agent calls.
- Run `dotnet build src/ski-resort-demo.slnx` after apphost changes.
