using System.Threading;
using System.Threading.Tasks;
using HotChocolate;
using LojaGraphQL.Api.Data;
using LojaGraphQL.Api.Models;

namespace LojaGraphQL.Api.GraphQL;

public sealed class Mutation
{
    public async Task<CriarProdutoPayload> CriarProdutoAsync(
        CriarProdutoInput input,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        string nome = input.Nome.Trim();

        if (string.IsNullOrWhiteSpace(nome))
        {
            throw CreateError(
                "Informe o nome do produto.",
                "PRODUTO_NOME_INVALIDO");
        }

        if (input.Preco <= 0)
        {
            throw CreateError(
                "O preço deve ser maior que zero.",
                "PRODUTO_PRECO_INVALIDO");
        }

        Produto produto = new()
        {
            Nome = nome,
            Categoria = input.Categoria.Trim(),
            Preco = input.Preco,
            Estoque = input.Estoque
        };

        db.Produtos.Add(produto);
        await db.SaveChangesAsync(cancellationToken);

        return new CriarProdutoPayload
        {
            Produto = produto
        };
    }

    private static GraphQLException CreateError(string message, string code)
        => new(
            ErrorBuilder.New()
                .SetMessage(message)
                .SetCode(code)
                .Build());
}

public sealed class CriarProdutoInput
{
    public string Nome { get; init; } = string.Empty;

    public string Categoria { get; init; } = string.Empty;

    public decimal Preco { get; init; }

    public int Estoque { get; init; }
}

public sealed class CriarProdutoPayload
{
    public required Produto Produto { get; init; }
}
