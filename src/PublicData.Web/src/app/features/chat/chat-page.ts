import { DatePipe } from '@angular/common';
import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { ConversationDto, ConversationSummary, ChatStreamEvent } from '../../core/api.models';
import { ApiService } from '../../core/api.service';
import { ChatStreamService } from '../../core/chat-stream.service';
import { StatusStore } from '../../core/status.store';
import { AnswerText } from '../../shared/answer-text';
import { formatDuration } from '../../shared/format';
import { UiMessage, UiToolCall } from './chat.models';
import { ToolCallCard } from './tool-call-card';

let localKey = 0;

const nextFrame = (callback: () => void) =>
  typeof requestAnimationFrame === 'function' ? requestAnimationFrame(callback) : setTimeout(callback);

@Component({
  selector: 'app-chat-page',
  imports: [ToolCallCard, AnswerText, DatePipe],
  templateUrl: './chat-page.html',
  styleUrl: './chat-page.scss',
})
export class ChatPage {
  private readonly api = inject(ApiService);
  private readonly stream = inject(ChatStreamService);
  private readonly status = inject(StatusStore);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly questionInput = viewChild<ElementRef<HTMLTextAreaElement>>('question');
  private abort: AbortController | null = null;

  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly activeId = signal<string | null>(null);
  protected readonly messages = signal<UiMessage[]>([]);
  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly elapsed = signal(0);
  protected readonly transport = computed(() => this.status.current()?.mcp.transport ?? null);
  protected readonly modelName = computed(() => this.status.current()?.ollama.effectiveModel ?? 'local');
  protected readonly formatDuration = formatDuration;

  protected readonly suggestions = [
    'Olá! O que você consegue fazer?',
    // "Quanto foi indicado": the model mirrors the verb of the question, and the data is the indicated value.
    'Quanto foi indicado em emendas Pix para Campinas (SP) em 2026 e por quais parlamentares?',
    'Quanto foi indicado em emendas Pix para Recife em 2025?',
    'Quanto Santa Rita recebeu de emendas Pix em 2026?',
  ];

  constructor() {
    this.loadConversations();
    // Leaving the page does NOT abort the turn: the server finishes it and saves the answer and the audit rows,
    // which show up when the conversation is opened again. Only "Parar" cancels.

    // Keep the newest message in view as the stream grows.
    effect(() => {
      this.messages();
      const element = this.scroller()?.nativeElement;
      if (element) {
        nextFrame(() => element.scrollTo?.({ top: element.scrollHeight }));
      }
    });
  }

  protected loadConversations(): void {
    this.api.conversations().subscribe({ next: (list) => this.conversations.set(list), error: () => this.conversations.set([]) });
  }

  protected open(id: string): void {
    if (this.busy()) {
      return;
    }
    this.activeId.set(id);
    this.api.conversation(id).subscribe({
      // Two quick clicks: only the conversation that is still active may fill the screen.
      next: (conversation) => {
        if (this.activeId() === id) {
          this.messages.set(toUiMessages(conversation));
        }
      },
      error: () => {
        if (this.activeId() === id) {
          this.activeId.set(null);
          this.messages.set([]);
          this.loadConversations();
        }
      },
    });
  }

  protected newConversation(): void {
    if (this.busy()) {
      return;
    }
    this.activeId.set(null);
    this.messages.set([]);
    this.questionInput()?.nativeElement.focus();
  }

