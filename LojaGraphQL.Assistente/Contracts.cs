using System.Text.Json;

namespace LojaGraphQL.Assistente;

public sealed record ChatRequest(string? Pergunta);
public sealed record ToolTrace(string Nome, JsonElement Argumentos, object Resultado, long DuracaoMs);
public sealed record ChatResponse(string Resposta, IReadOnlyList<ToolTrace> Ferramentas);
public sealed record Produto(int Id, string Nome, string Categoria, decimal Preco, int Estoque);
public sealed record ItemPedido(int ProdutoId, int Quantidade, decimal ValorUnitario, Produto? Produto);
public sealed record Pedido(int Id, string Cliente, string Status, DateTime CriadoEm, List<ItemPedido> Itens);

public sealed class LabException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public interface IModelClient
{
    Task<JsonElement> GenerateAsync(IReadOnlyList<object> contents, bool allowTools,
        CancellationToken cancellationToken);
}
