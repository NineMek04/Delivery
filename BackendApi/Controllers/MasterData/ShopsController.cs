using BackendApi.Core;
using BackendApi.Core.Mappings;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Controllers.MasterData
{
    /// <summary>
    /// API จัดการข้อมูลร้านค้า (CRUD และ Spatial Location)
    /// </summary>
    [Authorize]
    public class ShopsController : CrudControllerBase<Shop, ShopDto>
    {
        // สืบทอด CRUD อัตโนมัติ:
        //   GET    /api/v1/shops         → ดึงรายการร้านค้าทั้งหมด (แบ่งหน้า)
        //   GET    /api/v1/shops/{id}    → ดึงข้อมูลร้านค้าเดี่ยว
        //   PUT    /api/v1/shops/{id}    → แก้ไขข้อมูลร้านค้า
        //   DELETE /api/v1/shops/{id}    → ลบร้านค้า (Soft Delete)

        /// <summary>
        /// ซ่อน base Create ที่รับ ShopDto เพื่อป้องกัน Swagger conflict
        /// และป้องกัน "Geometry has Z dimension" จาก auto-mapping
        /// </summary>
        [NonAction]
        public override Task<ActionResult<ShopDto>> Create(
            [FromBody] ShopDto dto,
            CancellationToken cancellationToken = default)
            => base.Create(dto, cancellationToken);

        /// <summary>
        /// สร้างร้านค้าใหม่ — รับ CreateShopDto เพื่อให้ Swagger แสดง schema ที่ถูกต้อง
        /// และใช้ mapping CreateShopDto → Shop ผ่าน CreatePoint() ที่ force 2D
        /// ป้องกัน "Geometry has Z dimension but column does not" ใน PostGIS
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ShopDto>> CreateShop(
            [FromBody] CreateShopDto dto,
            CancellationToken cancellationToken = default)
        {
            var entity = dto.Adapt<Shop>();
            DB.InsertObject(entity);
            await DB.CommitChangesAsync(cancellationToken);

            return CreatedAtAction(nameof(GetById),
                new { id = entity.Id },
                entity.Adapt<ShopDto>());
        }
    }
}
