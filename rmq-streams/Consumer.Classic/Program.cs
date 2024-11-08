using System.Collections;
using Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Consumer.Classic;
using RabbitMQ.Client;

var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.Configuration.AddJsonFile("appsettings.json", optional: true);
hostBuilder.Configuration.AddEnvironmentVariables();
hostBuilder.Configuration.AddCommandLine(args);

foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) { Console.WriteLine($"{e.Key}={e.Value}"); }

var otel = hostBuilder.Services.AddOpenTelemetry();
otel.WithMetrics(metrics =>
{
    metrics.AddMeter(TelemetryConstants.MeterName)
        //.AddPrometheusHttpListener()
        ;
});
if (hostBuilder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] != null)
    otel.UseOtlpExporter();
    
var brokerConnectionString = hostBuilder.Configuration.GetConnectionString("broker")!;
hostBuilder.Services.AddSingleton<ConnectionFactory>(sp => new ConnectionFactory()
{
    Uri = new Uri(brokerConnectionString)
});
hostBuilder.Services.AddHostedService<ClassicConsumerService>();

await hostBuilder.Build().RunAsync();