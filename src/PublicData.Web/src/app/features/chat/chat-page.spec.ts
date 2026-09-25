import { ConversationDto } from '../../core/api.models';
import { toUiMessages } from './chat-page';

describe('toUiMessages', () => {
  it('rebuilds the MCP cards of a stored conversation', () => {
    const conversation: ConversationDto = {
      id: 'c',
      title: 'Campinas',
      createdAt: '2026-09-25T15:00:00Z',
      messages: [
        { id: 1, role: 'user', content: 'Quanto Campinas recebeu?', model: null, elapsedMs: null, sourceLine: null, createdAt: '', toolCalls: [] },
        {
          id: 2,
          role: 'assistant',
          content: 'R$ 7,91 milhões.',
          model: 'llama3.2-mcp-v1',
          elapsedMs: 28000,
          sourceLine: 'Fonte: Transferegov',
          createdAt: '',
          toolCalls: [
            {
              id: 9,
              callId: 'x',
              toolName: 'get_city_amendments',
              arguments: { city: 'Campinas' },
              isError: false,
              errorMessage: null,
              durationMs: 586,
              result: {},
            },
          ],
        },
      ],
    };

    const [question, answer] = toUiMessages(conversation);

    expect(question.role).toBe('user');
    expect(answer.source).toBe('Fonte: Transferegov');
    expect(answer.toolCalls).toEqual([
      { callId: 'x', tool: 'get_city_amendments', arguments: { city: 'Campinas' }, status: 'ok', elapsedMs: 586, result: {} },
    ]);
  });
});
