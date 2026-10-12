using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using LojaGraphQL.Api.Data;
using LojaGraphQL.Api.Models;
using LojaGraphQL.Core.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace LojaGraphQL.Api.Controllers;

public sealed record SolicitarRelatorio(
    int AtrasoSegundos = 5, bool FalhaPermanente = false, int AtrasoPublicacaoSegundos = 0);

[ApiController]
[Authorize]
[Route("api/relatorios")]
public sealed class RelatoriosController(
    AppDbContext db, IConfiguration configuration, ILogger<RelatoriosController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return Ok(await db.Relatorios.AsNoTracking()
            .Where(item => item.UsuarioId == userId)
            .OrderByDescending(item => item.CriadoEm).Take(50).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        Relatorio? report = await db.Relatorios.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.UsuarioId == userId, ct);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpPost]
    public async Task<IActionResult> Solicitar(SolicitarRelatorio input, CancellationToken ct)
    {
        if (input.AtrasoSegundos is < 0 or > 30 || input.AtrasoPublicacaoSegundos is < 0 or > 30)
        {
            return BadRequest(new { erro = "Atrasos devem estar entre 0 e 30 segundos." });
        }

        bool laboratory = configuration.GetValue<bool>("Laboratorio");
        if (!laboratory && (input.FalhaPermanente || input.AtrasoPublicacaoSegundos > 0))
        {
            return BadRequest(new { erro = "Injecao de falha disponivel somente no laboratorio." });
        }

        Relatorio report = new()
        {
            UsuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            AtrasoSegundos = input.AtrasoSegundos,
            FalhaPermanente = input.FalhaPermanente
        };
        db.Relatorios.Add(report);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Relatorio {ReportId} persistido antes da publicacao", report.Id);
        if (input.AtrasoPublicacaoSegundos > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(input.AtrasoPublicacaoSegundos), ct);
        }

        try
        {
            ConnectionFactory factory = ReportBroker.Factory(
                configuration["Rabbit:Host"]!, configuration["Rabbit:User"]!,
                configuration["Rabbit:Password"]!);
            await using IConnection connection = await factory.CreateConnectionAsync(ct);
            await using IChannel channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(true, true), ct);
            await ReportBroker.DeclareAsync(channel, ct);
            await ReportBroker.PublishAsync(channel, new(report.Id), ct);
        }
        catch (Exception exception) when (exception is RabbitMQClientException or IOException
            or SocketException or TimeoutException)
        {
            logger.LogError(exception, "Publicacao do relatorio {ReportId} falhou", report.Id);
            const string error = "Publicacao nao confirmada; verificar broker. Nao ha outbox neste laboratorio.";
            await db.Relatorios.Where(item => item.Id == report.Id &&
                    item.Status == StatusRelatorio.AguardandoPublicacao && item.Conclusoes == 0)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(item => item.Status, StatusRelatorio.FalhaPublicacao)
                    .SetProperty(item => item.Erro, error), ct);
            return StatusCode(503, new { id = report.Id, erro = error });
        }

        await db.Relatorios.Where(item => item.Id == report.Id &&
                item.Status == StatusRelatorio.AguardandoPublicacao)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, StatusRelatorio.NaFila), ct);
        return AcceptedAtAction(nameof(Obter), new { id = report.Id }, new { id = report.Id });
    }
}
