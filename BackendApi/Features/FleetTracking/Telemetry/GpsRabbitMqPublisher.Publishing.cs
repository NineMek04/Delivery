using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace BackendApi.Features.FleetTracking.Telemetry
{
    /// <summary>
    /// Raw RabbitMQ Publisher for GPS Telemetry.
    /// Manages a dedicated, thread-safe connection and channel to publish
    /// persistent telemetry messages to the durable queue 'gps_telemetry_queue'.
    /// </summary>
    public partial class GpsRabbitMqPublisher
    {
        /// <summary>
        /// Publishes a GPS TrackPoint to RabbitMQ as a persistent message.
        /// </summary>
        private readonly System.Threading.Channels.Channel<TrackPoint> _channelQueue;
        private readonly System.Threading.Channels.Channel<TrackPoint> _snapChannelQueue;
        private readonly System.Threading.CancellationTokenSource _cts = new();
        private readonly Task _processQueueTask;
        private readonly Task _processSnapQueueTask;

        private void OnApplicationStopping()
        {
            _logger.LogInformation("GpsRabbitMqPublisher: Application is stopping. Draining queues...");
            
            // Mark channels as completed so background loops finish reading remaining messages
            _channelQueue.Writer.TryComplete();
            _snapChannelQueue.Writer.TryComplete();

            try
            {
                // Wait up to 5 seconds for background publishing tasks to finish processing remaining items
                Task.WaitAll(new[] { _processQueueTask, _processSnapQueueTask }, TimeSpan.FromSeconds(5));
                _logger.LogInformation("GpsRabbitMqPublisher: Queues drained successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GpsRabbitMqPublisher: Error occurred while waiting for queues to drain.");
            }
        }

        private async System.Threading.Tasks.Task ProcessQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var points = new List<TrackPoint>();
                try
                {
                    EnsureConnection();
                    
                    if (await _channelQueue.Reader.WaitToReadAsync(_cts.Token))
                    {
                        // Buffer up to 500 messages that are immediately available
                        while (points.Count < 500 && _channelQueue.Reader.TryRead(out var p))
                        {
                            points.Add(p);
                        }

                        if (points.Count > 0)
                        {
                            lock (_connectionLock)
                            {
                                if (_channel == null) break; // Re-ensure connection on next loop

                                var batch = _channel.CreateBasicPublishBatch();
                                foreach (var point in points)
                                {
                                    var message = JsonSerializer.Serialize(point);
                                    var body = Encoding.UTF8.GetBytes(message);
                                    var properties = _channel.CreateBasicProperties();
                                    properties.Persistent = true;
                                    properties.Type = nameof(TrackPoint);

                                    batch.Add(
                                        exchange: "",
                                        routingKey: QueueName,
                                        mandatory: true,
                                        properties: properties,
                                        body: body.AsMemory()
                                    );
                                }
                                batch.Publish();
                                _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
                            }
                        }
                    }
                }
                catch (System.OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in GpsRabbitMqPublisher background worker.");
                    foreach (var point in points)
                    {
                        _channelQueue.Writer.TryWrite(point);
                    }
                    await System.Threading.Tasks.Task.Delay(1000, _cts.Token);
                }
            }
        }

        private async System.Threading.Tasks.Task ProcessSnapQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var points = new List<TrackPoint>();
                try
                {
                    EnsureConnection();
                    
                    if (await _snapChannelQueue.Reader.WaitToReadAsync(_cts.Token))
                    {
                        // Buffer up to 500 messages that are immediately available
                        while (points.Count < 500 && _snapChannelQueue.Reader.TryRead(out var p))
                        {
                            points.Add(p);
                        }

                        if (points.Count > 0)
                        {
                            lock (_connectionLock)
                            {
                                if (_channel == null) break; // Re-ensure connection on next loop

                                var batch = _channel.CreateBasicPublishBatch();
                                foreach (var point in points)
                                {
                                    var message = JsonSerializer.Serialize(point);
                                    var body = Encoding.UTF8.GetBytes(message);
                                    var properties = _channel.CreateBasicProperties();
                                    properties.Persistent = true;
                                    properties.Type = nameof(TrackPoint);

                                    batch.Add(
                                        exchange: "",
                                        routingKey: SnapQueueName,
                                        mandatory: true,
                                        properties: properties,
                                        body: body.AsMemory()
                                    );
                                }
                                batch.Publish();
                                _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
                            }
                        }
                    }
                }
                catch (System.OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in GpsRabbitMqPublisher background snap worker.");
                    foreach (var point in points)
                    {
                        _snapChannelQueue.Writer.TryWrite(point);
                    }
                    await System.Threading.Tasks.Task.Delay(1000, _cts.Token);
                }
            }
        }

        public void Publish(TrackPoint point)
        {
            // Non-blocking fire-and-forget publish to memory channel
            _channelQueue.Writer.TryWrite(point);
        }

        public void PublishBatch(IEnumerable<TrackPoint> points)
        {
            if (points == null) return;
            foreach (var point in points)
            {
                _channelQueue.Writer.TryWrite(point);
            }
        }

        public void PublishForSnap(TrackPoint point)
        {
            // Non-blocking fire-and-forget publish to snap memory channel
            _snapChannelQueue.Writer.TryWrite(point);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _cts.Cancel();
            lock (_connectionLock)
            {
                _channel?.Dispose();
                _connection?.Dispose();
            }
            _disposed = true;
        }
        }
}
