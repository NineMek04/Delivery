using BackendApi.Core;
using BackendApi.Models;
using BackendApi.Models.DTOs;

namespace BackendApi.Controllers.MasterData;

/// <summary>
/// จัดการข้อมูล Rider (ไรเดอร์/พนักงานขับรถ)
/// </summary>
public class RidersController : CrudControllerBase<Rider, RiderDto>
{
    // CRUD ทั้งหมดสืบทอดมาจาก CrudControllerBase:
    //   GET    /api/v1/riders         → GetAll (แบ่งหน้า)
    //   GET    /api/v1/riders/{id}    → GetById
    //   POST   /api/v1/riders         → Create
    //   PUT    /api/v1/riders/{id}    → Update
    //   DELETE /api/v1/riders/{id}    → Delete
}
