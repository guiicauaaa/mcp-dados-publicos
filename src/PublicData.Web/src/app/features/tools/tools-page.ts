import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { McpToolInfo, ServerLogLine, ToolInvokeResponse } from '../../core/api.models';
import { ApiService } from '../../core/api.service';
import { StatusStore } from '../../core/status.store';
import { formatDuration } from '../../shared/format';
import { JsonView } from '../../shared/json-view';

const examples: Record<string, Record<string, unknown>> = {
  get_city_amendments: { city: 'Campinas', state: 'SP', year: 2026 },
  get_current_datetime: {},
};

const maxLogLines = 200;

/** What the MCP server exposes (tools/list), a direct call without the model, and the server process log. */
@Component({
  selector: 'app-tools-page',
  imports: [JsonView, DatePipe],
  templateUrl: './tools-page.html',
  styleUrl: './tools-page.scss',
})
export class ToolsPage {
  private readonly api = inject(ApiService);
  private readonly statusStore = inject(StatusStore);
  private lastSequence = 0;

  protected readonly status = this.statusStore.current;
  protected readonly tools = this.statusStore.tools;
  protected readonly drafts = signal<Record<string, string>>({});
  protected readonly results = signal<Record<string, ToolInvokeResponse | { failure: string }>>({});
  protected readonly running = signal<string | null>(null);
  protected readonly log = signal<ServerLogLine[]>([]);
  protected readonly showSdkLines = signal(false);
  protected readonly visibleLog = computed(() => (this.showSdkLines() ? this.log() : this.log().filter((l) => l.serverOwn)));
  protected readonly formatDuration = formatDuration;

  constructor() {
    this.pollLog();
    const timer = setInterval(() => this.pollLog(), 2000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected draftFor(tool: McpToolInfo): string {
    return this.drafts()[tool.name] ?? JSON.stringify(examples[tool.name] ?? {}, null, 2);
  }

  protected parameters(tool: McpToolInfo) {
    const required = new Set(tool.inputSchema.required ?? []);
    return Object.entries(tool.inputSchema.properties ?? {}).map(([name, schema]) => ({
      name,
      type: schema.type ?? '—',
      description: schema.description ?? '',
      required: required.has(name),
    }));
  }

  protected onDraft(tool: McpToolInfo, event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.drafts.update((d) => ({ ...d, [tool.name]: value }));
  }

  protected invoke(tool: McpToolInfo): void {
    let args: Record<string, unknown>;
    try {
      const parsed: unknown = JSON.parse(this.draftFor(tool) || '{}');
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
        throw new Error('Os argumentos precisam ser um objeto JSON, por exemplo {"city": "Campinas"}.');
      }
      args = parsed as Record<string, unknown>;
    } catch (error) {
      this.results.update((r) => ({ ...r, [tool.name]: { failure: `JSON inválido: ${(error as Error).message}` } }));
      return;
    }

    this.running.set(tool.name);
    this.api.invokeTool(tool.name, args).subscribe({
      next: (response) => {
        this.results.update((r) => ({ ...r, [tool.name]: response }));
        this.running.set(null);
      },
      error: (error) => {
        this.results.update((r) => ({ ...r, [tool.name]: { failure: error?.error?.detail ?? 'A API não respondeu.' } }));
        this.running.set(null);
      },
    });
  }

  protected resultOf(tool: McpToolInfo): ToolInvokeResponse | null {
    const result = this.results()[tool.name];
    return result && !('failure' in result) ? result : null;
  }

  protected failureOf(tool: McpToolInfo): string | null {
    const result = this.results()[tool.name];
    return result && 'failure' in result ? result.failure : null;
  }

  protected toggleSdkLines(event: Event): void {
    this.showSdkLines.set((event.target as HTMLInputElement).checked);
  }

  private pollLog(): void {
    this.api.serverLog(this.lastSequence).subscribe((lines) => {
      if (lines.length === 0) {
        return;
      }
      this.lastSequence = lines[lines.length - 1].sequence;
      this.log.update((current) => [...current, ...lines].slice(-maxLogLines));
    });
  }
}
