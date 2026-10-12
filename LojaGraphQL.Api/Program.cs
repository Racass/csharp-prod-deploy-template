using System.Text.Json.Serialization;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LojaGraphQL.Api.Data;
using LojaGraphQL.Api.GraphQL;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LojaGraphQL.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.ReferenceHandler =
                    ReferenceHandler.IgnoreCycles)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter()));
        builder.Services.AddOpenApi();
        builder.Services.AddDbContext<AppDbContext>(options =>
        {
            string? connection = builder.Configuration.GetConnectionString("DefaultConnection");
            if (builder.Configuration["DatabaseProvider"] == "Sqlite")
            {
                options.UseSqlite(connection);
            }
            else
            {
                options.UseNpgsql(connection, postgres =>
                    postgres.MigrationsAssembly(typeof(Program).Assembly.FullName));
            }
        });
        string? keys = builder.Configuration["DataProtection:Path"];
        if (!string.IsNullOrWhiteSpace(keys))
        {
            builder.Services.AddDataProtection().SetApplicationName("LojaGraphQL")
                .PersistKeysToFileSystem(new DirectoryInfo(keys));
        }
        builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
        builder.Services.AddAuthorization();
        builder.Services
            .AddIdentityApiEndpoints<IdentityUser>()
            .AddEntityFrameworkStores<AppDbContext>();

        builder.Services
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .AddMutationType<Mutation>()
            .AddProjections()
            .AddFiltering()
            .AddSorting();

        WebApplication app = builder.Build();

        app.MapOpenApi();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapIdentityApi<IdentityUser>();
        app.MapControllers();
        app.MapGraphQL();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapHealthChecks("/health");

        await DbSeeder.SeedAsync(app.Services);
        await app.RunAsync();
    }
}
