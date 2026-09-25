# Smoke com o modelo real (llama3.2-mcp-v1)

Gerado por `dotnet run --project src/PublicData.Chat -- --smoke` em 25/09/2026 15:29.

Rodada 1: 24/24 OK, 1 com meta-texto.
Rodada 2: 24/24 OK, 0 com meta-texto.
Rodada 3: 24/24 OK, 0 com meta-texto.

| Rodada | Caso | Pergunta | Esperado | Resultado | Tempo | Chamadas MCP |
|---|---|---|---|---|---|---|
| 1 | capacidades | Olá! O que você consegue fazer? | NoTool | OK | 19.3 s | — |
| 1 | emendas | Quanto Campinas (SP) recebeu de emendas Pix em 2026 e de quais parlamentares? | Tool | OK | 30.2 s | get_city_amendments {"city":"Campinas","state":"SP","year":"2026"} |
| 1 | seguimento | E em 2025? | Tool | OK | 28.4 s | get_city_amendments {"city":"Campinas","state":"SP","year":2025} |
| 1 | ambigua | Quanto Santa Rita recebeu de emendas Pix em 2026? | ToolError | OK | 21.7 s | get_city_amendments {"city":"Santa Rita","year":2026} |
| 1 | sem_municipio | Quanto a prefeitura recebeu de emendas Pix em 2025? | NoData | OK | 14.5 s | get_city_amendments {"city":"","state":"","year":2025} |
| 1 | inexistente | Quanto Xyzópolis recebeu de emendas Pix em 2025? | ToolError | OK | 13.8 s | get_city_amendments {"city":"Xyzópolis","year":2025} |
| 1 | c01 | oi | NoTool | OK | 7.9 s | — |
| 1 | c02 | Obrigado pela ajuda! | NoTool | OK | 17.6 s | — |
| 1 | c03 | O que é uma emenda Pix? | NoTool | OK | 4.2 s | — |
| 1 | c04 | Que dia é hoje? | NoTool | OK | 2.1 s | — |
| 1 | c05 | que horas são? | NoTool | OK | 1.3 s | — |
| 1 | c06 | Em que ano estamos? | NoTool | OK | 1.1 s | — |
| 1 | c07 | Quanto Recife recebeu de emendas Pix em 2025? | Tool | OK | 15.0 s | get_city_amendments {"year":2025,"city":"Recife"} |
| 1 | c08 | Quais parlamentares mandaram emendas Pix para Goiânia este ano? | Tool | OK | 19.8 s | get_city_amendments {"city":"Goiánia","year":"2026"} |
| 1 | c09 | Qual a capital de Goiás? | NoTool | OK | 15.5 s | — |
| 1 | c10 | Me explique a LRF | NoTool | OK | 7.8 s | — |
| 1 | c11 | tchau | NoTool | OK | 2.7 s | — |
| 1 | c12 | Boa tarde, tudo bem? | NoTool | OK | 7.4 s | — |
| 1 | c13 | Qual o dia da semana hoje? | NoTool | OK | 1.1 s | — |
| 1 | c14 | Quanto Belo Horizonte recebeu de emendas Pix no ano passado? | Tool | OK | 22.1 s | get_city_amendments {"city":"Belo Horizonte","year":2025} |
| 1 | c15 | Por que as emendas Pix são polêmicas? | NoTool | OK | 27.4 s | — |
| 1 | c16 | Você usa inteligência artificial? | NoTool | OK | 3.2 s | — |
| 1 | c17 | Quanto Anápolis (GO) recebeu de emendas Pix em 2026? | Tool | OK | 25.5 s | get_city_amendments {"city":"Anápolis","year":2026} |
| 1 | c18 | Quem é você? | NoTool | OK | 13.3 s | — |
| 2 | capacidades | Olá! O que você consegue fazer? | NoTool | OK | 24.8 s | — |
| 2 | emendas | Quanto Campinas (SP) recebeu de emendas Pix em 2026 e de quais parlamentares? | Tool | OK | 17.1 s | get_city_amendments {"city":"Campinas","year":"2026"} |
| 2 | seguimento | E em 2025? | Tool | OK | 30.1 s | get_city_amendments {"state":"SP","year":2025,"city":"Campinas"} |
| 2 | ambigua | Quanto Santa Rita recebeu de emendas Pix em 2026? | ToolError | OK | 21.4 s | get_city_amendments {"city":"Santa Rita","year":2026} |
| 2 | sem_municipio | Quanto a prefeitura recebeu de emendas Pix em 2025? | NoData | OK | 10.9 s | get_city_amendments {"city":"","year":2025} |
| 2 | inexistente | Quanto Xyzópolis recebeu de emendas Pix em 2025? | ToolError | OK | 12.8 s | get_city_amendments {"city":"Xyzópolis","year":2025} |
| 2 | c01 | oi | NoTool | OK | 14.3 s | — |
| 2 | c02 | Obrigado pela ajuda! | NoTool | OK | 4.3 s | — |
| 2 | c03 | O que é uma emenda Pix? | NoTool | OK | 3.6 s | — |
| 2 | c04 | Que dia é hoje? | NoTool | OK | 1.8 s | — |
| 2 | c05 | que horas são? | NoTool | OK | 2.1 s | — |
| 2 | c06 | Em que ano estamos? | NoTool | OK | 0.9 s | — |
| 2 | c07 | Quanto Recife recebeu de emendas Pix em 2025? | Tool | OK | 13.0 s | get_city_amendments {"city":"Recife","year":2025} |
| 2 | c08 | Quais parlamentares mandaram emendas Pix para Goiânia este ano? | Tool | OK | 16.9 s | get_city_amendments {"city":"Goiánia","year":"2026"} |
| 2 | c09 | Qual a capital de Goiás? | NoTool | OK | 13.6 s | — |
| 2 | c10 | Me explique a LRF | NoTool | OK | 10.1 s | — |
| 2 | c11 | tchau | NoTool | OK | 1.6 s | — |
| 2 | c12 | Boa tarde, tudo bem? | NoTool | OK | 2.6 s | — |
| 2 | c13 | Qual o dia da semana hoje? | NoTool | OK | 1.0 s | — |
| 2 | c14 | Quanto Belo Horizonte recebeu de emendas Pix no ano passado? | Tool | OK | 17.8 s | get_city_amendments {"city":"Belo Horizonte","year":2025} |
| 2 | c15 | Por que as emendas Pix são polêmicas? | NoTool | OK | 11.2 s | — |
| 2 | c16 | Você usa inteligência artificial? | NoTool | OK | 13.7 s | — |
| 2 | c17 | Quanto Anápolis (GO) recebeu de emendas Pix em 2026? | Tool | OK | 19.9 s | get_city_amendments {"city":"Anápolis","year":2026} |
| 2 | c18 | Quem é você? | NoTool | OK | 9.0 s | — |
| 3 | capacidades | Olá! O que você consegue fazer? | NoTool | OK | 7.8 s | — |
| 3 | emendas | Quanto Campinas (SP) recebeu de emendas Pix em 2026 e de quais parlamentares? | Tool | OK | 14.0 s | get_city_amendments {"city":"Campinas","year":"2026"} |
| 3 | seguimento | E em 2025? | Tool | OK | 39.1 s | get_city_amendments {"city":"Campinas","state":"SP","year":2025} |
| 3 | ambigua | Quanto Santa Rita recebeu de emendas Pix em 2026? | ToolError | OK | 13.9 s | get_city_amendments {"city":"Santa Rita","year":2026} |
| 3 | sem_municipio | Quanto a prefeitura recebeu de emendas Pix em 2025? | NoData | OK | 20.3 s | get_city_amendments {"city":"","state":"","year":2025} |
| 3 | inexistente | Quanto Xyzópolis recebeu de emendas Pix em 2025? | ToolError | OK | 13.2 s | get_city_amendments {"city":"Xyzópolis","year":2025} |
| 3 | c01 | oi | NoTool | OK | 7.5 s | — |
| 3 | c02 | Obrigado pela ajuda! | NoTool | OK | 4.8 s | — |
| 3 | c03 | O que é uma emenda Pix? | NoTool | OK | 3.6 s | — |
| 3 | c04 | Que dia é hoje? | NoTool | OK | 1.7 s | — |
| 3 | c05 | que horas são? | NoTool | OK | 2.2 s | — |
| 3 | c06 | Em que ano estamos? | NoTool | OK | 1.0 s | — |
| 3 | c07 | Quanto Recife recebeu de emendas Pix em 2025? | Tool | OK | 13.2 s | get_city_amendments {"city":"Recife","year":2025} |
| 3 | c08 | Quais parlamentares mandaram emendas Pix para Goiânia este ano? | Tool | OK | 21.9 s | get_city_amendments {"year":"2026","city":"Goiánia"} |
| 3 | c09 | Qual a capital de Goiás? | NoTool | OK | 7.0 s | — |
| 3 | c10 | Me explique a LRF | NoTool | OK | 6.5 s | — |
| 3 | c11 | tchau | NoTool | OK | 1.6 s | — |
| 3 | c12 | Boa tarde, tudo bem? | NoTool | OK | 2.5 s | — |
| 3 | c13 | Qual o dia da semana hoje? | NoTool | OK | 1.0 s | — |
| 3 | c14 | Quanto Belo Horizonte recebeu de emendas Pix no ano passado? | Tool | OK | 18.6 s | get_city_amendments {"city":"Belo Horizonte","year":2025} |
| 3 | c15 | Por que as emendas Pix são polêmicas? | NoTool | OK | 18.8 s | — |
| 3 | c16 | Você usa inteligência artificial? | NoTool | OK | 1.5 s | — |
| 3 | c17 | Quanto Anápolis (GO) recebeu de emendas Pix em 2026? | Tool | OK | 22.5 s | get_city_amendments {"city":"Anápolis","year":2026} |
| 3 | c18 | Quem é você? | NoTool | OK | 9.5 s | — |
