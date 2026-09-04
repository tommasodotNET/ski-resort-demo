export interface ResponsesStreamEvent {
  content?: string;
  contextId?: string;
}

export type AdvisorArchitecture = 'a2a' | 'skill';

const ADVISOR_AGENT_NAMES: Record<AdvisorArchitecture, string> = {
  a2a: 'skiadvisora2a-ha',
  skill: 'skiadvisorskill-ha',
};

interface ResponseStreamPayload {
  type?: string;
  delta?: string;
  conversation_id?: string;
  response?: { id?: string; output_text?: string; conversation_id?: string; conversation?: { id?: string } };
  item?: { id?: string; type?: string; role?: string; content?: Array<{ text?: string; type?: string }> };
  content_index?: number;
  output_index?: number;
}

function extractContent(payload: ResponseStreamPayload): string | undefined {
  if (payload.type === 'response.output_text.delta' && typeof payload.delta === 'string') {
    return payload.delta;
  }

  return undefined;
}

function extractContextId(payload: ResponseStreamPayload): string | undefined {
  return payload.conversation_id ?? payload.response?.conversation_id ?? payload.response?.conversation?.id;
}

async function* parseSseStream(stream: ReadableStream<Uint8Array>): AsyncGenerator<ResponseStreamPayload> {
  const reader = stream.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  try {
    while (true) {
      const { value, done } = await reader.read();
      buffer += decoder.decode(value, { stream: !done });

      let boundary = buffer.indexOf('\n\n');
      while (boundary !== -1) {
        const rawEvent = buffer.slice(0, boundary).trim();
        buffer = buffer.slice(boundary + 2);

        const data = rawEvent
          .split('\n')
          .filter((line) => line.startsWith('data:'))
          .map((line) => line.slice(5).trim())
          .join('\n');

        if (data && data !== '[DONE]') {
          yield JSON.parse(data) as ResponseStreamPayload;
        }

        boundary = buffer.indexOf('\n\n');
      }

      if (done) {
        break;
      }
    }
  } finally {
    reader.releaseLock();
  }
}

export async function* sendMessageStream(
  text: string,
  architecture: AdvisorArchitecture,
  contextId?: string,
): AsyncGenerator<ResponsesStreamEvent, void, undefined> {
  const agentName = ADVISOR_AGENT_NAMES[architecture];
  const requestBody: Record<string, unknown> = {
    model: agentName,
    agent_reference: { type: 'agent_reference', name: agentName },
    input: [
      {
        type: 'message',
        role: 'user',
        content: [{ type: 'input_text', text }],
      },
    ],
    stream: true,
    metadata: { entity_id: agentName },
  };

  if (contextId) {
    requestBody.conversation = contextId;
  }

  const response = await fetch(`/responses/${architecture}`, {
    method: 'POST',
    headers: {
      Accept: 'text/event-stream',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(requestBody),
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => '');
    throw new Error(`Responses API request failed: ${response.status}${errorText ? ` ${errorText}` : ''}`);
  }

  if (!response.body) {
    throw new Error('Responses API did not return a stream.');
  }

  for await (const payload of parseSseStream(response.body)) {
    const nextContextId = extractContextId(payload) ?? contextId;
    const content = extractContent(payload);

    if (nextContextId || content) {
      yield { content, contextId: nextContextId };
    }
  }
}

export function resetClient() {
  // No client instance is cached for the Responses API transport.
}