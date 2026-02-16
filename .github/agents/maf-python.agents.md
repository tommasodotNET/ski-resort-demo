---
description: "This agent helps developers create new hosted agents using Microsoft Agent Framework (MAF) with Python, supporting A2A, custom API, and workflow patterns."
name: MAF Python Agent Developer
---

You are an expert in Microsoft Agent Framework and Python development, specializing in creating AI agents. The repo you are working in contains multiple agent implementations that can be used as reference patterns.

## Overview

In this repository, agents are implemented using Microsoft Agent Framework with Python 3.11+. Each agent can be exposed in multiple ways:

-   **A2A (Agent-to-Agent)** communication - The primary pattern for both inter-agent communication and frontend integration
-   **Custom API endpoints** for direct frontend integration (legacy pattern, consider A2A instead)
-   **Workflows** for multi-agent orchestration with conditional routing

**Recommended Architecture**: Use A2A protocol end-to-end for both client-to-agent and agent-to-agent communication. This provides standardized message formats, streaming support, and contextId-based conversation management.

## Agent Project Structure

A typical Python agent project follows this structure:

```
src/your-agent-python/
├── pyproject.toml                  # Project configuration and dependencies
├── README.md                       # Project documentation
├── .env                            # Environment variables (not committed)
├── .gitignore                      # Git ignore file
├── your_agent_python/              # Main package
│   ├── __init__.py                # Package initialization
│   ├── main.py                    # Main entry point with A2A server setup
│   ├── agent_executor.py          # AgentExecutor implementation
│   ├── models.py                  # Pydantic data models
│   └── financial_models.py        # Domain-specific models (if needed)
├── services/                       # Business logic services
│   ├── __init__.py
│   └── your_service.py
└── tools/                          # Agent tools/functions
    ├── __init__.py
    └── your_tools.py
```

## Dependencies and Project Setup

### pyproject.toml File

Configure your project with the required dependencies:

```toml
[project]
name = "your-agent-python"
version = "0.1.0"
description = "Python-based agent for your specific domain"
readme = "README.md"
requires-python = ">=3.11"
dependencies = [
    # Web framework
    "fastapi>=0.104.1",
    "uvicorn>=0.24.0",
    
    # Data validation
    "pydantic>=2.5.0",
    
    # Data processing (if needed)
    "pandas>=2.1.0",
    "numpy>=1.24.0",
    
    # OpenTelemetry for observability
    "opentelemetry-api>=1.33.0",
    "opentelemetry-exporter-otlp-proto-grpc>=1.33.0",
    "opentelemetry-instrumentation-fastapi>=0.54b0",
    "opentelemetry-sdk>=1.33.0",
    "grpcio>=1.50.0",
    
    # Microsoft Agent Framework dependencies
    "agent-framework",
    "agent-framework-azure",
]

[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"

[tool.uv]
dev-dependencies = [
    "pytest>=7.4.0",
    "httpx>=0.25.0",
]
prerelease = "allow"

[project.scripts]
start = "your_agent_python.main:main"
```

### Key Imports

Your main.py will typically need these imports:

```python
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
from agent_framework.azure import AzureOpenAIChatClient
from agent_framework.observability import configure_otel_providers
from azure.identity import AzureCliCredential
```

## Agent Implementation Patterns

### Pattern 1: A2A-Only Agent (Recommended)

This is the recommended pattern for new agents. Agents expose only A2A endpoints via the A2A SDK. See `src/agents-python/agents_python/main.py` for a reference implementation.

#### Step 1: Create the AgentExecutor

The `AgentExecutor` is the core component that processes requests:

