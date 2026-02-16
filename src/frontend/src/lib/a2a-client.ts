import { A2AClient } from '@a2a-js/sdk/client';
import type { MessageSendParams } from '@a2a-js/sdk';
import { v4 as uuidv4 } from 'uuid';

export interface A2AStreamEvent {
  content?: string;
  contextId?: string;
}

let clientPromise: Promise<InstanceType<typeof A2AClient>> | null = null;

async function createClient(): Promise<InstanceType<typeof A2AClient>> {
  // Fetch agent card through the Vite proxy
  const res = await fetch('/agenta2a/v1/card', {
    headers: { Accept: 'application/json' },
  });
  if (!res.ok) throw new Error(`Failed to fetch agent card: ${res.status}`);
  const card = await res.json();
  // Override the card URL to use the proxy path instead of the backend's direct URL
  card.url = '/agenta2a';
  return new A2AClient(card);
}

function getClient() {
  if (!clientPromise) {
    clientPromise = createClient();
  }
  return clientPromise;
}

export async function* sendMessageStream(
  text: string,
  contextId?: string,
): AsyncGenerator<A2AStreamEvent, void, undefined> {
  const client = await getClient();

  const params: MessageSendParams = {
    message: {
      messageId: uuidv4(),
      role: 'user',
      kind: 'message',
      parts: [{ kind: 'text', text }],
      contextId,
    },
  };

  const stream = client.sendMessageStream(params);

  for await (const event of stream) {
    if (event.kind === 'message') {
      const cid = (event as Record<string, unknown>).contextId as string | undefined;
      if (event.parts && Array.isArray(event.parts)) {
        for (const part of event.parts) {
          if (part.kind === 'text' && part.text) {
            yield { content: part.text, contextId: cid };
          }
        }
      }
    } else if (event.kind === 'status-update') {
      const e = event as Record<string, unknown>;
      const cid = e.contextId as string | undefined;
      if (cid) yield { contextId: cid };
      const status = e.status as Record<string, unknown> | undefined;
      const msg = status?.message as Record<string, unknown> | undefined;
      const parts = msg?.parts as Array<{ kind?: string; text?: string }> | undefined;
      if (parts) {
        for (const part of parts) {
          if (part.kind === 'text' && part.text) {
            yield { content: part.text, contextId: cid };
          }
        }
      }
    }
  }
}

export function resetClient() {
  clientPromise = null;
}
