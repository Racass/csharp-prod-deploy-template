using LojaGraphQL.Api.Data;
using LojaGraphQL.Worker;
using Microsoft.EntityFrameworkCore;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHostedService<ReportWorker>();
await builder.Build().RunAsync();
