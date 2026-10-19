using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LojaGraphQL.Assistente;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LojaGraphQL.Api.Tests;

[TestClass]
public sealed class AssistenteTests
{
    private const string Pedidos = """
        [
          {"id":1,"cliente":"Cliente ficticio","status":"Aberto","criadoEm":"2026-10-01T00:00:00Z",
           "itens":[{"produtoId":1,"quantidade":2,"valorUnitario":12.50,"produto":null}]},
          {"id":2,"cliente":"Outro ficticio","status":"Concluido","criadoEm":"2026-10-01T00:00:00Z",
           "itens":[{"produtoId":2,"quantidade":3,"valorUnitario":10,"produto":null}]}
        ]
        """;

    [TestMethod]
    public async Task FerramentasConsultamLojaECalculamResumoSemModelo()
    {
        using Handler handler = new(_ => Json(Pedidos));
        using HttpClient http = Client(handler);
        LojaTools tools = new(http);
        object result = await tools.ExecuteAsync("ResumirPedidos", Element("{}"), CancellationToken.None);
        JsonElement json = JsonSerializer.SerializeToElement(result);
        Assert.AreEqual(2, json.GetProperty("totalPedidos").GetInt32());
        Assert.AreEqual(55m, json.GetProperty("valorTotal").GetDecimal());
        Assert.AreEqual(2, json.GetProperty("porEstado").GetArrayLength());
    }

    [TestMethod]
    public async Task ProdutoFiltraDadosEInformaTruncamento()
    {
        object[] produtos = Enumerable.Range(1, 23).Select(id => (object)new
        {
            id, nome = $"Teclado {id}", categoria = "Perifericos", preco = 10, estoque = 2,
        }).Append(new { id = 24, nome = "Mouse", categoria = "Outra", preco = 5, estoque = 1 }).ToArray();
        using Handler handler = new(_ => Json(JsonSerializer.Serialize(produtos)));
        using HttpClient http = Client(handler);
        LojaTools tools = new(http);
        JsonElement result = JsonSerializer.SerializeToElement(await tools.ExecuteAsync(
            "ConsultarProdutos", Element("""{"termo":"teclado","categoria":"perifericos"}"""), CancellationToken.None));
        Assert.AreEqual(23, result.GetProperty("totalEncontrados").GetInt32());
        Assert.IsTrue(result.GetProperty("truncado").GetBoolean());
        Assert.AreEqual(20, result.GetProperty("produtos").GetArrayLength());
    }

    [TestMethod]
    public async Task PedidoUsaValorDoItemENaoPrecoAtualDoProduto()
    {
        using Handler handler = new(request =>
        {
            StringAssert.EndsWith(request.RequestUri!.AbsolutePath, "/api/pedidos/7");
            return Json("""
                {"id":7,"cliente":"Ficticio","status":"Aberto","criadoEm":"2026-10-01T00:00:00Z",
                 "itens":[{"produtoId":1,"quantidade":2,"valorUnitario":12.50,
                 "produto":{"id":1,"nome":"Produto","categoria":"Teste","preco":999,"estoque":2}}]}
                """);
        });
        using HttpClient http = Client(handler);
        LojaTools tools = new(http);
        JsonElement result = JsonSerializer.SerializeToElement(await tools.ExecuteAsync(
            "ConsultarPedido", Element("""{"id":7}"""), CancellationToken.None));
        Assert.AreEqual(25m, result.GetProperty("total").GetDecimal());
    }

    [TestMethod]
    public async Task ArgumentosInvalidosNaoExecutamHttp()
    {
        using Handler handler = new(_ => throw new AssertFailedException("Nao deveria consultar a loja."));
        using HttpClient http = Client(handler);
        LojaTools tools = new(http);
        foreach ((string name, string args) in new[]
        {
            ("ConsultarPedido", """{"id":-1}"""),
            ("ConsultarPedido", """{"id":"1"}"""),
            ("ConsultarProdutos", """{"sql":"DELETE"}"""),
            ("ExcluirPedido", """{"id":1}"""),
        })
        {
            await Assert.ThrowsExactlyAsync<LabException>(() =>
                tools.ExecuteAsync(name, Element(args), CancellationToken.None));
        }
    }

    [TestMethod]
    public async Task ChatPreservaAssinaturaEDevolveRastreioExecutado()
    {
        JsonElement toolContent = Element("""
            {"role":"model","parts":[{"thoughtSignature":"assinatura-teste",
             "functionCall":{"id":"chamada-1","name":"ResumirPedidos","args":{}}}]}
            """);
        ScriptedModel model = new((contents, _) =>
        {
            if (contents.Count == 1)
            {
                return toolContent;
            }
            string history = JsonSerializer.Serialize(contents);
            StringAssert.Contains(history, "assinatura-teste");
            StringAssert.Contains(history, "chamada-1");
            StringAssert.Contains(history, "valorTotal");
            return Element("""{"role":"model","parts":[{"thought":true,"text":"privado"},{"text":"Total: 55."}]}""");
        });
        using Handler handler = new(_ => Json(Pedidos));
        using HttpClient http = Client(handler);
        ChatResponse result = await new ChatService(model, new LojaTools(http))
            .AskAsync("Qual o resumo?", CancellationToken.None);
        Assert.AreEqual("Total: 55.", result.Resposta);
        Assert.AreEqual(1, result.Ferramentas.Count);
        Assert.AreEqual("ResumirPedidos", result.Ferramentas[0].Nome);
        Assert.AreEqual(2, model.Calls);
    }

