using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Prometheus;

namespace BackendApi.Services.Telemetry;

public sealed class EfCoreMetricsInterceptor : DbCommandInterceptor
{
    private static readonly Histogram DbQueryDuration = Metrics.CreateHistogram(
        "delivery_db_query_duration_seconds",
        "Duration of EF Core database queries in seconds.",
        new HistogramConfiguration
        {
            LabelNames = new[] { "query_type" },
            Buckets = new double[] { 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0 }
        });

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        DbQueryDuration.WithLabels("reader").Observe(eventData.Duration.TotalSeconds);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        DbQueryDuration.WithLabels("reader").Observe(eventData.Duration.TotalSeconds);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        DbQueryDuration.WithLabels("non_query").Observe(eventData.Duration.TotalSeconds);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        DbQueryDuration.WithLabels("non_query").Observe(eventData.Duration.TotalSeconds);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        DbQueryDuration.WithLabels("scalar").Observe(eventData.Duration.TotalSeconds);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        DbQueryDuration.WithLabels("scalar").Observe(eventData.Duration.TotalSeconds);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }
}
