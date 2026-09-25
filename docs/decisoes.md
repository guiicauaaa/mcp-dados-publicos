# Decisões técnicas

Registros curtos das decisões que moldaram o projeto: o contexto, o que foi escolhido e por quê. Os números
vêm de medições feitas em 25/09/2026 nesta implementação (Windows 11, CPU, 16 GB, Ollama 0.34.4).

## ADR-01 · SDK oficial de MCP para C# (2.2.0), nos dois lados

**Contexto.** O repositório de referência implementa o cliente com JSON-RPC escrito à mão (sem o handshake
`initialize`, id fixo) sobre o `ModelContextProtocol 0.4.1-preview`. A spec atual é a 2026-07-28, em que o
cliente sonda `server/discover` antes de cair para `initialize`.

**Decisão.** `ModelContextProtocol` 2.2.0 no servidor e `ModelContextProtocol.Core` 2.2.0 no cliente
(`McpClient.CreateAsync`, `StdioClientTransport`, `HttpClientTransport`). As ferramentas vêm do `tools/list`
como `McpClientTool`, que já é um `AIFunction` do Microsoft.Extensions.AI.

**Consequências.** Negociação de protocolo, correlação de ids, cancelamento e erros ficam por conta da
biblioteca. Três armadilhas medidas e tratadas: `DiscoverProbeTimeout` de 5 s derrubava o cold start para o
protocolo antigo (subimos para 30 s); o `DisposeAsync` do stdio sempre espera o `ShutdownTimeout` (1 s);
e o cancelamento do cliente não chega ao servidor por stdio (timeout de 45 s do lado do cliente).

## ADR-02 · Dois transportes: stdio e Streamable HTTP

**Decisão.** O mesmo servidor roda em **stdio** (padrão, processo filho, como no exemplo de referência;
um `dotnet run` sobe tudo) e em **Streamable HTTP stateless** (`--http` ou `MCP_TRANSPORT=http`), usado pelo
Docker Compose, onde o servidor MCP é um serviço separado. O cliente escolhe por configuração.

**Por quê.** stdio é o caminho mais simples e verificável localmente (processo próprio, stderr próprio). HTTP
é o caminho de produção: vários clientes, escala horizontal, sem sessão. Os dois têm teste de integração.

**Limite consciente.** Nesta entrega o transporte HTTP não tem autenticação: fica só na rede interna do
Compose, e o servidor recusa requisições com cabeçalho `Origin` (a spec pede validar o Origin contra DNS
rebinding; o único cliente é a API .NET, que não envia Origin). Em produção entrariam autenticação
(OAuth, como a spec prevê), rate limit e TLS.

## ADR-03 · Microsoft.Extensions.AI + OllamaSharp (e não HTTP à mão nem Semantic Kernel)

**Decisão.** `IChatClient` do Microsoft.Extensions.AI 10.10 com `OllamaApiClient` (OllamaSharp 5.4.30) e o
`FunctionInvokingChatClient` (limite de 3 iterações; na última, as ferramentas saem e o modelo tem de responder
em texto). O pacote `Microsoft.Extensions.AI.Ollama` está deprecado e aponta para o OllamaSharp.

**Por quê.** Trocar de provedor é trocar uma linha; o loop de ferramentas tem limite de iterações e de erros;
e o Semantic Kernel hoje se apoia nessas mesmas abstrações. Para um fluxo de uma ferramenta, a camada extra
não se paga.

## ADR-04 · Transferegov (emendas Pix) como API externa

**Contexto.** Oito APIs públicas testadas ao vivo. Critérios: sem chave, estável, ligada a controle e
aplicação de recursos públicos, e utilizável por um modelo de 3B.

| API | Resultado |
|---|---|
| **Transferegov (transferências especiais)** | Escolhida: sem chave, 0,1 a 0,9 s, filtro no servidor (PostgREST), gzip. |
| SICONFI / Tesouro | Mais aderente à LRF, mas 75 a 230 KB sem gzip, de 0,3 a 27 s, falhas: ficou para evolução. |
| Câmara (CEAP) | Exige um contorno (`idLegislatura`) e, em ano eleitoral, perguntas sobre gastos de deputados soam partidárias. |
| Portal da Transparência | Exige cadastro de chave: quem avalia teria de pedir credencial. |
| BrasilAPI (CNPJ) | Traz sócios (dado pessoal) e CNPJ de 14 dígitos é argumento ruim para um 3B. |
| CNES/DATASUS, Senado, IBGE | Instável, sem filtro no servidor, ou redundante com o snapshot de municípios. |

