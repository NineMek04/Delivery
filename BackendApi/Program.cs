using BackendApi.Setup;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var dotEnvValues = DotEnvLoader.Load(builder.Environment.ContentRootPath)
        .ToDictionary(pair => pair.Key.Replace("__", ":"), pair => pair.Value);

    if (dotEnvValues.Count > 0)
    {
        builder.Configuration.AddInMemoryCollection(dotEnvValues);
    }

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day));

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.AddServerHeader = false;
    });

    builder.Services.AddBackendApiServices(builder.Configuration);

    var app = builder.Build();

    app.UseBackendApiPipeline();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed");
}
finally
{
    Log.CloseAndFlush();
}
