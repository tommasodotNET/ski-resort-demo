"""Offline tests of the real MAF skill selection and native invocation loop."""
from __future__ import annotations

import asyncio
from contextlib import AsyncExitStack
import json
import os
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, patch

from agent_framework import (
    Agent, AgentSession, BaseChatClient, ChatMiddlewareLayer,
    ChatResponse, ChatResponseUpdate, Content, FunctionInvocationLayer,
    FunctionInvocationContext, Message, ResponseStream, SkillsProvider,
    tool,
)
from mcp import ClientSession
from mcp.types import (
    CallToolResult, EmptyResult, ListToolsResult, ReadResourceResult, ResourcesCapability,
    ServerCapabilities, TextContent, TextResourceContents, Tool, ToolsCapability,
)

from skills_orchestrator_python.agent_builder import INSTRUCTIONS
from skills_orchestrator_python.config import SkillProviderConfig
from skills_orchestrator_python.native_mcp import SkillToolsMiddleware, SkillConnection


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
        self.skill_error = None
        self.skill_text = (
            f"---\nname: {skill}\ndescription: {skill} domain\n---\n"
            "After successful load_skill all provider tools are registered for the next "
            "model iteration. Choose according to descriptions and parameter schemas."
        )
        self.session = ClientSession(None, None)
        # Fake only the SDK transport/initialization, never the native MAF wrappers.
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
            Tool(name=f"{prefix}_current_conditions", description="Current resort conditions",
                 inputSchema={"type": "object", "properties": {}}),
            Tool(name=f"{prefix}_storm_status", description="Authoritative storm status",
                 inputSchema={"type": "object", "properties": {}}),
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
                if self.skill_error:
                    raise self.skill_error
                text = self.skill_text
            else:
                raise ValueError("Only instruction resources exist")
            return ReadResourceResult(contents=[TextResourceContents(uri=uri, text=text)])
        if request.method == "tools/call":
            self.calls.append((request.params.name, request.params.arguments))
            if isinstance(self.result, BaseException):
                raise self.result
            return self.result
        raise AssertionError(f"Unexpected MCP method: {request.method}")


@tool
async def ski_researcher_agent(query: str) -> str:
    """Research general skiing."""
    return query


async def make_agent(connections, steps, stack, *, auto_approve=True):
    registration = SkillToolsMiddleware(connections)
    client = ScriptedClient(steps)
    middleware = [registration]
    agent = Agent(
        client, instructions=INSTRUCTIONS, tools=[ski_researcher_agent],
        context_providers=[SkillsProvider(
            registration.source,
            disable_load_skill_approval=auto_approve,
            disable_read_skill_resource_approval=auto_approve,
        )], middleware=middleware,
    )
    await registration.initialize(agent, stack)
    return agent, client


def native_steps(fixture):
    return [
        call("load_skill", {"skill_name": fixture.skill}),
        call(f"{fixture.prefix}_{fixture.operation}", {"hours": 2}),
        Message("assistant", ["done"]),
    ]


def names(options):
    return {t.name for t in options["tools"]}


class NativeLoopTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.stack = await self.enterAsyncContext(AsyncExitStack())
        self.weather = FakeMCP()
        self.safety = FakeMCP("safety", "safety", "safety_risk")
        self.connections = [self.weather.connection, self.safety.connection]

    def assert_no_helpers(self, options):
        for name in names(options):
            self.assertFalse(name.endswith(("_load_tool", "_unload_tool", "_list_mcp_tools")), name)

    def assert_catalog(self, options, fixture):
        exposed = {t.name: t for t in options["tools"]}
        for page in fixture.pages.values():
            for advertised in page.tools:
                native = exposed[f"{fixture.prefix}_{advertised.name}"]
                self.assertEqual(native.parameters(), advertised.inputSchema)
                self.assertEqual(native.description, advertised.description)
                self.assertEqual(native.approval_mode, "never_require")
        self.assert_no_helpers(options)

    async def test_successful_skill_load_exposes_all_three_native_schemas_then_direct_call(self):
        def initial(messages, options):
            text = json.dumps([m.to_dict() for m in messages]) + str(options.get("instructions"))
            schemas = json.dumps([t.to_dict() for t in options["tools"]])
            for fixture in (self.weather, self.safety):
                for advertised in fixture.pages[None].tools:
                    self.assertNotIn(advertised.name, text)
                    self.assertNotIn(advertised.name, schemas)
                    self.assertNotIn(advertised.description, schemas)
            self.assertEqual(names(options), {
                "load_skill", "read_skill_resource", "run_skill_script", "ski_researcher_agent",
            })
            self.assertNotIn('"hours"', schemas)
            self.assert_no_helpers(options)
            return call("load_skill", {"skill_name": "weather"})

        def loaded(messages, options):
            self.assert_catalog(options, self.weather)
            self.assertFalse(any(n.startswith("safety_") for n in names(options)))
            return call("weather_weather_forecast", {"hours": 2})

        agent, _ = await make_agent(self.connections, [
            initial, loaded, Message("assistant", ["done"]),
        ], self.stack)
        response = await agent.run("forecast", session=agent.create_session())
        self.assertEqual(response.text, "done")
        self.assertFalse(any(c.type == "function_approval_request"
                             for m in response.messages for c in m.contents))
        self.assertEqual(self.weather.calls, [("weather_forecast", {"hours": 2})])
        self.assertEqual(self.safety.calls, [])
        self.assertEqual(self.weather.reads, ["skill://index.json", "skill://weather/SKILL.md"])
        self.assertEqual(self.safety.reads, ["skill://index.json"])

    async def test_native_streaming_loop(self):
        agent, _ = await make_agent(self.connections, native_steps(self.weather), self.stack)
        updates = [u async for u in agent.run("forecast", session=agent.create_session(), stream=True)]
        self.assertTrue(any(u.text == "done" for u in updates))
        self.assertFalse(any(c.type == "function_approval_request" for u in updates for c in u.contents))
        self.assertEqual(len(self.weather.calls), 1)

    async def test_followup_and_restored_history_start_hidden_then_reload(self):
        agent, client = await make_agent(self.connections, native_steps(self.weather), self.stack)
        session = agent.create_session()
        await agent.run("first", session=session)
        for restore in (False, True):
            if restore:
                session = AgentSession.from_dict(session.to_dict())

            def fresh(messages, options):
                self.assertNotIn("weather_weather_forecast", names(options))
                self.assert_no_helpers(options)
                return call("load_skill", {"skill_name": "weather"}, "reload")

            client.steps = [fresh, *native_steps(self.weather)[1:]]
            self.assertEqual((await agent.run("followup", session=session)).text, "done")
        self.assertEqual(len(self.weather.calls), 3)
        self.assertEqual(self.weather.reads.count("skill://weather/SKILL.md"), 1)

    async def test_parallel_users_do_not_share_registrations(self):
        loaded, observed = asyncio.Event(), asyncio.Event()
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
                            message = call("load_skill", {"skill_name": "weather"})
                        elif step == 1:
                            test.assert_catalog(options, test.weather)
                            test.assertFalse(any(n.startswith("safety_") for n in names(options)))
                            loaded.set()
                            await observed.wait()
                            message = call("weather_weather_forecast", {"hours": 2})
                        else:
                            message = Message("assistant", ["a done"])
                    elif step == 0:
                        await loaded.wait()
                        test.assertNotIn("weather_weather_forecast", names(options))
                        message = call("load_skill", {"skill_name": "safety"})
                    elif step == 1:
                        test.assert_catalog(options, test.safety)
                        test.assertNotIn("weather_weather_forecast", names(options))
                        observed.set()
                        message = call("safety_safety_risk", {"hours": 2})
                    else:
                        message = Message("assistant", ["b done"])
                    return ChatResponse(messages=[message])
                return response()

        agent, _ = await make_agent(self.connections, [], self.stack)
        agent.client = ConcurrentClient()
        responses = await asyncio.wait_for(asyncio.gather(
            agent.run("a", session=agent.create_session()),
            agent.run("b", session=agent.create_session()),
        ), timeout=10)
        self.assertEqual([r.text for r in responses], ["a done", "b done"])
        self.assertEqual(len(self.weather.calls), 1)
        self.assertEqual(len(self.safety.calls), 1)

    async def test_parallel_skill_groups_and_repeated_load_are_idempotent(self):
        load = Message("assistant", [
            Content.from_function_call("w1", "load_skill", arguments={"skill_name": "weather"}),
            Content.from_function_call("s1", "load_skill", arguments={"skill_name": "safety"}),
            Content.from_function_call("w2", "load_skill", arguments={"skill_name": "WEATHER"}),
        ])

        def loaded(messages, options):
            self.assert_catalog(options, self.weather)
            self.assert_catalog(options, self.safety)
            self.assertEqual(len(names(options)), len(options["tools"]))
            return call("load_skill", {"skill_name": "weather"})

        operations = Message("assistant", [
            Content.from_function_call("call-w", "weather_weather_forecast", arguments={"hours": 2}),
            Content.from_function_call("call-s", "safety_safety_risk", arguments={"hours": 2}),
        ])
        agent, _ = await make_agent(self.connections, [
            load, loaded, operations, Message("assistant", ["both done"]),
        ], self.stack)
        response = await agent.run("both", session=agent.create_session())
        self.assertEqual(response.text, "both done")
        self.assertFalse(any(c.type == "function_approval_request"
                             for m in response.messages for c in m.contents))
        self.assertEqual(len(self.weather.calls), 1)
        self.assertEqual(len(self.safety.calls), 1)

    async def test_new_advertised_operation_included_without_configuration_change(self):
        self.weather.pages[None].tools.append(Tool(
            name="fresh_operation", description="New advertised operation",
            inputSchema=ADDITIONAL_INPUT_SCHEMA,
        ))

        def loaded(messages, options):
            self.assert_catalog(options, self.weather)
            self.assertNotIn("safety_fresh_operation", names(options))
            return call("weather_fresh_operation", {"resort_area": "summit"})

        agent, _ = await make_agent(self.connections, [
            call("load_skill", {"skill_name": "weather"}), loaded, Message("assistant", ["done"]),
        ], self.stack)
        await agent.run("new operation", session=agent.create_session())
        self.assertEqual(self.weather.calls, [("fresh_operation", {"resort_area": "summit"})])

    async def test_sequential_skill_loads_retain_both_groups_including_streaming(self):
        for stream in (False, True):
            with self.subTest(stream=stream):
                def weather_loaded(messages, options):
                    self.assert_catalog(options, self.weather)
                    self.assertNotIn("safety_safety_risk", names(options))
                    return call("load_skill", {"skill_name": "safety"}, "load-safety")

                def both_loaded(messages, options):
                    self.assert_catalog(options, self.weather)
                    self.assert_catalog(options, self.safety)
                    return Message("assistant", ["both visible"])

                agent, _ = await make_agent(self.connections, [
                    call("load_skill", {"skill_name": "weather"}),
                    weather_loaded, both_loaded,
                ], self.stack)
                response = agent.run("both", session=agent.create_session(), stream=stream)
                if stream:
                    updates = [update async for update in response]
                    self.assertFalse(any(c.type == "function_approval_request"
                                         for update in updates for c in update.contents))
                    self.assertEqual((await response.get_final_response()).text, "both visible")
                else:
                    self.assertEqual((await response).text, "both visible")

    async def test_unknown_empty_and_failed_skill_loads_do_not_register(self):
        for name, error in (("unknown", None), ("", None), ("weather", RuntimeError("read failed")),
                            ("weather", None)):
            with self.subTest(name=name, error=error):
                fixture = FakeMCP()
                fixture.skill_error = error
                if name == "weather" and error is None:
                    fixture.skill_text = ""  # Native MCPSkill rejects empty/non-text content.

                def rejected(messages, options):
                    self.assertNotIn("weather_weather_forecast", names(options))
                    text = json.dumps([m.to_dict() for m in messages])
                    self.assertTrue("Error:" in text or "exception" in text)
                    return Message("assistant", ["failed"])

                agent, _ = await make_agent([fixture.connection], [
                    call("load_skill", {"skill_name": name}), rejected,
                ], self.stack)
                self.assertEqual((await agent.run("load", session=agent.create_session())).text, "failed")
                self.assertEqual(fixture.calls, [])

    async def test_pending_native_skill_approval_does_not_register(self):
        agent, client = await make_agent(self.connections, [
            call("load_skill", {"skill_name": "weather"}),
        ], self.stack, auto_approve=False)
        response = await agent.run("load", session=agent.create_session())
        self.assertTrue(any(c.type == "function_approval_request"
                            for m in response.messages for c in m.contents))
        self.assertNotIn("weather_weather_forecast", names(client.requests[-1][1]))
        self.assertNotIn("skill://weather/SKILL.md", self.weather.reads)

    async def test_known_name_with_non_success_result_does_not_register(self):
        registration = SkillToolsMiddleware(self.connections)
        await registration.initialize(SimpleNamespace(), self.stack)
        for result in ([Content.from_text("Error: read failed")], "not native content", None):
            async def next_call():
                context.result = result
            context = FunctionInvocationContext(
                function=SimpleNamespace(name="load_skill"), arguments={"skill_name": "weather"},
                tools=[],
            )
            await registration.process(context, next_call)
            self.assertIs(context.result, result)
            self.assertEqual(context.tools, [])

    async def test_cancelled_skill_read_does_not_register_or_poison_next_run(self):
        self.weather.skill_error = asyncio.CancelledError()
        agent, client = await make_agent(self.connections, [
            call("load_skill", {"skill_name": "weather"}),
        ], self.stack)
        with self.assertRaises(asyncio.CancelledError):
            await agent.run("load", session=agent.create_session())
        self.assertEqual(self.weather.calls, [])
        self.weather.skill_error = None

        def fresh(messages, options):
            self.assertNotIn("weather_weather_forecast", names(options))
            return call("load_skill", {"skill_name": "weather"})

        client.steps = [fresh, *native_steps(self.weather)[1:]]
        self.assertEqual((await agent.run("retry", session=agent.create_session())).text, "done")
        self.assertEqual(len(self.weather.calls), 1)

    async def test_native_pagination_and_different_skill_prefix_mapping(self):
        weather = self.weather
        first, *rest = weather.pages[None].tools
        weather.pages = {None: ListToolsResult(tools=rest, nextCursor="second"),
                         "second": ListToolsResult(tools=[first])}
        for skill, prefix in (("ski-coach", "skicoach"), ("lift-traffic", "lifttraffic")):
            fixture = FakeMCP(skill, prefix, "weather_forecast")
            agent, _ = await make_agent([weather.connection, fixture.connection], [
                call("load_skill", {"skill_name": skill}),
                call(f"{prefix}_weather_forecast", {"hours": 2}), Message("assistant", ["done"]),
            ], self.stack)
            await agent.run("other", session=agent.create_session())
            self.assertEqual(fixture.calls, [("weather_forecast", {"hours": 2})])
        self.assertEqual(weather.cursors, [None, "second", None, "second"])
        self.assertEqual(weather.calls, [])

    async def test_ambiguous_skill_identity_rejected(self):
        duplicate = FakeMCP("weather", "other")
        with self.assertRaisesRegex(ValueError, "Ambiguous.*weather"):
            await make_agent([self.weather.connection, duplicate.connection], [], self.stack)

    async def test_native_errors_and_cancellation(self):
        for result in (
            CallToolResult(content=[TextContent(type="text", text="upstream unavailable")], isError=True),
            RuntimeError("transport failed"),
        ):
            self.weather.result = result
            agent, client = await make_agent(self.connections, native_steps(self.weather), self.stack)
            await agent.run("forecast", session=agent.create_session())
            text = json.dumps([m.to_dict() for m in client.requests[-1][0]])
            self.assertIn("exception", text)
        self.weather.result = asyncio.CancelledError()
        agent, _ = await make_agent(self.connections, native_steps(self.weather), self.stack)
        with self.assertRaises(asyncio.CancelledError):
            await agent.run("forecast", session=agent.create_session())

    async def test_shared_wrapper_lifetime_success_failure_cancellation_and_abandoned_stream(self):
        from skills_orchestrator_python import native_mcp
        native_class = native_mcp.MCPStreamableHTTPTool
        created = []

        def create(**kwargs):
            native = native_class(**kwargs)
            native.close = AsyncMock(wraps=native.close)
            created.append(native)
            return native

        for outcome in ("success", "model failure", "cancel", "stream close", "stream cancel"):
            with self.subTest(outcome=outcome):
                created.clear()
                fixture = FakeMCP()
                with patch.object(native_mcp, "MCPStreamableHTTPTool", side_effect=create):
                    async with AsyncExitStack() as stack:
                        steps = native_steps(fixture)
                        if outcome == "model failure":
                            def fail(*_args):
                                raise RuntimeError("model failure")
                            steps = [fail]
                        if outcome in ("cancel", "stream cancel"):
                            fixture.result = asyncio.CancelledError()
                        agent, client = await make_agent([fixture.connection], steps, stack)
                        if outcome == "cancel":
                            with self.assertRaises(asyncio.CancelledError):
                                await agent.run("forecast", session=agent.create_session())
                        elif outcome == "model failure":
                            with self.assertRaisesRegex(RuntimeError, "model failure"):
                                await agent.run("forecast", session=agent.create_session())
                        elif outcome.startswith("stream"):
                            response = agent.run("forecast", session=agent.create_session(), stream=True)
                            if outcome == "stream cancel":
                                with self.assertRaises(asyncio.CancelledError):
                                    async for _ in response:
                                        pass
                            else:
                                async for _ in response:
                                    if len(client.requests) >= 2:
                                        break
                            # MAF ResponseStream has no public aclose API. Abandoning
                            # a pull stream holds no per-run MCP resource to close.
                        else:
                            await agent.run("forecast", session=agent.create_session())
                        created[0].close.assert_not_awaited()
                        # Neither an early stream close nor an error leaks schemas to another run.
                        def fresh(messages, options):
                            self.assertNotIn("weather_weather_forecast", names(options))
                            return Message("assistant", ["fresh"])
                        client.steps = [fresh]
                        self.assertEqual((await agent.run("followup", session=agent.create_session())).text, "fresh")
                    created[0].close.assert_awaited_once()


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
        self.assertEqual(len(captured["middleware"]), 1)
        self.assertIsInstance(captured["middleware"][0], SkillToolsMiddleware)
        self.assertIn("weather", captured["middleware"][0].catalogs)


if __name__ == "__main__":
    unittest.main()