**Decisão.** Uma ferramenta, `get_city_amendments(city, state, year)`, filtrando pelo **CNPJ** do município
(o nome no Transferegov é sensível a acento e sem padrão), com `select` explícito (sem banco, agência e
conta do beneficiário) e sem contar planos IMPEDIDOS (reindicados com o mesmo valor, somariam em dobro).
O CNPJ vem de um snapshot embutido dos 5.570 municípios (SICONFI `/entes`), então resolver a cidade não
custa rede; acento e caixa são ignorados e nomes parciais voltam com sugestões. Uma exceção conferida
contra a API: o Transferegov registra Brasília pelo CNPJ do Distrito Federal (00394601000126), não pelo que
o SICONFI lista para o ente municipal. O script de atualização aplica essa correção, e as 27 capitais
foram conferidas.

## ADR-05 · Modelo derivado `llama3.2-mcp-v1` (mesmos pesos, template corrigido)

**Contexto.** O template do llama3.2 no Ollama injeta, na última mensagem do usuário, *"Given the following
functions, please respond with a JSON for a function call…"* sempre que há ferramentas. O modelo obedece:
chamava ferramenta até para "oi" e "capital da França". Medido: **6/18** decisões certas.

**Decisão.** Um `Modelfile` (`ollama/Modelfile`) com os mesmos pesos (`FROM llama3.2`) e só esse parágrafo
reescrito: *"First decide whether answering the prompt requires data that only one of these functions can
provide"*. O app cria o modelo sozinho pela API do Ollama (`/api/create` com `from` + `template`); se falhar,
usa o `llama3.2` original e mostra o comando manual.

**Resultado.** Com o original, **6/18** decisões certas; com o derivado, **18/18** no conjunto de controle.
Smoke final (24 perguntas × 3 rodadas, com o prompt final): **72/72**, ver
[smoke-llama32.md](smoke-llama32.md); e o roteiro do vídeo, **20/20 em 5 rodadas**. `--model llama3.2`
(console) ou `Chat:Model` (API) roda o original para comparação. O sufixo `-v1` muda se o template mudar,
porque um modelo existente não é recriado.

Histórico das medições (cada ajuste só entrou depois de medido):

| Rodada | Resultado | O que mudou em seguida |
|---|---|---|
| Template original | 4/7 no teste de decisão, 6/18 no controle | Template derivado |
| Derivado, 1º smoke | 48/48, mas "os parlamentares que **receberam**" e zero lido como erro | Nomes de campo e "Consulta concluída" (ADR-07) |
| 2º smoke | 47/48: "E em 2025?" repetiu os dados de 2026 numa rodada | Regra 1 do prompt (ADR-08) |
| Roteiro do vídeo × 5 | 20/20 | Prompt e descrição: "valor indicado, não pago" |
| Smoke × 3 | 72/72, mas "recebeu" em 15 de 18 respostas com dados | "valor indicado" na linha de Fonte; sugestões com "foi indicado" |
| Pergunta com "foi indicado" × 3 | 6/6 chamadas certas, "Foram indicados R$ …" nas 6 respostas | — |

## ADR-06 · Data no system prompt; `get_current_datetime` existe, mas não é oferecida ao modelo

**Contexto.** O PDF cita "data/hora do servidor" e o exemplo de referência tem `get_time`. Medido: com uma
segunda ferramenta de data, o llama3.2 chamava ferramenta em cumprimentos e ainda inventava "que dia é hoje".

**Decisão.** O servidor mantém `get_current_datetime` (fuso de Brasília; outros clientes MCP podem usar e o
painel de ferramentas a chama), mas o chat oferece ao modelo só `get_city_amendments`
(`Chat:ExposedTools`). A data e a hora de Brasília vão no system prompt, refeito a cada turno, o que também
resolve "este ano" e "ano passado" sem chamada extra.

## ADR-07 · Resultado da ferramenta pensado para um modelo de 3B

