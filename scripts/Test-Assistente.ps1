param(
    [string]$AssistantUrl = 'http://localhost:8081',
    [string]$StoreUrl = 'http://localhost:8080'
)

$ErrorActionPreference = 'Stop'
$status = Invoke-RestMethod "$AssistantUrl/api/status"
if (-not $status.chaveConfigurada) {
    throw 'Gemini real nao validado: configure GEMINI_API_KEY no assistente.'
}
if (-not $status.lojaDisponivel) { throw 'A loja nao esta disponivel para a validacao.' }
$products = @(Invoke-RestMethod "$StoreUrl/api/produtos")
$orders = @(Invoke-RestMethod "$StoreUrl/api/pedidos")
if ($products.Count -eq 0 -or $orders.Count -eq 0) {
    throw 'A demonstracao exige ao menos um produto e um pedido ficticio.'
}

function Invoke-Question {
    param([string]$Question, [string]$Tool)
    $body = @{ pergunta = $Question } | ConvertTo-Json
    $answer = Invoke-RestMethod "$AssistantUrl/api/chat" -Method Post `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 100
    if ([string]::IsNullOrWhiteSpace($answer.resposta)) { throw 'Resposta final vazia.' }
    $trace = @($answer.ferramentas | Where-Object { $_.nome -eq $Tool })
    if ($trace.Count -eq 0) { throw "A consulta nao executou a ferramenta $Tool." }
    return $trace[-1]
}

$catalog = Invoke-Question 'Consulte o catalogo atual completo usando ConsultarProdutos sem filtros.' 'ConsultarProdutos'
if ($catalog.resultado.totalEncontrados -ne $products.Count) {
    throw 'Contagem do catalogo difere da fonte.'
}
foreach ($product in $catalog.resultado.produtos) {
    $source = $products | Where-Object { $_.id -eq $product.id } | Select-Object -First 1
    if (-not $source -or $source.nome -ne $product.nome -or
        [decimal]$source.preco -ne [decimal]$product.preco -or $source.estoque -ne $product.estoque) {
        throw "Produto $($product.id) diverge da fonte."
    }
}
Write-Host 'PASS Gemini real: catalogo consultado e conferido.'

$id = $orders[0].id
$detail = Invoke-Question "Consulte o pedido $id usando ConsultarPedido e informe o estado e o total." 'ConsultarPedido'
$sourceOrder = Invoke-RestMethod "$StoreUrl/api/pedidos/$id"
[decimal]$expectedTotal = 0
foreach ($item in $sourceOrder.itens) { $expectedTotal += [decimal]$item.valorUnitario * $item.quantidade }
if ($detail.resultado.id -ne $id -or $detail.resultado.status -ne $sourceOrder.status -or
    [decimal]$detail.resultado.total -ne $expectedTotal) {
    throw 'Detalhe de pedido diverge da fonte.'
}
Write-Host 'PASS Gemini real: pedido e total conferidos.'

$summary = Invoke-Question 'Use ResumirPedidos para consultar contagens e valor total de todos os pedidos ativos.' 'ResumirPedidos'
[decimal]$expectedSummary = 0
foreach ($order in $orders) {
    foreach ($item in $order.itens) { $expectedSummary += [decimal]$item.valorUnitario * $item.quantidade }
}
if ($summary.resultado.totalPedidos -ne $orders.Count -or
    [decimal]$summary.resultado.valorTotal -ne $expectedSummary) {
    throw 'Resumo diverge da fonte.'
}
Write-Host 'PASS Gemini real: resumo conferido. Analise tambem a fidelidade da resposta textual na interface.'
