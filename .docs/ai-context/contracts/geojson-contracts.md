---
scope: Geospatial Contracts (Polyline, SRID, RouteGeometry, GPS)
source_of_truth:
  - AI-CHANGELOG.md (2026-05-19 OSRM+Polyline, 2026-05-19 Sim Curvy Navigation)
  - BackendApi/Core/PolylineEncoder.cs (codebase)
  - BackendApi/Services/Ai/OsrmRoutingService.cs (codebase)
  - admin-dashboard/src/app/features/sim-map/ (decodePolyline function)
related_contexts:
  - .docs/ai-context/contracts/signalr-contracts.md
  - .docs/ai-context/spec-backend.md
  - .docs/ai-context/spec-infra-devops.md
forbidden_patterns:
  - ใช้ SRID อื่นที่ไม่ใช่ 4326 (WGS84)
  - ส่ง coordinates ที่มี Z-axis เข้า PostGIS geometry(Point, 4326)
  - ใช้ geometry type อื่นที่ไม่ใช่ Point สำหรับ location fields
  - คำนวณ distance ใน C# ด้วย Haversine (ต้องใช้ PostGIS .Distance())
known_pitfalls:
  - Google Polyline ใช้ 1e5 precision — ห้ามใช้ 1e6 (เส้นทางเพี้ยน)
  - lat/lng order สลับกันระหว่าง Leaflet ([lat, lng]) และ GeoJSON/OSRM ([lng, lat])
  - PostGIS .Distance() คืนค่าเป็น degrees ไม่ใช่ meters — ต้องแปลงหรือใช้ ST_DWithin
---

# geojson-contracts.md — Geospatial Contracts

> **SRID Standard:** 4326 / WGS84 สำหรับทุก GPS coordinate  
> **For OSRM setup** → `spec-infra-devops.md`  
> **For backend spatial config** → `spec-backend.md`

---

## 1. Coordinate Standard (SRID 4326 / WGS84)

**ทุก GPS coordinate ในระบบต้องใช้:**
- SRID: **4326** (WGS84)
- Column Type: `geometry(Point, 4326)` (PostGIS)
- C# Type: `Point` (NetTopologySuite)
- JSON Format: `{ "lat": float, "lng": float }` (camelCase)

```
ห้ามใช้:
❌ SRID 3857 (Web Mercator)
❌ { "x": float, "y": float }
❌ [lat, lng] array (ยกเว้น Leaflet Map API ที่กำหนด)
```

---

## 2. LocationDto Schema

```json
{
  "lat": 17.4138,
  "lng": 102.7872
}
```

**C# DTO:**
```csharp
public class LocationDto
{
    [Range(-90, 90)]
    public double Lat { get; set; }

    [Range(-180, 180)]
    public double Lng { get; set; }
}
```

**Mapster mapping (MappingConfig.cs):**
```csharp
// LocationDto → Point (PostGIS)
TypeAdapterConfig<LocationDto, Point>.NewConfig()
    .MapWith(src => new Point(src.Lng, src.Lat) { SRID = 4326 });

// Point → LocationDto
TypeAdapterConfig<Point, LocationDto>.NewConfig()
    .MapWith(src => new LocationDto { Lat = src.Y, Lng = src.X });

// หมายเหตุ: PostGIS Point(X=lng, Y=lat) ← ลำดับสลับ!
```

---

## 3. Google Polyline Encoding Standard

**Algorithm:** Google Polyline Format (Precision 1e5 = 5 decimal places)

| Field | Value |
|---|---|
| **Stored In** | `Orders.EncodedPolyline` (PostgreSQL varchar/text) |
| **Encoding** | `PolylineEncoder.Encode(List<Coordinate>)` (BackendApi) |
| **Decoding** | `decodePolyline(string)` (Angular TypeScript) |
| **Precision** | **1e5** (5 decimal places ≈ 1.1 meter accuracy) |
| **Size Reduction** | ~99% vs JSON coordinate array |

