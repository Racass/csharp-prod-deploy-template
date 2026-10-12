using System;
using System.Collections.Generic;

namespace LojaGraphQL.Api.Models;

public sealed class Pedido
{
    public int Id { get; set; }

    public string Cliente { get; set; } = string.Empty;

    public StatusPedido Status { get; set; } = StatusPedido.Aberto;

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public bool Ativo { get; set; } = true;

    public List<ItemPedido> Itens { get; set; } = [];
}
