<#
.SYNOPSIS
  Gate de cobertura do RiskModule (SDD 5.1: minimo de 90%).
.DESCRIPTION
  Roda os testes unitarios e de integracao (Docker necessario), mescla a cobertura e falha se a cobertura de
  linhas de RiskGuard.Modules.Risk ficar abaixo do minimo. Migrations (geradas) e a fabrica de design-time do EF
  ficam fora da conta. Funciona no Windows PowerShell 5.1 e no PowerShell 7 (CI).
#>
param(
    [int]$Threshold = 90,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'coverage'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

$include = '[RiskGuard.Modules.Risk]*'
$excludeFiles = '**/Migrations/*.cs%2c**/*DbContextFactory.cs'
$unitJson = Join-Path $out 'unit.json'

dotnet test (Join-Path $root 'tests/RiskGuard.UnitTests') -c $Configuration `
    /p:CollectCoverage=true "/p:CoverletOutput=$unitJson" /p:CoverletOutputFormat=json `
    "/p:Include=$include" "/p:ExcludeByFile=$excludeFiles"
if ($LASTEXITCODE -ne 0) { throw 'Testes unitarios falharam.' }

dotnet test (Join-Path $root 'tests/RiskGuard.IntegrationTests') -c $Configuration `
    /p:CollectCoverage=true "/p:CoverletOutput=$out/" "/p:MergeWith=$unitJson" `
    '/p:CoverletOutputFormat="json%2ccobertura"' `
    "/p:Include=$include" "/p:ExcludeByFile=$excludeFiles" `
    "/p:Threshold=$Threshold" /p:ThresholdType=line /p:ThresholdStat=total
if ($LASTEXITCODE -ne 0) { throw "Cobertura de linhas do RiskModule abaixo de $Threshold% (ou testes falharam)." }

Write-Host "Cobertura do RiskModule >= $Threshold%. Relatorio: $out" -ForegroundColor Green
