using BackendApi.Core.Filters;
using BackendApi.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace BackendApi.UnitTests.Core;

public class ApiResponsePipelineTests
{
    [Fact]
    public void GlobalExceptionFilter_ReturnsStatusBearing500Envelope()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(candidate => candidate.EnvironmentName)
            .Returns(Environments.Production);
        var filter = new GlobalExceptionFilter(
            Mock.Of<ILogger<GlobalExceptionFilter>>(),
            environment.Object);
        var actionContext = CreateActionContext();
        var exceptionContext = new ExceptionContext(actionContext, [])
        {
            Exception = new InvalidOperationException("boom")
        };

        filter.OnException(exceptionContext);

        var result = Assert.IsType<ObjectResult>(exceptionContext.Result);
        var body = Assert.IsType<ApiResponse>(result.Value);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError, body.Status);
        Assert.False(body.Success);
        Assert.Equal("INTERNAL_ERROR", body.Code);
        Assert.True(exceptionContext.ExceptionHandled);
    }

    [Fact]
    public void GlobalExceptionFilter_ReturnsStatusBearing503EnvelopeForTimeout()
    {
        var filter = new GlobalExceptionFilter(
            Mock.Of<ILogger<GlobalExceptionFilter>>(),
            Mock.Of<IHostEnvironment>());
        var actionContext = CreateActionContext();
        var exceptionContext = new ExceptionContext(actionContext, [])
        {
            Exception = new TimeoutException("slow dependency")
        };

        filter.OnException(exceptionContext);

        var result = Assert.IsType<ObjectResult>(exceptionContext.Result);
        var body = Assert.IsType<ApiResponse>(result.Value);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, body.Status);
        Assert.Equal("SERVICE_UNAVAILABLE", body.Code);
    }

    private static ActionContext CreateActionContext() =>
        new(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor());
}
