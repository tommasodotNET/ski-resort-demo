# 🏔️ AlpineAI – Multi-Agent Ski Resort Demo

A distributed, multi-agent ski resort system built with **Microsoft Agent Framework (MAF)**, the **A2A protocol**, and **.NET Aspire**.

An AI-powered ski resort concierge that coordinates weather intelligence, lift traffic, safety evaluation, and personalized coaching through a network of specialist agents — all orchestrated by a central advisor and displayed on a real-time dashboard.

![.NET](https://img.shields.io/badge/.NET_10-512BD4?style=flat&logo=dotnet&logoColor=white)
![Python](https://img.shields.io/badge/Python_3.11+-3776AB?style=flat&logo=python&logoColor=white)
![Aspire](https://img.shields.io/badge/Aspire_13.1-512BD4?style=flat&logo=dotnet&logoColor=white)
![React](https://img.shields.io/badge/React_+_Vite-61DAFB?style=flat&logo=react&logoColor=black)

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   Frontend (Vite + React)            │
│         Real-time dashboard + AI Chat (A2A)         │
└──────────────┬──────────────────────┬───────────────┘
               │ REST                 │ A2A (streaming)
               ▼                      ▼
┌──────────────────────┐  ┌──────────────────────────┐
│   Data Generator     │  │   Advisor Agent (.NET)   │
│   (Python/FastAPI)   │  │   Orchestrator via A2A   │
└──────────────────────┘  └────┬───┬───┬───┬─────────┘
                               │   │   │   │  A2A
                    ┌──────────┘   │   │   └──────────┐
                    ▼              ▼   ▼              ▼
             ┌───────────┐ ┌──────────┐ ┌───────────┐ ┌──────────┐
             │  Weather   │ │   Lift   │ │  Safety   │ │Ski Coach │
             │  Agent     │ │  Traffic │ │  Agent    │ │  Agent   │
             │ (Python)   │ │  (.NET)  │ │ (Python)  │ │ (Python) │
             └───────────┘ └──────────┘ └───────────┘ └──────────┘
```

| Component | Language | Role |
|---|---|---|
| **Advisor Agent** | .NET | Central orchestrator — routes questions to specialist agents via A2A |
| **Weather Agent** | Python | Current conditions, forecasts, storm alerts |
| **Lift Traffic Agent** | .NET | Lift status, wait times, congestion analysis |
| **Safety Agent** | Python | Risk evaluation, slope safety, closures |
| **Ski Coach Agent** | Python | Personalized slope recommendations, day plans |
| **Data Generator** | Python | Continuously generates synthetic resort telemetry |
| **Frontend** | React/Vite | Real-time dashboard with AI chat panel |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Python 3.11+](https://www.python.org/downloads/)
- [uv](https://docs.astral.sh/uv/) (Python package manager)
- [Node.js 20+](https://nodejs.org/)
- [.NET Aspire CLI](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling)
- An **Azure AI Foundry** resource with a `gpt-4.1` (or similar) deployment
- **Azure CLI** authenticated (`az login`)

### Install Aspire CLI

Refer to the [official Aspire documentation](https://aspire.dev/get-started/install-cli/) for installation instructions.

## Setup

### 1. Clone the repository

```bash
git clone https://github.com/tommasodotNET/ski-resort-demo.git
cd ski-resort-demo
```

### 2. Configure Azure settings

Edit `src/apphost.settings.Development.json` with your Azure details:

```json
{
    "Azure": {
        "TenantId": "<your-tenant-id>",
        "SubscriptionId": "<your-subscription-id>",
        "AllowResourceGroupCreation": true,
        "ResourceGroup": "<your-resource-group>",
        "Location": "<your-azure-region>",
        "CredentialSource": "AzureCli"
    },
    "Parameters": {
        "existingFoundryName": "<your-azure-ai-foundry-name>",
        "existingFoundryResourceGroup": "<foundry-resource-group>"
    }
}
```

> **Note:** The Azure AI Foundry resource must have a chat completion model deployed (e.g., `gpt-4.1`). The deployment name is configured in the Aspire AppHost.

### 3. Run the application

From the `src/` directory:

```bash
cd src
aspire run
```

This single command starts **all services**:
- 2 .NET agents (advisor + lift traffic)
- 3 Python agents (weather + safety + ski coach)
- Data generator (Python/FastAPI)
- Frontend (Vite dev server)
- Cosmos DB emulator

Open the **Aspire dashboard** (URL shown in terminal output) to see all services, logs, and distributed traces.

The **frontend** will be available at the URL assigned by Aspire (shown in the dashboard).

## Project Structure

```
src/
├── apphost.cs                      # Aspire orchestration (all services wired here)
├── apphost.settings.Development.json  # Azure configuration
├── advisor-agent-dotnet/           # .NET orchestrator agent (A2A)
├── lift-traffic-agent-dotnet/      # .NET lift traffic agent (A2A)
├── weather-agent-python/           # Python weather agent (A2A)
├── safety-agent-python/            # Python safety agent (A2A)
├── ski-coach-agent-python/         # Python ski coach agent (A2A)
├── data-generator/                 # Python FastAPI data generator
├── frontend/                       # Vite + React + Tailwind dashboard
├── shared-services/                # .NET shared library (Cosmos, thread store)
└── service-defaults/               # Aspire service defaults
```

## Configuration

### Data Generator

The data generation speed and drift magnitudes are configurable via `src/data-generator/data_generator/config.json`:

```json
{
  "update_interval_seconds": { "min": 5, "max": 10 },
  "weather": { "temperature_drift": 0.1, "wind_speed_drift": 0.5, ... },
  "lifts": { "queue_drift": 3, "status_change_probability": 0.002 },
  ...
}
```

### Frontend

The dashboard polling interval is configurable via `src/frontend/public/config.json`:

```json
{
  "pollingIntervalMs": 10000
}
```

Changes are picked up automatically without restarting.

## How It Works

1. **Data Generator** continuously produces synthetic weather, lift, slope, and safety telemetry via a REST API.

2. **Specialist agents** (weather, lift, safety, coach) each wrap specific tools using MAF and expose them over the **A2A protocol**. Each agent calls the data generator's API to fetch current conditions.

3. **Advisor Agent** is the central orchestrator. It registers all specialist agents as tools (via A2A) and selectively invokes only the relevant agents based on the user's question.

4. **Frontend** displays real-time data panels (weather, lifts, slopes, safety) by polling the data generator, and provides an AI chat panel that streams responses from the advisor agent via the A2A protocol.

## Key Technologies

- **[Microsoft Agent Framework (MAF)](https://github.com/microsoft/agents)** — Agent creation, tool registration, and orchestration
- **[A2A Protocol](https://github.com/google/A2A)** — Agent-to-agent communication over JSON-RPC + SSE streaming
- **[Aspire](https://aspire.dev)** — Distributed app orchestration, service discovery, observability
- **[Azure AI Foundry](https://ai.azure.com)** — LLM backend (GPT-4.1)
- **[Vite](https://vitejs.dev) + [React](https://react.dev)** — Frontend dashboard
- **[Azure Cosmos DB](https://learn.microsoft.com/azure/cosmos-db/)** — Conversation thread persistence

## Further Reading

See [ARCHITECTURE.md](ARCHITECTURE.md) for the detailed system architecture document.
