# บทเรียนการพัฒนาซอฟต์แวร์ระดับองค์กร (Enterprise Software Development Lessons)

เอกสารนี้รวบรวมเทคนิค โครงสร้างสถาปัตยกรรม และมาตรฐานความปลอดภัยที่ใช้ในโปรเจกต์ `Delivery` โดยมุ่งเน้นการออกแบบระบบที่รองรับการขยายตัว (Scalability) ทนทานต่อข้อผิดพลาด (Resiliency) และปลอดภัยตามมาตรฐานสากล (OWASP)

---

## บทที่ 1: สถาปัตยกรรมแนวตั้ง (Vertical Slice Architecture)

**เป้าหมาย:** ลดความซับซ้อนของการแก้ไขโค้ดข้าม Layer จัดกลุ่มโค้ดตาม "ฟีเจอร์" (Feature-driven) แทนการจัดตาม "ชนิดของไฟล์" (Layer-driven)

### ❌ ตัวอย่างที่ไม่ดี (Traditional N-Tier Architecture)
การจัดโครงสร้างแบบเดิมทำให้เวลาเพิ่มฟีเจอร์หนึ่งฟีเจอร์ ต้องกระโดดข้ามโฟลเดอร์ไปมา
```text
/Controllers
  - TelemetryController.cs
  - OrderController.cs
/Services
  - TelemetryService.cs
  - OrderService.cs
/Repositories
  - TelemetryRepository.cs
```

### ✅ ตัวอย่างที่ดี (Vertical Slice - รูปแบบที่ใช้ในโปรเจกต์นี้)
ในโปรเจกต์ของเรา จัดกลุ่มโค้ดทั้งหมดที่เกี่ยวกับ "การติดตามตำแหน่ง" ไว้ด้วยกัน ทำให้ดูแลรักษาง่าย (High Cohesion)
```text
/Features/FleetTracking/Telemetry
  - TelemetryController.cs    (รับ Request)
  - TelemetryService.cs       (Business Logic)
  - GpsRabbitMqPublisher.cs   (ส่งข้อความ)
```

---

## บทที่ 2: การสื่อสารข้ามระบบด้วย Message Broker (RabbitMQ)

**เป้าหมาย:** แยกการทำงานที่ใช้เวลานานออกจาก HTTP Request Cycle ทันที (Asynchronous Processing & Decoupling)

### ❌ ตัวอย่างที่ไม่ดี (Synchronous / Blocking)
API รอให้ทำงานเสร็จทั้งหมดก่อนตอบกลับ ทำให้ผู้ใช้รู้สึกว่าระบบช้า และถ้าระบบใดระบบหนึ่งล่ม API ก็จะล่มตาม
```csharp
[HttpPost]
public async Task<IActionResult> UpdateLocation(LocationDto dto)
{
    // 1. บันทึกลง Database (ใช้เวลา)
    await _db.SaveAsync(dto);
    // 2. เรียกไปที่ AI Engine ผ่าน HTTP (ใช้เวลาและอาจล่มได้)
    await _httpClient.PostAsync("http://ai-engine/calculate", dto);
    
    return Ok(); // ผู้ใช้ต้องรอ 1+2 เสร็จ
}
```

### ✅ ตัวอย่างที่ดี (Asynchronous Messaging - มาตรฐานระดับองค์กร)
API แค่รับข้อมูลและโยนเข้าคิว (Queue) แล้วตอบกลับทันที (Fast Response) ระบบเบื้องหลังจะค่อยๆ ดึงคิวไปทำงาน
```csharp
[HttpPost]
public async Task<IActionResult> UpdateLocation(LocationDto dto)
{
    // โยนงานเข้า RabbitMQ แล้วจบเลย
    await _publisher.PublishTelemetryAsync(dto);
    
    return Accepted(new { Message = "Location received and queued for processing." });
}
```
*Note: ในโปรเจกต์นี้เราใช้ `GpsRabbitMqPublisher` ในการ Publish ข้อความ และใช้มาตรฐานการตั้งชื่อ Event เช่น `RiderLocationUpdatedTelemetryEvent`*

