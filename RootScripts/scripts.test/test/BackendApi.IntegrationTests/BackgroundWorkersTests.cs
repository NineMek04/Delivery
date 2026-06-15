using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using BackendApi.Services.BackgroundWorkers;
using Xunit;

namespace BackendApi.IntegrationTests;

public class BackgroundWorkersTests
{
    [Fact]
    public async Task HeartbeatMonitor_WithCancelledToken_ExitsGracefully()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<HeartbeatMonitor>>();

        // Setup mock service provider to return a mock scope factory which throws (will be caught by internal try-catch)
        mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Throws(new InvalidOperationException("Simulated scope factory failure"));

        var worker = new HeartbeatMonitor(mockServiceProvider.Object, mockConfig.Object, mockLogger.Object);
        var cts = new CancellationTokenSource();

        // Cancel immediately to prevent long delay execution
        cts.Cancel();

        // Act
        var executeTask = worker.StartAsync(cts.Token);
        
        // Assert
        // The task should complete immediately and gracefully since token is already cancelled
        var exception = await Record.ExceptionAsync(() => executeTask);
        Assert.Null(exception);
    }

    [Fact]
    public async Task DispatchTimeoutWorker_WithCancelledToken_ExitsGracefully()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockLogger = new Mock<ILogger<DispatchTimeoutWorker>>();

        mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Throws(new InvalidOperationException("Simulated scope factory failure"));

        var worker = new DispatchTimeoutWorker(mockServiceProvider.Object, mockLogger.Object);
        var cts = new CancellationTokenSource();

        // Cancel immediately
        cts.Cancel();

        // Act
        var executeTask = worker.StartAsync(cts.Token);

        // Assert
        var exception = await Record.ExceptionAsync(() => executeTask);
        Assert.Null(exception);
    }
}
