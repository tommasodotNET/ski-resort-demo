"""Opt-in native MAF E2E against isolated local .NET providers, without Azure.

Build the four providers, then:
RUN_LIVE_MCP_TESTS=1 uv run python -m unittest discover -s tests -p test_live_mcp.py -v
Existing app processes are never restarted or reused by these tests.
"""
from __future__ import annotations

import asyncio
from contextlib import AsyncExitStack
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import threading
import unittest
from unittest.mock import AsyncMock
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from agent_framework import Message
from mcp import ClientSession
from mcp.client.streamable_http import streamable_http_client
from pydantic import AnyUrl

from skills_orchestrator_python.config import DEFAULT_SKILL_PROVIDERS
from skills_orchestrator_python.native_mcp import SkillConnection
from test_native_mcp import call, make_agent


SRC = Path(__file__).resolve().parents[2]
PROVIDERS = (
    ("weather", "weather-skills", "WeatherSkill.Dotnet"),
    ("safety", "safety-skills", "SafetySkill.Dotnet"),
    ("ski-coach", "ski-coach-skills", "SkiCoachSkill.Dotnet"),
    ("lift-traffic", "lift-traffic-skills", "LiftTrafficSkill.Dotnet"),
)
# Test-only provider contract: catalogs drive execution; this catches missing coverage.
EXPECTED_OPERATIONS = {
    "weather": {"weather_current_conditions", "weather_forecast", "weather_storm_status"},
    "safety": {"safety_risk", "safety_slope_safety", "safety_closed_slopes"},
    "skicoach": {"ski_coach_recommendations", "ski_coach_day_plan"},
    "lifttraffic": {"lift_traffic_lifts", "lift_traffic_lift_status",
                    "lift_traffic_wait_times", "lift_traffic_least_busy_area"},
}
WEATHER = {
    "temperature": -5.0, "wind_speed": 15.0, "snow_intensity": 1,
    "visibility": 5000, "timestamp": "2026-09-10T08:00:00Z",
}
LIFTS = [{
    "lift_id": "chairlift-alpha", "name": "Alpha", "status": "open",
    "queue_length": 4, "wait_time_minutes": 2, "throughput_rate": 1200,
    "serves_slopes": ["valley-run"], "timestamp": "2026-09-10T08:00:00Z",
}]
SLOPES = [{
    "slope_id": "valley-run", "name": "Valley Run", "difficulty": "green",
    "is_open": True, "groomed": True, "snow_depth_cm": 80,
    "served_by_lift_id": "chairlift-alpha",
}]
FIXTURES = {
    "/api/weather": WEATHER, "/api/lifts": LIFTS,
    "/api/lifts/chairlift-alpha": LIFTS[0], "/api/slopes": SLOPES,
    "/api/safety": {
        "avalanche_risk_index": 0.1, "incident_reports": [],
        "timestamp": "2026-09-10T08:00:00Z",
    },
}
FIXTURES["/api/current-state"] = {
    "weather": WEATHER, "lifts": LIFTS, "slopes": SLOPES, "safety": FIXTURES["/api/safety"],
}


class DataHandler(BaseHTTPRequestHandler):
    failing = False

    def do_GET(self):
        body = FIXTURES.get(self.path)
        status = 503 if self.failing else (200 if body is not None else 404)
        encoded = json.dumps(body if status == 200 else {"error": "fixture unavailable"}).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def log_message(self, *_args):
        pass


