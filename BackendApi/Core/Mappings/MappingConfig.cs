using BackendApi.Models;
using BackendApi.Models.DTOs;
using Mapster;
using NetTopologySuite.Geometries;

namespace BackendApi.Core.Mappings;

/// <summary>
/// จุดศูนย์รวมตั้งค่าการแปลง Entity ↔ DTO ด้วย Mapster
/// ทุกคนในทีมต้องมาลงทะเบียน Mapping ที่นี่
/// </summary>
public static class MappingConfig
{
    public static TypeAdapterConfig Configure()
    {
        var config = TypeAdapterConfig.GlobalSettings;
        config.Default.IgnoreNullValues(true);

        // ================================================
        // Order Entity ↔ OrderDto
        // ================================================
        config.NewConfig<Order, OrderDto>()
            .Map(dest => dest.PickupLat, src => src.PickupLocation != null ? src.PickupLocation.Y : (double?)null)
            .Map(dest => dest.PickupLng, src => src.PickupLocation != null ? src.PickupLocation.X : (double?)null)
            .Map(dest => dest.DropoffLat, src => src.DropoffLocation != null ? src.DropoffLocation.Y : (double?)null)
            .Map(dest => dest.DropoffLng, src => src.DropoffLocation != null ? src.DropoffLocation.X : (double?)null);

        config.NewConfig<CreateOrderDto, Order>()
            .Map(dest => dest.PickupLocation, src => CreatePoint(src.PickupLng, src.PickupLat))
            .Map(dest => dest.DropoffLocation, src => CreatePoint(src.DropoffLng, src.DropoffLat))
            .Map(dest => dest.Id, _ => Guid.NewGuid().ToString());

        // ================================================
        // Rider Entity ↔ RiderDto
        // ================================================
        config.NewConfig<Rider, RiderDto>()
            .Map(dest => dest.Lat, src => src.CurrentLocation != null ? src.CurrentLocation.Y : (double?)null)
            .Map(dest => dest.Lng, src => src.CurrentLocation != null ? src.CurrentLocation.X : (double?)null);

        config.NewConfig<CreateRiderDto, Rider>()
            .Map(dest => dest.CurrentLocation, src => src.Lat.HasValue && src.Lng.HasValue
                ? CreatePoint(src.Lng.Value, src.Lat.Value)
                : null)
            .Map(dest => dest.Id, _ => Guid.NewGuid().ToString());

        return config;
    }

    /// <summary>
    /// สร้าง PostGIS Point จาก lng/lat (SRID 4326)
    /// </summary>
    private static Point CreatePoint(double lng, double lat)
    {
        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        return factory.CreatePoint(new Coordinate(lng, lat));
    }
}
