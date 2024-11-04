using Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Producer.Classic;
using RabbitMQ.Client;

foreach (var x in Environment.GetEnvironmentVariables())
{
    Console.WriteLine(x);
}

var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.Configuration.AddJsonFile("appsettings.json", optional: true);
hostBuilder.Configuration.AddEnvironmentVariables();
hostBuilder.Configuration.AddCommandLine(args);

var otel = hostBuilder.Services.AddOpenTelemetry();
otel.WithMetrics(metrics =>
{
    metrics.AddMeter(TelemetryConstants.MeterName)
        .AddPrometheusHttpListener();
});
if (hostBuilder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] != null)
    otel.UseOtlpExporter();

var brokerConnectionString = hostBuilder.Configuration.GetConnectionString("broker")!;
hostBuilder.Services.AddSingleton<ConnectionFactory>(sp => new ConnectionFactory()
{
    Uri = new Uri(brokerConnectionString)
});
hostBuilder.Services.AddHostedService<ClassicProducerService>();

await hostBuilder.Build().RunAsync();