@unittest.skipUnless(os.getenv("RUN_LIVE_MCP_TESTS") == "1", "requires built local .NET providers")
class LiveNativeMcpTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="ski-native-mcp-test-")
        self.addCleanup(self.temp.cleanup)
        DataHandler.failing = False
        data = ThreadingHTTPServer(("127.0.0.1", 0), DataHandler)
        thread = threading.Thread(target=data.serve_forever, daemon=True)
        thread.start()

        def stop_data():
            data.shutdown()
            data.server_close()
            thread.join(timeout=5)

        self.addCleanup(stop_data)
        self.endpoints = {}
        for skill, folder, assembly in PROVIDERS:
            dll = SRC / folder / "bin" / "Debug" / "net10.0" / f"{assembly}.dll"
            self.assertTrue(dll.is_file(), f"Build {folder} before running live tests")
            log_path = Path(self.temp.name) / f"{skill}.log"
            log = self.enterContext(log_path.open("w+"))
            env = dict(os.environ)
            for key in tuple(env):
                if key.startswith(("OTEL_", "ASPIRE_", "services__")):
                    env.pop(key)
            env.update({
                "ASPNETCORE_ENVIRONMENT": "Development", "DOTNET_ENVIRONMENT": "Development",
                "services__datagenerator__http__0": f"http://127.0.0.1:{data.server_port}",
            })
            process = subprocess.Popen(
                ["dotnet", str(dll), "--urls", "http://127.0.0.1:0"],
                cwd=SRC / folder, env=env, stdout=log, stderr=subprocess.STDOUT,
            )

            def stop_process(process=process):
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=5)

            self.addCleanup(stop_process)
            endpoint = None
            for _ in range(200):
                text = log_path.read_text()
                match = re.search(r"Now listening on: (http://127\.0\.0\.1:\d+)", text)
                if match:
                    endpoint = match.group(1) + "/skillsmcp"
                    break
                if process.poll() is not None:
                    self.fail(f"{skill} exited:\n{text}")
                await asyncio.sleep(0.1)
            self.assertIsNotNone(endpoint, f"{skill} did not start:\n{log_path.read_text()}")
            self.endpoints[skill] = endpoint

    async def test_all_four_native_skill_groups_direct_calls_and_errors(self):
        async with AsyncExitStack() as stack:
            self.stack = stack
            sessions, connections, catalogs = {}, [], {}
            for config, (skill, _, _) in zip(DEFAULT_SKILL_PROVIDERS, PROVIDERS, strict=True):
                endpoint = self.endpoints[skill]
                read, write, _ = await stack.enter_async_context(streamable_http_client(endpoint))
                session = await stack.enter_async_context(ClientSession(read, write))
                await session.initialize()
                session.read_resource = AsyncMock(wraps=session.read_resource)
                session.call_tool = AsyncMock(wraps=session.call_tool)
                sessions[skill] = session
                connections.append(SkillConnection(config, endpoint, session))
                tools = {}
                cursor = None
                while True:
                    page = await session.list_tools(cursor=cursor)
                    tools.update((tool.name, tool) for tool in page.tools)
                    cursor = page.nextCursor
                    if not cursor:
                        break
                self.assertEqual(set(tools), EXPECTED_OPERATIONS[config.key])
                catalogs[config.key] = tools

            sample_arguments = {
                "hours": 2, "liftId": "chairlift-alpha", "slopeId": "valley-run",
                "skillLevel": "beginner", "area": "all", "preferences": "avoid_crowds,groomed_only",
            }
            tested_calls = []
            for connection, (skill, _, _) in zip(connections, PROVIDERS, strict=True):
                with self.subTest(skill=skill):
                    session = sessions[skill]
                    resources = await session.list_resources()
                    resource_uris = [str(resource.uri) for resource in resources.resources]
                    self.assertIn("skill://index.json", resource_uris)
                    self.assertTrue(all(uri == "skill://index.json" or uri.endswith("/SKILL.md")
                                        for uri in resource_uris), resource_uris)
                    templates = await session.list_resource_templates()
                    self.assertEqual(templates.resourceTemplates, [])
                    tools = catalogs[connection.config.key]
                    canonical = await session.read_resource(AnyUrl(f"skill://{skill}/SKILL.md"))
                    instructions = "\n".join(content.text for content in canonical.contents)
                    self.assertIn("load_skill", instructions)
                    self.assertIn("next model iteration", instructions.lower())
                    self.assertNotIn("_load_tool", instructions)
                    self.assertNotIn("read_skill_resource", instructions)
                    for name, tool in tools.items():
                        args = {key: sample_arguments[key] for key in tool.inputSchema.get("properties", {})
                                if key in sample_arguments}
                        await self._assert_native_loop(
                            connections, sessions, catalogs, skill, connection, name, args, tool,
                        )
                        tested_calls.append((skill, name, args))

            self.assertEqual(len(tested_calls), 12)
            self.assertEqual(
                {name for _, name, _ in tested_calls},
                set().union(*EXPECTED_OPERATIONS.values()),
            )
            # No legacy operational resources: provider errors remain errors.
            DataHandler.failing = True
            try:
                for skill, name, args in tested_calls:
                    self.assertTrue((await sessions[skill].call_tool(name, args)).isError, name)
                await self._assert_native_error(connections)
            finally:
                DataHandler.failing = False
            self.assertTrue((await sessions["weather"].call_tool("weather_forecast", {"hours": 0})).isError)
            self.assertTrue((await sessions["ski-coach"].call_tool("ski_coach_day_plan", {"skillLevel": 1})).isError)

    async def _assert_native_loop(self, connections, sessions, catalogs, skill, connection, name, args, schema):
        prefix = connection.config.key
        full_name = f"{prefix}_{name}"
        for session in sessions.values():
            session.read_resource.reset_mock()
            session.call_tool.reset_mock()

        def initial(messages, options):
            text = json.dumps([message.to_dict() for message in messages]) + str(options.get("instructions"))
            names = {tool.name for tool in options["tools"]}
            tool_context = json.dumps([tool.to_dict() for tool in options["tools"]])
            for remote in connections:
                for operation, advertised in catalogs[remote.config.key].items():
                    self.assertNotIn(operation, text)
                    self.assertNotIn(operation, tool_context)
                    self.assertNotIn(json.dumps(advertised.inputSchema), tool_context)
                    self.assertNotIn(f"{remote.config.key}_{operation}", names)
            return call("load_skill", {"skill_name": skill})

        def skill_loaded(messages, options):
            exposed = {tool.name: tool for tool in options["tools"]}
            self.assertIn(full_name, exposed)
            self.assertEqual(exposed[full_name].parameters(), schema.inputSchema)
            self.assertFalse(any(tool.endswith(("_load_tool", "_list_mcp_tools", "_unload_tool"))
                                 for tool in exposed))
            for remote in connections:
                for operation, advertised in catalogs[remote.config.key].items():
                    other = f"{remote.config.key}_{operation}"
                    if remote is connection:
                        self.assertEqual(exposed[other].parameters(), advertised.inputSchema)
                        self.assertEqual(exposed[other].description, advertised.description)
                        self.assertEqual(exposed[other].approval_mode, "never_require")
                    else:
                        self.assertNotIn(other, exposed)
            return call(full_name, args)

        def done(messages, options):
            result = [c for m in messages for c in m.contents if c.type == "function_result"][-1]
            self.assertIsNone(result.exception)
            self.assertNotIn('"isError": true', json.dumps(result.to_dict()))
            return Message("assistant", ["live native result"])

        agent, _ = await make_agent(connections, [initial, skill_loaded, done], self.stack)
        response = await agent.run("ski advice", session=agent.create_session())
        self.assertEqual(response.text, "live native result")
        self.assertFalse(any(c.type == "function_approval_request"
                             for m in response.messages for c in m.contents))
        for key, session in sessions.items():
            reads = [str(args.args[0]) for args in session.read_resource.await_args_list]
            self.assertTrue(all(uri == "skill://index.json" or uri == f"skill://{key}/SKILL.md"
                                for uri in reads), reads)
            if key == skill:
                self.assertIn(f"skill://{skill}/SKILL.md", reads)
                self.assertEqual(session.call_tool.await_count, 1)
                self.assertEqual(session.call_tool.await_args.args[0], name)
                self.assertEqual(session.call_tool.await_args.kwargs["arguments"], args)
            else:
                session.call_tool.assert_not_awaited()

    async def _assert_native_error(self, connections):
        def failed(messages, options):
            results = [c for m in messages for c in m.contents if c.type == "function_result"]
            self.assertIsNotNone(results[-1].exception)
            return Message("assistant", ["failure reported"])
        agent, _ = await make_agent(connections, [
            call("load_skill", {"skill_name": "weather"}),
            call("weather_weather_current_conditions", {}), failed,
        ], self.stack)
        self.assertEqual((await agent.run("weather", session=agent.create_session())).text, "failure reported")


if __name__ == "__main__":
    unittest.main()
