import { Service } from '@angular/core';
import { ChatStreamEvent } from './api.models';
import { SseParser } from './sse-parser';

export interface ChatRequest {
  conversationId: string | null;
  message: string;
}

/** POST /api/chat and hands every Server-Sent Event to the caller as soon as it arrives. */
@Service()
export class ChatStreamService {
  async send(request: ChatRequest, onEvent: (event: ChatStreamEvent) => void, signal: AbortSignal): Promise<void> {
    const response = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
      body: JSON.stringify(request),
      signal,
    });

    if (!response.ok || !response.body) {
      throw new Error(`A API respondeu ${response.status} ${response.statusText}.`);
    }

    const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
    const parser = new SseParser();
    try {
      while (true) {
        const { value, done } = await reader.read();
        if (done) {
          break;
        }
        for (const message of parser.push(value)) {
          onEvent({ type: message.event, data: JSON.parse(message.data) } as ChatStreamEvent);
        }
      }
    } finally {
      reader.releaseLock();
    }
  }
}
