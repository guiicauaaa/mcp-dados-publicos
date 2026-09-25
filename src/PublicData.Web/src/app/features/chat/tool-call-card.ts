import { Component, computed, input } from '@angular/core';
import { JsonView } from '../../shared/json-view';
import { argumentPairs, formatDuration } from '../../shared/format';
import { UiToolCall } from './chat.models';

/**
 * One MCP tools/call: the arguments the MODEL chose, the status, the duration and the result the MCP server
 * returned. This card is the visible proof that the data came through MCP.
 */
@Component({
  selector: 'app-tool-call-card',
  imports: [JsonView],
  template: `
    <article class="card" [class]="call().status" [attr.aria-label]="'Chamada MCP ' + call().tool">
      <header>
        <span class="kind">tools/call</span>
        <code class="name">{{ call().tool }}</code>
        @if (transport()) {
          <span class="badge">MCP · {{ transport() }}</span>
        }
        <span class="status" role="status">
          @switch (call().status) {
            @case ('running') {
              <span class="spinner" aria-hidden="true"></span> consultando…
            }
            @case ('ok') {
              <span aria-hidden="true">✓</span> ok em {{ duration() }}
            }
            @case ('error') {
              <span aria-hidden="true">!</span> erro em {{ duration() }}
            }
          }
        </span>
      </header>

      <dl class="args">
        <dt>Argumentos escolhidos pelo modelo</dt>
        <dd>
          @for (pair of args(); track pair.key) {
            <span class="arg"><span class="key">{{ pair.key }}</span> {{ pair.value }}</span>
          } @empty {
            <span class="arg empty">(sem argumentos)</span>
          }
        </dd>
      </dl>

      @if (errorMessage(); as message) {
        <p class="error-text">{{ message }}</p>
      }

      @if (call().status !== 'running') {
        <details>
          <summary>Resposta do servidor MCP</summary>
          <app-json-view [value]="call().result" label="Resultado da ferramenta" />
        </details>
      }
    </article>
  `,
  styleUrl: './tool-call-card.scss',
})
export class ToolCallCard {
  readonly call = input.required<UiToolCall>();
  readonly transport = input<string | null>(null);

  protected readonly args = computed(() => argumentPairs(this.call().arguments));
  protected readonly duration = computed(() => formatDuration(this.call().elapsedMs));
  protected readonly errorMessage = computed(() => {
    const result = this.call().result;
    return this.call().status === 'error' && result && typeof result === 'object' && 'error' in result
      ? String((result as { error: unknown }).error)
      : null;
  });
}
