import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { req } from '../http/delivery-http-request';
import { unwrapValue } from './base-api.service';

// ──────────────────────────────────────────────────────────────────────
// DTOs (mirror ของ Backend RiderCompletedOrderDto)
// ──────────────────────────────────────────────────────────────────────

export interface RiderCompletedOrder {
  id: string;
  trackingCode: string;
  shopName: string | null;
  deliveryAddress: string | null;
  deliveryFee: number;
  distanceKm: number;
  pickupLat: number | null;
  pickupLng: number | null;
  dropoffLat: number | null;
  dropoffLng: number | null;
  assignedAt: string | null;
  completedAt: string | null;
  createdAt: string | null;
  rating: number | null;
}

export interface RiderGpsPoint {
  lat: number;
  lng: number;
  recordedAt: string;
  orderId: string | null;
}

@Injectable({ providedIn: 'root' })
export class RiderHistoryService {

  /**
   * ดึงรายการออเดอร์ COMPLETED ของ Rider ในช่วงเวลาที่กำหนด
   * → GET /api/v1/riders/{riderId}/completed-orders?from=...&to=...&limit=...
   */
  getCompletedOrders(
    riderId: string,
    from: Date,
    to: Date,
    limit = 100
  ): Observable<RiderCompletedOrder[]> {
    const params = new URLSearchParams({
      from: from.toISOString(),
      to:   to.toISOString(),
      limit: limit.toString()
    });

    return req<any>(`/riders/${encodeURIComponent(riderId)}/completed-orders?${params}`)
      .get()
      .pipe(map(res => unwrapValue<RiderCompletedOrder[]>(res) ?? []));
  }

  /**
   * ดึง GPS history ของ Rider ในช่วงเวลาที่กำหนด
   * → GET /api/v1/rider-locations/{riderId}/history?from=...&to=...&limit=...
   *
   * @param from  เวลาเริ่มต้น — ปกติใช้ order.assignedAt หรือ order.createdAt
   * @param to    เวลาสิ้นสุด — ปกติใช้ order.completedAt + buffer เล็กน้อย
   */
  getGpsHistory(
    riderId: string,
    from: Date,
    to: Date,
    limit = 2000
  ): Observable<RiderGpsPoint[]> {
    const params = new URLSearchParams({
      from:  from.toISOString(),
      to:    to.toISOString(),
      limit: limit.toString()
    });

    return req<any>(
      `rider-locations/${encodeURIComponent(riderId)}/history?${params}`
    )
      .get()
      .pipe(
        map(res => {
          // รองรับทั้ง ApiResponse wrapper และ raw array
          const raw = res?.value ?? res?.data ?? res;
          if (!Array.isArray(raw)) return [];
          return raw
            .map((pt: any) => ({
              lat:        Number(pt.lat ?? pt.Lat),
              lng:        Number(pt.lng ?? pt.Lng),
              recordedAt: pt.recordedAt ?? pt.RecordedAt ?? '',
              orderId:    pt.orderId ?? pt.OrderId ?? null
            }))
            .filter((pt: RiderGpsPoint) =>
              Number.isFinite(pt.lat) &&
              Number.isFinite(pt.lng) &&
              !(pt.lat === 0 && pt.lng === 0)
            );
        })
      );
  }

  // ──────────────────────────────────────────────────────────────────
  // Helpers สำหรับสร้าง Date range จาก preset
  // ──────────────────────────────────────────────────────────────────

  /**
   * คืน { from, to } ตาม preset — to = ตอนนี้, from = ย้อนหลัง N วัน
   * preset 'TODAY' = ตั้งแต่ 00:00 น. ของวันปัจจุบัน (Asia/Bangkok) → เวลาปัจจุบัน
   */
  static buildDateRange(preset: 'TODAY' | '7D' | '14D' | '30D'): { from: Date; to: Date } {
    const to   = new Date();
    let   from: Date;

    switch (preset) {
      case 'TODAY': {
        // 00:00:00 ของวันนี้ตาม local time (แล้ว JS จะ toISOString เป็น UTC อัตโนมัติ)
        from = new Date(to);
        from.setHours(0, 0, 0, 0);
        break;
      }
      case '7D':  from = new Date(to.getTime() -  7 * 86400_000); break;
      case '14D': from = new Date(to.getTime() - 14 * 86400_000); break;
      case '30D': from = new Date(to.getTime() - 30 * 86400_000); break;
    }

    return { from, to };
  }

  /**
   * สร้าง time window สำหรับ GPS history ของออเดอร์
   * from = assignedAt (หรือ createdAt ถ้า assignedAt null) − 5 นาที buffer
   * to   = completedAt + 5 นาที buffer (ให้ได้ GPS ถึงจุดส่ง)
   */
  static buildOrderGpsWindow(order: RiderCompletedOrder): { from: Date; to: Date } {
    const BUFFER_MS = 5 * 60 * 1000; // 5 นาที

    const fromStr = order.assignedAt ?? order.createdAt;
    const toStr   = order.completedAt;

    const from = fromStr
      ? new Date(new Date(fromStr).getTime() - BUFFER_MS)
      : new Date(Date.now() - 3600_000); // fallback: 1 ชม. ก่อนหน้า

    const to = toStr
      ? new Date(new Date(toStr).getTime() + BUFFER_MS)
      : new Date();

    return { from, to };
  }
}
