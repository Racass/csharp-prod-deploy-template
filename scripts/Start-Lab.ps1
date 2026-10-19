param(
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{7,40}$')][string]$Version,
    [ValidatePattern('^[a-z0-9][a-z0-9._-]*/[a-z0-9][a-z0-9._-]*$')]
    [string]$ImageNamespace = 'racass/csharp-prod-deploy-template'
)

. "$PSScriptRoot\Common.ps1"
Push-Location "$PSScriptRoot\.."
try {
    Get-Command docker -ErrorAction Stop | Out-Null
    Invoke-Docker version
    Invoke-Docker compose version
    if (-not (Test-Path '.env')) { Copy-Item '.env.example' '.env' }
    $settings = Get-Content '.env'
    foreach ($entry in @{ IMAGE_NAMESPACE = $ImageNamespace; IMAGE_TAG = $Version }.GetEnumerator()) {
        $pattern = "^$($entry.Key)="
        if ($settings -match $pattern) {
            $settings = $settings -replace "$pattern.*", "$($entry.Key)=$($entry.Value)"
        } else {
            $settings += "$($entry.Key)=$($entry.Value)"
        }
    }
    Set-Content '.env' -Value $settings -Encoding utf8
    $env:IMAGE_NAMESPACE = $ImageNamespace
    $env:IMAGE_TAG = $Version
    Invoke-Docker compose config --quiet
    Invoke-Docker compose pull
    Invoke-Docker compose up -d --wait --wait-timeout 180
    Invoke-Docker compose ps
    Invoke-RestMethod 'http://localhost:8080/health' | Out-Null
    $assistant = Invoke-RestMethod 'http://localhost:8081/api/status'
    Write-Host 'Assistente: http://localhost:8081 | chat, saude e ferramentas C#'
    if (-not $assistant.chaveConfigurada) {
        Write-Warning 'GEMINI_API_KEY ausente. Configure no .env e execute docker compose up -d assistente.'
    }
    if ([string]::IsNullOrWhiteSpace($assistant.modelo)) {
        Write-Warning 'GEMINI_MODEL ausente. Escolha um modelo gratuito com ferramentas no .env e recrie o assistente.'
    }
    Write-Host 'Saude nao valida chave/cota Gemini; envie uma pergunta para verificar o provedor.'
    Write-Host 'Web: http://localhost:8080 | GraphQL: http://localhost:8080/graphql'
    Write-Host 'RabbitMQ: http://localhost:15672 | usuario: loja | senha: RABBIT_PASSWORD do .env'
    Write-Host 'Namespace e versao foram salvos no .env e definidos nesta sessao.'
} catch {
    & docker compose ps
    & docker compose logs --tail 50
    throw
} finally {
    Pop-Location
}
