<#
.SYNOPSIS
  Sobe a API em modo Production NESTA maquina e a expoe com Cloudflare Tunnel, para testar webhooks de lojas reais (Shopify, Nuvemshop).
.DESCRIPTION
  1. confere dotnet, docker e cloudflared;
  2. sobe PostgreSQL e Redis (docker compose) e espera o banco ficar pronto;
  3. aplica as migrations em um processo separado, com a credencial administrativa (a API nunca recebe essa credencial);
  4. sobe a API em Production, com os segredos do Auth0 lidos dos user-secrets, e espera /health/live;
  5. abre um Quick Tunnel (sem conta e sem dominio) e imprime as URLs de webhook.

  POR QUE Production: em Development o painel do Hangfire fica aberto para qualquer um; o tunel o tornaria publico.
  A URL do Quick Tunnel muda a cada execucao e nao tem garantia de disponibilidade: serve para teste, nao para deixar no ar.
  Tudo roda no seu computador: se ele dormir, os webhooks falham (as lojas costumam reenviar).

  Pre-requisitos: docker, .NET 8 SDK, cloudflared (winget install Cloudflare.cloudflared) e os dados do Auth0 nos user-secrets
  do projeto RiskGuard.Api (docs/auth0-setup.md): pelo menos Identity:Audience e Identity:Auth0:Domain (ou Identity:Authority).
.PARAMETER TenantId
  Id do lojista (impresso pela CLI --provision-tenant). Entra nas URLs de webhook impressas.
.PARAMETER Port
  Porta local da API (padrao 5080).
.PARAMETER SkipMigrate
  Nao roda as migrations (use quando o banco ja esta atualizado).
.EXAMPLE
  ./scripts/dev-tunnel.ps1 -TenantId 3f2c0d3e-1111-2222-3333-444455556666
#>
[CmdletBinding()]
param(
    [string]$TenantId,
    [int]$Port = 5080,
    [switch]$SkipMigrate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $root 'src/RiskGuard.Api/RiskGuard.Api.csproj'
$logDir = Join-Path $root '.dev-tunnel'
$localUrl = "http://localhost:$Port"

function Require-Command([string]$name, [string]$hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "'$name' nao encontrado. $hint"
    }
}

Require-Command 'dotnet' 'Instale o .NET 8 SDK.'
Require-Command 'docker' 'Instale e abra o Docker Desktop.'
Require-Command 'cloudflared' 'Instale com: winget install Cloudflare.cloudflared (depois abra um novo terminal).'

# --- segredos do Auth0 (user-secrets nao sao lidos fora de Development, entao viram variaveis de ambiente deste processo) ---
$secretEnv = @{}
foreach ($line in (& dotnet user-secrets list --project $apiProject)) {
    if ($line -match '^(?<key>[^=]+?)\s*=\s*(?<value>.*)$') {
        $secretEnv[$Matches.key.Trim()] = $Matches.value
    }
}
$hasAudience = $secretEnv.ContainsKey('Identity:Audience')
$hasProvider = $secretEnv.ContainsKey('Identity:Auth0:Domain') -or $secretEnv.ContainsKey('Identity:Authority')
if (-not ($hasAudience -and $hasProvider)) {
    throw "Faltam dados do Auth0 nos user-secrets (Identity:Audience e Identity:Auth0:Domain ou Identity:Authority). Veja docs/auth0-setup.md, secao 6."
}

# --- infra local ---
Push-Location $root
try {
    & docker compose up -d
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up falhou. O Docker esta rodando?' }

    $ready = $false
    for ($i = 0; $i -lt 40 -and -not $ready; $i++) {
        & docker compose exec -T postgres pg_isready -U riskguard -d riskguard *> $null
        $ready = ($LASTEXITCODE -eq 0)
        if (-not $ready) { Start-Sleep -Seconds 2 }
    }
    if (-not $ready) { throw 'O PostgreSQL nao ficou pronto a tempo.' }
}
finally { Pop-Location }

