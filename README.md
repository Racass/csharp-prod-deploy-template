# Aula 09 - Assistente com IA na loja

IA (Inteligencia Artificial) integrada a C#: perguntas em linguagem natural,
ferramentas somente leitura e respostas baseadas nos dados ficticios da loja.
O projeto chega pronto: a atividade e observar, experimentar e conferir.
Cada aluno realiza o laboratorio individualmente, com seu proprio ambiente,
sua chave pessoal e suas evidencias.

Esta branch `aula09-ia` evolui a Aula08 sem alterar `main` ou `gabarito`.
O projeto usa .NET 10 e acrescenta um quinto servico aos quatro anteriores.
E um laboratorio local, nao uma configuracao pronta para producao.

## Preparacao

Requisitos: Git, PowerShell, Docker Desktop em modo Linux e acesso a internet.
Nao e necessario instalar .NET para executar as imagens publicadas.

```powershell
git clone --branch aula09-ia --single-branch https://github.com/Racass/csharp-prod-deploy-template.git
Set-Location csharp-prod-deploy-template
Copy-Item .env.example .env
```

Nao substitua um `.env` existente: preserve as configuracoes e acrescente os
campos Gemini. Nao use "Use this template" esperando que a branch nao padrao
seja selecionada automaticamente.

Cada aluno deve criar sua chave pessoal no
[Google AI Studio](https://aistudio.google.com/apikey), usando um projeto na
camada gratuita, sem ativar faturamento. No arquivo `.env` ignorado pelo Git,
preencha `GEMINI_API_KEY`. Confira tambem `GEMINI_MODEL`: o exemplo usa um
modelo documentado com ferramentas, mas disponibilidade e cotas devem ser
verificadas no projeto do aluno. Nao ha troca automatica para modelos pagos.

Nao envie a chave ao professor, ao GitHub ou ao navegador. Nao a coloque
no codigo, imagem, workflow, print, resposta HTTP ou evidencia.
HTTP (Hypertext Transfer Protocol, protocolo de transferencia de hipertexto)
e usado internamente para consultar a loja; o provedor usa conexao HTTPS
(HTTP Secure, HTTP seguro).

Use apenas dados ficticios. Na camada gratuita, conteudo pode ser usado para
melhorar produtos do Google. Cotas sao por projeto, nao por chave.
Consulte [precos](https://ai.google.dev/gemini-api/docs/pricing) e
[limites atuais](https://ai.google.dev/gemini-api/docs/rate-limits).

## Executar a versao pronta

```powershell
.\scripts\Start-Lab.ps1 -Version (Get-Content .\reference-version.txt).Trim()
```

- Assistente, saude e rastreio: http://localhost:8081
- Loja existente: http://localhost:8080
- Consultas de produtos: http://localhost:8080/api/produtos
- Consultas de pedidos: http://localhost:8080/api/pedidos
- RabbitMQ: http://localhost:15672 (credenciais de laboratorio no `.env`)

O deploy ja esta implementado. Nao e necessario editar workflows ou publicar
imagens para esta atividade. `reference-version.txt` identifica uma publicacao;
a existencia de imagens nao comprova que Gemini foi validado com uma chave real.

A pagina mostra saude local, acesso a web e se existe chave configurada.
**Chave configurada nao significa chave valida, cota disponivel ou provedor
validado.** O painel de saude nao envia chamadas ao Gemini.
Depois de alterar `.env`, execute `docker compose up -d assistente`.

## Arquitetura

```text
Navegador -> Assistente C# :8081 -> Gemini
                   |
                   +-> Web :8080 -> PostgreSQL

RabbitMQ e worker continuam ativos para os relatorios da Aula08.
Nao participam do caminho de uma pergunta ao assistente.
```

O modelo solicita ferramentas; o C# valida e executa os metodos. O assistente
nao acessa o banco diretamente, nao executa SQL livre e nao escreve dados.
SQL (Structured Query Language, linguagem de consulta estruturada) nao e
uma ferramenta disponivel para o modelo.

As consultas retornam dados ficticios compartilhados da loja. Nao diga
"meus pedidos": nao ha personalizacao por usuario nesta atividade.
A autorizacao existente da Aula08 permanece, mas nao e o assunto desta aula.

## Ferramentas prontas

| Ferramenta | Entrada | Retorno |
|---|---|---|
| `ConsultarProdutos` | `termo` e `categoria` opcionais | Produtos, precos, estoque e indicacao de lista parcial |
| `ConsultarPedido` | `id` inteiro positivo | Cliente ficticio, estado, itens e total calculado em C# |
| `ResumirPedidos` | Nenhuma | Contagens por estado e valores calculados em C# |

Os totais usam `Quantidade * ValorUnitario` dos itens, nao o preco atual do
catalogo. A consulta de produtos limita a resposta a 20 registros, e a de
pedido a 30 itens, informando truncamento. O resumo calcula sobre todos os
pedidos ativos retornados pela loja, nao sobre uma pagina silenciosamente parcial.
O pequeno conjunto de dados e uma simplificacao didatica.

## Experimentos

1. Abra produtos e pedidos na loja para conhecer os dados e identificadores.
2. Pergunte quais produtos de uma categoria existente estao disponiveis.
3. Consulte um pedido pelo identificador. Confira estado, itens e total.
4. Peca o resumo dos pedidos. Compare contagens e valores com a fonte.
5. Reformule uma pergunta e observe se a ferramenta ou apenas a prosa mudou.

O rastreio mostra somente ferramentas realmente executadas: nome, argumentos,
dados e duracao. Nao representa o raciocinio interno do modelo.
Cada envio e independente; nao ha memoria entre perguntas.
Uma resposta sem ferramenta nao comprova consulta a dados atuais.

Registre pergunta, rastreio e conferencia dos fatos. Nao exija uma sequencia
literal de ferramentas ou a mesma redacao entre respostas.

## Codigo para acompanhar a demonstracao

- `LojaGraphQL.Assistente\GeminiClient.cs`: instrucoes, contratos enviados
  e chamada ao modelo, preservando assinaturas das respostas de ferramentas.
- `LojaGraphQL.Assistente\ChatService.cs`: ciclo limitado de execucao.
- `LojaGraphQL.Assistente\LojaTools.cs`: tres ferramentas e calculos C#.
- `LojaGraphQL.Assistente\Program.cs`: endpoints e erros explicitos.
- `LojaGraphQL.Assistente\wwwroot\index.html`: pagina sem chave no navegador.

O endpoint `POST /api/chat` recebe `{ "pergunta": "Resumo dos pedidos" }` e
retorna resposta e ferramentas executadas. O servidor limita perguntas a
2000 caracteres, tres rodadas de ferramentas, cinco chamadas de ferramenta
e espera total de 90 segundos. Nao realiza repeticoes ilimitadas.
Erros de chave, cota, rede e contrato sao explicitos; nao ha resposta
simulada disfarcada. Esses erros nao sao experimentos obrigatorios da aula.

## Validacao e desenvolvimento

```powershell
dotnet test LojaProducao.slnx -c Release
# Build local opcional; nao e o caminho principal da atividade.
docker compose -f compose.yaml -f compose.build.yaml up -d --build --wait
# Com chave e modelo configurados: verificacao REAL do provedor.
.\scripts\Test-Assistente.ps1
```

Os testes automatizados usam respostas controladas e nao exigem chave.
O script real compara o rastreio com consultas independentes da loja e falha
explicitamente se nao houver configuracao ou se a consulta nao for comprovada.
Ele nao substitui a analise critica da resposta final em linguagem natural.

Para execucao sem Docker, configure `GEMINI_API_KEY` e `GEMINI_MODEL` somente
no processo local, ou `Gemini:ApiKey` e `Gemini:Model` com User Secrets.
O Compose nao monta User Secrets automaticamente.

## Publicacao pronta e preservacao

O workflow testa a solucao e publica web, worker e assistente no
GHCR (GitHub Container Registry, registro de conteineres do GitHub).
Todas as imagens usam o commit como versao. A chave nao e necessaria no build.
Confira leitura publica das tres imagens apos a primeira publicacao.
O deploy e local e manual; nao e deploy automatico em nuvem.

```powershell
docker compose down
```

Nao use `down -v`: os volumes contem os dados e chaves de sessao da loja.
Nao opere projetos Docker de outras pessoas.
O [roteiro historico da Aula08](README-Aula08.md) permanece como referencia,
mas seus exercicios nao devem ser refeitos nesta aula.
