# 🖥️ ระบบหน้าจอเรียลไทม์และการสลายความจำรั่วไหล (Reactive UI & Memory Leak Prevention)

ทางด้านหน้าจอแอดมินบอร์ด (Angular 19) ที่เปิดรับการเชื่อมต่อ SignalR และแสดงแผนที่สด Leaflet เพื่อรับข้อมูล GPS/Heartbeat จากผู้ขับตลอดเวลา:

### 🟢 1. การอัปเดตตอบสนองแบบเรียลไทม์เฉพาะจุด (Reactive UI Refresh)
- หน้าจอจะไม่ใช้การรีเฟรชทั้งเพจหรือ Component แต่จะใช้วิธี Reactive Subscriptions เช่น ใน [DispatchQueueComponent.ts](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/features/orders/dispatch-queue/dispatch-queue.component.ts) หรือ [MapComponent.ts](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/features/map/map.component.ts)
- เมื่อมีข้อความหรือพิกัดใหม่ส่งเข้ามาใน SignalR Hub Stream ระบบจะประยุกต์แปลงค่าพารามิเตอร์ขารับ แล้วยิงสั่งเปลี่ยนภาพบน DOM ด้วย `cdr.markForCheck()` หรือ `cdr.detectChanges()` เพื่อลดภาระการคำนวณและทาสีหน้าเบราว์เซอร์ (DOM Repaint)

### 🟠 2. การกวาดล้างสัญญารับข่าวสาร (RxJS Unsubscription Teardown)
- ทุก Component ที่มีการ Subscribe บน RxJS Observable หรือ `interval()` จะต้องทำการยกเลิกสัญญาเพื่อไม่ให้แรมฝั่งเบราว์เซอร์ทะลุ (Memory Leak)
- **แนวทางปฏิบัติ:** ประกาศสะสม subscriptions ในคลาสย่อย:
  ```typescript
  private subscriptions = new Subscription();
  // ตอนใช้งาน
  this.subscriptions.add(this.service.data$.subscribe(...));
  ```
- **การล้างออก:** เรียกทำลายล้างใน Lifecycle `ngOnDestroy()`:
  ```typescript
  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }
  ```

### 🔴 3. การสลายแผนที่และวัตถุกราฟิก (Leaflet Map & SignalR Connector Teardown)
- การแสดงผลแผนที่ Leaflet ในหน้าจอ [map.component.ts](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/features/map/map.component.ts#L178) เมื่อนักพัฒนากดเปลี่ยนไปใช้เมนูอื่น เมมโมรี่ของ Marker หรือ Canvas Element ใน Leaflet จะยังติดอยู่ใน RAM เบราว์เซอร์หากไม่ได้สั่งล้างทิ้ง
- **แนวทางปฏิบัติ:** ต้องสั่งลบ markers, circles, polyline และสั่งลบล้างตัวแผนที่หลักออกนอก DOM ใน `ngOnDestroy()`:
  ```typescript
  ngOnDestroy(): void {
    // 1. ยกเลิกสัญญาทั้งหมด
    this.subscriptions.unsubscribe();
    
    // 2. หยุดและสลายสายเชื่อมต่อ SignalR เพื่อหยุดรับส่ง Socket ชั่วคราว
    this.trackingService.stopConnection(); // สั่ง hubConnection.stop() และเซ็ต null
    this.draw.stopAllAnimations();

    // 3. สั่งลบกราฟิกเวกเตอร์ Leaflet ออกจากหน่วยความจำ
    if (this.pickupRouteLine) this.pickupRouteLine.remove();
    if (this.deliveryRouteLine) this.deliveryRouteLine.remove();
    if (this.activeRadarCircle) this.activeRadarCircle.remove();
    this.candidateMarkers.forEach(m => m.remove());
    this.accuracyCircles.forEach(circle => circle.remove());
    this.accuracyCircles.clear();

    // 4. สั่งลบและสลาย Leaflet Map Container
    if (this.map) {
      this.map.remove(); // ลบ event listeners และ DOM nodes ของ Leaflet ทั้งหมด
    }
  }
  ```
