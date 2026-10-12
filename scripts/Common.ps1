$ErrorActionPreference = 'Stop'

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) {
        throw "Docker falhou ($LASTEXITCODE): docker $($args -join ' ')"
    }
}

function Assert-Lab {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Wait-Report {
    param([guid]$Id, [hashtable]$Headers, [string]$Status = 'Concluido', [int]$Timeout = 120)
    $deadline = (Get-Date).AddSeconds($Timeout)
    do {
        $report = Invoke-RestMethod "http://localhost:8080/api/relatorios/$Id" -Headers $Headers
        if ($report.status -eq $Status) { return $report }
        if ($report.status -in @('Falhou', 'FalhaPublicacao') -and $report.status -ne $Status) {
            throw "Relatorio $Id terminou em $($report.status): $($report.erro)"
        }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    throw "Timeout aguardando $Status para $Id; ultimo estado: $($report.status)"
}

function New-LabSession {
    $email = "aluno-$([guid]::NewGuid().ToString('N'))@example.test"
    $body = @{ email = $email; password = 'Laboratorio#2026' } | ConvertTo-Json
    Invoke-RestMethod 'http://localhost:8080/register' -Method Post -ContentType 'application/json' -Body $body | Out-Null
    $login = Invoke-RestMethod 'http://localhost:8080/login' -Method Post -ContentType 'application/json' -Body $body
    return @{ Authorization = "Bearer $($login.accessToken)" }
}

function Request-Report {
    param([hashtable]$Headers, [int]$Delay = 1, [bool]$Fail = $false, [int]$PublishDelay = 0)
    $body = @{ atrasoSegundos = $Delay; falhaPermanente = $Fail; atrasoPublicacaoSegundos = $PublishDelay } | ConvertTo-Json
    return Invoke-RestMethod 'http://localhost:8080/api/relatorios' -Method Post -Headers $Headers -ContentType 'application/json' -Body $body
}