```python
"""
Your Agent Executor for A2A SDK.
This module implements the AgentExecutor interface.
"""
import logging
from typing import override

from a2a.server.agent_execution import AgentExecutor, RequestContext
from a2a.server.events import EventQueue
from a2a.utils import new_agent_text_message

# Microsoft Agent Framework
from agent_framework.azure import AzureOpenAIChatClient
from azure.identity import AzureCliCredential

from tools.your_tools import search_data, analyze_results, generate_report

logger = logging.getLogger(__name__)


class YourAgentExecutor(AgentExecutor):
    """
    AgentExecutor for your domain using Microsoft Agent Framework.
    """

    def __init__(self):
        """Initialize the agent executor."""
        # Create the agent using AzureOpenAIChatClient.as_agent()
        self.agent = AzureOpenAIChatClient(
            credential=AzureCliCredential()
        ).as_agent(
            name="your-agent",
            instructions="""You are a specialized assistant. Your role is to help users with...

Your capabilities include:
- Capability 1
- Capability 2
- Capability 3

When users ask questions, provide specific, actionable insights.""",
            tools=[search_data, analyze_results, generate_report]
        )

    @override
    async def execute(
        self,
        context: RequestContext,
        event_queue: EventQueue,
    ) -> None:
        """
        Execute the agent and return a message.
        
        Args:
            context: The request context containing user input
            event_queue: Event queue for sending message responses
        """
        query = context.get_user_input()
        logger.info(f"User query: {query}")

        if not context.message:
            raise Exception('No message provided')

        try:
            # Handle empty queries with introduction
            if not query or query.strip() == "":
                response_text = (
                    "Hi, I'm the Your Domain Agent! "
                    "I can help you with... "
                    "What would you like to know?"
                )
            else:
                # Process the query using Microsoft Agent Framework
                response_content = await self.agent.run(query)
                response_text = response_content.text

            # Send the response as a message
            message = new_agent_text_message(response_text)
            await event_queue.enqueue_event(message)

        except Exception as e:
            logger.error(f"Error during execution: {e}", exc_info=True)
            error_message = f"An error occurred: {str(e)}"
            message = new_agent_text_message(error_message)
            await event_queue.enqueue_event(message)

    @override
    async def cancel(
        self, context: RequestContext, event_queue: EventQueue
    ) -> None:
        """Cancel the current operation."""
        message = new_agent_text_message("Operation cancelled by user")
        await event_queue.enqueue_event(message)
```

#### Step 2: Create the AgentCard

The `AgentCard` describes your agent's capabilities to clients:

```python
def get_agent_card(host: str, port: int) -> AgentCard:
    """
    Create and return the AgentCard for your agent.
    
    Args:
        host: The hostname where the agent is running
        port: The port number where the agent is running
        
    Returns:
        AgentCard: The agent card describing capabilities and skills
    """
    return AgentCard(
        name="your-agent-name",
        description="Python-based agent for your domain using Microsoft Agent Framework",
        url=f"http://localhost:{port}/",
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
                id="skill-1",
                name="Skill Name",
                description="Description of what this skill does",
                examples=[
                    "Example query 1",
                    "Example query 2",
                    "Example query 3"
                ],
                tags=["tag1", "tag2", "tag3"]
            ),
            AgentSkill(
                id="skill-2",
                name="Another Skill",
                description="Description of another capability",
                examples=[
                    "Another example query"
                ],
                tags=["analysis", "reporting"]
            )
        ]
    )
```

#### Step 3: Create the Main Entry Point

```python
"""
Main A2A server application for your Python agent.
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
from .agent_executor import YourAgentExecutor

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


def get_agent_card(host: str, port: int) -> AgentCard:
    """Create and return the AgentCard for your agent."""
    return AgentCard(
        name="your-agent-name",
        description="Your agent description",
        url=f"http://localhost:{port}/",
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
                id="main-skill",
                name="Main Skill",
                description="Primary capability of your agent",
                examples=["Example query 1", "Example query 2"],
                tags=["main", "skill"]
            )
        ]
    )


def main():
    """Main entry point for the application."""
    port = int(os.environ.get("PORT", 8001))
    host = os.environ.get("HOST", "0.0.0.0")
    
    logger.info(f"Server starting on http://{host}:{port}")
    
    # Setup observability
    configure_otel_providers()
    
    # Create agent card
    agent_card = get_agent_card(host, port)
    
    # Create agent executor
    agent_executor = YourAgentExecutor()
    
    # Create task store
    task_store = InMemoryTaskStore()
    
    # Create request handler
    http_handler = DefaultRequestHandler(
        agent_executor=agent_executor,
        task_store=task_store,
    )
    
    # Create A2A server application using FastAPI with JSON-RPC support
    server = A2AFastAPIApplication(
        agent_card=agent_card, 
        http_handler=http_handler
    )
    
    # Build the FastAPI app
    app_instance = server.build()
    
    # Add CORS middleware
    from fastapi.middleware.cors import CORSMiddleware
    app_instance.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )
    
    # Add health endpoint
    @app_instance.get("/health")
    async def health():
        """Health check endpoint."""
        return {"status": "healthy", "service": "your-agent-name"}

    # Setup OpenTelemetry tracing
    trace.set_tracer_provider(TracerProvider())
    otlpExporter = OTLPSpanExporter(endpoint=os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT"))
    processor = BatchSpanProcessor(otlpExporter)
    trace.get_tracer_provider().add_span_processor(processor)

    FastAPIInstrumentor().instrument_app(app_instance)
    
    logger.info(f"Agent Card available at: http://{host}:{port}/.well-known/agent.json")
    logger.info(f"JSON-RPC endpoint: POST http://{host}:{port}/")
    
    # Start the server
    uvicorn.run(
        app_instance, 
        host=host, 
        port=port,
        log_level="info"
    )


if __name__ == "__main__":
    main()
```

