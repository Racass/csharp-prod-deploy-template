. "$PSScriptRoot\Common.ps1"
Push-Location "$PSScriptRoot\.."
try {
    $headers = New-LabSession
    Invoke-Docker compose stop rabbitmq
    $http = New-Object System.Net.Http.HttpClient
    try {
        $http.DefaultRequestHeaders.Add('Authorization', $headers.Authorization)
        $http.Timeout = [TimeSpan]::FromSeconds(30)
        $content = New-Object System.Net.Http.StringContent(
            '{"atrasoSegundos":1}', [Text.Encoding]::UTF8, 'application/json')
        $response = $http.PostAsync('http://localhost:8080/api/relatorios', $content).GetAwaiter().GetResult()
        Assert-Lab ([int]$response.StatusCode -eq 503) 'Broker indisponivel retornou falsa aceitacao'
        $errorBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        $report = Invoke-RestMethod "http://localhost:8080/api/relatorios/$($errorBody.id)" -Headers $headers
        Assert-Lab ($report.status -eq 'FalhaPublicacao') 'Falha de publicacao nao ficou observavel'
        $response.Dispose()
        $content.Dispose()
    } finally {
        $http.Dispose()
        Invoke-Docker compose up -d --wait --wait-timeout 180 rabbitmq
    }
    $next = Request-Report $headers
    $done = Wait-Report $next.id $headers
    Assert-Lab ($done.conclusoes -eq 1) 'Worker nao recuperou conexao apos retorno do broker'
    Write-Host 'PASS: broker indisponivel retorna 503 com estado explicito; nova tarefa conclui apos recuperacao'
} finally {
    Pop-Location
}
