using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LojaGraphQL.Assistente;

public sealed class LojaTools(HttpClient http)
{
    public static readonly object[] Definitions =
    [
        new
        {
            name = "ConsultarProdutos",
            description = "Consulta produtos ativos da loja ficticia com preco e estoque atuais. Pode filtrar por termo e categoria.",
            parameters = new
            {
                type = "OBJECT",
                properties = new
                {
                    termo = new { type = "STRING", description = "Trecho do nome, opcional." },
                    categoria = new { type = "STRING", description = "Categoria exata, opcional." },
                },
            },
        },
        new
        {
            name = "ConsultarPedido",
            description = "Consulta um pedido ficticio pelo identificador, incluindo itens e total calculado em C#.",
            parameters = new
            {
                type = "OBJECT",
                properties = new { id = new { type = "INTEGER", description = "Identificador positivo do pedido." } },
                required = new[] { "id" },
            },
        },
        new
        {
            name = "ResumirPedidos",
            description = "Consulta todos os pedidos ativos da loja ficticia e calcula contagens por estado e valor total em C#.",
            parameters = new { type = "OBJECT", properties = new { } },
        },
    ];

    public async Task<object> ExecuteAsync(string name, JsonElement args, CancellationToken ct)
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            throw new LabException(502, "A ferramenta recebeu argumentos que nao sao um objeto.");
        }

        return name switch
        {
            "ConsultarProdutos" => await ConsultarProdutosAsync(args, ct),
            "ConsultarPedido" => await ConsultarPedidoAsync(args, ct),
            "ResumirPedidos" => await ResumirPedidosAsync(args, ct),
            _ => throw new LabException(502, "O modelo solicitou uma ferramenta nao permitida."),
        };
    }

    private async Task<object> ConsultarProdutosAsync(JsonElement args, CancellationToken ct)
    {
        CheckProperties(args, "termo", "categoria");
        string? termo = ReadText(args, "termo");
        string? categoria = ReadText(args, "categoria");
        List<Produto> produtos = await GetAsync<List<Produto>>("api/produtos", ct);
        Produto[] encontrados = produtos
            .Where(p => termo is null || p.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase))
            .Where(p => categoria is null || p.Categoria.Equals(categoria, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Id).ToArray();
        return new
        {
            totalEncontrados = encontrados.Length,
            limite = 20,
            truncado = encontrados.Length > 20,
            produtos = encontrados.Take(20),
        };
    }

    private async Task<object> ConsultarPedidoAsync(JsonElement args, CancellationToken ct)
    {
        CheckProperties(args, "id");
        if (!args.TryGetProperty("id", out JsonElement idValue) ||
            idValue.ValueKind != JsonValueKind.Number ||
            !idValue.TryGetInt32(out int id) || id <= 0)
        {
            throw new LabException(502, "ConsultarPedido exige um id inteiro positivo.");
        }

        using HttpResponseMessage response = await http.GetAsync($"api/pedidos/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new { encontrado = false, id };
        }
        EnsureSuccess(response);
        Pedido pedido = await ReadAsync<Pedido>(response, ct);
        return new
        {
            encontrado = true,
            pedido.Id,
            pedido.Cliente,
            pedido.Status,
            pedido.CriadoEm,
            total = Total(pedido),
            totalItens = pedido.Itens.Count,
            truncado = pedido.Itens.Count > 30,
            itens = pedido.Itens.Take(30).Select(i => new
            {
                i.ProdutoId,
                nome = i.Produto?.Nome,
                i.Quantidade,
                i.ValorUnitario,
                subtotal = i.Quantidade * i.ValorUnitario,
            }),
        };
    }

    private async Task<object> ResumirPedidosAsync(JsonElement args, CancellationToken ct)
    {
        CheckProperties(args);
        List<Pedido> pedidos = await GetAsync<List<Pedido>>("api/pedidos", ct);
        return new
        {
            totalPedidos = pedidos.Count,
            valorTotal = pedidos.Sum(Total),
            porEstado = pedidos.GroupBy(p => p.Status).OrderBy(g => g.Key)
                .Select(g => new { estado = g.Key, quantidade = g.Count(), valor = g.Sum(Total) }),
            fonte = "Todos os pedidos ativos retornados por GET /api/pedidos.",
        };
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync(path, ct);
        EnsureSuccess(response);
        return await ReadAsync<T>(response, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct)
                ?? throw new LabException(502, "A loja devolveu uma resposta vazia.");
        }
        catch (JsonException)
        {
            throw new LabException(502, "A loja devolveu dados fora do contrato esperado.");
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new LabException(502, $"A consulta a loja falhou (HTTP {(int)response.StatusCode}).");
        }
    }

    private static decimal Total(Pedido pedido) =>
        pedido.Itens.Sum(i => i.Quantidade * i.ValorUnitario);

    private static string? ReadText(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 100)
        {
            throw new LabException(502, $"O argumento {name} deve ser texto com ate 100 caracteres.");
        }
        string text = value.GetString()!.Trim();
        return text.Length == 0 ? null : text;
    }

    private static void CheckProperties(JsonElement args, params string[] allowed)
    {
        if (args.EnumerateObject().Any(p => !allowed.Contains(p.Name)))
        {
            throw new LabException(502, "A ferramenta recebeu argumentos nao previstos no contrato.");
        }
    }
}
