import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { SystemStatus } from './core/api.models';

const status: SystemStatus = {
  ollama: {
    state: 'ready',
    baseUrl: 'http://localhost:11434',
    requestedModel: 'llama3.2-mcp-v1',
    effectiveModel: 'llama3.2-mcp-v1',
    ollamaVersion: '0.34.4',
    derivedModelCreated: false,
    message: null,
  },
  mcp: {
    state: 'ready',
    transport: 'stdio',
    endpoint: 'mcp-server/PublicData.McpServer.dll',
    serverName: 'public-data-mcp',
    serverVersion: '1.0.0',
    protocolVersion: '2026-07-28',
    tools: [],
    message: null,
  },
  database: { state: 'ready', message: null },
};

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('renders the brand and the main navigation', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.name')?.textContent).toContain('Assistente de Dados Públicos');
    const links = Array.from(element.querySelectorAll('nav a')).map((a) => a.textContent?.trim());
    expect(links).toEqual(['Chat', 'Auditoria', 'Ferramentas MCP']);
  });

  it('shows the model, the MCP transport and protocol from /api/status', async () => {
    const fixture = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController).expectOne('/api/status').flush(status);
    await fixture.whenStable();
    fixture.detectChanges();

    const chips = (fixture.nativeElement as HTMLElement).querySelector('app-status-chips')?.textContent ?? '';
    expect(chips).toContain('llama3.2-mcp-v1');
    expect(chips).toContain('stdio · 2026-07-28');
    expect(chips).toContain('PostgreSQL');
  });
});
