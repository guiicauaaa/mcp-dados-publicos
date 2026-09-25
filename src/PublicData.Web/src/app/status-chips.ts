import { Component, computed, inject } from '@angular/core';
import { ComponentState } from './core/api.models';
import { StatusStore } from './core/status.store';

interface Chip {
  label: string;
  value: string;
  state: ComponentState | 'down';
  detail: string;
}

/** Model, MCP server and database state, from /api/status. */
@Component({
  selector: 'app-status-chips',
  template: `
    <ul class="chips" aria-label="Estado dos componentes">
      @for (chip of chips(); track chip.label) {
        <li class="chip" [class]="chip.state" [title]="chip.detail">
          <span class="dot" aria-hidden="true"></span>
          <span class="label">{{ chip.label }}</span>
          <span class="value">{{ chip.value }}</span>
          <span class="sr-only">({{ stateText(chip.state) }})</span>
        </li>
      }
    </ul>
  `,
  styles: `
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      list-style: none;
      margin: 0;
      padding: 0;
    }
    .chip {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      padding: 0.25rem 0.625rem;
      border-radius: 999px;
      border: 1px solid var(--border);
      background: var(--surface);
      font-size: 0.75rem;
      white-space: nowrap;
    }
    .label {
      color: var(--text-muted);
    }
    .dot {
      width: 0.5rem;
      height: 0.5rem;
      border-radius: 50%;
      background: var(--text-muted);
    }
    .ready .dot {
      background: var(--ok);
    }
    .starting .dot {
      background: var(--accent);
    }
    .unavailable .dot,
    .down .dot {
      background: var(--warn);
    }
  `,
})
export class StatusChips {
  private readonly store = inject(StatusStore);

  protected readonly chips = computed<Chip[]>(() => {
    const status = this.store.current();
    if (this.store.apiUnreachable() || !status) {
      return [{ label: 'API', value: status ? 'fora do ar' : 'conectando…', state: 'down', detail: 'Não consegui falar com a API.' }];
    }

    const { ollama, mcp, database } = status;
    return [
      {
        label: 'Modelo',
        value: ollama.effectiveModel ?? ollama.requestedModel,
        state: ollama.state,
        detail: ollama.message ?? `Ollama ${ollama.ollamaVersion ?? ''} em ${ollama.baseUrl}`,
      },
      {
        label: 'MCP',
        value: mcp.state === 'ready' ? `${mcp.transport} · ${mcp.protocolVersion}` : mcp.transport,
        state: mcp.state,
        detail: mcp.message ?? `${mcp.serverName} ${mcp.serverVersion} · ${mcp.tools.length} ferramentas`,
      },
      {
        label: 'Banco',
        value: 'PostgreSQL',
        state: database.state,
        detail: database.message ?? 'Histórico e auditoria',
      },
    ];
  });

  protected stateText(state: Chip['state']): string {
    return state === 'ready' ? 'pronto' : state === 'starting' ? 'iniciando' : 'indisponível';
  }
}
