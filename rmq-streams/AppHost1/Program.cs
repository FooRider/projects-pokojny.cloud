using System.Diagnostics.Contracts;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

//var rabbitMq = builder.AddRabbitMQ("broker");
var rabbitMq = builder.AddContainer("broker", "rabbitmq", "4.0-management-streams")
    .WithDockerfile("RabbitMqStreamsImage")
    .WithEnvironment("RABBITMQ_NODENAME", "rabbit@localhost")
    .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "-rabbitmq_stream advertised_host localhost")
    .WithEndpoint(
        targetPort: 5672,
        scheme: "amqp",
        name: "classic",
        isProxied: false)
    .WithEndpoint(
        targetPort: 5552,
        scheme: "tcp",
        name: "streams",
        isProxied: false)
    .WithEndpoint(
        port: 15672,
        targetPort: 15672,
        scheme: "http",
        name: "management",
        isProxied: false,
        isExternal: true);

var rmqClassicEndpoint = rabbitMq.GetEndpoint("classic");

var producerClassic = builder.AddProject<Projects.Producer_Classic>($"producer-classic")
    .WithEnvironment("ConnectionStrings__broker",
        $"amqp://guest:guest@{rmqClassicEndpoint.Property(EndpointProperty.Host)}:{rmqClassicEndpoint.Property(EndpointProperty.Port)}/")
    .WithReplicas(4);

var consumerClassic = builder.AddProject<Projects.Consumer_Classic>("consumer-classic")
    .WithEnvironment("ConnectionStrings__broker", $"amqp://guest:guest@{rmqClassicEndpoint.Property(EndpointProperty.Host)}:{rmqClassicEndpoint.Property(EndpointProperty.Port)}/");

builder.Build().Run();