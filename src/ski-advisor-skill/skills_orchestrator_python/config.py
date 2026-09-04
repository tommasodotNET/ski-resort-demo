"""Configuration and remote skill-provider discovery for the skills orchestrator.

Resolves, for each skill-backed specialist domain (weather, safety, ski-coach,
lift-traffic), the streamable-HTTP MCP endpoint of a
*skill-provider* resource that publishes SEP-2640 Agent Skills
(``skill://index.json`` + ``skill://<name>/SKILL.md``) and live sibling
resources read through those skills.

All four skill-provider implementations are owned and built in .NET (this
project never implements a skill-provider server itself, only the MCP
*client*/orchestrator side):

- ``weatherskills``, ``safetyskills``, ``skicoachskills``,
  ``lifttrafficskills``: standalone MCP skill-provider projects, one per specialist, each with its own
  dedicated compact ``*skills`` Aspire resource, paired with that specialist's
  compact ``*agenta2a`` resource (``lift-traffic-skills`` is a
  lightweight MCP host that reuses lift-traffic's existing data capability
  without starting its own chat agent).

Resolution order for each provider's URL:

1. An explicit full URL via a per-provider environment variable, e.g.
   ``WEATHER_SKILLS_MCP_URL=https://weatherskills.internal/skillsmcp``.
2. Aspire's standard service-discovery convention for the provider's resource
   name (e.g. ``weatherskills``): the orchestrator reads
   ``services__<resource>__https__0`` (falling back to the ``http``
   scheme) and appends a configurable path (default ``/skillsmcp``, overridable
   via ``<KEY>_SKILLS_MCP_PATH`` -- this matches the path all four .NET skill
   providers map their MCP endpoint on, via ``app.MapMcp("/skillsmcp")``).
3. If neither is set, the provider is treated as unconfigured and is skipped;
   the orchestrator still starts and serves the remaining providers.

This lets the orchestrator run standalone (explicit URLs) or as an Aspire
resource that references the .NET MCP skill-provider resources, without
hardcoding a deployment topology.
"""
from __future__ import annotations

import os
from dataclasses import dataclass

#: Environment variable holding the Foundry project endpoint, matching the
#: convention already used by the sibling A2A specialist agents
#: (``weather-agent-a2a``, ``safety-agent-a2a``, ``ski-coach-agent-a2a``).
FOUNDRY_PROJECT_ENDPOINT_ENV = "GPT41_URI"

#: Environment variable overriding the Foundry model deployment name.
FOUNDRY_MODEL_ENV = "GPT41_MODEL"

#: Default Foundry model deployment name, matching the apphost's
#: `AddModelDeployment("gpt41", ...)` resource name.
DEFAULT_FOUNDRY_MODEL = "gpt41"

#: Default path appended to an Aspire-discovered service base URL to reach the
#: MCP streamable-HTTP endpoint, when the provider does not override it. Matches
#: the path the .NET skill-provider projects map their MCP endpoint on
#: (``app.MapMcp("/skillsmcp")`` in each of their ``Program.cs``).
DEFAULT_MCP_PATH = "/skillsmcp"


@dataclass(frozen=True)
class SkillProviderConfig:
    """Describes how to locate one remote MCP skill-provider resource."""

    key: str
    """Short identifier used in environment variable names and logs (e.g. "weather")."""

    resource_name: str
    """The conventional Aspire resource name for this provider."""

    default_path: str = DEFAULT_MCP_PATH
    """Default URL path appended to the Aspire-discovered base URL."""

    @property
    def url_env_var(self) -> str:
        """Environment variable name for an explicit, full MCP endpoint URL override."""
        return f"{self.key.upper()}_SKILLS_MCP_URL"

    @property
    def path_env_var(self) -> str:
        """Environment variable name overriding the path appended to the discovered base URL."""
        return f"{self.key.upper()}_SKILLS_MCP_PATH"


#: The four specialist skill-provider resources this orchestrator discovers by default.
#: Each provider has a dedicated compact ``*skills`` Aspire resource paired
#: with the corresponding compact ``*agenta2a`` resource.
DEFAULT_SKILL_PROVIDERS: tuple[SkillProviderConfig, ...] = (
    SkillProviderConfig(key="weather", resource_name="weatherskills"),
    SkillProviderConfig(key="safety", resource_name="safetyskills"),
    SkillProviderConfig(key="skicoach", resource_name="skicoachskills"),
    SkillProviderConfig(key="lifttraffic", resource_name="lifttrafficskills"),
)



def resolve_service_base_url(resource_name: str, *, env: dict[str, str] | None = None) -> str | None:
    """Resolve an Aspire service-discovery base URL for ``resource_name``.

    Prefers HTTPS, falls back to HTTP, matching the
    ``services__<resource>__https__0`` / ``services__<resource>__http__0``
    convention already used elsewhere in this repo (see the Python specialist
    agents' ``services/*.py`` clients for the data-generator service).

    Args:
        resource_name: The Aspire resource name to look up.
        env: Optional environment mapping to read from (defaults to ``os.environ``);
            provided for testability.

    Returns:
        The base URL (no trailing slash) or ``None`` if not configured.
    """
    source = env if env is not None else os.environ
    for scheme in ("https", "http"):
        value = source.get(f"services__{resource_name}__{scheme}__0")
        if value:
            return value.rstrip("/")
    return None


def resolve_skill_provider_url(
    provider: SkillProviderConfig, *, env: dict[str, str] | None = None
) -> str | None:
    """Resolve the full MCP endpoint URL for a skill provider, or ``None`` if unconfigured.

    Args:
        provider: The provider configuration to resolve.
        env: Optional environment mapping to read from (defaults to ``os.environ``);
            provided for testability.

    Returns:
        The full MCP streamable-HTTP endpoint URL, or ``None``.
    """
    source = env if env is not None else os.environ
    explicit = source.get(provider.url_env_var)
    if explicit:
        return explicit

    base_url = resolve_service_base_url(provider.resource_name, env=source)
    if not base_url:
        return None

    path = source.get(provider.path_env_var, provider.default_path)
    if not path.startswith("/"):
        path = f"/{path}"
    return f"{base_url}{path}"


