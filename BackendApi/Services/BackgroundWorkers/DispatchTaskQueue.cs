using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BackendApi.Services.BackgroundWorkers;

public class DispatchTaskQueue : IDispatchTaskQueue
{
    private readonly Channel<DispatchTask> _queue;

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
    }

    public async ValueTask<DispatchTask> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}
