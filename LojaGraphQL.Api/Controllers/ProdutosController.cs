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
public sealed class ProdutosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Produto>>> ListarAsync(
        CancellationToken cancellationToken)
    {
        List<Produto> produtos = await db.Produtos
            .AsNoTracking()
            .Where(produto => produto.Ativo)
            .OrderBy(produto => produto.Nome)
            .ToListAsync(cancellationToken);

        return Ok(produtos);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Produto>> ObterPorIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        Produto? produto = await db.Produtos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == id && item.Ativo,
                cancellationToken);

        return produto is null ? NotFound() : Ok(produto);
    }
}