def resolve_configured_skill_providers(
    providers: tuple[SkillProviderConfig, ...] = DEFAULT_SKILL_PROVIDERS,
    *,
    env: dict[str, str] | None = None,
) -> dict[str, str]:
    """Return ``{provider.key: url}`` for every provider that resolves to a URL."""
    result: dict[str, str] = {}
    for provider in providers:
        url = resolve_skill_provider_url(provider, env=env)
        if url:
            result[provider.key] = url
    return result


#: Environment variables for the durable conversation-history backend, matching the
#: names used verbatim by the official Agent Framework Python sample this integration is
#: based on (``python/samples/02-agents/conversations/cosmos_history_provider.py``), so this
#: project can be configured/tested identically to that sample, independent of Aspire.
COSMOS_ENDPOINT_ENV = "AZURE_COSMOS_ENDPOINT"
COSMOS_DATABASE_NAME_ENV = "AZURE_COSMOS_DATABASE_NAME"
COSMOS_CONTAINER_NAME_ENV = "AZURE_COSMOS_CONTAINER_NAME"
COSMOS_KEY_ENV = "AZURE_COSMOS_KEY"
COSMOS_CONNECTION_STRING_ENV = "ConnectionStrings__skillhistory"

#: Default database name, matching the apphost's existing `db.AddCosmosDatabase("db")`
#: resource -- the same Cosmos DB account + database already provisioned for the
#: `voice-advisor-agent`'s own conversation persistence.
DEFAULT_COSMOS_DATABASE_NAME = "db"

#: Default container name for this orchestrator's own session/message history. A
#: *dedicated* container (not the existing `conversations`/`sessions` containers) is used
#: deliberately: `CosmosHistoryProvider` always partitions on `/session_id`
#: (`PartitionKey(path="/session_id")`, hardcoded in `agent_framework_azure_cosmos`), which
#: does not match the `/conversationId` partition key of the existing containers -- reusing
#: either of those verbatim would silently defeat partitioning (or fail entirely) rather than
#: "match Cosmos resources". Using a new container in the *same* Cosmos account/database is
#: the closest feasible match: same underlying resource, correct partition key for this
#: library's schema. See README.md / the project report for the exact AppHost container
#: definition this expects (`db.AddContainer("skillhistory", "/session_id")`).
DEFAULT_COSMOS_CONTAINER_NAME = "skillhistory"

#: `CosmosHistoryProvider(source_id=...)` value: identifies which agent wrote a given
#: history record (its documents are additionally filtered by this field), and doubles as
#: this orchestrator's own identity, matching its AgentCard/Aspire resource name.
COSMOS_HISTORY_SOURCE_ID = "skiadvisorskill"
SKI_RESEARCHER_AGENT_NAME_ENV = "SKIRESEARCHER_AGENTNAME"
SKI_RESEARCHER_PROJECT_ENDPOINT_ENV = "SKIRESEARCHER_PROJECTENDPOINT"


@dataclass(frozen=True)
class CosmosHistoryConfig:
    """Resolved configuration for the durable Cosmos DB conversation-history backend."""

    endpoint: str
    database_name: str
    container_name: str
    key: str | None
    """Optional Cosmos account key. If unset, an Azure AD credential is used instead."""


def get_cosmos_history_config(*, env: dict[str, str] | None = None) -> CosmosHistoryConfig | None:
    """Resolve the Cosmos DB history-provider configuration, or ``None`` if unconfigured.

    Conversation history persistence is an optional enhancement: if
    ``AZURE_COSMOS_ENDPOINT`` is not set, the orchestrator still starts and runs --
    sessions are simply not durable across process restarts (each A2A task gets a
    fresh, unpersisted `AgentSession`), matching this project's existing
    graceful-degradation philosophy for unconfigured optional dependencies.

    Args:
        env: Optional environment mapping to read from (defaults to ``os.environ``);
            provided for testability.

    Returns:
        The resolved `CosmosHistoryConfig`, or ``None`` if `AZURE_COSMOS_ENDPOINT` is unset.
    """
    source = env if env is not None else os.environ
    endpoint = source.get(COSMOS_ENDPOINT_ENV)
    key = source.get(COSMOS_KEY_ENV) or None
    connection_string = source.get(COSMOS_CONNECTION_STRING_ENV)
    if not endpoint and connection_string:
        values = dict(
            part.split("=", 1)
            for part in connection_string.split(";")
            if "=" in part
        )
        endpoint = values.get("AccountEndpoint")
        key = values.get("AccountKey") or key

    if not endpoint:
        return None

    return CosmosHistoryConfig(
        endpoint=endpoint,
        database_name=source.get(COSMOS_DATABASE_NAME_ENV, DEFAULT_COSMOS_DATABASE_NAME),
        container_name=source.get(COSMOS_CONTAINER_NAME_ENV, DEFAULT_COSMOS_CONTAINER_NAME),
        key=key,
    )


def get_foundry_config(*, env: dict[str, str] | None = None) -> tuple[str | None, str]:
    """Return ``(project_endpoint, model_deployment_name)`` for the Foundry chat client."""
    source = env if env is not None else os.environ
    endpoint = source.get(FOUNDRY_PROJECT_ENDPOINT_ENV)
    model = source.get(FOUNDRY_MODEL_ENV, DEFAULT_FOUNDRY_MODEL)
    return endpoint, model
