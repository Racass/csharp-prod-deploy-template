using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotChocolate.Data;
using LojaGraphQL.Api.Data;
using LojaGraphQL.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LojaGraphQL.Api.GraphQL;

public sealed class Query
{
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Produto> GetProdutos(AppDbContext db)
        => db.Produtos
            .AsNoTracking()
            .Where(produto => produto.Ativo);

    [UseProjection]
    public Task<Produto?> GetProdutoPorIdAsync(
        int id,
        AppDbContext db,
        CancellationToken cancellationToken)
        => db.Produtos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                produto => produto.Id == id && produto.Ativo,
                cancellationToken);

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Pedido> GetPedidos(AppDbContext db)
        => db.Pedidos
            .AsNoTracking()
            .Where(pedido => pedido.Ativo);

    [UseProjection]
    public Task<Pedido?> GetPedidoPorIdAsync(
        int id,
        AppDbContext db,
        CancellationToken cancellationToken)
        => db.Pedidos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                pedido => pedido.Id == id && pedido.Ativo,
                cancellationToken);
}