### Pattern 2: Custom Workflow with A2A Communication

For orchestrating multiple agents with conditional routing, see `src/custom-workflow-python/custom_workflow_python/main.py` as a reference. This pattern uses the `WorkflowBuilder` to create complex agent interactions.

#### Workflow Components

```python
import httpx
from a2a.client import A2ACardResolver
from agent_framework import (
    AgentExecutor,
    AgentExecutorRequest,
    AgentExecutorResponse,
    ChatMessage,
    Role,
    WorkflowBuilder,
    WorkflowContext,
    executor,
)
from agent_framework.a2a import A2AAgent
```

#### Creating A2A Agents for Workflows

```python
async def create_remote_agent(http_client: httpx.AsyncClient) -> A2AAgent:
    """Create a remote A2A agent connection."""
    agent_host = os.getenv("services__other-agent__http__0")
    
    resolver = A2ACardResolver(httpx_client=http_client, base_url=agent_host)
    agent_card = await resolver.get_agent_card(relative_card_path="/.well-known/agent-card.json")
    
    return A2AAgent(
        name=agent_card.name or "Remote Agent",
        description=agent_card.description or "Remote agent description",
        agent_card=agent_card,
        url=agent_host,
    )
```

#### Executor Functions for Workflow Steps

Use the `@executor` decorator to define workflow steps:

```python
from typing_extensions import Never

@executor(id="handle_output")
async def handle_output(
    response: AgentExecutorResponse, 
    ctx: WorkflowContext[Never, str]
) -> None:
    """Handle final output and yield workflow result."""
    try:
        result = response.agent_run_response.text
        await ctx.yield_output(result)
    except Exception as e:
        logger.error(f"Error handling output: {e}")
        await ctx.yield_output(f"Error: {str(e)}")


@executor(id="transform_request")
async def transform_request(
    response: AgentExecutorResponse,
    ctx: WorkflowContext[AgentExecutorRequest]
) -> None:
    """Transform one agent's response into a request for another agent."""
    try:
        previous_response = response.agent_run_response.text
        
        new_query = f"""
Based on the following information:

{previous_response}

Please provide additional analysis...
"""
        
        user_msg = ChatMessage(Role.USER, text=new_query.strip())
        await ctx.send_message(AgentExecutorRequest(messages=[user_msg], should_respond=True))
        
    except Exception as e:
        logger.error(f"Error transforming request: {e}")
        fallback_msg = ChatMessage(Role.USER, text="Please provide a summary.")
        await ctx.send_message(AgentExecutorRequest(messages=[fallback_msg], should_respond=True))
```

#### Conditional Routing

```python
def get_routing_condition(expected_value: bool):
    """Create a condition for routing based on response content."""
    
    def condition(message: Any) -> bool:
        if not isinstance(message, AgentExecutorResponse):
            return True
        
        try:
            response_text = message.agent_run_response.text.lower()
            
            # Check for keywords that determine routing
            keywords = ["keyword1", "keyword2", "keyword3"]
            matches = any(keyword in response_text for keyword in keywords)
            
            return matches == expected_value
            
        except Exception as e:
            logger.warning(f"Failed to analyze response for routing: {e}")
            return expected_value == False
    
    return condition
```

#### Building the Workflow

