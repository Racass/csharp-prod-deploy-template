using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LojaGraphQL.Api.Data;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AppDbContext> options = new();
        options.UseNpgsql("Host=localhost;Database=loja;Username=loja;Password=laboratorio-only",
            postgres => postgres.MigrationsAssembly(typeof(Program).Assembly.FullName));
        return new AppDbContext(options.Options);
    }
}
