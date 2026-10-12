. "$PSScriptRoot\Common.ps1"
Push-Location "$PSScriptRoot\.."
try {
    Invoke-Docker compose config --quiet
    Invoke-Docker compose up -d --wait --wait-timeout 180 --scale worker=1
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
    $orders = @(Invoke-RestMethod 'http://localhost:8080/api/pedidos')
    Assert-Lab (@($rows).Count -eq $orders.Count) 'Relatorio omitiu ou duplicou pedidos'
    foreach ($order in $orders) {
        $row = $rows | Where-Object { $_.Id -eq $order.id }
        [decimal]$expected = 0
        foreach ($line in $order.itens) {
            $expected += [decimal]$line.quantidade * [decimal]$line.valorUnitario
        }
        Assert-Lab ([decimal]$row.Total -eq $expected -and $row.Cliente -eq $order.cliente) "Relatorio incorreto para pedido $($order.id)"
    }
    $http = New-Object System.Net.Http.HttpClient
    try {
        $http.DefaultRequestHeaders.Add('Authorization', $other.Authorization)
        $access = $http.GetAsync("http://localhost:8080/api/relatorios/$($first.id)").GetAwaiter().GetResult()
        Assert-Lab ([int]$access.StatusCode -eq 404) 'Vazamento de relatorio entre usuarios'
        $access.Dispose()
    } finally {
        $http.Dispose()
    }
    Write-Host "PASS: Identity, REST, GraphQL, mutation, relatorio e isolamento por usuario; id=$($first.id) tentativas=$($done.tentativas) conclusoes=$($done.conclusoes)"
    $duplicate = @{
        properties = @{ delivery_mode = 2; content_type = 'application/json'; message_id = $first.id }
        routing_key = 'relatorios'
        payload = (@{ ReportId = $first.id } | ConvertTo-Json -Compress)
        payload_encoding = 'string'
    } | ConvertTo-Json
    $published = Invoke-RestMethod 'http://localhost:15672/api/exchanges/%2F/amq.default/publish' `
        -Headers (Get-RabbitHeaders) -Method Post -ContentType 'application/json' -Body $duplicate
    Assert-Lab ([bool]$published.routed) 'Duplicata nao foi roteada para fila'
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $duplicateLogs = @(& docker compose logs --no-log-prefix worker)
        $ignored = $duplicateLogs | Where-Object { $_ -match "Relatorio $($first.id) ja concluido; duplicata ignorada" }
        if ($ignored) { break }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    Assert-Lab ([bool]$ignored) 'Worker nao confirmou tratamento da duplicata'
    $again = Wait-Report $first.id $headers
    Assert-Lab ($again.conclusoes -eq 1 -and $again.tentativas -eq $done.tentativas) 'Reentrega apos conclusao repetiu efeito'
    Write-Host "PASS: duplicata apos conclusao ignorada sem repetir efeito ou tentativa; id=$($first.id) tentativas=$($again.tentativas) conclusoes=$($again.conclusoes)"

    Invoke-Docker compose stop worker
    $pending = Request-Report $headers
    Start-Sleep -Seconds 2
    $state = Invoke-RestMethod "http://localhost:8080/api/relatorios/$($pending.id)" -Headers $headers
    Assert-Lab ($state.status -eq 'NaFila') 'Mensagem nao permaneceu pendente'
    $queued = Invoke-RestMethod 'http://localhost:15672/api/queues/%2F/relatorios/get' `
        -Headers (Get-RabbitHeaders) -Method Post -ContentType 'application/json' `
        -Body '{"count":100,"ackmode":"ack_requeue_true","encoding":"auto"}'
    Assert-Lab ([bool]($queued | Where-Object { $_.payload -match [regex]::Escape($pending.id) })) 'Mensagem solicitada nao encontrada na fila'
    Invoke-Docker compose up -d --force-recreate rabbitmq
    Invoke-Docker compose up -d --wait --wait-timeout 180 rabbitmq
    Invoke-Docker compose up -d worker
    $done = Wait-Report $pending.id $headers
    Assert-Lab ($done.conclusoes -eq 1) 'Recuperacao duplicou resultado'
    Write-Host "PASS: worker parado, mensagem sobrevive a recriacao do broker e recuperacao; id=$($pending.id) conclusoes=$($done.conclusoes)"

    Invoke-Docker compose up -d --scale worker=2
    $batch = 1..4 | ForEach-Object { Request-Report $headers -Delay 2 }
    foreach ($item in $batch) {
        $done = Wait-Report $item.id $headers
        Assert-Lab ($done.conclusoes -eq 1) 'Escala duplicou resultado'
    }
    $workers = @(& docker compose ps -q worker | Where-Object { $_ })
    Assert-Lab ($workers.Count -eq 2) 'Dois workers nao ficaram ativos'
    $logs = @(& docker compose logs --no-log-prefix worker)
    $ids = ($batch.id | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $distribution = @($logs | Where-Object { $_ -match "Relatorio ($ids) tentativa \d+ worker " } |
        ForEach-Object { if ($_ -match 'worker (\S+)$') { $Matches[1] } } | Sort-Object -Unique)
    Assert-Lab ($distribution.Count -eq 2) 'Lote nao demonstrou processamento por dois workers'
    Write-Host "PASS: quatro resultados unicos, distribuidos entre workers $($distribution -join ', '); lote=$($batch.id -join ', ')"

    Invoke-Docker compose up -d --scale worker=1
    $interrupted = Request-Report $headers -Delay 10
    Wait-Report $interrupted.id $headers -Status 'Processando' | Out-Null
    Invoke-Docker compose kill -s SIGKILL worker
    Invoke-Docker compose up -d worker
    $done = Wait-Report $interrupted.id $headers
    Assert-Lab ($done.conclusoes -eq 1 -and $done.tentativas -ge 2) 'Interrupcao nao produziu reentrega idempotente'
    Write-Host "PASS: interrupcao abrupta e reentrega; id=$($interrupted.id) tentativas=$($done.tentativas) conclusoes=$($done.conclusoes)"

    $failed = Request-Report $headers -Delay 1 -Fail $true
    $failure = Wait-Report $failed.id $headers -Status 'Falhou'
    Assert-Lab ($failure.tentativas -eq 3 -and $failure.conclusoes -eq 0) 'Tentativas nao limitadas a tres'
    Start-Sleep -Seconds 2
    $messages = Invoke-RestMethod 'http://localhost:15672/api/queues/%2F/relatorios.dlq/get' `
        -Headers (Get-RabbitHeaders) -Method Post -ContentType 'application/json' `
        -Body '{"count":100,"ackmode":"ack_requeue_true","encoding":"auto"}'
    Assert-Lab ([bool]($messages | Where-Object { $_.payload -match [regex]::Escape($failed.id) })) 'Mensagem de falha nao encontrada na DLQ'
    Write-Host "PASS: tres tentativas e mensagem identificada na DLQ (mantida na fila); id=$($failed.id) tentativas=$($failure.tentativas) conclusoes=$($failure.conclusoes)"

    Invoke-Docker compose up -d --force-recreate web
    Invoke-Docker compose up -d --wait --wait-timeout 180
    $persisted = Invoke-RestMethod "http://localhost:8080/api/relatorios/$($first.id)" -Headers $headers
    Assert-Lab ($persisted.status -eq 'Concluido') 'Recriacao perdeu dados ou invalidou token Identity'
    Write-Host "PASS: persistencia e token apos recriacao; id=$($first.id) status=$($persisted.status)"
} finally {
    Pop-Location
}
