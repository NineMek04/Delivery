using BackendApi.Setup;
using Serilog;
using Microsoft.OpenApi.Extensions;

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
    
    // Prevent ThreadPool Starvation on Thundering Herd (500 riders concurrent Redis awaits)
    System.Threading.ThreadPool.SetMinThreads(1000, 1000);

    // --- 2. Configuration & Environment (.env support) ---
    var dotEnvValues = DotEnvLoader.Load(builder.Environment.ContentRootPath)
        .ToDictionary(pair => pair.Key.Replace("__", ":"), pair => pair.Value);

    if (dotEnvValues.Count > 0)
    {
        builder.Configuration.AddInMemoryCollection(dotEnvValues);
    }

    // --- Vault Secrets Configuration ---
    builder.Configuration.AddVaultConfiguration();

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

    // --- Swagger Spec Auto-Generation Mode ---
    if (args.Contains("--generate-swagger") || builder.Configuration["SWAGGER_GEN"] == "true")
    {
        Log.Information("Generating Swagger/OpenAPI spec file...");
        using (var scope = app.Services.CreateScope())
        {
            var swaggerProvider = scope.ServiceProvider.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>();
            var swagger = swaggerProvider.GetSwagger("v1", null, "/");
            var swaggerJson = swagger.SerializeAsJson(Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0);
            await File.WriteAllTextAsync("swagger.json", swaggerJson);
            Log.Information("Swagger spec file generated successfully at swagger.json");
        }
        return; // Exit successfully immediately
    }

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
