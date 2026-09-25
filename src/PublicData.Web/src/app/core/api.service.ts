import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AuditDetail,
  AuditFilter,
  AuditItem,
  AuditSummary,
  ConversationDto,
  ConversationSummary,
  PagedResult,
  ServerLogLine,
  SystemStatus,
  ToolInvokeResponse,
} from './api.models';

@Service()
export class ApiService {
  private readonly http = inject(HttpClient);

  status(): Observable<SystemStatus> {
    return this.http.get<SystemStatus>('/api/status');
  }

  conversations(): Observable<ConversationSummary[]> {
    return this.http.get<ConversationSummary[]>('/api/conversations');
  }

  conversation(id: string): Observable<ConversationDto> {
    return this.http.get<ConversationDto>(`/api/conversations/${id}`);
  }

  deleteConversation(id: string): Observable<void> {
    return this.http.delete<void>(`/api/conversations/${id}`);
  }

  auditCalls(filter: AuditFilter): Observable<PagedResult<AuditItem>> {
    let params = new HttpParams().set('page', filter.page).set('pageSize', filter.pageSize);
    if (filter.tool) params = params.set('tool', filter.tool);
    if (filter.status) params = params.set('status', filter.status);
    if (filter.origin) params = params.set('origin', filter.origin);
    return this.http.get<PagedResult<AuditItem>>('/api/audit/tool-calls', { params });
  }

  auditDetail(id: number): Observable<AuditDetail> {
    return this.http.get<AuditDetail>(`/api/audit/tool-calls/${id}`);
  }

  auditSummary(): Observable<AuditSummary> {
    return this.http.get<AuditSummary>('/api/audit/summary');
  }

  invokeTool(name: string, args: Record<string, unknown>): Observable<ToolInvokeResponse> {
    return this.http.post<ToolInvokeResponse>(`/api/mcp/tools/${encodeURIComponent(name)}/invoke`, { arguments: args });
  }

  serverLog(since: number): Observable<ServerLogLine[]> {
    return this.http.get<ServerLogLine[]>('/api/mcp/log', { params: { since } });
  }
}
