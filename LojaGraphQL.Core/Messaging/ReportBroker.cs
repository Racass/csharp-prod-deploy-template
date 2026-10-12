using System.Text.Json;
using RabbitMQ.Client;

namespace LojaGraphQL.Core.Messaging;

public sealed record ReportMessage(Guid ReportId);

public static class ReportBroker
{
    public const string Queue = "relatorios";
    public const string DeadQueue = "relatorios.dlq";
    public const int MaxAttempts = 3;

    public static ConnectionFactory Factory(string host, string user, string password) => new()
    {
        HostName = host,
        UserName = user,
        Password = password,
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
        RequestedConnectionTimeout = TimeSpan.FromSeconds(10)
    };

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.QueueDeclareAsync(DeadQueue, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false,
            autoDelete: false, arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = DeadQueue
            }, cancellationToken: ct);
    }

    public static async Task PublishAsync(
        IChannel channel, ReportMessage message, CancellationToken ct)
    {
        BasicProperties properties = new()
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.ReportId.ToString()
        };
        await channel.BasicPublishAsync("", Queue, mandatory: true,
            basicProperties: properties,
            body: JsonSerializer.SerializeToUtf8Bytes(message), cancellationToken: ct);
    }
}
