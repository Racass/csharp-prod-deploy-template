using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using LojaGraphQL.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LojaGraphQL.Api.Tests;

[TestClass]
public sealed class ApiIntegrationTests
{
    [TestMethod]
    public async Task RestApiContinuaDisponivel()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/produtos");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task IdentityPermiteRegistrarEAutenticar()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = factory.CreateClient();
        string email = $"aluno-{Guid.NewGuid():N}@fiap.com.br";
        const string password = "GraphQL#2026";

        HttpResponseMessage register = await client.PostAsJsonAsync(
            "/register",
            new { email, password });
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/login",
            new { email, password });

        Assert.AreEqual(HttpStatusCode.OK, register.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
    }

    [TestMethod]
    public async Task GraphQLRetornaSomenteCamposSolicitados()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/graphql",
            new { query = "{ produtos { nome preco } }" });
        string json = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement produto = document.RootElement
            .GetProperty("data")
            .GetProperty("produtos")[0];
        Assert.IsTrue(produto.TryGetProperty("nome", out _));
        Assert.IsTrue(produto.TryGetProperty("preco", out _));
        Assert.IsFalse(produto.TryGetProperty("estoque", out _));
    }

    [TestMethod]
    public async Task GraphQLNavegaPedidoItensEProduto()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/graphql",
            new
            {
                query = """
                    {
                      pedidos {
                        cliente
                        itens {
                          quantidade
                          produto {
                            nome
                          }
                        }
                      }
                    }
                    """
            });
        string json = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(json, "Ada Lovelace");
        StringAssert.Contains(json, "Teclado mecânico");
    }

    [TestMethod]
    public async Task GraphQLFiltraProdutosPorCategoria()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/graphql",
            new
            {
                query = """
                    query ProdutosPorCategoria($categoria: String!) {
                      produtos(where: { categoria: { eq: $categoria } }) {
                        nome
                        categoria
                      }
                    }
                    """,
                variables = new { categoria = "Monitores" }
            });
        string json = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(json, "Monitor 27 polegadas");
        Assert.IsFalse(json.Contains("Teclado mecânico", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GraphQLExpoeMutationCriarProduto()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/graphql",
            new
            {
                query = """
                    {
                      __schema {
                        mutationType {
                          fields {
                            name
                          }
                        }
                      }
                    }
                    """
            });
        string json = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(json, "criarProduto");
    }

    [TestMethod]
    public async Task GraphQLCriaProdutoEPersiste()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage mutationResponse = await client.PostAsJsonAsync(
            "/graphql",
            new
            {
                query = """
                    mutation CriarProduto($input: CriarProdutoInput!) {
                      criarProduto(input: $input) {
                        produto {
                          id
                          nome
                          categoria
                          preco
                          estoque
                        }
                      }
                    }
                    """,
                variables = new
                {
                    input = new
                    {
                        nome = "Webcam 4K",
                        categoria = "Vídeo",
                        preco = 799.90m,
                        estoque = 4
                    }
                }
            });
        string mutationJson = await mutationResponse.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, mutationResponse.StatusCode);
        using JsonDocument mutationDocument = JsonDocument.Parse(mutationJson);
        Assert.IsFalse(mutationDocument.RootElement.TryGetProperty("errors", out _));
        JsonElement produtoCriado = mutationDocument.RootElement
            .GetProperty("data")
            .GetProperty("criarProduto")
            .GetProperty("produto");
        int id = produtoCriado.GetProperty("id").GetInt32();
        Assert.IsTrue(id > 0);
        Assert.AreEqual("Webcam 4K", produtoCriado.GetProperty("nome").GetString());

        HttpResponseMessage queryResponse = await client.PostAsJsonAsync(
            "/graphql",
            new
            {
                query = """
                    query ProdutoCriado($id: Int!) {
                      produtoPorId(id: $id) {
                        nome
                        categoria
                        preco
                        estoque
                      }
                    }
                    """,
                variables = new { id }
            });
        string queryJson = await queryResponse.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, queryResponse.StatusCode);
        StringAssert.Contains(queryJson, "Webcam 4K");
        StringAssert.Contains(queryJson, "Vídeo");
    }

    [TestMethod]
    public async Task GraphQLRejeitaEntradasInvalidasSemPersistir()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = factory.CreateClient();

        int quantidadeInicial = await CountProdutosAsync(client);

        HttpResponseMessage mutationResponse = await client.PostAsJsonAsync(
            "/graphql",
            new
            {
                query = """
                    mutation CriarProduto($input: CriarProdutoInput!) {
                      criarProduto(input: $input) {
                        produto {
                          id
                        }
                      }
                    }
                    """,
                variables = new
                {
                    input = new
                    {
                        nome = "Produto inválido da aula",
                        categoria = "Teste",
                        preco = 0,
                        estoque = 1
                    }
                }
            });
        string mutationJson = await mutationResponse.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, mutationResponse.StatusCode);
        StringAssert.Contains(mutationJson, "PRODUTO_PRECO_INVALIDO");

        HttpResponseMessage nomeResponse = await client.PostAsJsonAsync(
            "/graphql",
            new
            {
                query = """
                    mutation CriarProduto($input: CriarProdutoInput!) {
                      criarProduto(input: $input) {
                        produto {
                          id
                        }
                      }
                    }
                    """,
                variables = new
                {
                    input = new
                    {
                        nome = "   ",
                        categoria = "Teste",
                        preco = 10,
                        estoque = 1
                    }
                }
            });
        string nomeJson = await nomeResponse.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, nomeResponse.StatusCode);
        StringAssert.Contains(nomeJson, "PRODUTO_NOME_INVALIDO");
        Assert.AreEqual(quantidadeInicial, await CountProdutosAsync(client));
    }

    private static WebApplicationFactory<Program> CreateFactory()
        => new TestApplicationFactory();

    private static async Task<int> CountProdutosAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/graphql",
            new { query = "{ produtos { id } }" });
        string json = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("data")
            .GetProperty("produtos")
            .GetArrayLength();
    }

    private sealed class TestApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"aula07-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source={databasePath};Pooling=False");
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            File.Delete(databasePath);
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }
    }
}
