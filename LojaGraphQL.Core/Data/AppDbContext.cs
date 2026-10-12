using LojaGraphQL.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LojaGraphQL.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Produto> Produtos => Set<Produto>();

    public DbSet<Pedido> Pedidos => Set<Pedido>();

    public DbSet<ItemPedido> ItensPedido => Set<ItemPedido>();

    public DbSet<Relatorio> Relatorios => Set<Relatorio>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Relatorio>().Property(item => item.Status).HasConversion<string>();
        builder.Entity<Relatorio>().Property(item => item.UsuarioId).HasMaxLength(450);

        builder.Entity<Produto>()
            .Property(produto => produto.Preco)
            .HasPrecision(10, 2);

        builder.Entity<ItemPedido>()
            .Property(item => item.ValorUnitario)
            .HasPrecision(10, 2);

        builder.Entity<ItemPedido>()
            .HasOne(item => item.Pedido)
            .WithMany(pedido => pedido.Itens)
            .HasForeignKey(item => item.PedidoId);

        builder.Entity<ItemPedido>()
            .HasOne(item => item.Produto)
            .WithMany(produto => produto.ItensPedido)
            .HasForeignKey(item => item.ProdutoId);
    }
}
