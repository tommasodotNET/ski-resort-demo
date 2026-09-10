"""Offline tests of the real MAF skills/progressive-MCP/automatic invocation loop."""
from __future__ import annotations

import asyncio
from contextlib import AsyncExitStack
import json
import os
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, patch

from agent_framework import (
    Agent, AgentSession, AggregatingSkillsSource, BaseChatClient, ChatMiddlewareLayer,
    ChatResponse, ChatResponseUpdate, Content, FunctionInvocationLayer, MCPSkillsSource,
    Message, ResponseStream, SkillsProvider, ToolApprovalMiddleware,
)
from mcp import ClientSession
from mcp.types import (
    CallToolResult, EmptyResult, ListToolsResult, ReadResourceResult, ResourcesCapability,
    ServerCapabilities, TextContent, TextResourceContents, Tool, ToolsCapability,
)

from skills_orchestrator_python.agent_builder import INSTRUCTIONS
from skills_orchestrator_python.config import SkillProviderConfig
from skills_orchestrator_python.native_mcp import NativeMCPToolsMiddleware, SkillConnection


INPUT_SCHEMA = {
    "type": "object", "properties": {"hours": {"type": "integer", "minimum": 1, "maximum": 24}},
    "required": ["hours"], "additionalProperties": False,
}
ADDITIONAL_INPUT_SCHEMA = {
    "type": "object", "properties": {"resort_area": {"type": "string", "enum": ["valley", "summit"]}},
    "required": ["resort_area"], "additionalProperties": False,
}


def call(name, arguments, call_id=None):
    return Message("assistant", [
        Content.from_function_call(call_id or name, name, arguments=arguments)
    ])


class ScriptedClient(FunctionInvocationLayer, ChatMiddlewareLayer, BaseChatClient):
    """Replace the model only; MAF performs actual native tool invocation."""
    def __init__(self, steps):
        super().__init__()
        self.steps = list(steps)
        self.requests = []

    def _inner_get_response(self, *, messages, stream, options, **kwargs):
        self.requests.append((list(messages), dict(options)))
        if not self.steps:
            raise AssertionError("Unexpected model call")
        step = self.steps.pop(0)
        if callable(step):
            step = step(messages, options)

        async def response():
            await asyncio.sleep(0)
            return ChatResponse(messages=[step])

        async def updates():
            await asyncio.sleep(0)
            yield ChatResponseUpdate(role="assistant", contents=step.contents)

        return ResponseStream(updates(), finalizer=ChatResponse.from_updates) if stream else response()


class FakeMCP:
    """Actual SDK ClientSession with its transport replaced by a local fixture."""
    def __init__(self, skill="weather", prefix="weather", operation="weather_forecast"):
        self.skill, self.prefix, self.operation = skill, prefix, operation
        self.reads, self.calls, self.cursors = [], [], []
        self.session = ClientSession(None, None)
        self.session._request_id = 1
        self.session._server_capabilities = ServerCapabilities(
            tools=ToolsCapability(), resources=ResourcesCapability(),
        )
        self.session.send_request = AsyncMock(side_effect=self.send)
        self.result = CallToolResult(
            content=[TextContent(type="text", text='{"forecast_hours":2}')],
            structuredContent={"forecast_hours": 2},
        )
        self.pages = {None: ListToolsResult(tools=[
            Tool(name=operation, description="Full authoritative forecast schema",
                 inputSchema=INPUT_SCHEMA, outputSchema={"type": "object"}),
            Tool(name="hidden_operation", description="Additional provider-advertised read-only operation",
                 inputSchema=ADDITIONAL_INPUT_SCHEMA),
        ])}
        self.connection = SkillConnection(
            SkillProviderConfig(prefix, prefix),
            f"http://configured-{prefix}.invalid/skillsmcp", self.session,
        )

    async def send(self, request, result_type, **kwargs):
        request = request.root
        if request.method == "ping":
            return EmptyResult()
        if request.method == "tools/list":
            cursor = request.params.cursor if request.params else None
            self.cursors.append(cursor)
            return self.pages[cursor]
        if request.method == "resources/read":
            uri = str(request.params.uri)
            self.reads.append(uri)
            if uri == "skill://index.json":
                text = json.dumps({"skills": [{
                    "name": self.skill, "description": f"{self.skill} domain",
                    "type": "skill-md", "url": f"skill://{self.skill}/SKILL.md",
                }]})
            elif uri == f"skill://{self.skill}/SKILL.md":
                text = (
                    f"---\nname: {self.skill}\ndescription: {self.skill} domain\n---\n"
                    f'Use {self.prefix}_load_tool with {{"tool":"{self.operation}"}}; '
                    f"then call {self.prefix}_{self.operation} on the next iteration."
                )
            else:
                raise ValueError("Only instruction resources exist")
            return ReadResourceResult(contents=[TextResourceContents(uri=uri, text=text)])
        if request.method == "tools/call":
            self.calls.append((request.params.name, request.params.arguments))
            if isinstance(self.result, BaseException):
                raise self.result
            return self.result
        raise AssertionError(f"Unexpected MCP method: {request.method}")


