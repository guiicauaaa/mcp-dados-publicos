import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ChatStreamEvent } from '../../core/api.models';
import { ChatRequest, ChatStreamService } from '../../core/chat-stream.service';
import { ChatPage } from './chat-page';
import { UiMessage } from './chat.models';

/** Replays scripted SSE events; in "hang" mode it then waits until the request is aborted. */
class StubChatStream {
  events: ChatStreamEvent[] = [];
  hang = false;
  requests: ChatRequest[] = [];

  async send(request: ChatRequest, onEvent: (event: ChatStreamEvent) => void, signal: AbortSignal): Promise<void> {
    this.requests.push(request);
    for (const event of this.events) {
      onEvent(event);
    }
    if (this.hang) {
      await new Promise<void>((_, reject) =>
        signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))),
      );
    }
  }
}

describe('ChatPage flow', () => {
  let stream: StubChatStream;

  beforeEach(async () => {
    stream = new StubChatStream();
    await TestBed.configureTestingModule({
      imports: [ChatPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ChatStreamService, useValue: stream }],
    }).compileComponents();
  });

  function create() {
    const fixture = TestBed.createComponent(ChatPage);
    const page = fixture.componentInstance as unknown as {
      send(text?: string): Promise<void>;
      stop(): void;
      messages(): UiMessage[];
      activeId(): string | null;
      busy(): boolean;
    };
    return { fixture, page };
  }

  it('shows the MCP card, the answer and the source as the events arrive', async () => {
    stream.events = [
      { type: 'conversation', data: { conversationId: 'c-1', title: 'Campinas' } },
      { type: 'tool_call', data: { callId: 'x', tool: 'get_city_amendments', arguments: { city: 'Campinas' }, at: '' } },
      { type: 'tool_result', data: { callId: 'x', tool: 'get_city_amendments', isError: false, elapsedMs: 586, preview: null, result: {} } },
      {
        type: 'answer',
        data: { messageId: 7, text: 'Foram indicados R$ 7,91 milhões.', model: 'llama3.2-mcp-v1', elapsedMs: 18000, source: 'Fonte: Transferegov', usedTools: true },
      },
      { type: 'done', data: {} },
    ];
    const { page } = create();

    await page.send('Quanto Campinas recebeu?');

    const [question, answer] = page.messages();
    expect(question.text).toBe('Quanto Campinas recebeu?');
    expect(answer.pending).toBe(false);
    expect(answer.text).toBe('Foram indicados R$ 7,91 milhões.');
    expect(answer.source).toBe('Fonte: Transferegov');
    expect(answer.toolCalls).toEqual([
      { callId: 'x', tool: 'get_city_amendments', arguments: { city: 'Campinas' }, status: 'ok', elapsedMs: 586, result: {} },
    ]);
    expect(page.activeId()).toBe('c-1');
    expect(page.busy()).toBe(false);
  });

  it('sends the follow-up in the same conversation', async () => {
    stream.events = [{ type: 'conversation', data: { conversationId: 'c-1', title: 't' } }];
    const { page } = create();

    await page.send('Primeira');
    await page.send('E em 2025?');

    expect(stream.requests.map((r) => r.conversationId)).toEqual([null, 'c-1']);
  });

  it('stopping a turn leaves no card spinning and says it was cancelled', async () => {
    stream.events = [{ type: 'tool_call', data: { callId: 'x', tool: 'get_city_amendments', arguments: {}, at: '' } }];
    stream.hang = true;
    const { page } = create();

    const turn = page.send('Quanto Campinas recebeu?');
    await Promise.resolve();
    page.stop();
    await turn;

    const answer = page.messages()[1];
    expect(answer.error).toBe('Resposta cancelada.');
    expect(answer.toolCalls[0].status).toBe('error');
    expect(page.busy()).toBe(false);
  });
});
