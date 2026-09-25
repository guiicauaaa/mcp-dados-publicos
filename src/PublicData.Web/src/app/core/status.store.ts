import { computed, DestroyRef, inject, Service, signal } from '@angular/core';
import { SystemStatus } from './api.models';
import { ApiService } from './api.service';

/** Polls /api/status: the header chips and the tools page read from here. */
@Service()
export class StatusStore {
  private readonly api = inject(ApiService);
  private readonly status = signal<SystemStatus | null>(null);
  private readonly failed = signal(false);

  readonly current = this.status.asReadonly();
  readonly apiUnreachable = this.failed.asReadonly();
  readonly tools = computed(() => this.status()?.mcp.tools ?? []);

  constructor() {
    this.refresh();
    const timer = setInterval(() => this.refresh(), 10_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  refresh(): void {
    this.api.status().subscribe({
      next: (status) => {
        this.status.set(status);
        this.failed.set(false);
      },
      error: () => this.failed.set(true),
    });
  }
}
