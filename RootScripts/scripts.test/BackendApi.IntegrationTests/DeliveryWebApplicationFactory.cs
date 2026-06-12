using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Security;
using BackendApi.Services;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace BackendApi.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory ที่ใช้ Testcontainers PostgreSQL
/// สำหรับรันเทสที่ต้องการ Full HTTP Pipeline (Auth, Controllers, Services)
/// </summary>
public class DeliveryWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private readonly RabbitMqContainer _rabbitMqContainer;
    private readonly RedisContainer _redisContainer;

    public DeliveryWebApplicationFactory()
    {
        // Set environment variables for Program.cs to use before WebApplicationFactory injects test configs
        Environment.SetEnvironmentVariable("Jwt__Key", "ThisIsADummyKeyForTestingPurposesMustBeAtLeast32Bytes!");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "TestIssuer");
        Environment.SetEnvironmentVariable("Jwt__Audience", "TestAudience");

        _container = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:15-3.3")
            .WithDatabase("delivery_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        _rabbitMqContainer = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management-alpine")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();

        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_container.StartAsync(), _rabbitMqContainer.StartAsync(), _redisContainer.StartAsync());

        // Set environment variables for Program.cs to use, ensuring they are loaded into ConfigurationManager
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", $"{_redisContainer.Hostname}:{_redisContainer.GetMappedPublicPort(6379)}");
        Environment.SetEnvironmentVariable("Redis", $"{_redisContainer.Hostname}:{_redisContainer.GetMappedPublicPort(6379)}");
        Environment.SetEnvironmentVariable("MessageBroker__Host", _rabbitMqContainer.Hostname);
        Environment.SetEnvironmentVariable("MessageBroker__Port", _rabbitMqContainer.GetMappedPublicPort(5672).ToString());
        Environment.SetEnvironmentVariable("MessageBroker__Username", "guest");
        Environment.SetEnvironmentVariable("MessageBroker__Password", "guest");
        Environment.SetEnvironmentVariable("RateLimiting__Global__PermitLimit", "99999");
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", "99999");

        // Create PostGIS extension before EF Core migration
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS postgis;";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(string AccessToken, string UserId)> CreatePrivilegedUserAndGetTokenAsync(
        HttpClient client,
        string role)
    {
        if (role != AuthConstants.AdminRole && role != AuthConstants.DispatcherRole)
            throw new ArgumentOutOfRangeException(nameof(role), "Only privileged test users may use this helper.");

        var email = $"privileged_{Guid.NewGuid():N}@test.com";
        const string password = "TestPass123!";
        var user = new User
        {
            Email = email,
            PasswordHash = PasswordHasher.HashPassword(password),
            FullName = "Privileged Test User",
            Role = role,
            IsActive = true
        };

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Email = email, Password = password });
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = document.RootElement
            .GetProperty("value")
            .GetProperty("accessToken")
            .GetString();

        return (accessToken!, user.Id);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_container.DisposeAsync().AsTask(), _rabbitMqContainer.DisposeAsync().AsTask(), _redisContainer.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Inject minimum configuration required to pass validation during startup
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:Key", "ThisIsADummyKeyForTestingPurposesMustBeAtLeast32Bytes!" },
                { "Jwt:Issuer", "TestIssuer" },
                { "Jwt:Audience", "TestAudience" },
                { "Jwt:ExpirationMinutes", "60" },
                { "RateLimiting:Global:PermitLimit", "99999" },
                { "RateLimiting:Auth:PermitLimit", "99999" },
                { "MessageBroker:Host", _rabbitMqContainer.Hostname },
                { "MessageBroker:Port", _rabbitMqContainer.GetMappedPublicPort(5672).ToString() },
                { "MessageBroker:Username", "guest" },
                { "MessageBroker:Password", "guest" },
                { "ConnectionStrings:Redis", $"{_redisContainer.Hostname}:{_redisContainer.GetMappedPublicPort(6379)}" },
                { "Redis", $"{_redisContainer.Hostname}:{_redisContainer.GetMappedPublicPort(6379)}" }
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the existing ApplicationDbContext registration
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            // Remove existing ICurrentUserService
            var userServiceDesc = services.SingleOrDefault(
                d => d.ServiceType == typeof(ICurrentUserService));
            if (userServiceDesc is not null)
                services.Remove(userServiceDesc);

            // Register Testcontainers-backed database
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
            dataSourceBuilder.UseNetTopologySuite(handleOrdinates: NetTopologySuite.Geometries.Ordinates.XY);
            var dataSource = dataSourceBuilder.Build();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(dataSource, npgsql =>
                    npgsql.UseNetTopologySuite(handleOrdinates: NetTopologySuite.Geometries.Ordinates.XY)));

            services.AddScoped<ICurrentUserService, DummyCurrentUserService>();

            // Ensure the database is created and migrated
            using var scope = services.BuildServiceProvider().CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Database.Migrate();
        });
    }
}
