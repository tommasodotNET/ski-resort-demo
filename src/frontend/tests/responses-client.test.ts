import assert from 'node:assert/strict';
import { afterEach, mock, test } from 'node:test';
import { resetClient, sendMessageStream } from '../src/lib/responses-client.ts';

afterEach(() => {
  mock.restoreAll();
  resetClient();
});

function mockResponses(payloads: object[]) {
  const respond: typeof globalThis.fetch = async () => new Response(
    payloads.map(payload => `data: ${JSON.stringify(payload)}\n\n`).join(''),
    { headers: { 'Content-Type': 'text/event-stream' } },
  );
  return mock.method(globalThis, 'fetch', respond);
}

test('skills conversations retain their ID across turns when the server does not echo it', async () => {
  const fetch = mockResponses([
    { type: 'response.created', response: { id: 'response-1' } },
    { type: 'response.output_text.delta', delta: 'Hello' },
    { type: 'response.completed', response: { id: 'response-1' } },
  ]);
  const events = await Array.fromAsync(sendMessageStream('Remember my preference', 'skill'));
  const firstRequest = JSON.parse(fetch.mock.calls[0].arguments[1]!.body as string);
  assert.match(firstRequest.conversation, /^[0-9a-f-]{36}$/);
  assert.ok(events.every(event => event.contextId === firstRequest.conversation));
  assert.equal(events.map(event => event.content ?? '').join(''), 'Hello');

  await Array.fromAsync(sendMessageStream('What is my preference?', 'skill', events[0].contextId));
  const followup = JSON.parse(fetch.mock.calls[1].arguments[1]!.body as string);
  assert.equal(followup.conversation, firstRequest.conversation);
  assert.equal(followup.input.length, 1);

  await Array.fromAsync(sendMessageStream('New conversation', 'skill'));
  const fresh = JSON.parse(fetch.mock.calls[2].arguments[1]!.body as string);
  assert.notEqual(fresh.conversation, firstRequest.conversation);
});

test('A2A keeps server-assigned conversation IDs through later stream events', async () => {
  const fetch = mockResponses([
    { type: 'response.created', response: { conversation: { id: 'a2a-conversation' } } },
    { type: 'response.output_text.delta', delta: 'Hello' },
    { type: 'response.completed' },
  ]);
  const events = await Array.fromAsync(sendMessageStream('Hello', 'a2a'));
  const request = JSON.parse(fetch.mock.calls[0].arguments[1]!.body as string);
  assert.equal(request.conversation, undefined);
  assert.ok(events.every(event => event.contextId === 'a2a-conversation'));
});

test('an existing voice conversation ID is reused by the skills chat', async () => {
  const fetch = mockResponses([{ type: 'response.output_text.delta', delta: 'Hello' }]);
  const events = await Array.fromAsync(sendMessageStream('Continue', 'skill', 'voice-conversation'));
  const request = JSON.parse(fetch.mock.calls[0].arguments[1]!.body as string);
  assert.equal(request.conversation, 'voice-conversation');
  assert.equal(events[0].contextId, 'voice-conversation');
});

test('a hosted skills response can replace the provisional ID with its server-assigned ID', async () => {
  const fetch = mockResponses([
    { type: 'response.created', response: { conversation_id: 'conv_hosted' } },
    { type: 'response.output_text.delta', delta: 'Hello' },
  ]);
  const events = await Array.fromAsync(sendMessageStream('Hello', 'skill'));
  assert.ok(events.every(event => event.contextId === 'conv_hosted'));
  await Array.fromAsync(sendMessageStream('Continue', 'skill', events[0].contextId));
  const request = JSON.parse(fetch.mock.calls[1].arguments[1]!.body as string);
  assert.equal(request.conversation, 'conv_hosted');
});

test('failed requests surface the server error', async () => {
  mock.method(globalThis, 'fetch', async () => new Response('History unavailable', { status: 503 }));
  await assert.rejects(
    Array.fromAsync(sendMessageStream('Hello', 'skill')),
    /Responses API request failed: 503 History unavailable/,
  );
});