def make_agent(connections, steps):
    sources = [MCPSkillsSource(client=connection.session) for connection in connections]
    skills = SkillsProvider(sources[0] if len(sources) == 1 else AggregatingSkillsSource(sources))
    client = ScriptedClient(steps)
    agent = Agent(
        client, instructions=INSTRUCTIONS, context_providers=[skills],
        middleware=[
            ToolApprovalMiddleware(auto_approval_rules=[SkillsProvider.read_only_tools_auto_approval_rule]),
            NativeMCPToolsMiddleware(connections),
        ],
    )
    return agent, client


def native_steps(fixture):
    return [
        call("load_skill", {"skill_name": fixture.skill}),
        call(f"{fixture.prefix}_load_tool", {"tool": fixture.operation}),
        call(f"{fixture.prefix}_{fixture.operation}", {"hours": 2}),
        Message("assistant", ["done"]),
    ]


class NativeLoopTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.weather = FakeMCP()
        self.safety = FakeMCP("safety", "safety", "safety_risk")
        self.connections = [self.weather.connection, self.safety.connection]

    async def test_native_skill_load_then_tool_load_then_direct_call(self):
        def initial(messages, options):
            text = json.dumps([m.to_dict() for m in messages]) + str(options.get("instructions"))
            for operation in ("weather_forecast", "safety_risk", "hidden_operation"):
                self.assertNotIn(operation, text)
                self.assertNotIn(operation, json.dumps([t.parameters() for t in options["tools"]]))
            names = {t.name for t in options["tools"]}
            self.assertIn("weather_load_tool", names)
            self.assertIn("safety_load_tool", names)
            self.assertNotIn("weather_weather_forecast", names)
            self.assertIn("load_skill", names)
            return call("load_skill", {"skill_name": "weather"})

        def skill_loaded(messages, options):
            text = json.dumps([m.to_dict() for m in messages])
            self.assertIn("weather_forecast", text)
            self.assertNotIn("safety_risk", text)
            self.assertNotIn("weather_weather_forecast", {t.name for t in options["tools"]})
            return call("weather_load_tool", {"tool": "weather_forecast"})

        def operation_loaded(messages, options):
            tools = {t.name: t for t in options["tools"]}
            self.assertIn("weather_weather_forecast", tools)
            self.assertNotIn("safety_safety_risk", tools)
            self.assertNotIn("weather_hidden_operation", tools)
            self.assertEqual(tools["weather_weather_forecast"].parameters(), INPUT_SCHEMA)
            return call("weather_weather_forecast", {"hours": 2})

        agent, _ = make_agent(self.connections, [
            initial, skill_loaded, operation_loaded, Message("assistant", ["done"]),
        ])
        response = await agent.run("forecast", session=agent.create_session())
        self.assertEqual(response.text, "done")
        self.assertFalse(any(c.type == "function_approval_request"
                             for m in response.messages for c in m.contents))
        self.assertEqual(self.weather.calls, [("weather_forecast", {"hours": 2})])
        self.assertEqual(self.safety.calls, [])
        self.assertTrue(all(uri in {"skill://index.json", "skill://weather/SKILL.md"}
                            for uri in self.weather.reads))

    async def test_native_streaming_loop(self):
        agent, _ = make_agent(self.connections, native_steps(self.weather))
        updates = [u async for u in agent.run("forecast", session=agent.create_session(), stream=True)]
        self.assertTrue(any(u.text == "done" for u in updates))
        self.assertFalse(any(c.type == "function_approval_request" for u in updates for c in u.contents))
        self.assertEqual(len(self.weather.calls), 1)

    async def test_followup_and_restored_session_reload_native_tools(self):
        agent, client = make_agent(self.connections, native_steps(self.weather))
        session = agent.create_session()
        await agent.run("first", session=session)
        for restore in (False, True):
            if restore:
                session = AgentSession.from_dict(session.to_dict())

            def fresh(messages, options):
                self.assertNotIn("weather_weather_forecast", {t.name for t in options["tools"]})
                return call("load_skill", {"skill_name": "weather"}, "reload")

            client.steps = [fresh, *native_steps(self.weather)[1:]]
            self.assertEqual((await agent.run("followup", session=session)).text, "done")
        self.assertEqual(len(self.weather.calls), 3)

    async def test_parallel_users_do_not_share_native_loaded_tools(self):
        loaded = asyncio.Event()
        observed = asyncio.Event()
        counts = {"a": 0, "b": 0}
        test = self

        class ConcurrentClient(FunctionInvocationLayer, ChatMiddlewareLayer, BaseChatClient):
            def _inner_get_response(self, *, messages, stream, options, **kwargs):
                async def response():
                    user = next(m.text for m in reversed(messages) if m.role == "user" and m.text)
                    step = counts[user]
                    counts[user] += 1
                    if user == "a":
                        if step == 0:
                            message = call("weather_load_tool", {"tool": "weather_forecast"})
                        elif step == 1:
                            test.assertIn("weather_weather_forecast", {t.name for t in options["tools"]})
                            loaded.set()
                            await observed.wait()
                            message = call("weather_weather_forecast", {"hours": 2})
                        else:
                            message = Message("assistant", ["a done"])
                    else:
                        await loaded.wait()
                        test.assertNotIn("weather_weather_forecast", {t.name for t in options["tools"]})
                        observed.set()
                        message = Message("assistant", ["b done"])
                    return ChatResponse(messages=[message])
                return response()

        agent, _ = make_agent(self.connections, [])
        agent.client = ConcurrentClient()
        responses = await asyncio.wait_for(asyncio.gather(
            agent.run("a", session=agent.create_session()),
            agent.run("b", session=agent.create_session()),
        ), timeout=10)
        self.assertEqual([r.text for r in responses], ["a done", "b done"])
        self.assertEqual(len(self.weather.calls), 1)

    async def test_parallel_native_operations_auto_invoke_without_approval(self):
        load = Message("assistant", [
            Content.from_function_call("load-w", "weather_load_tool", arguments={"tool": "weather_forecast"}),
            Content.from_function_call("load-s", "safety_load_tool", arguments={"tool": "safety_risk"}),
        ])
        operations = Message("assistant", [
            Content.from_function_call("call-w", "weather_weather_forecast", arguments={"hours": 2}),
            Content.from_function_call("call-s", "safety_safety_risk", arguments={"hours": 2}),
        ])
        agent, _ = make_agent(self.connections, [load, operations, Message("assistant", ["both done"])])
        session = agent.create_session()
        response = await agent.run("both", session=session)
        self.assertEqual(response.text, "both done")
        self.assertFalse(any(c.type == "function_approval_request"
                             for m in response.messages for c in m.contents))
        self.assertEqual(len(self.weather.calls), 1)
        self.assertEqual(len(self.safety.calls), 1)

    async def test_native_instances_close_on_success_failure_and_cancellation(self):
        from skills_orchestrator_python import native_mcp
        native_class = native_mcp.MCPStreamableHTTPTool
        created = []

        def create(**kwargs):
            tool = native_class(**kwargs)
            tool.close = AsyncMock(wraps=tool.close)
            created.append(tool)
            return tool

        for outcome in ("success", "model failure", "cancel"):
            with self.subTest(outcome=outcome):
                created.clear()
                steps = native_steps(self.weather)
                if outcome == "model failure":
                    def fail(*_args):
                        raise RuntimeError("model failure")
                    steps = [fail]
                if outcome == "cancel":
                    self.weather.result = asyncio.CancelledError()
                agent, _ = make_agent(self.connections, steps)
                with patch.object(native_mcp, "MCPStreamableHTTPTool", side_effect=create):
                    if outcome == "cancel":
                        with self.assertRaises(asyncio.CancelledError):
                            await agent.run("forecast", session=agent.create_session())
                    elif outcome == "model failure":
                        with self.assertRaisesRegex(RuntimeError, "model failure"):
                            await agent.run("forecast", session=agent.create_session())
                    else:
                        await agent.run("forecast", session=agent.create_session())
                self.assertTrue(created)
                for tool in created:
                    tool.close.assert_awaited_once()

    async def test_native_catalog_loader_unloader_and_list(self):
        def listed(messages, options):
            text = json.dumps([m.to_dict() for m in messages])
            self.assertIn("Full authoritative forecast schema", text)
            self.assertIn("hidden_operation", text)
            return call("weather_load_tool", {"tool": ["weather_forecast"]})

        def loaded(messages, options):
            self.assertIn("weather_weather_forecast", {t.name for t in options["tools"]})
            return call("weather_unload_tool", {"tool": "weather_forecast"})

        def unloaded(messages, options):
            self.assertNotIn("weather_weather_forecast", {t.name for t in options["tools"]})
            return Message("assistant", ["done"])

        agent, _ = make_agent(self.connections, [
            call("weather_list_mcp_tools", {}), listed, loaded, unloaded,
        ])
        await agent.run("catalog", session=agent.create_session())

    async def test_newly_advertised_operation_loads_and_calls_without_host_config_change(self):
        original_config = self.weather.connection.config

        def initial(messages, options):
            text = json.dumps([m.to_dict() for m in messages]) + str(options.get("instructions"))
            tool_context = json.dumps([t.to_dict() for t in options["tools"]])
            for marker in ("hidden_operation", "resort_area", "Additional provider-advertised"):
                self.assertNotIn(marker, text)
                self.assertNotIn(marker, tool_context)
            return call("weather_load_tool", {"tool": "hidden_operation"})

        def loaded(messages, options):
            tools = {t.name: t for t in options["tools"]}
            self.assertEqual(tools["weather_hidden_operation"].parameters(), ADDITIONAL_INPUT_SCHEMA)
            self.assertNotIn("weather_weather_forecast", tools)
            self.assertNotIn("safety_hidden_operation", tools)
            return call("weather_hidden_operation", {"resort_area": "summit"})

        def done(messages, options):
            result = [c for m in messages for c in m.contents if c.type == "function_result"][-1]
            self.assertIsNone(result.exception)
            return Message("assistant", ["done"])

        agent, _ = make_agent(self.connections, [initial, loaded, done])
        self.assertEqual((await agent.run("additional operation", session=agent.create_session())).text, "done")
        self.assertIs(self.weather.connection.config, original_config)
        self.assertEqual(self.weather.calls, [("hidden_operation", {"resort_area": "summit"})])
        self.assertEqual(self.safety.calls, [])

    async def test_native_rejects_loading_unknown_tool(self):
        def rejected(messages, options):
            self.assertNotIn("weather_unknown_operation", {t.name for t in options["tools"]})
            self.assertIn("not available", json.dumps([m.to_dict() for m in messages]).lower())
            return Message("assistant", ["denied"])
        agent, _ = make_agent(self.connections, [
            call("weather_load_tool", {"tool": "unknown_operation"}), rejected,
        ])
        await agent.run("load", session=agent.create_session())
        self.assertEqual(self.weather.calls, [])

    async def test_native_errors_and_cancellation(self):
        for result in (
            CallToolResult(content=[TextContent(type="text", text="upstream unavailable")], isError=True),
            RuntimeError("transport failed"),
        ):
            self.weather.result = result
            agent, client = make_agent(self.connections, native_steps(self.weather))
            await agent.run("forecast", session=agent.create_session())
            text = json.dumps([m.to_dict() for m in client.requests[-1][0]])
            self.assertIn("exception", text)
        self.weather.result = asyncio.CancelledError()
        agent, _ = make_agent(self.connections, native_steps(self.weather))
        with self.assertRaises(asyncio.CancelledError):
            await agent.run("forecast", session=agent.create_session())

    async def test_native_pagination_and_prefix_routing(self):
        weather = self.weather
        first, second = weather.pages[None].tools
        weather.pages = {None: ListToolsResult(tools=[second], nextCursor="second"),
                         "second": ListToolsResult(tools=[first])}
        collision = FakeMCP("other", "other", "weather_forecast")
        agent, _ = make_agent([weather.connection, collision.connection], [
            call("other_load_tool", {"tool": "weather_forecast"}),
            call("other_weather_forecast", {"hours": 2}), Message("assistant", ["done"]),
        ])
        await agent.run("other", session=agent.create_session())
        self.assertEqual(weather.cursors, [None, "second"])
        self.assertEqual(weather.calls, [])
        self.assertEqual(collision.calls, [("weather_forecast", {"hours": 2})])


