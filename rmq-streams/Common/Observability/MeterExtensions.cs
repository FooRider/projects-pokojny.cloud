using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Numerics;
using Microsoft.Extensions.Logging;

namespace Common.Observability;

public static class MeterExtensions
{
    public static ObservableGauge<T> CreateGaugeFromCounter<T>(
        this Meter meter,
        string metricName,
        Func<T> counterAccessor,
        string? unit = null,
        string? description = null,
        ILogger? logger = null) 
        where T : struct, INumber<T>, IDivisionOperators<T, double, T>
    {
        var previousValue = counterAccessor();
        var previousTicks = Stopwatch.GetTimestamp();
        return meter.CreateObservableGauge<T>(
            metricName,
            () =>
            {
                var currentValue = counterAccessor();
                var currentTicks = Stopwatch.GetTimestamp();

                var newMessagesSent = currentValue - previousValue;
                var elapsed = Stopwatch.GetElapsedTime(previousTicks, currentTicks);
                var rate = newMessagesSent / elapsed.TotalSeconds;
                logger?.LogInformation("Current sending rate: {Rate}", rate);

                previousValue = currentValue;
                previousTicks = currentTicks;

                return [new Measurement<T>(rate)];
            },
            unit: unit,
            description: description);
    }
}