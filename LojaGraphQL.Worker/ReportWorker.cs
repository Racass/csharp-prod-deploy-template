using System.Data;
using System.Text.Json;
using LojaGraphQL.Api.Data;
using LojaGraphQL.Api.Models;
using LojaGraphQL.Core.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace LojaGraphQL.Worker;

public sealed class ReportWorker(
    IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ReportWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory factory = ReportBroker.Factory(
                    configuration["Rabbit:Host"]!, configuration["Rabbit:User"]!,
                    configuration["Rabbit:Password"]!);
                await using IConnection connection = await factory.CreateConnectionAsync(stoppingToken);
                await using IChannel channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(true, true), stoppingToken);
                await ReportBroker.DeclareAsync(channel, stoppingToken);
                await channel.BasicQosAsync(0, 1, false, stoppingToken);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    try
                    {
                        ReportMessage? message;
                        try
                        {
                            message = JsonSerializer.Deserialize<ReportMessage>(delivery.Body.Span);
                        }
                        catch (JsonException exception)
                        {
                            logger.LogError(exception, "Mensagem invalida encaminhada para DLQ");
                            await channel.BasicRejectAsync(delivery.DeliveryTag, false, stoppingToken);
                            return;
                        }

                        if (message is null || message.ReportId == Guid.Empty)
                        {
                            logger.LogError("Mensagem sem identificador encaminhada para DLQ");
                            await channel.BasicRejectAsync(delivery.DeliveryTag, false, stoppingToken);
                            return;
                        }

                        bool deadLetter = await ProcessAsync(message.ReportId, stoppingToken);
                        if (deadLetter)
                        {
                            await channel.BasicRejectAsync(delivery.DeliveryTag, false, stoppingToken);
                        }
                        else
                        {
                            await channel.BasicAckAsync(delivery.DeliveryTag, false, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        logger.LogInformation("Worker interrompido; mensagem nao confirmada sera reentregue");
                    }
                    catch (Exception exception) when (exception is RabbitMQClientException or
                        System.IO.IOException or Npgsql.NpgsqlException or DbUpdateException)
                    {
                        logger.LogError(exception, "Dependencia indisponivel; consumo sera reiniciado");
                        await channel.CloseAsync(cancellationToken: CancellationToken.None);
                    }
                };
                await channel.BasicConsumeAsync(ReportBroker.Queue, false, consumer, stoppingToken);
                logger.LogInformation("Worker {WorkerId} consumindo {Queue}", Environment.MachineName, ReportBroker.Queue);
                while (channel.IsOpen && !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is RabbitMQClientException or
                System.IO.IOException or System.Net.Sockets.SocketException or TimeoutException)
            {
                logger.LogError(exception, "Broker indisponivel; nova conexao em 5 segundos");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessAsync(Guid id, CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Relatorio? report;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            report = await db.Relatorios.FromSqlInterpolated(
                $"""SELECT * FROM "Relatorios" WHERE "Id" = {id} FOR UPDATE""")
                .SingleOrDefaultAsync(ct);
            if (report is null)
            {
                logger.LogError("Relatorio {ReportId} nao encontrado", id);
                return true;
            }

            if (report.Conclusoes > 0)
            {
                logger.LogInformation("Relatorio {ReportId} ja concluido; duplicata ignorada", id);
                return false;
            }

            if (report.Tentativas >= ReportBroker.MaxAttempts)
            {
                report.Status = StatusRelatorio.Falhou;
                report.Erro = "Limite de tentativas atingido.";
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            }

            report.Tentativas++;
            report.Status = StatusRelatorio.Processando;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        logger.LogInformation("Relatorio {ReportId} tentativa {Attempt} worker {WorkerId}",
            id, report.Tentativas, Environment.MachineName);
        await Task.Delay(TimeSpan.FromSeconds(report.AtrasoSegundos), ct);
        if (report.FalhaPermanente)
        {
            bool final = report.Tentativas >= ReportBroker.MaxAttempts;
            await db.Relatorios.Where(item => item.Id == id && item.Conclusoes == 0)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(item => item.Status, final ? StatusRelatorio.Falhou : StatusRelatorio.NaFila)
                    .SetProperty(item => item.Erro, "Falha permanente injetada pelo laboratorio."), ct);
            logger.LogWarning("Relatorio {ReportId} falhou; tentativa {Attempt}", id, report.Tentativas);
            if (final)
            {
                return true;
            }

            ConnectionFactory factory = ReportBroker.Factory(
                configuration["Rabbit:Host"]!, configuration["Rabbit:User"]!, configuration["Rabbit:Password"]!);
            await using IConnection connection = await factory.CreateConnectionAsync(ct);
            await using IChannel publisher = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
            await ReportBroker.PublishAsync(publisher, new(id), ct);
            return false;
        }

        var rows = await db.Pedidos.AsNoTracking().Where(item => item.Ativo)
            .Select(item => new
            {
                item.Id,
                item.Cliente,
                Total = item.Itens.Sum(line => line.Quantidade * line.ValorUnitario)
            }).ToListAsync(ct);
        string result = JsonSerializer.Serialize(rows);
        int changed = await db.Relatorios.Where(item => item.Id == id && item.Conclusoes == 0)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Resultado, result)
                .SetProperty(item => item.Status, StatusRelatorio.Concluido)
                .SetProperty(item => item.Erro, (string?)null)
                .SetProperty(item => item.Conclusoes, item => item.Conclusoes + 1), ct);
        logger.LogInformation("Relatorio {ReportId} concluido; efeitos persistidos {Effects}", id, changed);
        return false;
    }
}
