"""Responses host identity is established before SDK telemetry initialization."""
from __future__ import annotations

import os
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, patch

from azure.ai.agentserver.core._tracing import _create_resource

from skills_orchestrator_python import foundry_responses_main as responses


class ResponsesTelemetryTests(unittest.IsolatedAsyncioTestCase):
    async def test_service_name_at_host_construction(self):
        cases = [
            ({}, "skiadvisorskill"),
            ({"OTEL_SERVICE_NAME": "aspire-skills"}, "aspire-skills"),
            ({"FOUNDRY_AGENT_NAME": "deployed-skills"}, "deployed-skills"),
            ({
                "FOUNDRY_AGENT_NAME": "deployed-skills",
                "OTEL_SERVICE_NAME": "aspire-skills",
            }, "deployed-skills"),
            ({"FOUNDRY_AGENT_NAME": "", "OTEL_SERVICE_NAME": ""}, "skiadvisorskill"),
        ]
        for environment, expected in cases:
            with self.subTest(environment=environment):
                built = SimpleNamespace(
                    agent=object(),
                    history_backend="cosmos",
                    connected_providers=["weather"],
                    skipped_providers=[],
                )
                server = SimpleNamespace(run_async=AsyncMock())

                def create_host(agent, *, history_source):
                    self.assertIs(agent, built.agent)
                    self.assertEqual(history_source, "agent")
                    self.assertEqual(
                        _create_resource().attributes["service.name"], expected
                    )
                    return server

                with (
                    patch.dict(os.environ, environment, clear=True),
                    patch.object(
                        responses, "build_orchestrator_agent", AsyncMock(return_value=built)
                    ),
                    patch.object(responses, "ResponsesHostServer", side_effect=create_host),
                ):
                    await responses._run()
                server.run_async.assert_awaited_once_with(host="0.0.0.0", port=8088)
