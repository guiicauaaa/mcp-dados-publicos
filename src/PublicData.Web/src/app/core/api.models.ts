/** Mirrors the DTOs of PublicData.Api (JSON in camelCase). */

export type ComponentState = 'starting' | 'ready' | 'unavailable';

export interface ModelStatus {
  state: ComponentState;
  baseUrl: string;
  requestedModel: string;
  effectiveModel: string | null;
  ollamaVersion: string | null;
  derivedModelCreated: boolean;
  message: string | null;
}

export interface McpToolInfo {
  name: string;
  title: string | null;
  description: string | null;
  inputSchema: JsonSchema;
  offeredToModel: boolean;
}

export interface JsonSchema {
  type?: string;
  properties?: Record<string, { type?: string; description?: string; default?: unknown }>;
  required?: string[];
}

export interface McpStatus {
  state: ComponentState;
  transport: string;
  endpoint: string | null;
  serverName: string | null;
  serverVersion: string | null;
  protocolVersion: string | null;
  tools: McpToolInfo[];
  message: string | null;
}

export interface SystemStatus {
  ollama: ModelStatus;
  mcp: McpStatus;
  database: { state: ComponentState; message: string | null };
}

export interface ConversationSummary {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  messageCount: number;
}

export interface ToolCallDto {
  id: number;
  callId: string;
  toolName: string;
  arguments: Record<string, unknown> | null;
  isError: boolean;
  errorMessage: string | null;
  durationMs: number;
  result: unknown;
}

export interface MessageDto {
  id: number;
  role: 'user' | 'assistant';
  content: string;
  model: string | null;
  elapsedMs: number | null;
  sourceLine: string | null;
  createdAt: string;
  toolCalls: ToolCallDto[];
}

export interface ConversationDto {
  id: string;
  title: string;
  createdAt: string;
  messages: MessageDto[];
}

export interface AuditItem {
  id: number;
  createdAt: string;
  origin: 'chat' | 'manual';
  toolName: string;
  arguments: Record<string, unknown> | null;
  isError: boolean;
  errorMessage: string | null;
  durationMs: number;
  mcpServer: string;
  mcpTransport: string;
  model: string | null;
  conversationId: string | null;
  conversationTitle: string | null;
}

export interface AuditDetail {
  call: AuditItem;
  result: unknown;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ToolUsage {
  toolName: string;
  total: number;
  errors: number;
  averageDurationMs: number;
}

export interface AuditSummary {
  total: number;
  errors: number;
  averageDurationMs: number;
  last24Hours: number;
  byTool: ToolUsage[];
}

export interface AuditFilter {
  tool?: string;
  status?: 'ok' | 'error';
  origin?: 'chat' | 'manual';
  page: number;
  pageSize: number;
}

export interface ToolInvokeResponse {
  tool: string;
  isError: boolean;
  elapsedMs: number;
  result: unknown;
  server: string;
  transport: string;
}

export interface ServerLogLine {
  sequence: number;
  at: string;
  level: string | null;
  category: string | null;
  message: string;
  serverOwn: boolean;
}

/** Events of POST /api/chat (Server-Sent Events). */
export type ChatStreamEvent =
  | { type: 'conversation'; data: { conversationId: string; title: string } }
  | { type: 'tool_call'; data: { callId: string; tool: string; arguments: Record<string, unknown> | null; at: string } }
  | {
      type: 'tool_result';
      data: { callId: string; tool: string; isError: boolean; elapsedMs: number; preview: string | null; result: unknown };
    }
  | {
      type: 'answer';
      data: { messageId: number; text: string; model: string; elapsedMs: number; source: string | null; usedTools: boolean };
    }
  | { type: 'warning'; data: { message: string } }
  | { type: 'error'; data: { message: string } }
  | { type: 'done'; data: Record<string, never> };
