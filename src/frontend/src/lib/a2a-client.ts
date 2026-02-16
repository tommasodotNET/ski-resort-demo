import { A2AClient } from '@a2a-js/sdk/client';
import type { MessageSendParams, Message } from '@a2a-js/sdk';
import { v4 as uuidv4 } from 'uuid';

export interface A2AStreamEvent {
  content?: string;
  contextId?: string;
}

let clientInstance: A2AClient | null = null;

async function getClient(): Promise<A2AClient> {
  if (!clientInstance) {
    clientInstance = await A2AClient.fromCardUrl('/agenta2a/v1/card');
  }
  return clientInstance;
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
      const message = event as Message;
      if (message.parts && Array.isArray(message.parts)) {
        for (const part of message.parts) {
          if (part.kind === 'text' && part.text) {
            yield {
              content: part.text,
              contextId: message.contextId,
            };
          }
        }
      }
    }

    if (event.kind === 'task' && event.contextId) {
      yield { contextId: event.contextId };
    }

    if (event.kind === 'status-update' && event.contextId) {
      yield { contextId: event.contextId };
    }
  }
}

export function resetClient() {
  clientInstance = null;
}