# --- configuracao comum (credenciais de DESENVOLVIMENTO local, as mesmas do docker-compose) ---
$dev = Get-Content (Join-Path $root 'src/RiskGuard.Api/appsettings.Development.json') -Raw | ConvertFrom-Json
foreach ($pair in $secretEnv.GetEnumerator()) {
    Set-Item -Path ("Env:" + ($pair.Key -replace ':', '__')) -Value $pair.Value
}
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = $localUrl
$env:Postgres__ConnectionString = $dev.Postgres.ConnectionString
$env:Postgres__JobsConnectionString = $dev.Postgres.JobsConnectionString
$env:Redis__ConnectionString = $dev.Redis.ConnectionString
if (-not $env:Security__EncryptionKey) { $env:Security__EncryptionKey = $dev.Security.EncryptionKey }
# o cloudflared chega na API por loopback: sem isso o rate limit veria 127.0.0.1 para todo mundo
$env:Proxy__KnownProxies__0 = '127.0.0.1'

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# --- migrations: processo separado, com a credencial administrativa (a API nao a recebe) ---
if (-not $SkipMigrate) {
    Write-Host 'Aplicando migrations...' -ForegroundColor Cyan
    $env:Postgres__AdminConnectionString = $dev.Postgres.AdminConnectionString
    try {
        & dotnet run --project $apiProject -c Release --no-launch-profile -- --migrate
        if ($LASTEXITCODE -ne 0) { throw 'As migrations falharam.' }
    }
    finally { Remove-Item Env:Postgres__AdminConnectionString -ErrorAction SilentlyContinue }
}

$api = $null
$tunnel = $null
try {
    # --- API ---
    Write-Host "Subindo a API em Production ($localUrl)..." -ForegroundColor Cyan
    $api = Start-Process dotnet -ArgumentList @('run', '--project', "`"$apiProject`"", '-c', 'Release', '--no-launch-profile') `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir 'api.log') -RedirectStandardError (Join-Path $logDir 'api.err.log')

    $up = $false
    for ($i = 0; $i -lt 40 -and -not $up; $i++) {
        if ($api.HasExited) { throw "A API encerrou ao subir. Veja $logDir\api.err.log e $logDir\api.log." }
        try { $up = (Invoke-WebRequest "$localUrl/health/live" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $up) { throw "A API nao respondeu em /health/live. Veja $logDir\api.err.log." }

    # --- tunel ---
    Write-Host 'Abrindo o tunel...' -ForegroundColor Cyan
    $tunnelLog = Join-Path $logDir 'cloudflared.log'
    Remove-Item $tunnelLog -ErrorAction SilentlyContinue
    $tunnel = Start-Process cloudflared -ArgumentList @('tunnel', '--url', $localUrl, '--no-autoupdate') `
        -PassThru -WindowStyle Hidden -RedirectStandardError $tunnelLog -RedirectStandardOutput (Join-Path $logDir 'cloudflared.out.log')

    $publicUrl = $null
    for ($i = 0; $i -lt 40 -and -not $publicUrl; $i++) {
        if ($tunnel.HasExited) { throw "O cloudflared encerrou. Veja $tunnelLog." }
        if (Test-Path $tunnelLog) {
            $match = Select-String -Path $tunnelLog -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' | Select-Object -First 1
            if ($match) { $publicUrl = $match.Matches[0].Value }
        }
        if (-not $publicUrl) { Start-Sleep -Seconds 1 }
    }
    if (-not $publicUrl) { throw "Nao consegui ler a URL do tunel. Veja $tunnelLog." }

    $tenant = if ($TenantId) { $TenantId } else { '<tenantId>' }
    Write-Host ''
    Write-Host 'No ar. Esta URL muda a cada execucao.' -ForegroundColor Green
    Write-Host "  API publica:          $publicUrl"
    Write-Host "  Webhook Shopify:      $publicUrl/api/v1/webhooks/shopify/$tenant"
    Write-Host "  Webhook Nuvemshop:    $publicUrl/api/v1/webhooks/nuvemshop/$tenant"
    Write-Host "  Logs:                 $logDir"
    Write-Host ''
    Write-Host 'Ctrl+C para encerrar a API e o tunel.' -ForegroundColor Yellow

    while (-not $api.HasExited -and -not $tunnel.HasExited) { Start-Sleep -Seconds 2 }
    if ($api.HasExited) { Write-Warning "A API encerrou. Veja $logDir\api.err.log." }
    if ($tunnel.HasExited) { Write-Warning "O tunel encerrou. Veja $tunnelLog." }
}
finally {
    foreach ($p in @($tunnel, $api)) {
        # /T encerra a arvore: `dotnet run` inicia a API como processo filho
        if ($p -and -not $p.HasExited) { & taskkill /PID $p.Id /T /F *> $null }
    }
    Write-Host 'Encerrado.'
}