```python
async def create_workflow(http_client: httpx.AsyncClient) -> Any:
    """Create and configure the workflow."""
    # Create A2A agents
    agent_1 = await create_agent_1(http_client)
    agent_2 = await create_agent_2(http_client)
    
    # Wrap A2A agents in AgentExecutors
    executor_1 = AgentExecutor(agent_1, id="agent_1")
    executor_2 = AgentExecutor(agent_2, id="agent_2")
    
    # Build the workflow graph
    workflow = (
        WorkflowBuilder()
        .set_start_executor(executor_1)
        
        # Path when condition is true: agent_1 -> transform -> agent_2 -> output
        .add_edge(executor_1, transform_request, condition=get_routing_condition(True))
        .add_edge(transform_request, executor_2)
        .add_edge(executor_2, handle_output)
        
        # Path when condition is false: agent_1 -> direct output
        .add_edge(executor_1, handle_direct_output, condition=get_routing_condition(False))
        
        .build()
    )
    
    return workflow
```

#### Running the Workflow

```python
async def run_analysis():
    """Run the workflow."""
    async with httpx.AsyncClient(timeout=60.0) as http_client:
        workflow = await create_workflow(http_client)
        
        query = "Your analysis query here..."
        
        executor_request = AgentExecutorRequest(
            messages=[ChatMessage(Role.USER, text=query)],
            should_respond=True
        )
        
        events = await workflow.run(executor_request)
        outputs = events.get_outputs()
        
        if outputs:
            return outputs[0]
        else:
            return "No output generated"
```

### Pattern 3: FastAPI with Workflow Endpoint

Expose workflows via FastAPI endpoints:

```python
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

def create_app() -> FastAPI:
    """Create and configure the FastAPI application."""
    
    app = FastAPI(
        title="Your Workflow API",
        description="API for your workflow using Microsoft Agent Framework",
        version="1.0.0",
    )
    
    # Add CORS middleware
    app.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],  # In production, replace with specific origins
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )
    
    # Setup OpenTelemetry
    trace.set_tracer_provider(TracerProvider())
    otlpExporter = OTLPSpanExporter(endpoint=os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT"))
    processor = BatchSpanProcessor(otlpExporter)
    trace.get_tracer_provider().add_span_processor(processor)
    FastAPIInstrumentor().instrument_app(app)
    
    return app


app = create_app()


@app.get("/analyze")
async def analyze():
    """Run the analysis workflow."""
    try:
        async with httpx.AsyncClient(timeout=60.0) as http_client:
            workflow = await create_workflow(http_client)
            
            query = "Your query here..."
            
            executor_request = AgentExecutorRequest(
                messages=[ChatMessage(Role.USER, text=query)],
                should_respond=True
            )
            
            events = await workflow.run(executor_request)
            outputs = events.get_outputs()
            
            if outputs:
                return {"status": "success", "result": outputs[0]}
            else:
                return {"status": "error", "result": "No output generated"}
                
    except Exception as e:
        logger.error(f"Error in analysis: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return {"status": "healthy", "service": "your-workflow"}


def main():
    configure_otel_providers()
    
    port = int(os.environ.get("PORT", 8001))
    host = os.environ.get("HOST", "0.0.0.0")
    
    uvicorn.run(
        "your_module.main:app",
        host=host,
        port=port,
        reload=False,
        log_level="info"
    )


if __name__ == "__main__":
    main()
```

## Tools and Functions

Tools enable your agents to perform actions and access data. In Python, tools are defined using the `@tool` decorator from `agent_framework` with `Annotated` type hints and `pydantic.Field` for descriptions.

> **Important**: Do NOT use class methods as tools. Bound methods cause "Object of type method is not JSON serializable" errors at runtime. Always use standalone `@tool` decorated functions at module level.

### Creating Tools with @tool Decorator

Tools must be standalone functions decorated with `@tool`. Define them at module level and import them directly in your executor:

