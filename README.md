# Loja: mensageria, entrega e operacao local

Template da Aula 08: continuidade da loja da Aula 07, com GraphQL (Graph Query
Language), ASP.NET Core Identity e API (Application Programming Interface)
REST (Representational State Transfer). Inclui PostgreSQL, RabbitMQ, web e
worker C# em .NET 10.

**Isto e um laboratorio, nao uma configuracao pronta para producao.**
Compartilhar banco entre web e worker e uma simplificacao; quatro conteineres
nao equivalem a quatro microservicos autonomos. Docker Compose nao e Kubernetes.

## Comece aqui

Use **Use this template → Create a new repository** e crie um repositorio
publico. Nao use fork para esta atividade: queremos verificar a publicacao
no namespace do seu proprio repositorio. Clone no computador e abra PowerShell
na raiz. Requisitos: Docker Desktop iniciado com conteineres Linux e Docker
Compose com suporte a `--wait`. Git e acesso ao GitHub/registro tambem sao
necessarios. SDK (Software Development Kit) .NET 10 so e necessario para
desenvolver/testar C# fora dos conteineres.

```powershell
docker version
docker compose version
```

O Docker Desktop inclui Compose. Se `docker` nao for encontrado depois de
uma instalacao, abra um novo terminal. Se o mecanismo Linux nao responder,
inicie o Docker Desktop e resolva os avisos de virtualizacao antes da atividade.

## Primeira execucao

O professor fornece a versao completa de commit de uma imagem publica de
referencia. Nao e preciso configurar contas de nuvem ou autenticar no registro.

```powershell
.\scripts\Start-Lab.ps1 -Version (Get-Content .\reference-version.txt).Trim()
```

`reference-version.txt` identifica uma versao de referencia publicada pelo
professor. Use esse arquivo somente para a primeira execucao; depois informe
o commit que voce publicou no seu proprio namespace.

O script copia `.env.example` para `.env` se ainda nao existir, baixa as imagens
e espera o ambiente ficar pronto. O argumento de versao e um commit hexadecimal
real, nao o texto de exemplo. O namespace e a versao ficam no `.env` e no ambiente da sessao PowerShell.
Um novo terminal consegue operar a ultima versao configurada pelo script.

- Web: http://localhost:8080 — cadastre uma conta ficticia e entre.
- GraphQL/Nitro: http://localhost:8080/graphql — consultas e mutation da Aula 07.
- RabbitMQ: http://localhost:15672 — usuario `loja`, senha indicada por
  `RABBIT_PASSWORD` em `.env`.

Solicite um relatorio no painel. Veja fila `relatorios`, estado, tentativas e
resultado. O resultado resume pedidos de demonstracao da loja; somente o
solicitante pode consultar seu relatorio.

## Seu exercicio: GitHub Actions

CI (Continuous Integration, integracao continua) executa testes e constroi
imagens. A entrega publica imagens no GHCR (GitHub Container Registry); o
deploy local e **manual**, nao CD (Continuous Deployment, deploy continuo).

Na branch principal, `.github\workflows\entrega.yml` e um exercicio: os testes
funcionam, mas a publicacao ainda precisa ser implementada. Complete:

1. Job `publish` dependente de `tests`, com `packages: write` somente nesse job.
2. Checkout e namespace em minusculas derivado de `github.repository`.
3. Login em `ghcr.io` com `github.actor` e `secrets.GITHUB_TOKEN`.
4. Build/push dos dois Dockerfiles, usando o commit completo como tag.
5. Resumo informando namespace e versao publicados.

