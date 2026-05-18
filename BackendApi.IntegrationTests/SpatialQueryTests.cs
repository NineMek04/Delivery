using BackendApi.Data;
using BackendApi.Core.Mappings;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Services;
using BackendApi.Services.BackgroundWorkers;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace BackendApi.IntegrationTests;

/// <summary>
/// Mock ICurrentUserService สำหรับ Test — ไม่ต้องการ HttpContext
/// </summary>
public class DummyCurrentUserService : ICurrentUserService
{
    public Guid? UserId => null;
    public string? UserName => "TestSystem";
    public string? IpAddress => "127.0.0.1";
}

/// <summary>
/// Integration Tests สำหรับ PostGIS Spatial Queries และ Table Partitioning
/// ใช้ Testcontainers รัน postgis/postgis:15-3.3 จริงๆ เพื่อทดสอบ E2E
/// </summary>
public class SpatialQueryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private ApplicationDbContext _dbContext = null!;

    public SpatialQueryTests()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:15-3.3")
            .WithDatabase("delivery_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // สร้าง PostGIS Extension ก่อน EF Core Migration จะใช้งาน
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS postgis;";
        await cmd.ExecuteNonQueryAsync();

        // ใช้ NpgsqlDataSource เพื่อ register NetTopologySuite type mapping
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        dataSourceBuilder.UseNetTopologySuite(handleOrdinates: Ordinates.XY);
        var dataSource = dataSourceBuilder.Build();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource, x => x.UseNetTopologySuite(handleOrdinates: Ordinates.XY))
            .Options;

        _dbContext = new ApplicationDbContext(_options, new DummyCurrentUserService());

        // รัน EF Core Migrations ทั้งหมด (รวม Phase3EnterpriseSpatialScaling)
        await _dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
            await _dbContext.DisposeAsync();

        await _container.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: GiST Index ทำงานถูกต้องสำหรับ Nearby Riders Query
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShopDto_Mapping_Should_Save_2D_Point_To_PostGis()
    {
        // Arrange: use the same Mapster mapping path as POST /api/v1/Shops.
        MappingConfig.Configure();
        var dto = new CreateShopDto
        {
            Name = "Thai",
            MenuName = "string",
            MenuPrice = 100000m,
            Lat = 90,
            Lng = 180
        };

        var shop = dto.Adapt<Shop>();
        shop.Location = MappingConfig.CreatePoint(dto.Lng, dto.Lat);
        Assert.NotNull(shop.Location);
        Assert.False(shop.Location.CoordinateSequence.HasZ);
        _dbContext.Shops.Add(shop);

        // Act
        await _dbContext.SaveChangesAsync();

        var ndims = await _dbContext.Database
            .SqlQuery<int>($@"SELECT ST_NDims(""Location"") AS ""Value"" FROM ""Shops"" WHERE ""Id"" = {shop.Id}")
            .SingleAsync();

        // Assert
        Assert.Equal(2, ndims);
        Assert.Equal(4326, shop.Location?.SRID);
        Assert.Equal(180, shop.Location?.X);
        Assert.Equal(90, shop.Location?.Y);
    }

    [Fact]
    public async Task GiST_Index_Should_Find_Nearby_Rider_Within_Distance()
    {
        // Arrange: Insert Rider ที่พิกัดใจกลาง กทม.
        var rider = new Rider
        {
            Id = "TEST_RIDER_GIST_1",
            Name = "Test Rider",
            State = BackendApi.Core.StateMachines.RiderState.IDLE,
            CurrentLocation = new Point(100.5018, 13.7563) { SRID = 4326 } // สยามสแควร์
        };
        _dbContext.Riders.Add(rider);
        await _dbContext.SaveChangesAsync();

        // Act: Query ด้วย ST_Distance (EF Core แปลเป็น ST_Distance ผ่าน NTS)
        var targetLocation = new Point(100.5050, 13.7580) { SRID = 4326 }; // ใกล้เคียง ~400m

        var nearbyRider = await _dbContext.Riders
            .Where(r => r.CurrentLocation != null
                     && r.CurrentLocation.Distance(targetLocation) < 0.01) // ~1.1km ใน degree
            .FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(nearbyRider);
        Assert.Equal("TEST_RIDER_GIST_1", nearbyRider.Id);
    }

    [Fact]
    public async Task GiST_Index_Should_Not_Find_Rider_Outside_Distance()
    {
        // Arrange: Insert Rider ที่เชียงใหม่
        var rider = new Rider
        {
            Id = "TEST_RIDER_GIST_2",
            Name = "Chiang Mai Rider",
            State = BackendApi.Core.StateMachines.RiderState.IDLE,
            CurrentLocation = new Point(98.9853, 18.7883) { SRID = 4326 } // เชียงใหม่
        };
        _dbContext.Riders.Add(rider);
        await _dbContext.SaveChangesAsync();

        // Act: Query หา Rider ใกล้ กทม. (ห่างกัน ~700km)
        var bangkokLocation = new Point(100.5018, 13.7563) { SRID = 4326 };

        var nearbyRider = await _dbContext.Riders
            .Where(r => r.Id == "TEST_RIDER_GIST_2"
                     && r.CurrentLocation != null
                     && r.CurrentLocation.Distance(bangkokLocation) < 0.1) // ~11km ใน degree
            .FirstOrDefaultAsync();

        // Assert: ไม่ควรเจอ Rider ที่เชียงใหม่
        Assert.Null(nearbyRider);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: Table Partitioning — Insert ลง Partition ที่ถูกต้อง
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RiderLocationHistory_Insert_Should_Go_To_Correct_Partition()
    {
        // Arrange: สร้าง Rider ก่อน (FK constraint)
        var rider = new Rider
        {
            Id = "TEST_RIDER_PARTITION_1",
            Name = "Partition Test Rider",
            State = BackendApi.Core.StateMachines.RiderState.IDLE,
            CurrentLocation = new Point(100.5018, 13.7563) { SRID = 4326 }
        };
        _dbContext.Riders.Add(rider);
        await _dbContext.SaveChangesAsync();

        // Act: Insert RiderLocationHistory ในเดือนปัจจุบัน
        var now = DateTime.UtcNow;
        var history = new RiderLocationHistory
        {
            Id = Guid.NewGuid().ToString(),
            RiderId = "TEST_RIDER_PARTITION_1",
            Location = new Point(100.5018, 13.7563) { SRID = 4326 },
            RecordedAt = now
        };
        _dbContext.RiderLocationHistories.Add(history);
        await _dbContext.SaveChangesAsync();

        // Assert: ตรวจสอบว่าข้อมูลอยู่ใน Partition ที่ถูกต้อง
        var partitionName = $"RiderLocationHistories_{now.Year}_{now.Month:D2}";
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT COUNT(*) FROM ""{partitionName}""
            WHERE ""RiderId"" = 'TEST_RIDER_PARTITION_1'";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task RiderLocationHistory_Bulk_Insert_Should_Succeed()
    {
        // Arrange: สร้าง Rider
        var rider = new Rider
        {
            Id = "TEST_RIDER_BULK_1",
            Name = "Bulk Insert Rider",
            State = BackendApi.Core.StateMachines.RiderState.IDLE,
            CurrentLocation = new Point(100.5018, 13.7563) { SRID = 4326 }
        };
        _dbContext.Riders.Add(rider);
        await _dbContext.SaveChangesAsync();

        // Act: Bulk Insert 10 GPS points (จำลอง GpsSyncWorker)
        var now = DateTime.UtcNow;
        var points = Enumerable.Range(0, 10).Select(i => new RiderLocationHistory
        {
            Id = Guid.NewGuid().ToString(),
            RiderId = "TEST_RIDER_BULK_1",
            Location = new Point(100.5018 + (i * 0.0001), 13.7563 + (i * 0.0001)) { SRID = 4326 },
            RecordedAt = now.AddSeconds(-i * 30) // ย้อนหลัง 30 วินาทีต่อจุด
        }).ToList();

        await _dbContext.RiderLocationHistories.AddRangeAsync(points);
        var inserted = await _dbContext.SaveChangesAsync();

        // Assert
        Assert.Equal(10, inserted);

        var totalCount = await _dbContext.RiderLocationHistories
            .Where(h => h.RiderId == "TEST_RIDER_BULK_1")
            .CountAsync();
        Assert.Equal(10, totalCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: PartitionMaintenanceWorker สร้าง Partition ล่วงหน้าได้
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PartitionMaintenanceWorker_Should_Create_Future_Partitions()
    {
        // Arrange: สร้าง ServiceProvider จำลองสำหรับ Worker
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddScoped<ApplicationDbContext>(sp => 
            new ApplicationDbContext(sp.GetRequiredService<DbContextOptions<ApplicationDbContext>>(), new DummyCurrentUserService()));
        var serviceProvider = services.BuildServiceProvider();

        var worker = new PartitionMaintenanceWorker(
            serviceProvider,
            NullLogger<PartitionMaintenanceWorker>.Instance);

        // Act: รัน Worker ครั้งเดียว (เรียก CreatePartitionsSafeAsync ผ่าน ExecuteAsync)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // เรียก method ผ่าน reflection เพราะเป็น private
        var method = typeof(PartitionMaintenanceWorker)
            .GetMethod("CreatePartitionsSafeAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        await (Task)method.Invoke(worker, [cts.Token])!;

        // Assert: ตรวจสอบว่า Partition ของเดือนถัดไปถูกสร้างแล้ว
        var nextMonth = DateTime.UtcNow.AddMonths(1);
        var partitionName = $"RiderLocationHistories_{nextMonth.Year}_{nextMonth.Month:D2}";

        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT COUNT(*) FROM pg_class
            WHERE relname = '{partitionName}' AND relkind = 'r'";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.True(count > 0, $"Partition '{partitionName}' should have been created by PartitionMaintenanceWorker");
    }
}
