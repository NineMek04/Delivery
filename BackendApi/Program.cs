using BackendApi.Setup;

var builder = WebApplication.CreateBuilder(args);

var dotEnvValues = DotEnvLoader.Load(builder.Environment.ContentRootPath)
    .ToDictionary(pair => pair.Key.Replace("__", ":"), pair => pair.Value);

if (dotEnvValues.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(dotEnvValues);
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

builder.Services.AddBackendApiServices(builder.Configuration);

var app = builder.Build();

app.UseBackendApiPipeline();

app.Run();