- Retorno compacto (~0,6 KB), com valores **já formatados** em pt-BR ("R$ 7,91 milhões", "R$ 1,99 milhão"),
  que o modelo só copia. `UseStructuredContent=false`: com `true`, o modelo recebia os dados em dobro.
- Nomes de campo que guiam a redação: com `by_parliamentarian` o modelo escreveu "os parlamentares que
  **receberam**"; com `sent_by_parliamentarian`, "enviadas pelos parlamentares".
- Resultado vazio diz "Consulta concluída: …" (sem isso, zero virava "a ferramenta retornou erro").
- **Valor indicado, não "recebido":** o Transferegov traz o valor indicado nos planos de ação, que não é o
  valor pago. A descrição da ferramenta e o system prompt dizem isso, mas medido: o 3B repete o verbo da
  pergunta ("recebeu" em 15 de 18 respostas quando a pergunta diz "recebeu"). Por isso a distinção ficou
  determinística: o campo `source` diz "valor indicado nos planos de ação" e vira a linha de Fonte de toda
  resposta com dados. As sugestões da tela perguntam "quanto foi indicado", e aí o modelo escreve "Foram
  indicados R$ …" (6/6).
- Erros de validação voltam como `isError` com mensagem acionável em pt-BR, antes de qualquer HTTP
  ("Há mais de um município chamado Santa Rita: Santa Rita - MA, Santa Rita - PB. Informe o estado.").

## ADR-08 · Proteções no cliente contra defeitos do modelo pequeno

- **Reparo de chamadas** (`ToolCallRepairChatClient`): converte a chamada escrita como texto
  (```json {"name": …}```, issue ollama#13519) e desembrulha argumentos no formato
  `{"type":"function","function":…,"parameters":{…}}`, só para ferramentas conhecidas.
- **Histórico compacto**: só o texto dos últimos 3 turnos. Com o histórico completo (chamadas e resultados
  antigos), o seguimento "E em 2025?" falhou 2/2; compacto, passou 2/2.
- **Linha de Fonte** montada pelo cliente a partir do resultado MCP, nunca pelo modelo.
- **Retry sem ferramentas** quando o Ollama devolve HTTP 500 (laço degenerado), mas só se nenhuma
  ferramenta foi chamada no turno, para o modelo nunca responder sem os dados que pediu.
- **Filtro de meta-texto** em turnos sem ferramenta ("Não é necessário usar a função…").
- **Regra de seguimento no prompt:** a versão anterior dizia "se esses dados já estiverem na conversa,
  responda com eles", e o modelo repetiu os números de 2026 para "E em 2025?". A regra agora manda consultar
  de novo quando o seguimento muda o ano ou o município.

## ADR-09 · PostgreSQL para histórico e trilha de auditoria

**Decisão.** EF Core 10 + Npgsql, migrations, snake_case. Tabelas `conversations`, `messages` e
`tool_calls`: toda chamada MCP (do chat ou do painel) com ferramenta, argumentos e resultado em `jsonb`,
duração, servidor, transporte e modelo. Apagar uma conversa mantém a auditoria (`ON DELETE SET NULL`).

**Por quê.** Controle e transparência sobre o que a IA consultou é o valor que um sistema de gestão pública
precisa demonstrar; e o histórico no banco deixa a API sem estado entre requisições.

## ADR-10 · Streaming por Server-Sent Events

**Decisão.** `POST /api/chat` devolve `text/event-stream` (`TypedResults.ServerSentEvents`, .NET 10) com
`conversation`, `tool_call`/`tool_result` (publicados pelo tracer enquanto o modelo trabalha), `answer` e
`done`. O Angular lê com `fetch` + `ReadableStream` e um parser próprio, porque o `EventSource` só faz GET.
SignalR seria desproporcional para um fluxo de mão única.

## ADR-11 · Docker Compose usa o Ollama da máquina por padrão

**Decisão.** O compose sobe PostgreSQL, servidor MCP (HTTP, sem porta publicada) e a API com o Angular; o
modelo fica no Ollama instalado na máquina (`host.docker.internal`). `docker-compose.ollama.yml` sobe o
Ollama em container como opção.

**Por quê.** Evita baixar outra cópia de 2 GB e aproveita GPU/Metal que o Ollama nativo usa (em container,
no Mac e no Windows sem configuração extra, a inferência fica em CPU).
