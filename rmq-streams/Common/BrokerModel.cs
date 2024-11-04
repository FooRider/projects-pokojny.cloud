using RabbitMQ.Client;

namespace Common;

public static class BrokerModel
{
    public const string InletExchangeName = "Inlet";
    
    public static async Task DeclareCommon(IChannel model, CancellationToken cancellationToken)
    {
        await model.ExchangeDeclareAsync(InletExchangeName,
            ExchangeType.Fanout,
            durable: false,
            autoDelete: true,
            cancellationToken: cancellationToken);
    }
}