# Geospatial Contracts

## 1. Coordinate Standard

- WGS84 / SRID 4326
- PostGIS `geometry(Point,4326)`
- NetTopologySuite `Point(X=lng, Y=lat) { SRID = 4326 }`
- REST/SignalR object `{ "lat": number, "lng": number }`
- Leaflet array `[lat,lng]`
- GeoJSON/OSRM array `[lng,lat]`

ห้ามส่ง Z-axis และห้ามใช้ SRID 3857 เป็น persisted location.

## 2. Validation

`lat` อยู่ใน `[-90,90]`, `lng` อยู่ใน `[-180,180]`.
ร้านค้าที่เปิดรับ order ต้องมีพิกัดที่ valid.
Mobile GPS accuracy > 50m ต้องถูกกรองก่อน ingestion.

## 3. Polyline

- Google encoded polyline precision 1e5
- encoder: `BackendApi/Core/Helpers/PolylineEncoder.cs`
- route client: `BackendApi/Features/AiRouting/OsrmRoutingService.cs`
- persisted order fields: `EncodedPolyline`, `RouteDistanceMeters`,
  `RouteDurationSeconds`
- client decode แล้วส่ง Leaflet เป็น `[lat,lng]`

## 4. Spatial Query

- persisted proximity/filter ใช้ PostGIS spatial operator/function และ GiST index
- ห้ามใช้ degree approximation เป็น production radius query เมื่อ ST_DWithin/
  geography conversion ใช้ได้
- Haversine อนุญาตเฉพาะ in-memory heuristic, AI matrix หรือ degraded fallback
- geometry columns ของ Riders, Orders, Shops, CustomerAddresses ต้องคง GiST index
- non-geometry foreign keys ใช้ B-tree ห้ามกำหนด GiST

## 5. OSRM

- ใช้ local OSRM MLD
- request coordinate order เป็น `lng,lat`
- response geometry coordinate order เป็น `lng,lat`
- ห้าม public OSRM production fallback
- trip waypoint sequence คือ visit-order value ต่อ input waypoint;
  compare ด้วย `sequence[inputIndex]`

## 6. GPS Broadcast

Canonical payload ใช้ camelCase `riderId`, `lat`, `lng`, `accuracy`, `timestamp`,
`state`. Client fallback casing มีไว้สำหรับ backward compatibility เท่านั้นและ
ห้ามใช้เป็นเหตุผลให้ producer ส่ง schema ไม่สม่ำเสมอ.
