export interface SendResult {
  contextId?: string;
}

export async function* sendMessage(
  text: string,
  contextId: string,
  onContextId?: (id: string) => void,
): AsyncGenerator<string> {
  const res = await fetch('/agenta2a', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      jsonrpc: '2.0',
      id: crypto.randomUUID(),
      method: 'message/stream',
      params: {
        message: {
          kind: 'message',
          role: 'user',
          messageId: crypto.randomUUID(),
          contextId: contextId,
          parts: [{ kind: 'text', text }],
        },
        configuration: {
          threadId: contextId,
          acceptedOutputModes: ['text'],
        },
      },
    }),
  });

  if (!res.ok) throw new Error(`A2A error ${res.status}`);
  if (!res.body) throw new Error('No response body');

  const contentType = res.headers.get('content-type') ?? '';

  // If plain JSON response (not SSE), parse directly
  if (contentType.includes('application/json')) {
    const json = await res.json();
    // Capture contextId from response for follow-up messages
    const returnedContextId = json?.result?.contextId;
    if (returnedContextId && onContextId) onContextId(returnedContextId);
    const parts = json?.result?.parts ?? [];
    for (const part of parts) {
      if (part.kind === 'text' && part.text) {
        yield part.text;
      }
    }
    return;
  }

  // SSE streaming response
  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    const lines = buffer.split('\n');
    buffer = lines.pop() ?? '';

    for (const line of lines) {
      const trimmed = line.trim();
      // Handle both SSE "data: {...}" and raw JSON lines
      const json = trimmed.startsWith('data:') ? trimmed.slice(5).trim() : trimmed;
      if (!json || !json.startsWith('{')) continue;
      try {
        const evt = JSON.parse(json);
        const parts = evt?.result?.parts
          ?? evt?.result?.message?.parts
          ?? evt?.result?.status?.message?.parts
          ?? evt?.result?.artifact?.parts
          ?? [];
        for (const part of parts) {
          if (part.kind === 'text' && part.text) {
            yield part.text;
          }
        }
      } catch { /* skip */ }
    }
  }

  // Handle any remaining buffer
  if (buffer.trim()) {
    try {
      const json = buffer.trim().startsWith('data:') ? buffer.trim().slice(5).trim() : buffer.trim();
      if (json.startsWith('{')) {
        const evt = JSON.parse(json);
        const parts = evt?.result?.parts
          ?? evt?.result?.message?.parts
          ?? evt?.result?.status?.message?.parts
          ?? evt?.result?.artifact?.parts
          ?? [];
        for (const part of parts) {
          if (part.kind === 'text' && part.text) {
            yield part.text;
          }
        }
      }
    } catch { /* skip */ }
  }
}
