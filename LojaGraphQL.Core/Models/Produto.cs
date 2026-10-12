using System.Collections.Generic;

namespace LojaGraphQL.Api.Models;

public sealed class Produto
{
    public int Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string Categoria { get; set; } = string.Empty;

    public decimal Preco { get; set; }

    public int Estoque { get; set; }

    public bool Ativo { get; set; } = true;

    public List<ItemPedido> ItensPedido { get; set; } = [];
}
