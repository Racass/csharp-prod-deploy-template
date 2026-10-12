namespace LojaGraphQL.Api.Models;

public sealed class ItemPedido
{
    public int Id { get; set; }

    public int PedidoId { get; set; }

    public int ProdutoId { get; set; }

    public int Quantidade { get; set; }

    public decimal ValorUnitario { get; set; }

    public Pedido Pedido { get; set; } = null!;

    public Produto Produto { get; set; } = null!;
}
