import { SseParser } from './sse-parser';

describe('SseParser', () => {
  it('parses complete events with their type and data', () => {
    const parser = new SseParser();

    const events = parser.push('event: tool_call\ndata: {"tool":"get_city_amendments"}\n\nevent: done\ndata: {}\n\n');

    expect(events).toEqual([
      { event: 'tool_call', data: '{"tool":"get_city_amendments"}' },
      { event: 'done', data: '{}' },
    ]);
  });

  it('keeps partial events until the chunk that completes them arrives', () => {
    const parser = new SseParser();

    expect(parser.push('event: answer\ndata: {"text":"Campi')).toEqual([]);
    expect(parser.push('nas"}\n')).toEqual([]);
    expect(parser.push('\n')).toEqual([{ event: 'answer', data: '{"text":"Campinas"}' }]);
  });

  it('accepts CRLF line endings, comments and multi-line data', () => {
    const parser = new SseParser();

    const events = parser.push(': keep-alive\r\n\r\nevent: x\r\ndata: a\r\ndata: b\r\n\r\n');

    expect(events).toEqual([{ event: 'x', data: 'a\nb' }]);
  });

  it('uses "message" when the event has no type', () => {
    expect(new SseParser().push('data: 1\n\n')).toEqual([{ event: 'message', data: '1' }]);
  });
});
