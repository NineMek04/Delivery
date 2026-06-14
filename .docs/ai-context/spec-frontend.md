# Angular 19 Admin Dashboard

**Version:** 1.0.0 | **Updated:** 2026-06-14

## 1. Stack And Auth

- Angular 19 standalone components
- RxJS Observable APIs
- Leaflet with `preferCanvas: true` on operational maps
- `@microsoft/signalr`
- SweetAlert2
- Dashboard auth ใช้ HttpOnly cookies, `withCredentials: true` และ
  `X-XSRF-TOKEN`; localStorage เก็บได้เฉพาะ non-secret user metadata/expiry

## 2. Active Routes

```text
/login
/register
/dashboard
/map
/orders
/analytics
/riders
/shops
/customer
/store-partner
```

Admin layout ใช้ `authGuard` + `roleGuard` สำหรับ Admin/Dispatcher.
Customer และ StorePartner มี role route แยก ไม่มี `/map-live` หรือ sim-map route
ใน active router

## 3. HTTP Rules

- CRUD service สืบทอด `BaseApiService<T>`
- custom endpoint ใช้ `DeliveryHttpRequest`/`req<T>()`
- return type เป็น `Observable<T>`
- unwrap `ApiResponse.value` ใน service helper ไม่ทำซ้ำใน component
- generated OpenAPI DTO เป็นค่าเริ่มต้น; local view model ใช้ได้เมื่อไม่ duplicate
  generated transport contract
- ห้าม hardcode host; relative API path ผ่าน environment/proxy

## 4. SignalR And State

Canonical events อยู่ใน `contracts/signalr-contracts.md`.
GPS mapper ต้องรองรับ legacy casing ชั่วคราว แต่ model ใหม่ใช้ camelCase.
Reconnect ต้องไม่สร้าง subscription ซ้ำ และต้องตรวจ connection state ก่อน invoke.

## 5. Map Rules

- `preferCanvas: true`
- marker movement ที่ต้อง smooth ใช้ `requestAnimationFrame`
- ห้าม auto-fit camera ระหว่าง user interaction โดยไม่มี explicit follow mode
- popup content ต้อง escape และ bind event programmatically
- decode Google polyline precision 1e5 และส่ง Leaflet เป็น `[lat,lng]`
- rider state สีใช้ `IDLE`, `RESERVED`, `BUSY`, `STALE`, `OFFLINE`;
  ห้ามใช้ Order state เช่น `DELIVERING` เป็น Rider state

## 6. RxJS And UI

- ห้าม nested subscribe
- ใช้ `switchMap`, `combineLatest`, `forkJoin` ตาม semantics
- ทุก long-lived subscription ต้อง teardown ด้วย `takeUntilDestroyed` หรือ
  aggregate `Subscription`
- loading, empty, permission และ API error states ต้องแสดงแยกกัน
- 401 ให้ refresh แบบ single-flight; refresh ล้มเหลวต้อง clear state และ
  navigate `/login` ด้วย replace URL

## 7. OpenAPI

`npm run generate:api` สร้าง output ใต้ `src/app/api/generated/`.
ห้ามแก้ generated file ด้วยมือ และห้ามสร้าง DTO ซ้ำเมื่อ schema มีอยู่แล้ว
