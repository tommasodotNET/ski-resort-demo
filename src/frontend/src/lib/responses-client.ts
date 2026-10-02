export interface ResponsesStreamEvent {
  content?: string;
  contextId?: string;
}

export type AdvisorArchitecture = 'a2a' | 'skill';

const ADVISOR_AGENT_NAMES: Record<AdvisorArchitecture, string> = {
  a2a: 'skiadvisora2a-ha',
  skill: 'skiadvisorskill-ha',
};

const hostedContinuations = new Map<string, { responseId: string; agentSessionId: string }>();

interface ResponseStreamPayload {
  type?: string;
  delta?: string;
  conversation_id?: string;
  error?: { message?: string };
  response?: {
    id?: string;
    response_id?: string;
    agent_session_id?: string;
    output_text?: string;
    conversation_id?: string;
    conversation?: { id?: string };
    error?: { message?: string };
  };
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
  // The local Python Responses host does not allocate a conversation ID for us.
  let conversationId = contextId ?? (architecture === 'skill' ? crypto.randomUUID() : undefined);
  const continuation = architecture === 'skill' && contextId ? hostedContinuations.get(contextId) : undefined;
  let serverConversationId: string | undefined;
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

  if (continuation) {
    requestBody.previous_response_id = continuation.responseId;
    requestBody.agent_session_id = continuation.agentSessionId;
  } else if (conversationId) {
    requestBody.conversation = conversationId;
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
    if (payload.type === 'error' || payload.type === 'response.failed' || payload.type === 'response.incomplete') {
      throw new Error(
        payload.error?.message ?? payload.response?.error?.message ?? 'Responses API did not complete the response.',
      );
    }
    serverConversationId = extractContextId(payload) ?? serverConversationId;
    conversationId = serverConversationId ?? conversationId;
    const responseId = payload.response?.id ?? payload.response?.response_id;
    const agentSessionId = payload.response?.agent_session_id;
    if (
      architecture === 'skill' && payload.type === 'response.completed' &&
      !serverConversationId && conversationId && responseId && agentSessionId
    ) {
      // Foundry needs both the hosted session and prior response to restore agent history.
      hostedContinuations.set(conversationId, { responseId, agentSessionId });
    }
    const content = extractContent(payload);

    if (conversationId || content) {
      yield { content, contextId: conversationId };
    }
  }
}

export function resetClient() {
  hostedContinuations.clear();
}