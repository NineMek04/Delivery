// =================================================================
// Controllers/MasterData/
// =================================================================
// โฟลเดอร์นี้สำหรับ Controller ที่สืบทอดมาจาก CrudControllerBase
// ใช้กับตารางข้อมูลพื้นฐาน (Master Data) เช่น:
//   - VehicleTypeController  (ประเภทรถ)
//   - WarehouseController    (ตำแหน่งคลังสินค้า)
//   - StatusLookupController (สถานะต่างๆ)
//
// ตัวอย่างการสร้าง:
//
// [Route("api/v1/vehicle-types")]
// public class VehicleTypeController : CrudControllerBase<VehicleType, VehicleTypeDto>
// {
// }
