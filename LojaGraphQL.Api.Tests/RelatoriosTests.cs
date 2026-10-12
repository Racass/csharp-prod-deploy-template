using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LojaGraphQL.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LojaGraphQL.Api.Tests;

[TestClass]
public sealed class RelatoriosTests
{
    [TestMethod]
    public async Task RelatoriosExigemAutenticacao()
    {
        await using Factory factory = new();
        HttpClient client = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/relatorios")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/relatorios", new { atrasoSegundos = 1 })).StatusCode);
    }

    [TestMethod]
    public async Task EntradaInvalidaNaoCriaSolicitacao()
    {
        await using Factory factory = new();
        HttpClient client = await Authenticated(factory);
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/relatorios", new { atrasoSegundos = 31 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("[]", await client.GetStringAsync("/api/relatorios"));
    }

    [TestMethod]
    public async Task FalhaInjetadaExigeModoLaboratorio()
    {
        await using Factory factory = new();
        HttpClient client = await Authenticated(factory);
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/relatorios", new { falhaPermanente = true });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("[]", await client.GetStringAsync("/api/relatorios"));
    }

    private static async Task<HttpClient> Authenticated(Factory factory)
    {
        HttpClient client = factory.CreateClient();
        var credentials = new { email = $"{Guid.NewGuid():N}@example.test", password = "Laboratorio#2026" };
        (await client.PostAsJsonAsync("/register", credentials)).EnsureSuccessStatusCode();
        HttpResponseMessage response = await client.PostAsJsonAsync("/login", credentials);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<Login>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login!.AccessToken);
        return client;
    }

    private sealed record Login(string AccessToken);

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"loja-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("Laboratorio", "false");
            builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={path};Pooling=False");
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }
}