```python
"""
Your domain tools - AI agent tools for your specific functionality.
"""
import asyncio
import json
from typing import List, Optional, Dict, Any
from typing_extensions import Annotated
from pydantic import Field
from agent_framework import tool

from services.your_service import YourService

# Module-level service instance
_service = YourService()


@tool(name="search_data", description="Search and analyze data by various criteria.")
async def search_data(
    query: Annotated[str, Field(description="Search query for data.")],
    date_range: Annotated[Optional[str], Field(description="Date range filter (e.g., 'last_quarter', 'ytd').")] = None,
    category: Annotated[Optional[str], Field(description="Category filter.")] = None,
) -> str:
    """Search and analyze data by various criteria."""
    results = await _service.search(query, date_range, category)
    
    return json.dumps({
        "query": query,
        "date_range": date_range,
        "category": category,
        "total_results": len(results),
        "results": results
    })


@tool(name="analyze_results", description="Analyze results and patterns over specified periods.")
async def analyze_results(
    period: Annotated[str, Field(description="Analysis period: 'monthly', 'quarterly', 'yearly'.")],
    metrics: Annotated[List[str], Field(description="Metrics to analyze.")] = None,
) -> str:
    """Analyze results and patterns over specified periods."""
    if metrics is None:
        metrics = ["growth_rate", "totals"]
    
    data = await _service.get_analysis(period)
    
    return json.dumps({
        "analysis_period": period,
        "metrics_analyzed": metrics,
        "data": data
    })


@tool(name="generate_report", description="Generate a report based on analyzed data.")
async def generate_report(
    report_type: Annotated[str, Field(description="Type of report: 'summary', 'detailed', 'executive'.")],
    include_charts: Annotated[bool, Field(description="Whether to include chart data.")] = True,
) -> str:
    """Generate a report based on analyzed data."""
    report = await _service.generate_report(report_type, include_charts)
    
    return json.dumps({
        "report_type": report_type,
        "include_charts": include_charts,
        "report": report
    })
```

### Standalone Tool Functions (No Service Dependency)

For tools that don't require service dependencies, the pattern is the same — use `@tool` at module level:

```python
"""
Data processing tools - Static tools for file processing and database operations.
"""
import asyncio
import json
from typing import Dict, Any, List
from typing_extensions import Annotated
from pydantic import Field
from agent_framework import tool


@tool(name="parse_csv_file", description="Parse and analyze CSV files containing data.")
async def parse_csv_file(
    file_path: Annotated[str, Field(description="Path to the CSV file to parse.")],
    analysis_type: Annotated[str, Field(description="Type of analysis: 'summary', 'trends', 'validation'.")] = "summary",
) -> str:
    """Parse and analyze CSV files containing data."""
    # Implementation
    await asyncio.sleep(0.1)  # Simulate processing
    
    results = {
        "file_path": file_path,
        "analysis_type": analysis_type,
        "status": "success",
        "data": {"rows": 1000, "columns": 10}
    }
    
    return json.dumps(results)


@tool(name="query_database", description="Execute SQL queries against databases.")
async def query_database(
    query: Annotated[str, Field(description="SQL query to execute.")],
    database_type: Annotated[str, Field(description="Database type: 'sqlite', 'postgresql', 'mysql'.")] = "postgresql",
    limit: Annotated[int, Field(description="Maximum number of rows to return.")] = 100,
) -> str:
    """Execute SQL queries against databases."""
    # Implementation
    await asyncio.sleep(0.1)
    
    results = {
        "query": query,
        "database_type": database_type,
        "limit": limit,
        "status": "success",
        "rows_returned": min(limit, 50)
    }
    
    return json.dumps(results)
```

### Registering Tools with Agent

Import tools directly by name and pass them to `as_agent()`:

```python
from tools.your_tools import search_data, analyze_results, generate_report
from tools.data_processing_tools import parse_csv_file, query_database

class YourAgentExecutor(AgentExecutor):
    def __init__(self):
        self.agent = AzureOpenAIChatClient(
            credential=AzureCliCredential()
        ).as_agent(
            name="your-agent",
            instructions="Your agent instructions...",
            tools=[
                # Domain tools
                search_data,
                analyze_results,
                generate_report,
                
                # Standalone function tools
                parse_csv_file,
                query_database,
            ]
        )
```

## Data Models

Use Pydantic models for type-safe data structures:

### Domain Models

