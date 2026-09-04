# Shared container image for the Skills Orchestrator's Microsoft Foundry hosted-agent
# (Responses protocol) and A2A entry points.
#
# This Dockerfile exists specifically so Aspire's `.AsHostedAgent(...)` publish-mode
# wiring can containerize this resource: for an `ExecutableResource` (e.g. one created
# via Aspire.Hosting.Python's `AddUvApp`), `AsHostedAgent` calls `PublishAsDockerFile()`
# internally, which builds from a `Dockerfile` at this project's working directory --
# see this project's README.md, "Foundry hosted Responses agent" section.
#
# It is NOT used by local `aspire run`/dev-loop execution. In publish mode both
# "skiadvisorskill" and "skiadvisorskilla2a" build from this directory, so the
# entrypoint preserves the default Responses startup while dispatching Aspire's
# Uvicorn argument list for the A2A surface.
FROM ghcr.io/astral-sh/uv:0.9-python3.12-bookworm-slim

WORKDIR /app

# Install dependencies first, from the lockfile alone, so this layer is cached across
# source-only changes. `--no-install-project` skips installing this package itself
# until its own source is copied in below.
COPY pyproject.toml uv.lock ./
RUN uv sync --locked --no-install-project --no-dev

COPY skills_orchestrator_python ./skills_orchestrator_python
COPY docker-entrypoint.sh ./
RUN uv sync --locked --no-dev

# Put the project's venv on PATH so `uv run` (and plain `python`/console scripts) resolve
# without needing `uv run` to re-sync at container start.
ENV PATH="/app/.venv/bin:${PATH}"

# Matches `azure-ai-agentserver-core`'s own default port and the port Aspire's
# `AsHostedAgent` assumes in run mode (`DEFAULT_AD_PORT`, default target port 8088).
EXPOSE 8088

ENTRYPOINT ["/app/docker-entrypoint.sh"]
CMD ["uv", "run", "--no-sync", "start-responses"]
