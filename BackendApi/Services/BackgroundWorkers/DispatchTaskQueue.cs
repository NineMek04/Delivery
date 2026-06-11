using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BackendApi.Services.BackgroundWorkers;

public class DispatchTaskQueue : IDispatchTaskQueue
{
    private readonly Channel<DispatchTask> _queue;
    private int _count = 0;

    public DispatchTaskQueue()
    {
        var options = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        };
        _queue = Channel.CreateUnbounded<DispatchTask>(options);
    }

    public async ValueTask QueueTaskAsync(DispatchTask task)
    {
        await _queue.Writer.WriteAsync(task);
        Interlocked.Increment(ref _count);
        BackendApi.Security.SecurityMetrics.DispatchQueueDepth.Set(_count);
    }

    public async ValueTask<DispatchTask> DequeueAsync(CancellationToken cancellationToken)
    {
        var task = await _queue.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _count);
        BackendApi.Security.SecurityMetrics.DispatchQueueDepth.Set(_count);
        return task;
    }
}
