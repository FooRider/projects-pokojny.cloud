using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
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
        Counter<long>? counter = meter.CreateCounter<long>(TelemetryConstants.MessagesSentMetricsName, "messages", "Count of messages sent");
        
        var messagesSent = 0L;
        var previousMessages = messagesSent;
        var previousTicks = Stopwatch.GetTimestamp();
        ObservableGauge<double>? gauge = meter.CreateObservableGauge<double>(
            TelemetryConstants.MessagesSendingRateMetricsName,
            () =>
            {
                var currentMessages = messagesSent;
                var currentTicks = Stopwatch.GetTimestamp();

                var newMessagesSent = currentMessages - previousMessages;
                var elapsed = Stopwatch.GetElapsedTime(previousTicks, currentTicks);
                var rate = newMessagesSent / elapsed.TotalSeconds;
                logger.LogInformation("Current sending rate: {Rate}", rate);

                previousMessages = currentMessages;
                previousTicks = currentTicks;

                return [new Measurement<double>(rate)];
            },
            unit: "mps",
            description: "Messages sending rate");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var properties = new BasicProperties();
            var msg = "Hello World!";
            var body = Encoding.UTF8.GetBytes(msg);
            await model.BasicPublishAsync(BrokerModel.InletExchangeName,
                routingKey: "",
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: stoppingToken);
            counter?.Add(1);
            messagesSent++;
            await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken);
        }
    }
}