---

## บทที่ 3: การประมวลผลเบื้องหลัง (Background Workers & Scope Management)

**เป้าหมาย:** การรันกระบวนการดึงข้อความจากคิว (Consumer) โดยไม่กระทบต่อการให้บริการ Web API

### 🛠 เทคนิคการใช้ BackgroundService
ใน .NET เราใช้ `BackgroundService` หรือ `IHostedService` เพื่อรันงานเบื้องหลัง 
**Gotcha (ข้อควรระวังระดับองค์กร):** Background Service จะมีสถานะเป็น **Singleton** แต่ออบเจกต์อย่างเช่น `DbContext` (Entity Framework) มักจะมีสถานะเป็น **Scoped** (ต่อ 1 Request) การฉีด (Inject) DbContext เข้า BackgroundService ตรงๆ จะทำให้เกิดข้อผิดพลาด

### ✅ ตัวอย่างที่ถูกต้อง
ต้องใช้ `IServiceScopeFactory` เพื่อสร้าง Scope ชั่วคราว (เสมือนการจำลอง 1 Request) ขึ้นมาทำงานแต่ละรอบ
```csharp
public class GpsRabbitMqConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GpsRabbitMqConsumerWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ... รับข้อความจาก RabbitMQ ...
        
        // สร้าง Scope ทุกครั้งที่มีข้อความใหม่เข้ามา
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Locations.AddAsync(newLocation);
            await dbContext.SaveChangesAsync();
        }
    }
}
```

---

## บทที่ 4: การป้องกันระบบด้วย Rate Limiting และ Redis

**เป้าหมาย:** ป้องกันการโจมตีแบบ DoS/DDoS และจำกัดการใช้ทรัพยากร (Resource Exhaustion) ไม่ให้เซิร์ฟเวอร์ฐานข้อมูลทำงานหนักเกินไป

### ❌ ตัวอย่างที่ไม่ดี
รับทุก Request ที่วิ่งเข้ามา และเช็คข้อมูลจาก Database โดยตรง ทำให้ Database เป็นคอขวด
```csharp
// ไม่มีการป้องกัน หากยิงมา 10,000 Request/วินาที DB จะร่วงทันที
var lastUpdate = await _db.Locations.OrderByDescending(l => l.Time).FirstOrDefaultAsync();
```

### ✅ ตัวอย่างที่ดี (Redis Rate Limiting)
ใช้ Redis ซึ่งทำงานบน Memory ที่มีความเร็วสูงมาก ในการนับจำนวน Request หากเกินโควต้า ให้ตีกลับเป็น `HTTP 429 Too Many Requests`
```csharp
// TelemetryController.cs
if (!await _rateLimiter.IsAllowedAsync(riderId))
{
    return StatusCode(StatusCodes.Status429TooManyRequests, 
        new ApiResponse<object> { Success = false, Message = "Rate limit exceeded. Please slow down." });
}
```
*ในระบบเรามีการสร้าง `GpsRedisRateLimiter` มาดูแลเรื่องนี้โดยเฉพาะ*

---

## บทที่ 5: ความปลอดภัยระดับแอปพลิเคชัน (OWASP Standard)

**เป้าหมาย:** ป้องกันช่องโหว่ความปลอดภัย โดยเฉพาะเมื่อต้องเชื่อมต่อกับ AI Engine

### 1. การตรวจสอบข้อมูลขาเข้าอย่างเข้มงวด (Strict Input Validation)
**ปัญหา:** LLM07 (Insecure Plugin Design / Input) รับข้อมูล JSON ที่ไม่ได้กำหนดโครงสร้างแน่ชัด อาจทำให้โค้ดพังหรือถูกฉีดโค้ดประสงค์ร้าย
**ทางแก้ (ใน Python AI Engine):**
บังคับใช้ Pydantic Model แบบ Strict Type แทนที่จะรับข้อมูลเป็น Array ธรรมดา ให้ระบุเป็น `Tuple` ที่มีขนาดและชนิดข้อมูลชัดเจน
```python
# ❌ แบบเก่า
depot_location: list # ใส่ข้อมูลมากี่ตัวก็ได้
    
# ✅ แบบใหม่ (Enterprise)
depot_location: Tuple[float, float] = Field(
    ..., description="GPS Coordinates [latitude, longitude]"
)
```