test('hosted skills follow-ups send the latest response and session IDs, not a provisional conversation', async () => {
  const fetch = mockResponses([{
    type: 'response.completed',
    response: { id: 'response-1', agent_session_id: 'hosted-session' },
  }]);
  const events = await Array.fromAsync(sendMessageStream('Remember my code', 'skill'));
  const contextId = events[0].contextId;
  fetch.mock.mockImplementation(async () => new Response(`data: ${JSON.stringify({
    type: 'response.completed',
    response: { response_id: 'response-2', agent_session_id: 'hosted-session' },
  })}\n\n`));
  await Array.fromAsync(sendMessageStream('Recall my code', 'skill', contextId));
  const followup = JSON.parse(fetch.mock.calls[1].arguments[1]!.body as string);
  assert.equal(followup.previous_response_id, 'response-1');
  assert.equal(followup.agent_session_id, 'hosted-session');
  assert.equal(followup.conversation, undefined);

  await Array.fromAsync(sendMessageStream('Continue', 'skill', contextId));
  assert.equal(JSON.parse(fetch.mock.calls[2].arguments[1]!.body as string).previous_response_id, 'response-2');
  await Array.fromAsync(sendMessageStream('New conversation', 'skill'));
  assert.equal(JSON.parse(fetch.mock.calls[3].arguments[1]!.body as string).agent_session_id, undefined);
  resetClient();
  await Array.fromAsync(sendMessageStream('Reset', 'skill', contextId));
  assert.equal(JSON.parse(fetch.mock.calls[4].arguments[1]!.body as string).agent_session_id, undefined);
});

test('local skills keep conversation IDs even when response and agent-session IDs are present', async () => {
  const fetch = mockResponses([
    { type: 'response.created', response: { conversation: { id: 'local-conversation' } } },
    { type: 'response.completed', response: { id: 'response-1', agent_session_id: 'local-session' } },
  ]);
  await Array.fromAsync(sendMessageStream('Hello', 'skill'));
  await Array.fromAsync(sendMessageStream('Continue', 'skill', 'local-conversation'));
  const followup = JSON.parse(fetch.mock.calls[1].arguments[1]!.body as string);
  assert.equal(followup.conversation, 'local-conversation');
  assert.equal(followup.previous_response_id, undefined);
  assert.equal(followup.agent_session_id, undefined);
});

test('a failed hosted turn does not replace the last successful continuation', async () => {
  const fetch = mockResponses([{
    type: 'response.completed',
    response: { id: 'successful', agent_session_id: 'session' },
  }]);
  const events = await Array.fromAsync(sendMessageStream('Hello', 'skill'));
  const contextId = events[0].contextId;
  fetch.mock.mockImplementation(async () => new Response(`data: ${JSON.stringify({
    type: 'response.failed',
    response: { id: 'failed', agent_session_id: 'session', error: { message: 'History failed' } },
  })}\n\n`));
  await assert.rejects(Array.fromAsync(sendMessageStream('Fail', 'skill', contextId)), /History failed/);
  await assert.rejects(Array.fromAsync(sendMessageStream('Retry', 'skill', contextId)), /History failed/);
  assert.equal(JSON.parse(fetch.mock.calls[2].arguments[1]!.body as string).previous_response_id, 'successful');
});

test('history persistence failures are surfaced even after partial response text', async () => {
  mockResponses([
    { type: 'response.output_text.delta', delta: 'Acknowledged' },
    { type: 'response.failed', response: { error: { message: 'Failed to persist history' } } },
  ]);
  await assert.rejects(
    Array.fromAsync(sendMessageStream('Remember this', 'skill')),
    /Failed to persist history/,
  );
});

test('incomplete responses are not reported as successful turns', async () => {
  mockResponses([{ type: 'response.incomplete' }]);
  await assert.rejects(
    Array.fromAsync(sendMessageStream('Remember this', 'skill')),
    /did not complete the response/,
  );
});
