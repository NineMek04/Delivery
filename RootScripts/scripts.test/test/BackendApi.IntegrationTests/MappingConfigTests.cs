using BackendApi.Core.Mappings;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Models.DTOs;
using Mapster;
using Xunit;

namespace BackendApi.IntegrationTests;

public class MappingConfigTests
{
    [Fact]
    public void CreateShopDto_Mapping_Should_Create_XY_Only_Point()
    {
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
        Assert.Equal(4326, shop.Location.SRID);
        Assert.False(shop.Location.CoordinateSequence.HasZ);
        Assert.True(double.IsNaN(shop.Location.Coordinate.Z));
        Assert.Equal(180, shop.Location.X);
        Assert.Equal(90, shop.Location.Y);
    }
}


