using System.Threading;
using System.Threading.Tasks;

namespace BackendApi.Services.BackgroundWorkers;

public enum DispatchTaskType
{
    CreateOrder,
    RetryOrder,
    BatchGroup
}

public record DispatchTask(DispatchTaskType Type, string Id, string? CorrelationId = null);

public interface IDispatchTaskQueue
{
    ValueTask QueueTaskAsync(DispatchTask task);
    ValueTask<DispatchTask> DequeueAsync(CancellationToken cancellationToken);
}