Use o [gabarito do template original](https://github.com/Racass/csharp-prod-deploy-template/tree/gabarito)
como referencia; ele nao aparece automaticamente no repositorio criado a
partir da branch principal. Nao copie
credenciais pessoais para YAML (YAML Ain't Markup Language) ou para o Git.
O token do workflow e fornecido pelo GitHub; nao crie um token pessoal.

Depois de um push, acompanhe **Actions → entrega**. O workflow precisa terminar
com os testes e as duas publicacoes aprovados.

**Primeira publicacao:** repositorio publico nao torna o pacote automaticamente
publico. Na sua pagina de Packages, abra cada pacote, **Package settings →
Change visibility → Public** se estiver privado, confirme e teste o download
sem login. Se ja estiver publico, nao precisa alterar.
Se a interface nao oferecer essa opcao, verifique o proprietario/permissoes.

```powershell
.\scripts\Start-Lab.ps1 `
  -ImageNamespace seuusuario/seurepositorio `
  -Version COMMIT_COMPLETO_PUBLICADO
```

### Comprovar download anonimo

Uma configuracao apenas vazia pode detectar automaticamente o helper de
credenciais do Windows. Use uma entrada vazia explicita para ghcr.io, sem
helpers, sem alterar a configuracao habitual nem fazer logout:

```powershell
$namespace = 'seuusuario/seurepositorio'
$versao = 'COMMIT_COMPLETO_PUBLICADO'
$anon = Join-Path $env:TEMP "docker-aula08-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $anon | Out-Null
'{"auths":{"ghcr.io":{}}}' |
  Set-Content (Join-Path $anon 'config.json') -Encoding ascii
docker --config $anon pull "ghcr.io/$namespace/web:$versao"
docker --config $anon pull "ghcr.io/$namespace/worker:$versao"
```

Substitua os valores pelo namespace/commit publicados. Preserve o config.json
sem segredos e as saidas/digests como evidencia. Manifest consulta o registro;
pull baixa as camadas necessarias. Cache sozinho nao comprova acesso anonimo.
Entradas unknown/unknown no indice podem ser atestacoes de proveniencia,
nao outras plataformas executaveis.

## Experimentos de operacao

Execute no mesmo terminal do deploy. Antes de provocar falhas, identifique
os servicos com `docker compose ps`. Banco e broker nao sao expostos na rede
externa; as portas de acesso ficam em `127.0.0.1`.

```powershell
docker compose logs -f worker
docker compose stop worker
# Solicite tarefas no painel: fila cresce, web continua atendendo.
docker compose up -d worker
# Observe recuperacao e resultados.
docker compose up -d --scale worker=2
# Solicite varias tarefas e identifique os workers nos logs.
# Salve os logs do lote enquanto ambas as replicas ainda existem.
docker compose up -d --scale worker=1
# Solicite tarefa com atraso; aguarde estado Processando.
docker compose kill -s SIGKILL worker
docker compose up -d worker
```

`SIGKILL` interrompe abruptamente; nao substitua por `stop` neste experimento.
Mensagens nao confirmadas podem ser reentregues. O resultado persistido deve
continuar com uma conclusao, apesar de mais de uma tentativa.

Selecione **Falha permanente** no painel: sao no maximo **3 tentativas**.
A mensagem termina em `relatorios.dlq`: DLQ (Dead Letter Queue, fila de
mensagens nao processaveis). Em RabbitMQ, observe `Ready` e `Unacked`
(mensagens disponiveis e entregues sem confirmacao). Atualize o painel do
broker se as metricas ainda nao refletirem o experimento.

```powershell
.\scripts\Test-Lab.ps1
.\scripts\Test-BrokerFailure.ps1
.\scripts\Test-NoOutbox.ps1
```

Os scripts usam contas ficticias novas e **alteram o ambiente do laboratorio**:
param, escalam, interrompem e recriam servicos. Nao execute em um ambiente real.
Os resultados e mensagens sao preservados para inspecao. O teste sem outbox
interrompe a web e deixa uma solicitacao orfa, intencionalmente.

Para guardar todos os streams PowerShell, inclusive Write-Host/Information:

```powershell
$evidencias = Join-Path $env:TEMP "aula08-evidencias-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $evidencias | Out-Null
.\scripts\Test-Lab.ps1 *>&1 |
  Tee-Object -FilePath (Join-Path $evidencias 'Test-Lab.log')
```

Repita para os outros dois scripts. Revise e sanitize antes de compartilhar;
nao grave tokens ou cabecalhos de autenticacao. Logs de replica removida
deixam de estar disponiveis no Compose: guarde a distribuicao antes de reduzir.

## Limitacoes de confiabilidade

Fila duravel, mensagem persistente e confirmacao de publicacao nao tornam
banco e broker uma transacao atomica. Sem **Transactional Outbox**, a web pode
cair depois de salvar e antes de publicar, deixando `AguardandoPublicacao`.
Nao existe republicacao automatica nesse caso. Publicacao sem confirmacao
retorna erro explicito e pode ter resultado incerto; nao prometa "exactly once".

Na falha de publicacao, HTTP (Hypertext Transfer Protocol) 503 devolve
`id` e `erro`. Consulte esse identificador autenticado em
`GET /api/relatorios/{id}` ou na lista do usuario. Novo POST cria outro
identificador: nao ha deduplicacao da requisicao nem republicacao automatica.
Nao repita o pedido as cegas.

O worker confirma consumo apos persistir e usa atualizacao atomica para evitar
duplicar o resultado. Isso nao impede executar novamente calculos nem protege
efeitos externos, como enviar email ou cobrar um pagamento. Interrupcoes
contam como tentativas; ao atingir o limite, o relatorio falha.

As chaves Identity ficam em volume para que recriar a web nao invalide tokens.
Tokens expiram normalmente. REST/GraphQL mantem os contratos didaticos da
Aula 07; nao sao uma auditoria de autorizacao pronta para producao.
Credenciais do `.env.example` sao publicas e apenas para laboratorio.

## Atualizar, reverter e parar

Publique uma mudanca visivel no painel, execute `Start-Lab.ps1` com o novo
commit e confirme a mudanca. Execute novamente com a versao anterior para
reverter. Use mudancas compativeis com o schema: voltar a imagem nao desfaz
migracoes de banco. Compose recria conteineres, sem garantia de disponibilidade.

Compare contagem e identificadores dos relatorios antes/depois, alem do
resultado de controle e da sessao. Registre digests: SHA (Secure Hash
Algorithm) identifica codigo, mas nao impede sobrescrever a tag nem garante
novo build identico com bases mutaveis. Fixacao de bases e politica de
imutabilidade sao requisitos adicionais de producao.

```powershell
docker compose down
```

Esse comando para e remove os conteineres, mas preserva os volumes.
**Nao use `down -v` durante os experimentos:** ele apaga banco, filas e chaves.
Nao use `docker system prune`. Para um reset intencional, confira o projeto,
salve o que precisa e confirme explicitamente a remocao dos volumes.

Para mudar a versao, execute `Start-Lab.ps1` novamente. Ele atualiza
`IMAGE_NAMESPACE` e `IMAGE_TAG` no `.env`, preservando as demais configuracoes.

## Desenvolvimento e contingencia

```powershell
dotnet tool restore
dotnet test .\LojaProducao.slnx
# Build local separado: nao demonstra o download das imagens publicadas.
$env:IMAGE_NAMESPACE = 'racass/csharp-prod-deploy-template'
$env:IMAGE_TAG = 'local'
Copy-Item .env.example .env  # somente se .env ainda nao existir
docker compose -f compose.yaml -f compose.build.yaml up -d --build --wait
```

Migracoes PostgreSQL pertencem a web; somente ela as aplica antes de atender.
Testes rapidos usam SQLite (Structured Query Language Lite), enquanto o
teste E2E (end-to-end, ponta a ponta) verifica o PostgreSQL real.

## Entrega

Informe o repositorio, a execucao aprovada do Actions e a versao das imagens.
Registre evidencias da fila acumulada, recuperacao, dois workers, reentrega
sem resultado duplicado e falha permanente. Explique a janela sem outbox e
a diferenca entre publicar uma imagem e fazer deploy.
