export type ToolCallStatus = 'running' | 'ok' | 'error';

export interface UiToolCall {
  callId: string;
  tool: string;
  arguments: Record<string, unknown> | null;
  status: ToolCallStatus;
  elapsedMs: number | null;
  result: unknown;
}

export interface UiMessage {
  /** Local key for @for tracking; the database id when loaded from history. */
  key: string;
  role: 'user' | 'assistant';
  text: string;
  model: string | null;
  elapsedMs: number | null;
  source: string | null;
  toolCalls: UiToolCall[];
  pending: boolean;
  error: string | null;
  warning: string | null;
}