  protected remove(conversation: ConversationSummary): void {
    if (this.busy() || !confirm(`Apagar a conversa "${conversation.title}"? A auditoria das chamadas MCP é mantida.`)) {
      return;
    }
    this.api.deleteConversation(conversation.id).subscribe(() => {
      if (this.activeId() === conversation.id) {
        this.newConversation();
      }
      this.loadConversations();
    });
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLTextAreaElement).value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }

  protected stop(): void {
    this.abort?.abort();
  }

  protected async send(text?: string): Promise<void> {
    const question = (text ?? this.draft()).trim();
    if (!question || this.busy()) {
      return;
    }

    this.draft.set('');
    this.busy.set(true);
    this.elapsed.set(0);
    const pendingKey = `local-${++localKey}`;
    this.messages.update((list) => [
      ...list,
      newMessage(`local-${++localKey}`, 'user', question),
      { ...newMessage(pendingKey, 'assistant', ''), pending: true },
    ]);

    const started = performance.now();
    const timer = setInterval(() => this.elapsed.set(Math.floor((performance.now() - started) / 1000)), 1000);
    this.abort = new AbortController();

    try {
      await this.stream.send({ conversationId: this.activeId(), message: question }, (event) => this.apply(pendingKey, event), this.abort.signal);
    } catch (error) {
      const cancelled = this.abort?.signal.aborted;
      this.updateMessage(pendingKey, (m) => ({
        ...m,
        pending: false,
        error: cancelled ? 'Resposta cancelada.' : `Não consegui falar com a API: ${(error as Error).message}`,
      }));
    } finally {
      clearInterval(timer);
      this.updateMessage(pendingKey, (m) =>
        closeRunningCalls(m.pending ? { ...m, pending: false, error: m.error ?? 'A resposta terminou sem conteúdo.' } : m),
      );
      this.busy.set(false);
      this.abort = null;
      this.loadConversations();
      nextFrame(() => this.questionInput()?.nativeElement.focus());
    }
  }

  private apply(key: string, event: ChatStreamEvent): void {
    switch (event.type) {
      case 'conversation':
        this.activeId.set(event.data.conversationId);
        break;
      case 'tool_call': {
        const call: UiToolCall = {
          callId: event.data.callId,
          tool: event.data.tool,
          arguments: event.data.arguments,
          status: 'running',
          elapsedMs: null,
          result: null,
        };
        this.updateMessage(key, (m) => ({ ...m, toolCalls: [...m.toolCalls, call] }));
        break;
      }
      case 'tool_result': {
        const { callId, isError, elapsedMs, result } = event.data;
        this.updateMessage(key, (m) => ({
          ...m,
          toolCalls: m.toolCalls.map((c) => (c.callId === callId ? { ...c, status: isError ? 'error' : 'ok', elapsedMs, result } : c)),
        }));
        break;
      }
      case 'answer':
        this.updateMessage(key, (m) => ({
          ...m,
          text: event.data.text,
          model: event.data.model,
          elapsedMs: event.data.elapsedMs,
          source: event.data.source,
          pending: false,
        }));
        break;
      case 'warning':
        this.updateMessage(key, (m) => ({ ...m, warning: event.data.message }));
        break;
      case 'error':
        this.updateMessage(key, (m) => closeRunningCalls({ ...m, pending: false, error: event.data.message }));
        break;
      case 'done':
        break;
    }
  }

  private updateMessage(key: string, change: (message: UiMessage) => UiMessage): void {
    this.messages.update((list) => list.map((m) => (m.key === key ? change(m) : m)));
  }
}

/** A card must never keep spinning after the turn ended (cancelled, network error, timeout). */
export function closeRunningCalls(message: UiMessage): UiMessage {
  return message.toolCalls.some((c) => c.status === 'running')
    ? {
        ...message,
        toolCalls: message.toolCalls.map((c) =>
          c.status === 'running' ? { ...c, status: 'error', result: { error: 'Chamada interrompida antes do resultado.' } } : c,
        ),
      }
    : message;
}

function newMessage(key: string, role: UiMessage['role'], text: string): UiMessage {
  return { key, role, text, model: null, elapsedMs: null, source: null, toolCalls: [], pending: false, error: null, warning: null };
}

export function toUiMessages(conversation: ConversationDto): UiMessage[] {
  return conversation.messages.map((m) => ({
    ...newMessage(`msg-${m.id}`, m.role, m.content),
    model: m.model,
    elapsedMs: m.elapsedMs,
    source: m.sourceLine,
    toolCalls: m.toolCalls.map((t) => ({
      callId: t.callId,
      tool: t.toolName,
      arguments: t.arguments,
      status: t.isError ? 'error' : 'ok',
      elapsedMs: t.durationMs,
      result: t.result,
    })),
  }));
}
