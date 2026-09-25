export interface TextSegment {
  text: string;
  bold: boolean;
}

export type AnswerBlock =
  | { kind: 'paragraph'; segments: TextSegment[] }
  | { kind: 'list'; ordered: boolean; items: TextSegment[][] };

const bullet = /^\s*(?:[*\-•])\s+(.*)$/;
const numbered = /^\s*\d+[.)]\s+(.*)$/;

/**
 * Turns the model's plain text (with the little markdown a 3B model writes: **bold**, "* item", "1. item")
 * into blocks rendered by the template. No innerHTML: the text never becomes markup.
 */
export function formatAnswer(text: string): AnswerBlock[] {
  const blocks: AnswerBlock[] = [];
  let paragraph: string[] = [];

  const flushParagraph = () => {
    if (paragraph.length > 0) {
      blocks.push({ kind: 'paragraph', segments: segments(paragraph.join(' ')) });
      paragraph = [];
    }
  };

  for (const rawLine of text.replace(/\r\n?/g, '\n').split('\n')) {
    const line = rawLine.trim();
    const item = bullet.exec(line) ?? numbered.exec(line);
    if (item) {
      flushParagraph();
      const ordered = numbered.test(line);
      const last = blocks.at(-1);
      if (last?.kind === 'list' && last.ordered === ordered) {
        last.items.push(segments(item[1]));
      } else {
        blocks.push({ kind: 'list', ordered, items: [segments(item[1])] });
      }
    } else if (line.length === 0) {
      flushParagraph();
    } else {
      paragraph.push(line);
    }
  }

  flushParagraph();
  return blocks;
}

export function segments(text: string): TextSegment[] {
  const result: TextSegment[] = [];
  const parts = text.split('**');
  parts.forEach((part, index) => {
    if (part.length > 0) {
      // Odd positions sit between a pair of "**"; an unpaired "**" at the end stays as plain text.
      const bold = index % 2 === 1 && index < parts.length - 1;
      result.push({ text: bold || index % 2 === 0 ? part : `**${part}`, bold });
    }
  });
  return result;
}

const secondsFormat = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** 586 → "586 ms"; 1943 → "1,9 s". */
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) {
    return '';
  }
  return ms < 1000 ? `${Math.round(ms)} ms` : `${secondsFormat.format(ms / 1000)} s`;
}

/** Compact "chave: valor" pairs for tool arguments. */
export function argumentPairs(args: Record<string, unknown> | null | undefined): { key: string; value: string }[] {
  return Object.entries(args ?? {}).map(([key, value]) => ({
    key,
    value: typeof value === 'string' ? value : JSON.stringify(value),
  }));
}