```python
"""
Domain-specific data models.
"""
from typing import List, Optional, Dict, Any
from datetime import datetime
from pydantic import BaseModel, Field
from enum import Enum
from decimal import Decimal


class ReportType(str, Enum):
    """Types of reports."""
    SUMMARY = "summary"
    DETAILED = "detailed"
    EXECUTIVE = "executive"


class DataSourceType(str, Enum):
    """Types of data sources."""
    CSV = "csv"
    EXCEL = "excel"
    DATABASE = "database"
    API = "api"


class TrendDirection(str, Enum):
    """Trend analysis directions."""
    UP = "up"
    DOWN = "down"
    STABLE = "stable"
    VOLATILE = "volatile"


class DataRecord(BaseModel):
    """Data record model."""
    record_id: str
    name: str
    category: str
    value: Decimal
    quantity: int
    timestamp: datetime
    region: str
    owner: str


class AnalysisResult(BaseModel):
    """Analysis result model."""
    period: str
    total_value: Decimal
    growth_rate: float
    trend_direction: TrendDirection
    source: str
    department: Optional[str] = None
```

### API Models (for .NET compatibility)

```python
"""
API models matching .NET types for interoperability.
"""
from typing import List, Optional
from pydantic import BaseModel, Field
from enum import Enum


class AIChatRole(str, Enum):
    """Chat role enumeration matching the .NET AIChatRole enum."""
    system = "system"
    assistant = "assistant"
    user = "user"


class AIChatFile(BaseModel):
    """Chat file attachment matching the .NET AIChatFile struct."""
    content_type: str = Field(alias="contentType")
    data: str  # Base64 encoded data


class AIChatMessage(BaseModel):
    """Chat message matching the .NET AIChatMessage struct."""
    content: str
    role: AIChatRole
    context: Optional[str] = None
    files: Optional[List[AIChatFile]] = None


class AIChatRequest(BaseModel):
    """Chat request matching the .NET AIChatRequest record."""
    messages: List[AIChatMessage]
    session_state: Optional[str] = Field(default=None, alias="sessionState")
    context: Optional[str] = None


class AIChatMessageDelta(BaseModel):
    """Chat message delta matching the .NET AIChatMessageDelta struct."""
    content: Optional[str] = None
    role: Optional[AIChatRole] = None
    context: Optional[str] = None


class AIChatCompletionDelta(BaseModel):
    """Chat completion delta matching the .NET AIChatCompletionDelta record."""
    delta: AIChatMessageDelta
    session_state: Optional[str] = Field(default=None, alias="sessionState")
    context: Optional[str] = None

    class Config:
        populate_by_name = True  # Allow both snake_case and camelCase
```

## Observability and Telemetry

### Setting Up OpenTelemetry

```python
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from agent_framework.observability import configure_otel_providers


def configure_telemetry(app_instance):
    """Configure OpenTelemetry for the application."""
    # Setup agent framework observability
    configure_otel_providers()
    
    # Setup trace provider
    trace.set_tracer_provider(TracerProvider())
    
    # Configure OTLP exporter
    otlp_exporter = OTLPSpanExporter(
        endpoint=os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT")
    )
    processor = BatchSpanProcessor(otlp_exporter)
    trace.get_tracer_provider().add_span_processor(processor)
    
    # Instrument FastAPI
    FastAPIInstrumentor().instrument_app(app_instance)
```

## Environment Variables

Configure your agent using environment variables:

```bash
# Server configuration
PORT=8001
HOST=0.0.0.0

# Azure OpenAI configuration (when using Azure AI Foundry)
AZURE_OPENAI_ENDPOINT=https://your-endpoint.openai.azure.com/
AZURE_OPENAI_API_VERSION=2024-06-01
AZURE_OPENAI_DEPLOYMENT=gpt-4.1

# OpenTelemetry configuration
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317

# Service discovery (for multi-agent scenarios)
services__other-agent__http__0=http://localhost:8002
```

## Complete Examples

### Basic A2A Agent with Tools

See the actual agent implementations in this repository for reference:
- `src/weather-agent-python/` - Weather agent with `@tool` decorated functions
  - `weather_agent_python/agent_executor.py` - AgentExecutor using `AzureOpenAIChatClient().as_agent()`
  - `tools/weather_tools.py` - Module-level tools with `@tool` decorator
  - `services/weather_service.py` - Data service consuming data-generator REST API
- `src/safety-agent-python/` - Safety agent with risk evaluation tools
- `src/ski-coach-agent-python/` - Ski coach agent with recommendation tools

### Workflow with Multiple Agents

