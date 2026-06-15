using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Services;
using BackendApi.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace BackendApi.UnitTests.Telemetry;

public class GpsHistoryServiceTests
{
    [Fact]
    public async Task GetHistoryAsync_ReturnsLatestLimitedPointsInChronologicalOrder()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUserService>();
        await using var db = new ApplicationDbContext(options, currentUser.Object);
        var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        var start = new DateTime(2026, 6, 14, 1, 0, 0, DateTimeKind.Utc);

        db.RiderLocationHistories.AddRange(
            Point("rider-1", factory, 13.70, 100.50, start),
            Point("rider-1", factory, 13.71, 100.51, start.AddMinutes(1)),
            Point("rider-1", factory, 13.72, 100.52, start.AddMinutes(2)),
            Point("rider-2", factory, 14.00, 101.00, start.AddMinutes(3)));
        await db.SaveChangesAsync();

        var service = new GpsHistoryService(db, NullLogger<GpsHistoryService>.Instance);
        var result = await service.GetHistoryAsync(
            "rider-1",
            start.AddMinutes(-1),
            start.AddMinutes(5),
            limit: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(start.AddMinutes(1), result[0].RecordedAt);
        Assert.Equal(start.AddMinutes(2), result[1].RecordedAt);
        Assert.Equal(13.71, result[0].Lat, 5);
        Assert.Equal(100.52, result[1].Lng, 5);
    }

    private static RiderLocationHistory Point(
        string riderId,
        GeometryFactory factory,
        double lat,
        double lng,
        DateTime recordedAt) =>
        new()
        {
            RiderId = riderId,
            Location = factory.CreatePoint(new Coordinate(lng, lat)),
            RecordedAt = recordedAt
        };
}
