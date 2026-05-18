using BackendApi.Core;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using Microsoft.AspNetCore.Authorization;

namespace BackendApi.Controllers.MasterData
{
    /// <summary>
    /// API จัดการข้อมูลร้านค้า (CRUD และ Spatial Location)
    /// </summary>
    [Authorize] // ต้องล็อกอินก่อนจึงจะเข้าถึงได้
    public class ShopsController : CrudControllerBase<Shop, ShopDto>
    {
        // สืบทอด CRUD อัตโนมัติ:
        //   GET    /api/v1/shops         → ดึงรายการร้านค้าทั้งหมด (แบ่งหน้า)
        //   GET    /api/v1/shops/{id}    → ดึงข้อมูลร้านค้าเดี่ยว
        //   POST   /api/v1/shops         → สร้างร้านค้าใหม่ (รองรับสิทธิ์ผู้ดูแลระบบ)
        //   PUT    /api/v1/shops/{id}    → แก้ไขข้อมูลร้านค้า
        //   DELETE /api/v1/shops/{id}    → ลบร้านค้า (Soft Delete)
    }
}
