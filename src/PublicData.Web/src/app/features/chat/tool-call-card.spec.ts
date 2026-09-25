import { TestBed } from '@angular/core/testing';
import { UiToolCall } from './chat.models';
import { ToolCallCard } from './tool-call-card';

async function render(call: UiToolCall) {
  const fixture = TestBed.createComponent(ToolCallCard);
  fixture.componentRef.setInput('call', call);
  fixture.componentRef.setInput('transport', 'stdio');
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('ToolCallCard', () => {
  const base: UiToolCall = {
    callId: 'c1',
    tool: 'get_city_amendments',
    arguments: { city: 'Campinas', year: 2026 },
    status: 'running',
    elapsedMs: null,
    result: null,
  };

  it('shows the tool, the transport and the arguments chosen by the model while running', async () => {
    const element = await render(base);

    expect(element.textContent).toContain('get_city_amendments');
    expect(element.textContent).toContain('MCP · stdio');
    expect(element.textContent).toContain('consultando');
    expect(Array.from(element.querySelectorAll('.arg')).map((a) => a.textContent?.trim())).toEqual(['city Campinas', 'year 2026']);
    expect(element.querySelector('details')).toBeNull();
  });

  it('shows the duration and the MCP result when it finishes', async () => {
    const element = await render({ ...base, status: 'ok', elapsedMs: 586, result: { total_indicated: 'R$ 7,91 milhões' } });

    expect(element.textContent).toContain('ok em 586 ms');
    expect(element.querySelector('details pre')?.textContent).toContain('R$ 7,91 milhões');
  });

  it('shows the tool error message', async () => {
    const element = await render({ ...base, status: 'error', elapsedMs: 41, result: { error: 'Informe o estado.' } });

    expect(element.querySelector('.error-text')?.textContent).toBe('Informe o estado.');
    expect(element.querySelector('article')?.classList).toContain('error');
  });
});
