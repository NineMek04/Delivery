using BackendApi.Setup;
using Serilog;

// --- 1. Bootstrap Logger (Early Logging for Start-up Failures) ---
if (Log.Logger.GetType().Name == "SilentLogger")
{
    Log.Logger = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .CreateLogger();
}

try
{
    Log.Information("Starting Delivery Backend API...");

    var builder = WebApplication.CreateBuilder(args);

    // --- 2. Configuration & Environment (.env support) ---
    var dotEnvValues = DotEnvLoader.Load(builder.Environment.ContentRootPath)
        .ToDictionary(pair => pair.Key.Replace("__", ":"), pair => pair.Value);

    if (dotEnvValues.Count > 0)
    {
        builder.Configuration.AddInMemoryCollection(dotEnvValues);
    }

    // --- 3. Logging (Serilog) ---
    var seqUrl = builder.Configuration["SEQ_URL"] ?? builder.Configuration["Seq:ServerUrl"] ?? "http://seq:5341";
    var seqApiKey = builder.Configuration["SEQ_API_KEY"];

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
        .WriteTo.Seq(seqUrl, apiKey: string.IsNullOrWhiteSpace(seqApiKey) ? null : seqApiKey));

    // --- 4. Infrastructure (Kestrel, etc.) ---
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.AddServerHeader = false;
    });

    // --- 5. Services Registration (The DI Container) ---
    builder.Services.AddBackendApiServices(builder.Configuration);

    var app = builder.Build();

    // --- 6. Pipeline Configuration (Middleware) ---
    app.UseBackendApiPipeline();

    // --- 7. Automatic Database Migration ---
    await app.MigrateDatabaseAsync();

    app.Run();
}
catch (Exception ex)
{
    if (ex.GetType().Name == "HostAbortedException")
    {
        throw;
    }
    Console.WriteLine($"[PROGRAM CRASH] {ex}");
    Log.Fatal(ex, "Application start-up failed");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
