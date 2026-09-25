import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-json-view',
  template: `<pre class="json" tabindex="0" [attr.aria-label]="label()"><code>{{ pretty() }}</code></pre>`,
  styles: `
    .json {
      margin: 0;
      padding: 0.75rem 0.875rem;
      max-height: 22rem;
      overflow: auto;
      background: var(--surface-sunken);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      font: 0.8125rem/1.5 var(--font-mono);
      color: var(--text);
      white-space: pre-wrap;
      word-break: break-word;
    }
    .json:focus-visible {
      outline: 2px solid var(--focus);
      outline-offset: 2px;
    }
  `,
})
export class JsonView {
  readonly value = input<unknown>();
  readonly label = input('JSON');

  protected readonly pretty = computed(() => JSON.stringify(this.value(), null, 2) ?? 'null');
}
