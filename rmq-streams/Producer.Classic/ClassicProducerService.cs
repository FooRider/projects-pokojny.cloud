using System.Diagnostics.Metrics;
using Common;
using Common.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;

namespace Producer.Classic;

public class ClassicProducerService(
    ConnectionFactory connectionFactory,
    MeterProvider meterProvider,
    ILogger<ClassicProducerService> logger)
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
        Counter<long>? counter = meter.CreateCounter<long>(TelemetryConstants.MessagesSentMetricsName, "messages",
            "Count of messages sent");
        
        var messagesSent = 0L;
        meter.CreateGaugeFromCounter(TelemetryConstants.MessagesSendingRateMetricsName,
            () => (double)messagesSent, "mps", "Messages sending rate", logger);

        var buffer = new byte[100_000];
        new Random(123).NextBytes(buffer);
        var rom = new ReadOnlyMemory<byte>(buffer);
        while (!stoppingToken.IsCancellationRequested)
        {
            var properties = new BasicProperties();
            await model.BasicPublishAsync(BrokerModel.InletExchangeName,
                routingKey: "",
                mandatory: false,
                basicProperties: properties,
                body: rom.Slice(0, 1_000),
                cancellationToken: stoppingToken);
            counter?.Add(1);
            messagesSent++;
            //await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken);
        }
    }
}