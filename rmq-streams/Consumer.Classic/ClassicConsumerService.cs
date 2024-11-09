using System.Diagnostics.Metrics;
using Common;
using Common.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Consumer.Classic;

public class ClassicConsumerService(
    ConnectionFactory connectionFactory,
    MeterProvider meterProvider,
    ILogger<ClassicConsumerService> logger)
    : BackgroundService
{
    private async Task<IConnection> CreateInitialConnection(CancellationToken stoppingToken)
    {
        var connectionResiliencePipeline = new Polly.ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions()
            {
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                MaxDelay = TimeSpan.FromSeconds(30),
                MaxRetryAttempts = 10
            })
            .Build();

        var res = await connectionResiliencePipeline.ExecuteAsync(async (cancellationToken) =>
        {
            if (stoppingToken.IsCancellationRequested)
                return Outcome.FromException<IConnection>(new OperationCanceledException());

            logger.LogInformation("Going to connect to RabbitMQ at {Uri}", connectionFactory.Uri);
            var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("Connection to RabbitMQ established");
            return Outcome.FromResult<IConnection>(connection);
        }, stoppingToken);

        res.ThrowIfException();
        return res.Result!;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await CreateInitialConnection(stoppingToken);

        var model = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await BrokerModel.DeclareCommon(model, stoppingToken);

        var meter = new Meter(TelemetryConstants.MeterName);
        Counter<long>? counter = meter.CreateCounter<long>(TelemetryConstants.MessagesReceivedMetricsName, "messages",
            "Count of messages received");

        var messagesReceived = 0L;
        meter.CreateGaugeFromCounter(TelemetryConstants.MessagesReceivingRateMetricsName,
            () => (double)messagesReceived, "mps", "Messages receiving rate", logger);

        var queue = await model.QueueDeclareAsync(
            queue: "",
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: stoppingToken
        );

        var consumer = new AsyncEventingBasicConsumer(model);
        consumer.ReceivedAsync += async (sender, @event) =>
        {
            counter.Add(1);
            messagesReceived++;
        };
        
        await model.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, stoppingToken);

        await model.QueueBindAsync(queue.QueueName, BrokerModel.InletExchangeName, routingKey: "", 
            cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
    }
}