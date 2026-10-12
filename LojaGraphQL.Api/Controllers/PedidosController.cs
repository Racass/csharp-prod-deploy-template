using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LojaGraphQL.Api.Data;
using LojaGraphQL.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LojaGraphQL.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PedidosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Pedido>>> ListarAsync(
        CancellationToken cancellationToken)
    {
        List<Pedido> pedidos = await db.Pedidos
            .AsNoTracking()
            .Where(pedido => pedido.Ativo)
            .Include(pedido => pedido.Itens)
            .ThenInclude(item => item.Produto)
            .OrderByDescending(pedido => pedido.CriadoEm)
            .ToListAsync(cancellationToken);

        return Ok(pedidos);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Pedido>> ObterPorIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        Pedido? pedido = await db.Pedidos
            .AsNoTracking()
            .Include(item => item.Itens)
            .ThenInclude(item => item.Produto)
            .FirstOrDefaultAsync(
                item => item.Id == id && item.Ativo,
                cancellationToken);

        return pedido is null ? NotFound() : Ok(pedido);
    }
}
