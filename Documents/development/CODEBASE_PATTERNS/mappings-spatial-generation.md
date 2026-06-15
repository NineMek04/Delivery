# 🤖 Mapster Mapping & Spatial Auto-Generation

รายละเอียดในไฟล์ [MappingConfig.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/Mappings/MappingConfig.cs):
- ผูกใช้แผนผังแปลงโมเดล Entity ↔ DTO ด้วยการตั้งค่าส่วนกลางหลีกเลี่ยงค่า null (`IgnoreNullValues(true)`)

> [!IMPORTANT]
> **เทคนิคป้องกันพิกัด 3 มิติ (Force 2D PostGIS Point Creation):**
> หากนักพัฒนานำเข้าข้อมูลพิกัดละติจูด/ลองจิจูดจากหน้าจอ (DTO) แล้วแปลงกลับเป็นคลาสภูมิศาสตร์ `Point` โดยตรง ตัวโปรแกรม NetTopologySuite อาจสร้างจุดพิกัดที่มีค่าแกนความสูง (Z Dimension) ติดไปด้วย ซึ่งจะนำไปสู่การเกิดข้อผิดพลาดฐานข้อมูลล่ม: `Geometry has Z dimension but column does not`
> 
> **แนวทางปฏิบัติ:** ระบบจึงเขียนฟังก์ชันตัวแปลง `CreatePoint` เพื่อบังคับสร้างเฉพาะพิกัด 2 มิติ (XY) ด้วยเทคนิค `PackedCoordinateSequenceFactory` เสมอก่อนบันทึก:
> ```csharp
> public static Point CreatePoint(double lng, double lat)
> {
>     var sequenceFactory = new PackedCoordinateSequenceFactory(PackedCoordinateSequenceFactory.PackedType.Double);
>     var factory = new GeometryFactory(new PrecisionModel(), srid: 4326, sequenceFactory);
>     var sequence = sequenceFactory.Create(1, Ordinates.XY);
>     sequence.SetX(0, lng);
>     sequence.SetY(0, lat);
>     return factory.CreatePoint(sequence);
> }
> ```