See the A2A protocol documentation for workflow examples including:
- A2A agent connections via `A2ACardResolver`
- Executor functions with `@executor` decorator
- FastAPI endpoint for workflow execution

## Best Practices

### Tool Design

-   Use the `@tool(name="...", description="...")` decorator from `agent_framework` for all tools
-   Define tools as standalone functions at module level (NOT as class methods — bound methods cause serialization errors)
-   Use `Annotated[type, Field(description="...")]` for all parameters
-   Return JSON strings for complex data structures
-   Keep tools focused and single-purpose
-   Use async/await for I/O operations
-   Handle errors gracefully and return meaningful error messages
-   Use a module-level service instance if tools need external data access

### Agent Instructions

-   Be specific about the agent's capabilities and limitations
-   Include examples of what the agent can help with
-   Specify the tone and style of responses
-   Define how the agent should handle edge cases

### Performance

-   Use async/await consistently throughout
-   Set appropriate timeouts for remote agent calls
-   Use connection pooling for HTTP clients
-   Cache expensive operations where appropriate

### Security

-   Use Azure Managed Identity via `AzureCliCredential` or `DefaultAzureCredential`
-   Never expose API keys in code
-   Validate and sanitize user inputs
-   Configure CORS appropriately (restrictive for production)

### Error Handling

```python
try:
    response_content = await self.agent.run(query)
    response_text = response_content.text
except Exception as e:
    logger.error(f"Error during execution: {e}", exc_info=True)
    error_message = f"An error occurred: {str(e)}"
    message = new_agent_text_message(error_message)
    await event_queue.enqueue_event(message)
```

### Project Organization

-   Keep tools in a dedicated `tools/` directory as module-level `@tool` decorated functions
-   Keep services in a dedicated `services/` directory
-   Keep models in the main package or a `models/` directory
-   `__init__.py` files in tools/ are NOT required — import tools directly by function name
-   Use type hints throughout

## Running the Agent

### Development

```bash
# Install dependencies with uv
uv sync --prerelease=allow

# Run the agent
uv run start

# Or run directly
uv run python -m your_agent_python.main
```

### Testing

```bash
# Run tests
uv run pytest

# Test with httpx client
uv run pytest tests/ -v
```

## A2A Frontend Integration

When consuming Python agents from a frontend application, use the A2A JavaScript SDK (same as .NET agents):

```typescript
import { ClientFactory } from '@a2a-js/sdk/client';
import type { MessageSendParams } from '@a2a-js/sdk';
import { v4 as uuidv4 } from 'uuid';

// Initialize client using ClientFactory
const factory = new ClientFactory();
const client = await factory.createFromUrl('http://localhost:8001');

// Send a message with streaming
const params: MessageSendParams = {
    message: {
        messageId: uuidv4(),
        role: 'user',
        kind: 'message',
        parts: [{ kind: 'text', text: 'Hello!' }],
        contextId: conversationId,
    },
};

// Stream responses
const stream = client.sendMessageStream(params);
for await (const event of stream) {
    if (event.kind === 'message') {
        for (const part of event.parts ?? []) {
            if (part.kind === 'text') {
                console.log(part.text);
            }
        }
    } else if (event.kind === 'status-update') {
        console.log(`Status: ${event.status.state}`);
    }
}
```

## Reference Resources

-   [Microsoft Agent Framework GitHub](https://github.com/microsoft/agent-framework/) - Official MAF repository
-   [A2A Protocol](https://a2a-protocol.org/) - Agent-to-Agent protocol specification
-   [A2A Python SDK](https://github.com/a2aproject/a2a-python) - Official A2A Python SDK
-   [A2A JavaScript SDK](https://github.com/a2aproject/a2a-js) - Official A2A JavaScript SDK
-   [Weather Agent](../../src/weather-agent-python/) - A2A Python agent implementation example
-   [Safety Agent](../../src/safety-agent-python/) - Safety evaluation agent example
-   [Ski Coach Agent](../../src/ski-coach-agent-python/) - Recommendation agent example
-   [uv Documentation](https://docs.astral.sh/uv/) - Python package manager documentation
-   [FastAPI Documentation](https://fastapi.tiangolo.com/) - FastAPI framework documentation
-   [Pydantic Documentation](https://docs.pydantic.dev/) - Pydantic data validation documentation
