using System;
using System.Threading.Tasks;
using LojaGraphQL.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LojaGraphQL.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (db.Database.IsSqlite())
        {
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            await db.Database.MigrateAsync();
        }

        if (await db.Produtos.AnyAsync())
        {
            return;
        }

        Produto teclado = new()
        {
            Nome = "Teclado mecânico",
            Categoria = "Periféricos",
            Preco = 349.90m,
            Estoque = 12
        };
        Produto mouse = new()
        {
            Nome = "Mouse sem fio",
            Categoria = "Periféricos",
            Preco = 159.90m,
            Estoque = 25
        };
        Produto monitor = new()
        {
            Nome = "Monitor 27 polegadas",
            Categoria = "Monitores",
            Preco = 1499.00m,
            Estoque = 7
        };

        Pedido pedido = new()
        {
            Cliente = "Ada Lovelace",
            Status = StatusPedido.Fechado,
            Itens =
            [
                new ItemPedido
                {
                    Produto = teclado,
                    Quantidade = 1,
                    ValorUnitario = teclado.Preco
                },
                new ItemPedido
                {
                    Produto = mouse,
                    Quantidade = 2,
                    ValorUnitario = mouse.Preco
                }
            ]
        };

        db.AddRange(teclado, mouse, monitor, pedido);
        await db.SaveChangesAsync();
    }
}