**Example:**
```
Input:  [[17.4138, 102.7872], [17.4150, 102.7880], [17.4200, 102.7900]]
Output: "_p~iF~ps|U..." (encoded string)
```

---

## 4. RouteGeometry Schema

RouteGeometry คือ result จาก OSRM routing ที่เก็บใน Order:

```json
{
  "encodedPolyline": "_p~iF~ps|U...",
  "distanceKm": 5.2,
  "durationSeconds": 720,
  "waypoints": [
    { "lat": 17.4138, "lng": 102.7872 },
    { "lat": 17.4200, "lng": 102.7900 }
  ]
}
```

**OSRM Response Format (source):**
```json
{
  "routes": [{
    "distance": 5200.0,        // meters
    "duration": 720.0,         // seconds
    "geometry": {
      "coordinates": [[102.7872, 17.4138], [102.7900, 17.4200]],  // [lng, lat]!
      "type": "LineString"
    }
  }]
}
```

> ⚠️ OSRM ส่ง `[longitude, latitude]` — ต้องแปลงก่อนใช้ใน Leaflet!

---

## 5. Leaflet Coordinate Order

```typescript
// Leaflet ใช้ [latitude, longitude] — สลับจาก GeoJSON!
const latlng: [number, number] = [17.4138, 102.7872];  // ✅ Leaflet
const geojson = { type: "Point", coordinates: [102.7872, 17.4138] };  // ✅ GeoJSON
```

**Conversion flow:**
```
OSRM [lng, lat] → Convert → Leaflet [lat, lng]
PostGIS Point(X=lng, Y=lat) → Mapster → LocationDto { lat, lng }
LocationDto { lat, lng } → Leaflet [lat, lng]
```

---

## 6. GPS Transmission Format (SignalR)

```typescript
// Client → Server (Rider GPS update)
connection.invoke('UpdateLocation', latitude, longitude, accuracy);
// latitude: number (WGS84)
// longitude: number (WGS84)
// accuracy: number (meters)

// Server → Client (GPS broadcast)
{
  "riderId": "uuid",
  "lat": 17.4138,    // camelCase
  "lng": 102.7872,
  "accuracy": 12.5,
  "timestamp": "ISO8601"
}
```

**Angular Fallback Mapper (required due to casing inconsistency):**
```typescript
function extractCoords(payload: any): { lat: number; lng: number } {
  return {
    lat: payload.latitude ?? payload.lat ?? payload.Lat ?? 0,
    lng: payload.longitude ?? payload.lng ?? payload.Lng ?? 0
  };
}
```

---

## 7. PostGIS Spatial Query Patterns

```csharp
// หา Riders ใกล้ Shop ใน 5km
var shopPoint = new Point(shopLng, shopLat) { SRID = 4326 };
var radiusDegrees = 5.0 / 111.0;  // approximate: 1° ≈ 111km

var riders = await DB.GetQuery<Rider>()
    .Where(r => r.State == RiderState.IDLE
             && r.CurrentLocation.Distance(shopPoint) <= radiusDegrees)
    .OrderBy(r => r.CurrentLocation.Distance(shopPoint))
    .Take(20)
    .ToListAsync();

// ดูระยะทาง (ST_DWithin แม่นกว่า .Distance() < radius)
// ใช้ HasPostgisExtension ใน ApplicationDbContext
```

---

## 8. GiST Index Requirements

ทุก `geometry(Point, 4326)` column ต้องมี GiST Index:

```csharp
// Entity Configuration (ต้องมีทุก geometry column)
entity.HasIndex(e => e.CurrentLocation)
    .HasMethod("gist")
    .HasDatabaseName("IX_Riders_CurrentLocation_Gist");

entity.HasIndex(e => e.Location)
    .HasMethod("gist")
    .HasDatabaseName("IX_Shops_Location_Gist");

// ห้ามใส่ .HasMethod("gist") บน non-geometry columns!
// เช่น ShopId (string) ห้ามมี gist index → migration error
```
