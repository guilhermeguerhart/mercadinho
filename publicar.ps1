# Gera o executável único em .\publish\MercadinhoSeuZe.exe
# Com -Desenvolvimento, gera em .\publish-dev\ uma versão com os atalhos de teste (Alt+1 no login).
param([switch]$Desenvolvimento)
$ErrorActionPreference = "Stop"
$projeto = Join-Path $PSScriptRoot "src\MercadinhoSeuZe\MercadinhoSeuZe.csproj"

if (-not (Test-Path (Join-Path $PSScriptRoot "src\MercadinhoSeuZe\appsettings.json"))) {
    Write-Warning "appsettings.json não encontrado: o .exe vai abrir, mas o login não vai funcionar. Veja o README."
}

$pasta = if ($Desenvolvimento) { "publish-dev" } else { "publish" }
$extra = if ($Desenvolvimento) { "-p:Desenvolvimento=true" } else { "-p:Desenvolvimento=false" }

dotnet publish $projeto -c Release -o (Join-Path $PSScriptRoot $pasta) $extra
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falhou. Se o erro for 'Access ... denied', feche o MercadinhoSeuZe.exe aberto e rode de novo."
    exit 1
}
Write-Host "Pronto: $pasta\MercadinhoSeuZe.exe"
