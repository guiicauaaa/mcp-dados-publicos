import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Chat · Assistente de Dados Públicos',
    loadComponent: () => import('./features/chat/chat-page').then((m) => m.ChatPage),
  },
  {
    path: 'auditoria',
    title: 'Auditoria · Assistente de Dados Públicos',
    loadComponent: () => import('./features/audit/audit-page').then((m) => m.AuditPage),
  },
  {
    path: 'ferramentas',
    title: 'Ferramentas MCP · Assistente de Dados Públicos',
    loadComponent: () => import('./features/tools/tools-page').then((m) => m.ToolsPage),
  },
  { path: '**', redirectTo: '' },
];
