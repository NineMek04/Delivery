using System.Text.Json;
using BackendApi.Models.DTOs;

namespace BackendApi.UnitTests.Models;

public class ShopDtoTests
{
    [Fact]
    public void DeserializePartialUpdate_WhenStateFieldsAreOmitted_LeavesThemNull()
    {
        var dto = JsonSerializer.Deserialize<ShopDto>(
            """{"name":"Updated shop"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(dto);
        Assert.Null(dto.IsOpen);
        Assert.Null(dto.PrepTimeMinutes);
    }
}
