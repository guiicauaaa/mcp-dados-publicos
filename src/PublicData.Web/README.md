# PublicData.Web

Frontend Angular 22 (standalone, signals, zoneless, Vitest) do Assistente de Dados Públicos.

| Tela | Rota | O que mostra |
|---|---|---|
| Chat | `/` | Conversa com o modelo local; cada chamada MCP aparece como um cartão em tempo real (SSE). |
| Auditoria | `/auditoria` | Trilha de todas as chamadas MCP gravada no PostgreSQL, com filtros e o resultado completo. |
| Ferramentas MCP | `/ferramentas` | O `tools/list` do servidor, chamada direta sem o modelo e o log do processo servidor. |

## Desenvolvimento

Com a API rodando em `http://localhost:5080` (veja o README da raiz):

```bash
npm ci
npm start          # http://localhost:4200, com proxy de /api para a API
npm run test:ci    # testes (Vitest)
npm run build      # gera o build em ../PublicData.Api/wwwroot, servido pela própria API
```

O streaming do chat usa `fetch` + `ReadableStream` (o `EventSource` do navegador só faz GET) e um parser
de Server-Sent Events próprio (`src/app/core/sse-parser.ts`). O texto do modelo nunca vira HTML: a
formatação (listas e negrito) é montada pelo template a partir de `src/app/shared/format.ts`.
