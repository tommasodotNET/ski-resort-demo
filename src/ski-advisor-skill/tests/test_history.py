"""Cosmos storage stays complete while the native loop receives clean history."""
from __future__ import annotations

from contextlib import AsyncExitStack
from copy import deepcopy
import unittest
from unittest.mock import AsyncMock, MagicMock

from agent_framework import AgentSession, Content, Message, SessionContext
from azure.cosmos.aio import ContainerProxy

from skills_orchestrator_python.history import ConversationCosmosHistoryProvider
from test_native_mcp import FakeMCP, call, make_agent, native_steps


class HistoryTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.documents = []
        self.container = MagicMock(spec=ContainerProxy)

        async def query(*, query, parameters, partition_key):
            source = next(p["value"] for p in parameters if p["name"] == "@source_id")
            for document in sorted(self.documents, key=lambda d: d["sort_key"]):
                if document["session_id"] == partition_key and document["source_id"] == source:
                    yield {"message": deepcopy(document["message"])}

        async def save(*, batch_operations, partition_key):
            for operation, (document,) in batch_operations:
                self.assertEqual(operation, "upsert")
                self.assertEqual(document["session_id"], partition_key)
                self.documents.append(deepcopy(document))

        self.container.query_items.side_effect = query
        self.container.execute_item_batch = AsyncMock(side_effect=save)

    def provider(self):
        return ConversationCosmosHistoryProvider(
            "skiadvisorskill", container_client=self.container
        )

    async def test_load_filters_pairs_without_mutating_storage_or_current_input(self):
        provider = self.provider()
        session = AgentSession()
        mixed = Message(
            "assistant",
            [Content.from_text("Checking the weather."), *call("weather", {}).contents],
            message_id="mixed",
            author_name="advisor",
            additional_properties={"marker": "preserved"},
        )
        stored = [
            Message("user", ["My locker is 42. What is the weather?"]),
            call("load_skill", {"skill_name": "weather"}),
            Message("tool", [Content.from_function_result("load_skill", result="instructions")]),
            mixed,
            Message("tool", [Content.from_function_result("weather", result="old reading")]),
            Message("assistant", ["The wind was 20 km/h."]),
        ]
        await provider.save_messages(session.session_id, stored)
        original_documents = deepcopy(self.documents)
        current_input = Message(
            "tool", [Content.from_function_result("current", result="fresh reading")]
        )
        context = SessionContext(
            session_id=session.session_id, input_messages=[current_input],
            context_messages={"other-provider": [Message("system", ["Keep me."])]},
        )
        await provider.before_run(agent=MagicMock(), session=session, context=context, state={})
        replay = context.context_messages[provider.source_id]
        self.assertEqual([m.text for m in replay], [
            "My locker is 42. What is the weather?", "Checking the weather.",
            "The wind was 20 km/h.",
        ])
        self.assertEqual(replay[1].message_id, "mixed")
        self.assertEqual(replay[1].author_name, "advisor")
        self.assertEqual(replay[1].additional_properties["marker"], "preserved")
        self.assertIs(context.input_messages[0], current_input)
        self.assertEqual(context.context_messages["other-provider"][0].text, "Keep me.")
        self.assertEqual(self.documents, original_documents)
        self.assertEqual(
            [m.to_dict() for m in await provider.get_messages(session.session_id)],
            [m.to_dict() for m in stored],
        )
        other = SessionContext(session_id="another-user", input_messages=[])
        await provider.before_run(agent=MagicMock(), session=session, context=other, state={})
        self.assertEqual(other.context_messages[provider.source_id], [])

    async def test_native_turns_restore_clean_history_and_keep_current_tool_results(self):
        for stream in (False, True):
            with self.subTest(stream=stream):
                async with AsyncExitStack() as stack:
                    weather = FakeMCP()
                    provider = self.provider()
                    agent, _ = await make_agent(
                        [weather.connection], native_steps(weather), stack,
                        history_provider=provider,
                    )
                    session = agent.create_session()
                    await agent.run("My locker is 42. Forecast?", session=session)
                    stored = await provider.get_messages(session.session_id)
                    self.assertTrue(any(m.role == "tool" for m in stored))
                    session = AgentSession.from_dict(session.to_dict())

                    def first(messages, options):
                        self.assertTrue(any("locker is 42" in m.text for m in messages))
                        self.assertTrue(any(m.text == "done" for m in messages))
                        self.assertFalse(any(m.role == "tool" for m in messages))
                        self.assertFalse(any(
                            c.type in {"function_call", "function_result"}
                            for m in messages for c in m.contents
                        ))
                        return call("load_skill", {"skill_name": "weather"})

                    def after_load(messages, options):
                        self.assertTrue(any(
                            c.type == "function_result" and c.call_id == "load_skill"
                            for m in messages for c in m.contents
                        ))
                        return call("weather_weather_forecast", {"hours": 2})

                    def after_operation(messages, options):
                        self.assertTrue(any(
                            c.type == "function_result" and c.call_id == "weather_weather_forecast"
                            for m in messages for c in m.contents
                        ))
                        return Message("assistant", ["fresh answer"])

                    # Rebuild both agent and provider, as after a process restart.
                    agent, _ = await make_agent(
                        [weather.connection], [first, after_load, after_operation], stack,
                        history_provider=self.provider(),
                    )
                    if stream:
                        updates = [
                            u async for u in agent.run("What about now?", session=session, stream=True)
                        ]
                        self.assertTrue(any(u.text == "fresh answer" for u in updates))
                    else:
                        result = await agent.run("What about now?", session=session)
                        self.assertEqual(result.text, "fresh answer")
                    complete = await provider.get_messages(session.session_id)
                    self.assertEqual(sum(m.role == "tool" for m in complete), 4)
                    self.assertEqual(sum(m.role == "user" for m in complete), 2)
                    self.assertEqual(len(weather.calls), 2)
