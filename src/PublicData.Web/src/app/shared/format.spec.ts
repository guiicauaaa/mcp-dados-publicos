import { argumentPairs, formatAnswer, formatDuration, segments } from './format';

describe('formatAnswer', () => {
  it('splits paragraphs and bullet lists as the model writes them', () => {
    const blocks = formatAnswer(
      'Campinas (SP) recebeu R$ 7,91 milhões em 2026.\n\n* Jonas Donizette: R$ 5,52 milhões\n* Marcos Pereira: R$ 1,99 milhão\n\nFim.',
    );

    expect(blocks.map((b) => b.kind)).toEqual(['paragraph', 'list', 'paragraph']);
    const list = blocks[1];
    expect(list.kind === 'list' && list.items.length).toBe(2);
  });

  it('recognizes numbered lists', () => {
    const [list] = formatAnswer('1. primeiro\n2. segundo');

    expect(list).toEqual({
      kind: 'list',
      ordered: true,
      items: [[{ text: 'primeiro', bold: false }], [{ text: 'segundo', bold: false }]],
    });
  });

  it('never produces markup from the text', () => {
    const [paragraph] = formatAnswer('<img src=x onerror=alert(1)> **ok**');

    expect(paragraph).toEqual({
      kind: 'paragraph',
      segments: [
        { text: '<img src=x onerror=alert(1)> ', bold: false },
        { text: 'ok', bold: true },
      ],
    });
  });
});

describe('segments', () => {
  it('keeps an unpaired ** as plain text', () => {
    expect(segments('a **b')).toEqual([
      { text: 'a ', bold: false },
      { text: '**b', bold: false },
    ]);
  });
});

describe('formatDuration', () => {
  it('uses milliseconds below one second and pt-BR seconds above', () => {
    expect(formatDuration(586)).toBe('586 ms');
    expect(formatDuration(1943)).toBe('1,9 s');
    expect(formatDuration(null)).toBe('');
  });
});

describe('argumentPairs', () => {
  it('shows strings as they are and other values as JSON', () => {
    expect(argumentPairs({ city: 'Campinas', year: 2026 })).toEqual([
      { key: 'city', value: 'Campinas' },
      { key: 'year', value: '2026' },
    ]);
  });
});