class SharedBuilderTests(unittest.IsolatedAsyncioTestCase):
    async def test_builder_keeps_native_skills_researcher_and_history(self):
        from skills_orchestrator_python import agent_builder as builder
        fixture = FakeMCP()
        captured = {}

        async def versions(**kwargs):
            yield SimpleNamespace(version="7")

        project = SimpleNamespace(agents=SimpleNamespace(list_versions=versions))
        research_tool, history = object(), object()
        researcher = SimpleNamespace(as_tool=lambda **kwargs: research_tool)

        class Stack:
            async def enter_async_context(self, value):
                return value

        class Client:
            def as_agent(self, **kwargs):
                captured.update(kwargs)
                return SimpleNamespace()

        with (
            patch.dict(os.environ, {builder.SKI_RESEARCHER_AGENT_NAME_ENV: "researcher",
                                    builder.SKI_RESEARCHER_PROJECT_ENDPOINT_ENV: "https://configured.example"}),
            patch.object(builder, "_discover_providers", AsyncMock(return_value=([fixture.connection], ["weather"], []))),
            patch.object(builder, "_build_history_provider", AsyncMock(return_value=(history, "cosmos"))),
            patch.object(builder, "get_foundry_config", return_value=("endpoint", "model")),
            patch.object(builder, "AsyncDefaultAzureCredential", return_value=object()),
            patch.object(builder, "DefaultAzureCredential", return_value=object()),
            patch.object(builder, "AIProjectClient", return_value=project),
            patch.object(builder, "FoundryAgent", return_value=researcher),
            patch.object(builder, "FoundryChatClient", return_value=Client()),
        ):
            built = await builder.build_orchestrator_agent(exit_stack=Stack(), enter_agent_context=False)
        self.assertEqual(captured["tools"], [research_tool])
        self.assertIs(type(captured["context_providers"][0]), SkillsProvider)
        self.assertIs(captured["context_providers"][-1], history)
        self.assertEqual(captured["default_options"], {"store": False})
        self.assertEqual(built.history_backend, "cosmos")
        self.assertIsInstance(captured["middleware"][0], ToolApprovalMiddleware)
        self.assertIsInstance(captured["middleware"][1], NativeMCPToolsMiddleware)


if __name__ == "__main__":
    unittest.main()
