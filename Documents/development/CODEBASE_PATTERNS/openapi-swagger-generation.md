# 🗺️ การสร้างเอกสารอ้างอิง API อัตโนมัติ (Automatic OpenAPI/Swagger Generation)

เพื่อลดข้อผิดพลาดในการพัฒนาและการเขียนโค้ดเบสที่ฝั่งนักพัฒนาหน้าบ้าน (Angular) หรือผู้ดูแลระบบ API เอกสารสเปก `swagger.json` จะถูกสร้างขึ้นเองแบบอัตโนมัติเมื่อทำการคอมไพล์โค้ด:

- **การตั้งค่าระดับโปรเจกต์ ([BackendApi.csproj](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/BackendApi.csproj#L43-L51)):**
  ระบบตั้งค่าแท็กงานสร้างเอกสารของ MSBuild:
  ```xml
  <Target Name="GenerateSwagger" AfterTargets="Build" Condition="'$(Configuration)' == 'Release' Or '$(SWAGGER_GEN_AUTO)' == 'true'">
    <Exec Command="dotnet $(TargetPath) --generate-swagger" />
  </Target>
  ```
  *คำอธิบาย:* เมื่อนักพัฒนาทำการสั่ง Build ในโหมด Release หรือส่งค่า `SWAGGER_GEN_AUTO=true` ตัวระบบสร้างไฟล์จะสั่งรันไบนารี API ทันทีหลังคอมไพล์เสร็จพร้อมแนบแฟล็ก `--generate-swagger`
- **การตรวจจับและส่งออกข้อมูล ([Program.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Program.cs#L69-L81)):**
  ที่จุดเริ่มต้นของไฟล์ Program ขาขึ้น จะเช็คคำสั่งอินพุตขาเข้า:
  ```csharp
  if (args.Contains("--generate-swagger") || builder.Configuration["SWAGGER_GEN"] == "true")
  {
      // ดึงสิทธิ์ ISwaggerProvider เพื่อดึงสเปกของ API ทั้งหมดในแอป
      var swaggerProvider = scope.ServiceProvider.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>();
      var swagger = swaggerProvider.GetSwagger("v1", null, "/");
      var swaggerJson = swagger.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0);
      await File.WriteAllTextAsync("swagger.json", swaggerJson); // ส่งออกเป็นไฟล์ดิสก์
      return; // สั่งปิดแอปพลิเคชันทันที เพื่อไม่ให้ไปบูตรัน API ต่อ
  }
  ```
