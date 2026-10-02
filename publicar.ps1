# Gera o executável único em .\publish\MercadinhoSeuZe.exe
$ErrorActionPreference = "Stop"
$projeto = Join-Path $PSScriptRoot "src\MercadinhoSeuZe\MercadinhoSeuZe.csproj"

if (-not (Test-Path (Join-Path $PSScriptRoot "src\MercadinhoSeuZe\appsettings.json"))) {
    Write-Warning "appsettings.json não encontrado: o .exe vai abrir, mas o login não vai funcionar. Veja o README."
}

dotnet publish $projeto -c Release -o (Join-Path $PSScriptRoot "publish")
Write-Host "Pronto: publish\MercadinhoSeuZe.exe"
