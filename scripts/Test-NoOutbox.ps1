. "$PSScriptRoot\Common.ps1"
Push-Location "$PSScriptRoot\.."
try {
    $headers = New-LabSession
    $before = @(Invoke-RestMethod 'http://localhost:8080/api/relatorios' -Headers $headers)
    $job = Start-Job -ScriptBlock {
        param($headers)
        Invoke-RestMethod 'http://localhost:8080/api/relatorios' -Method Post -Headers $headers -ContentType 'application/json' -Body '{"atrasoSegundos":1,"atrasoPublicacaoSegundos":30}'
    } -ArgumentList $headers
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $reports = @(Invoke-RestMethod 'http://localhost:8080/api/relatorios' -Headers $headers)
        $orphan = $reports | Where-Object { $_.status -eq 'AguardandoPublicacao' -and $_.id -notin $before.id } | Select-Object -First 1
        if ($orphan) { break }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    Assert-Lab ([bool]$orphan) 'Solicitacao nao entrou na janela anterior a publicacao'
    Invoke-Docker compose kill -s SIGKILL web
    Wait-Job $job -Timeout 15 | Out-Null
    Write-Host "Estado do pedido HTTP interrompido: $($job.State); falha de conexao esperada."
    if ($job.State -eq 'Running') { Stop-Job $job }
    Remove-Job $job -Force
    Invoke-Docker compose up -d --wait --wait-timeout 180
    Start-Sleep -Seconds 3
    $after = Invoke-RestMethod "http://localhost:8080/api/relatorios/$($orphan.id)" -Headers $headers
    Assert-Lab ($after.status -eq 'AguardandoPublicacao' -and $after.conclusoes -eq 0) 'Janela sem outbox nao foi reproduzida'
    Write-Host "PASS: limitacao sem outbox reproduzida. Relatorio $($orphan.id) persistiu, mas nao foi publicado."
} finally {
    if ($job -and $job.State -eq 'Running') { Stop-Job $job; Remove-Job $job -Force }
    Pop-Location
}
