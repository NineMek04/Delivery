using BackendApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackendApi.HealthChecks.Database;

public class PostGisHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;

    public PostGisHealthCheck(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct)
    {
        try
        {
            // ใช้ GetDbConnection() + ExecuteScalarAsync เพื่อรัน SELECT จริง
            // ExecuteSqlRawAsync ออกแบบสำหรับ DML เท่านั้น จะคืน -1 สำหรับ SELECT เสมอ
            var conn = _dbContext.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ST_AsText(ST_SetSRID(ST_Point(100.5, 13.7), 4326))";
            var result = await cmd.ExecuteScalarAsync(ct);

            if (result is string wkt && wkt.StartsWith("POINT"))
                return HealthCheckResult.Healthy($"PostGIS is operational. Test result: {wkt}");

            return HealthCheckResult.Degraded("PostGIS returned unexpected result.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostGIS check failed.", ex);
        }
    }
}