    [TestMethod]
    public async Task ChatInterrompeCicloDeFerramentasSemRespostaFalsa()
    {
        ScriptedModel model = new((_, _) => Element("""
            {"role":"model","parts":[{"functionCall":{"name":"ResumirPedidos","args":{}}}]}
            """));
        using Handler handler = new(_ => Json(Pedidos));
        using HttpClient http = Client(handler);
        await Assert.ThrowsExactlyAsync<LabException>(() =>
            new ChatService(model, new LojaTools(http)).AskAsync("Resumo", CancellationToken.None));
        Assert.AreEqual(4, model.Calls);
        Assert.IsFalse(model.LastAllowTools);
    }

    [TestMethod]
    public async Task GeminiEnviaContratoEPropagaCotaSemExporChave()
    {
        bool observed = false;
        using Handler handler = new(request =>
        {
            observed = true;
            Assert.IsTrue(request.Headers.Contains("x-goog-api-key"));
            Assert.IsFalse(request.RequestUri!.Query.Contains("key", StringComparison.OrdinalIgnoreCase));
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        });
        using HttpClient http = Client(handler);
        GeminiClient gemini = new(http, Config("fake-not-a-real-key"));
        LabException error = await Assert.ThrowsExactlyAsync<LabException>(() =>
            gemini.GenerateAsync([], true, CancellationToken.None));
        Assert.IsTrue(observed);
        Assert.AreEqual(429, error.StatusCode);
        Assert.IsFalse(error.Message.Contains("fake-not-a-real-key", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GeminiPreservaContratoDeFerramentaEAssinaturaRecebida()
    {
        using Handler handler = new(_ => Json("""
            {"candidates":[{"finishReason":"STOP","content":{"role":"model","parts":[
              {"thoughtSignature":"assinatura","functionCall":{"name":"ResumirPedidos","args":{}}}
            ]}}]}
            """));
        using HttpClient http = Client(handler);
        JsonElement content = await new GeminiClient(http, Config("fake-not-a-real-key"))
            .GenerateAsync([], true, CancellationToken.None);
        Assert.AreEqual("assinatura", content.GetProperty("parts")[0].GetProperty("thoughtSignature").GetString());
        Assert.AreEqual("ResumirPedidos",
            content.GetProperty("parts")[0].GetProperty("functionCall").GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task GeminiNaoApresentaRespostaTruncadaComoConcluida()
    {
        using Handler handler = new(_ => Json("""
            {"candidates":[{"finishReason":"MAX_TOKENS","content":{"role":"model","parts":[{"text":"Parcial"}]}}]}
            """));
        using HttpClient http = Client(handler);
        await Assert.ThrowsExactlyAsync<LabException>(() =>
            new GeminiClient(http, Config("fake-not-a-real-key")).GenerateAsync([], true, CancellationToken.None));
    }

    [TestMethod]
    public async Task PaginaSaudeEChaveAusenteSaoExplicitasSemChamarGemini()
    {
        await using WebApplicationFactory<LojaGraphQL.Assistente.Program> original = new();
        await using WebApplicationFactory<LojaGraphQL.Assistente.Program> factory =
            original.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Gemini:ApiKey"] = "", ["Gemini:Model"] = "modelo-teste" }));
                builder.ConfigureServices(services =>
                    services.AddHttpClient("health").ConfigurePrimaryHttpMessageHandler(() =>
                        new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
            });
        using HttpClient client = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        StringAssert.Contains(await client.GetStringAsync("/"), "Ferramentas executadas");
        JsonElement status = await client.GetFromJsonAsync<JsonElement>("/api/status");
        Assert.IsFalse(status.GetProperty("chaveConfigurada").GetBoolean());
        Assert.IsTrue(status.GetProperty("lojaDisponivel").GetBoolean());
        using HttpResponseMessage missing = await client.PostAsJsonAsync("/api/chat", new { pergunta = "Resumo" });
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, missing.StatusCode);
        StringAssert.Contains(await missing.Content.ReadAsStringAsync(), "GEMINI_API_KEY");
        using HttpResponseMessage invalid = await client.PostAsJsonAsync("/api/chat", new { pergunta = "" });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static IConfiguration Config(string key) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = key,
            ["Gemini:Model"] = "modelo-teste",
        }).Build();

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("http://loja.test/") };

    private static JsonElement Element(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response(request));
    }

    private sealed class ScriptedModel(Func<IReadOnlyList<object>, bool, JsonElement> generate) : IModelClient
    {
        public int Calls { get; private set; }
        public bool LastAllowTools { get; private set; }

        public Task<JsonElement> GenerateAsync(IReadOnlyList<object> contents, bool allowTools, CancellationToken ct)
        {
            Calls++;
            LastAllowTools = allowTools;
            return Task.FromResult(generate(contents, allowTools));
        }
    }
}