### 2. ป้องกันการใช้ทรัพยากรหมด (Resource Exhaustion Prevention)
**ปัญหา:** LLM04 (Model Denial of Service) แฮกเกอร์จงใจส่งโจทย์คำนวณเส้นทาง (VRP) ที่ซับซ้อนมากๆ จน CPU ของ AI Engine ค้าง
**ทางแก้:** กำหนดเวลา Timeout สูงสุดให้กับ AI Solver ไม่ว่าโจทย์จะยากแค่ไหน ต้องหยุดคำนวณภายในเวลาที่กำหนด
```python
# vrp_solver.py
search_parameters = pywrapcp.DefaultRoutingSearchParameters()
search_parameters.time_limit.seconds = 5 # 🛑 บังคับหยุดคำนวณภายใน 5 วินาที
```

### 3. การแยกเครือข่าย (Network Isolation)
**ปัญหา:** เปิดพอร์ตของเซอร์วิสภายในออกสู่ Public ทำให้คนนอกสามารถยิงตรงเข้าหา AI Engine หรือ Database ได้
**ทางแก้:** ใน `docker-compose.yml` ให้ลบ `ports` ออกจากเซอร์วิสที่ไม่จำเป็นต้องเข้าถึงจากภายนอก (Backend API เท่านั้นที่เผยแพร่ออกไป)
```yaml
# docker-compose.yml
  ai-service:
    build:
      context: ./ai-engine
    # ports:           <-- 🛑 ลบออก ปิดการเข้าถึงจากภายนอก
    #   - "8000:8000"  <-- Backend API เท่านั้นที่จะคุยกับ AI-Service ได้ผ่าน internal network
```

---

## บทที่ 6: Idempotency และการทดสอบระบบระดับองค์กร

### Idempotency (การทำงานซ้ำต้องได้ผลลัพธ์เดิม)
เมื่อใช้ RabbitMQ อาจเกิดเหตุการณ์ส่งข้อมูลซ้ำ (Duplicate Messages) Consumer จึงต้องเช็คก่อนเสมอ
**Best Practice:**
มีตาราง `ProcessedEvents` ใน Database เมื่อรับ Message มา จะเช็ค `EventId` ถ้ารันไปแล้วให้ข้ามทันที
```csharp
if (await _dbContext.ProcessedEvents.AnyAsync(e => e.EventId == message.Id)) {
    return; // ข้ามการทำงาน
}
```

### การทดสอบ (Testing Conventions & Mocks)
การทำ Unit Test ที่เชื่อมต่อกับ Database มักใช้ **EF Core In-Memory Database** ซึ่งมีข้อจำกัดคือ "ไม่รองรับการทำงานแบบ Transaction (BeginTransaction)"
**เทคนิคระดับองค์กร:** ต้องทำการ Mock ตัว Transaction ขึ้นมาหลอกระบบ เพื่อป้องกันโค้ดเกิด `NotSupportedException` ในตอนรัน Test
```csharp
// ตัวอย่างการ Mock Transaction ใน GpsRabbitMqConsumerWorkerTests.cs
var mockTransaction = new Mock<IDbContextTransaction>();
mockDatabaseFacade.Setup(d => d.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(mockTransaction.Object);
```
และโปรเจกต์นี้มีกฎเหล็ก (Rule) ว่า **ไฟล์ Test ทั้งหมดจะต้องอยู่ในโฟลเดอร์ `scripts.test/` เท่านั้น** เพื่อไม่ให้ไฟล์ทดสอบปะปนกับโค้ดของ Production

---

