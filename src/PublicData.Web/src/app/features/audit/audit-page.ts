import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuditDetail, AuditFilter, AuditItem, AuditSummary, PagedResult } from '../../core/api.models';
import { ApiService } from '../../core/api.service';
import { StatusStore } from '../../core/status.store';
import { argumentPairs, formatDuration } from '../../shared/format';
import { JsonView } from '../../shared/json-view';

/**
 * Audit trail of every MCP tool call (from the chat or from the tools panel): what was asked, with which
 * arguments, how long it took, which server answered and what it returned. "Controle e transparência".
 */
@Component({
  selector: 'app-audit-page',
  imports: [DatePipe, DecimalPipe, JsonView, RouterLink],
  templateUrl: './audit-page.html',
  styleUrl: './audit-page.scss',
})
export class AuditPage {
  private readonly api = inject(ApiService);
  private readonly status = inject(StatusStore);

  protected readonly summary = signal<AuditSummary | null>(null);
  protected readonly page = signal<PagedResult<AuditItem> | null>(null);
  protected readonly filter = signal<AuditFilter>({ page: 1, pageSize: 15 });
  protected readonly selected = signal<AuditDetail | null>(null);
  protected readonly loading = signal(false);

  protected readonly toolNames = computed(() => this.status.tools().map((t) => t.name));
  protected readonly totalPages = computed(() => {
    const current = this.page();
    return current ? Math.max(1, Math.ceil(current.total / current.pageSize)) : 1;
  });
  protected readonly errorRate = computed(() => {
    const s = this.summary();
    return s && s.total > 0 ? (100 * s.errors) / s.total : 0;
  });
  protected readonly argumentPairs = argumentPairs;
  protected readonly formatDuration = formatDuration;

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.api.auditSummary().subscribe((summary) => this.summary.set(summary));
    this.api.auditCalls(this.filter()).subscribe({
      next: (page) => {
        this.page.set(page);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  protected setFilter(field: 'tool' | 'status' | 'origin', event: Event): void {
    const value = (event.target as HTMLSelectElement).value || undefined;
    this.filter.update((f) => ({ ...f, [field]: value, page: 1 }));
    this.reload();
  }

  protected goTo(page: number): void {
    this.filter.update((f) => ({ ...f, page }));
    this.reload();
  }

  protected select(item: AuditItem): void {
    this.api.auditDetail(item.id).subscribe((detail) => this.selected.set(detail));
  }

  protected closeDetail(): void {
    this.selected.set(null);
  }
}
