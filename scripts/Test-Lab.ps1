. "$PSScriptRoot\Common.ps1"
Push-Location "$PSScriptRoot\.."
try {
    Invoke-Docker compose config --quiet
    $headers = New-LabSession
    $other = New-LabSession
    $products = Invoke-RestMethod 'http://localhost:8080/api/produtos'
    Assert-Lab ($products.Count -ge 3) 'REST sem produtos iniciais'
    $query = Invoke-RestMethod 'http://localhost:8080/graphql' -Method Post -ContentType 'application/json' -Body '{"query":"{ pedidos { id cliente itens { quantidade produto { nome } } } }"}'
    Assert-Lab ($query.data.pedidos.Count -ge 1) 'GraphQL sem pedidos'
    $mutation = Invoke-RestMethod 'http://localhost:8080/graphql' -Method Post -ContentType 'application/json' -Body '{"query":"mutation { criarProduto(input: { nome: \"Produto E2E\", categoria: \"Teste\", preco: 10, estoque: 1 }) { produto { id nome } } }"}'
    Assert-Lab ($mutation.data.criarProduto.produto.id -gt 0) 'Mutation nao persistiu'
    $first = Request-Report $headers
    $done = Wait-Report $first.id $headers
    Assert-Lab ($done.conclusoes -eq 1) 'Resultado duplicado'
    $rows = $done.resultado | ConvertFrom-Json
    Assert-Lab ($rows.Count -ge 1 -and $rows[0].Total -gt 0) 'Relatorio sem valores de pedidos'
    $access = Invoke-WebRequest "http://localhost:8080/api/relatorios/$($first.id)" -Headers $other -SkipHttpErrorCheck
    Assert-Lab ([int]$access.StatusCode -eq 404) 'Vazamento de relatorio entre usuarios'
    Write-Host 'PASS: Identity, REST, GraphQL, mutation, relatorio e isolamento por usuario'

    Invoke-Docker compose stop worker
    $pending = Request-Report $headers
    Start-Sleep -Seconds 2
    $state = Invoke-RestMethod "http://localhost:8080/api/relatorios/$($pending.id)" -Headers $headers
    Assert-Lab ($state.status -eq 'NaFila') 'Mensagem nao permaneceu pendente'
    Invoke-Docker compose up -d worker
    $done = Wait-Report $pending.id $headers
    Assert-Lab ($done.conclusoes -eq 1) 'Recuperacao duplicou resultado'
    Write-Host 'PASS: worker parado e recuperacao'

    Invoke-Docker compose up -d --scale worker=2
    $batch = 1..4 | ForEach-Object { Request-Report $headers -Delay 2 }
    foreach ($item in $batch) {
        $done = Wait-Report $item.id $headers
        Assert-Lab ($done.conclusoes -eq 1) 'Escala duplicou resultado'
    }
    $workers = @(& docker compose ps -q worker | Where-Object { $_ })
    Assert-Lab ($workers.Count -eq 2) 'Dois workers nao ficaram ativos'
    Write-Host 'PASS: dois workers ativos e quatro resultados unicos; conferir distribuicao nos logs'

    Invoke-Docker compose up -d --scale worker=1
    $interrupted = Request-Report $headers -Delay 10
    Wait-Report $interrupted.id $headers -Status 'Processando' | Out-Null
    Invoke-Docker compose kill -s SIGKILL worker
    Invoke-Docker compose up -d worker
    $done = Wait-Report $interrupted.id $headers
    Assert-Lab ($done.conclusoes -eq 1 -and $done.tentativas -ge 2) 'Interrupcao nao produziu reentrega idempotente'
    Write-Host 'PASS: interrupcao abrupta e reentrega'

    $failed = Request-Report $headers -Delay 1 -Fail $true
    $failure = Wait-Report $failed.id $headers -Status 'Falhou'
    Assert-Lab ($failure.tentativas -eq 3 -and $failure.conclusoes -eq 0) 'Tentativas nao limitadas a tres'
    Start-Sleep -Seconds 2
    Invoke-Docker compose exec -T rabbitmq rabbitmqctl list_queues name messages
    Write-Host 'PASS: falha permanente limitada; conferir relatorios.dlq acima'

    Invoke-Docker compose up -d --force-recreate web
    Invoke-Docker compose up -d --wait --wait-timeout 180
    $persisted = Invoke-RestMethod "http://localhost:8080/api/relatorios/$($first.id)" -Headers $headers
    Assert-Lab ($persisted.status -eq 'Concluido') 'Recriacao perdeu dados ou invalidou token Identity'
    Write-Host 'PASS: persistencia e token apos recriacao'
} finally {
    Pop-Location
}