## บทที่ 7: การสื่อสารแบบเรียลไทม์ (SignalR & WebSocket)

**เป้าหมาย:** สตรีมข้อมูลความถี่สูง (เช่น ตำแหน่ง GPS ของ Rider) ไปยังแอปพลิเคชันหน้าบ้านแบบเรียลไทม์

### ❌ ตัวอย่างที่ไม่ดี (Fat Hub)
การใส่ Business Logic เช่น การตรวจสอบสถานะ หรือการแก้ไขข้อมูลใน Database ลงใน SignalR Hub โดยตรง ทำให้ Hub บวมและทดสอบยาก
```csharp
public class TrackingHub : Hub
{
    public async Task UpdateLocation(LocationDto location)
    {
        // ❌ ไม่ควรใส่ Business Logic ใน Hub
        var rider = await _db.Riders.FindAsync(location.RiderId);
        rider.Update(location);
        await _db.SaveChangesAsync();
        await Clients.All.SendAsync("LocationUpdated", location);
    }
}
```

### ✅ ตัวอย่างที่ดี (Pure Transport Layer)
ตามกฎของโปรเจกต์ (Anti-Overengineering) `TrackingHub` ทำหน้าที่เป็นแค่ Transport Layer (เส้นทางผ่านข้อมูล) เท่านั้น ห้ามมีลอจิกเปลี่ยน State เด็ดขาด ต้องโยนเข้า Service หรือ Queue ทันที
```csharp
public class TrackingHub : Hub
{
    private readonly ITelemetryService _telemetryService;

    public async Task UpdateLocation(LocationDto location)
    {
        // ✅ รับข้อมูลแล้วส่งต่อให้ Service ทันที
        await _telemetryService.ProcessLocationAsync(location);
    }
}
```

---

## บทที่ 8: การติดตาม Log ข้ามระบบ (Distributed Tracing & Correlation)

**เป้าหมาย:** เมื่อระบบเป็น Microservices หรือมี Background Worker การค้นหา Log ของ Transaction เดียวกันจากหน้าจอ Command Prompt ทำได้ยาก จึงต้องมี ID กลาง

### 🛠 เทคนิคการทำ Trace Correlation
ตามกฎของ `AGENTS.md` บังคับให้ Log ทั้งหมดต้องมี Context ดังนี้เสมอ:
1. `CorrelationId` - รหัสอ้างอิงการทำงานตั้งแต่ระบบรับ Request จนกระบวนการทำงานเสร็จสิ้น
2. `OrderId` - รหัสออเดอร์ที่เกี่ยวข้อง
3. `RiderId` - รหัสพนักงานขับรถ

**ตัวอย่างการใช้ ILogger ร่วมกับ Log Scope:**
```csharp
// การสร้าง Scope ให้ Log เพื่อให้ทุก Log ที่เกิดในบล็อคนี้มีข้อมูลแนบไปด้วย
using (_logger.BeginScope(new Dictionary<string, object> 
{
    ["CorrelationId"] = Guid.NewGuid(),
    ["RiderId"] = riderId
}))
{
    _logger.LogInformation("Processing GPS Data..."); 
    // Log ผลลัพธ์ใน Console: [CorrelationId: 1234, RiderId: 99] Processing GPS Data...
}
```

---

## บทที่ 9: ประสิทธิภาพของฐานข้อมูล (Database Performance)

**เป้าหมาย:** ป้องกันปัญหา Memory Leak และลดภาระการทำงานของ Entity Framework (EF Core)

### ❌ ตัวอย่างที่ไม่ดี
ดึงข้อมูลมาแสดงผลอย่างเดียว แต่ไม่ยอมปิด Tracking ทำให้ EF Core เก็บข้อมูลไว้ในหน่วยความจำเปลืองๆ
```csharp
// ❌ เปลือง Memory มาก ถ้าดึงข้อมูล 10,000 record ระบบอาจจะช้าลง
var locations = await _db.Locations.Where(l => l.RiderId == 1).ToListAsync();
```

