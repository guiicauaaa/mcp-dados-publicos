# Regenera src/PublicData.McpServer/Data/municipios.csv a partir do SICONFI (Tesouro Nacional), endpoint /entes.
# O snapshot fica embutido no servidor MCP porque o /entes leva ~36 s a frio: resolver o município não custa rede.
# Uso (na raiz do repositório): pwsh scripts/update-municipios.ps1

$ErrorActionPreference = 'Stop'
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'siconfi-entes.json'
Invoke-WebRequest -Uri 'https://apidatalake.tesouro.gov.br/ords/siconfi/tt/entes' -OutFile $tmp -TimeoutSec 120

$json = Get-Content $tmp -Raw -Encoding UTF8 | ConvertFrom-Json
if ($json.hasMore) { throw 'Resposta paginada: repetir com ?offset= e juntar as páginas.' }

# O Transferegov registra Brasília pelo CNPJ do Distrito Federal (00394601000126), não pelo CNPJ que o SICONFI
# lista para o ente municipal 5300108; com o CNPJ do SICONFI, a consulta de emendas volta sempre vazia.
$cnpjOverrides = @{ '5300108' = '00394601000126' }

$lines = @('ibge;name;uf;sphere;cnpj;population;capital') + (
    $json.items |
        Where-Object { $_.esfera -eq 'M' } |
        Sort-Object { $_.cod_ibge } |
        ForEach-Object {
            $cnpj = if ($cnpjOverrides.ContainsKey("$($_.cod_ibge)")) { $cnpjOverrides["$($_.cod_ibge)"] } else { $_.cnpj }
            '{0};{1};{2};{3};{4};{5};{6}' -f $_.cod_ibge, $_.ente.Trim(), $_.uf, $_.esfera, $cnpj, $_.populacao, "$($_.capital)".Trim()
        })

$target = Join-Path $PSScriptRoot '..' 'src' 'PublicData.McpServer' 'Data' 'municipios.csv'
[IO.File]::WriteAllText($target, ($lines -join "`n") + "`n", (New-Object Text.UTF8Encoding $false))
Write-Host "Gravados $($lines.Count - 1) municípios em $target"