### ✅ ตัวอย่างที่ดี (AsNoTracking)
เมื่อต้องการดึงข้อมูลมาดูเฉยๆ (Read-only) โดยไม่มีการแก้ไข (Update/Delete) ต้องใช้ `.AsNoTracking()` เสมอ
```csharp
// ✅ เร็วขึ้นและไม่กิน Memory
var locations = await _db.Locations
                         .AsNoTracking()
                         .Where(l => l.RiderId == 1)
                         .ToListAsync();
```

---

## บทที่ 10: รูปแบบการเขียนโค้ด (Coding Standards & Clean Code)

**เป้าหมาย:** ให้โค้ดอ่านง่าย ดูแลรักษาง่าย และทำงานเป็นมาตรฐานเดียวกันทั้งทีม

1. **Anti-Overengineering:** อย่าใช้เครื่องมือที่เกินความจำเป็น กฎของเราบังคับใช้ PostgreSQL, Redis, RabbitMQ, SignalR ห้ามเพิ่ม Kafka หรือ Kubernetes หากระบบยังเล็ก เพื่อให้ดูแลง่าย (Predictability > Complexity)
2. **การตั้งชื่อ Event (Naming Conventions):** ต้องระบุจุดประสงค์ให้ชัดเจน 
   - *Domain Events:* สื่อสารภายใน (เช่น `OrderCreatedEvent`)
   - *Integration Events:* สื่อสารข้ามระบบ ต้องลงท้ายด้วย IntegrationEvent (เช่น `OrderCreatedIntegrationEvent`)
   - *Telemetry Events:* สตรีมข้อมูลเรียลไทม์ (เช่น `RiderLocationUpdatedTelemetryEvent`)
3. **โฟลเดอร์สำหรับทดสอบ (Single Test Hub Rule):** ไฟล์ทดสอบของ C#, Python และ E2E ต้องอยู่ภายใต้โฟลเดอร์ `scripts.test/` เท่านั้น ห้ามวางปนในโฟลเดอร์แอปหลัก (เช่น ห้ามมีโฟลเดอร์ `ai-engine/tests`)

---

## บทที่ 11: กลยุทธ์การทำ Caching (In-Memory vs Distributed)

**เป้าหมาย:** ลดภาระของฐานข้อมูลและเพิ่มความเร็วในการตอบสนอง (Response Time) โดยเลือกประเภท Cache ให้เหมาะสม

### 1. In-Memory Cache (`IMemoryCache`)
เหมาะสำหรับข้อมูลที่ **เปลี่ยนไม่บ่อย และขนาดไม่ใหญ่มาก** เช่น Master Data, Dropdown List ของหน้าเว็บ
- **ข้อดี:** เร็วที่สุด เพราะอยู่ใน RAM ของเซิร์ฟเวอร์ตัวเอง
- **ข้อเสีย:** หากระบบมีเซิร์ฟเวอร์หลายตัว (Scale Out) ข้อมูล Cache แต่ละตัวจะไม่เชื่อมกัน (Inconsistent)

### 2. Distributed Cache (Redis)
เหมาะสำหรับข้อมูลที่ **เปลี่ยนแปลงบ่อย หรือต้องการแชร์ร่วมกันหลายเซิร์ฟเวอร์** เช่น Rate Limiting, ข้อมูลตะกร้าสินค้า (Cart), พิกัด GPS ล่าสุด
- **ข้อดี:** ทุกเซิร์ฟเวอร์เห็นข้อมูลตรงกัน, ข้อมูลไม่หายเมื่อเซิร์ฟเวอร์แอปพลิเคชันรีสตาร์ท
- **ข้อเสีย:** ช้ากว่า In-Memory เล็กน้อย เพราะต้องสื่อสารผ่าน Network

### 🛠 ตัวอย่างที่ดี (Cache Aside Pattern)
เช็คใน Cache ก่อน ถ้าไม่มีค่อยไปดึงจาก DB แล้วนำไปใส่ Cache
```csharp
public async Task<string> GetRiderStatusAsync(int riderId)
{
    string cacheKey = $"rider_status_{riderId}";
    
    // 1. เช็คข้อมูลจาก Redis Cache
    var status = await _cache.GetStringAsync(cacheKey);
    if (!string.IsNullOrEmpty(status))
    {
        return status; // ✅ คืนค่าจาก Cache ทันที (ไม่ต้องแตะ DB)
    }

    // 2. ถ้าไม่มีใน Cache ให้ดึงจาก DB
    status = await _db.Riders.Where(r => r.Id == riderId).Select(r => r.Status).FirstOrDefaultAsync();

    // 3. เซฟลง Cache (ตั้งเวลาหมดอายุด้วย เช่น 5 นาที)
    var options = new DistributedCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(5));
    await _cache.SetStringAsync(cacheKey, status, options);

    return status;
}
```

---

## บทที่ 12: การ Deployment สถาปัตยกรรม Microservices

**เป้าหมาย:** สร้างระบบที่สามารถทำงานร่วมกันได้หลายเทคโนโลยี และง่ายต่อการนำขึ้น Server (CI/CD)

### 1. การแยกเซอร์วิสด้วย Container (Docker)
ในโปรเจกต์ `Delivery` เราใช้ `docker-compose.yml` เพื่อแยกส่วนประกอบออกจากกัน ทำให้แต่ละส่วนสามารถ Scale หรืออัปเกรดแยกกันได้:
- **`backend-api` (C# .NET):** รับหน้าที่ดูแล HTTP Request และ Business Logic ฝั่งหน้าบ้าน
- **`ai-service` (Python):** รับหน้าที่คำนวณเส้นทาง (VRP) ซึ่งแยกออกมาเพราะ AI มักจะกิน CPU และใช้ Python สะดวกกว่า
- **`postgres`, `redis`, `rabbitmq`:** เซอร์วิสพื้นฐาน (Infrastructure)

### 2. การจัดการ Environment Variables (Secrets)
**ห้ามฮาร์ดโค้ด (Hardcode)** รหัสผ่าน หรือ Connection String ลงในโค้ดเด็ดขาด (`appsettings.json` ไม่ควรเก็บรหัสจริงใน Production)
- ใน Docker เราจะส่งค่าผ่าน Environment Variables แทน เพื่อความปลอดภัย

### 3. Service Discovery & Networking
Container ต่างๆ ไม่จำเป็นต้องรู้ IP จริงของกันและกัน แต่จะเรียกหากันผ่าน **ชื่อ Service** เช่น `backend-api` สามารถเชื่อมต่อฐานข้อมูลโดยระบุ `Host=postgres` หรือส่งข้อมูลหา RabbitMQ โดยระบุ `Host=rabbitmq` เพราะ Docker จัดการวง Network ภายใน (Internal Network) ให้เรียบร้อยแล้ว และป้องกันไม่ให้คนนอกเข้าถึงโดยการไม่เปิด Port (ตามมาตรฐาน OWASP)

---

## บทสรุป

การเขียนโค้ดระดับองค์กร (Enterprise Grade) ไม่ใช่แค่การเขียนให้ "ทำงานได้" แต่ต้องคำนึงถึง:
1. **เมื่อคนใช้เยอะขึ้น (Scalability):** มีคิว (RabbitMQ), มีแคช/จำกัด Rate (Redis)
2. **เมื่อระบบใดระบบหนึ่งล่ม (Resiliency):** แยกชิ้นส่วนแบบ Asynchronous
3. **เมื่อถูกโจมตี (Security):** Validate ข้อมูล, มี Time limit, ไม่เปิด Port มั่วซั่ว
4. **เมื่อต้องการแก้โค้ดในระยะยาว (Maintainability):** ใช้ Vertical Slice และแยก Test ออกมาให้ชัดเจน
5. **เมื่อเกิดปัญหาระบบ (Observability):** มี Correlation ID เชื่อมโยง Log ทุกส่วน เพื่อให้ Debug ง่าย
