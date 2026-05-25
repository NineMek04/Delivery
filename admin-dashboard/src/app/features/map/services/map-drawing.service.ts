import { Injectable, OnDestroy } from '@angular/core';
import * as L from 'leaflet';
import { MapMathService } from './map-math.service';

@Injectable()
export class MapDrawingService implements OnDestroy {
  private mapInstance?: L.Map;
  public markerType: 'sim' | 'dashboard' = 'sim';
  
  public markerMap = new Map<string, L.Marker>();
  public markerPositions = new Map<string, L.LatLng>();
  public markerAnimations = new Map<string, number>();
  
  private riderPositionQueues = new Map<string, { lat: number; lng: number; timestamp: number }[]>();
  private activeAnimationLoops = new Map<string, boolean>();
  public routeLines: { pickup?: L.Polyline; delivery?: L.Polyline; completed?: L.Polyline } = {};
  public activeMarkers: { shop?: L.Marker; dropoff?: L.Marker } = {};
  public radarCircle?: L.Circle;
  public candidateMarkers: L.CircleMarker[] = [];

  constructor(private math: MapMathService) {}

  public initializeMap(map: L.Map) {
    this.mapInstance = map;
  }

  ngOnDestroy(): void {
    this.markerAnimations.forEach(frame => cancelAnimationFrame(frame));
    this.clearActiveLayers();
    this.mapInstance = undefined;
  }

  public get map(): L.Map | undefined {
    return this.mapInstance;
  }

  public createRiderIcon(riderId: string, assignedRiderId: string | null, bearing = 0, status = 'IDLE'): L.DivIcon {
    const isWinner = riderId === assignedRiderId;
    if (this.markerType === 'dashboard') {
      const color = ['IDLE', 'AVAILABLE'].includes(status) ? '#22c55e' : (isWinner ? '#2563eb' : '#f59e0b');
      const border = isWinner ? '3px solid #ffde21' : '3px solid white';
      const shadow = isWinner ? '0 0 15px rgba(37,99,235,0.8)' : '0 2px 5px rgba(0,0,0,0.3)';
      const animationClass = isWinner ? 'winner-pulse-marker' : '';

      return L.divIcon({
        className: `custom-rider-marker-div ${animationClass}`,
        html: `<div style="background-color: ${color}; width: 28px; height: 28px; border-radius: 50%; border: ${border}; box-shadow: ${shadow}; display: flex; align-items: center; justify-content: center; font-size: 14px; color: white; transform: rotate(${bearing}deg); transition: transform 0.1s linear;">🛵</div>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14]
      });
    } else {
      const winner = isWinner ? ' winner' : '';
      return L.divIcon({
        className: 'sim-marker',
        html: `<div class="sim-marker-core${winner}" style="transform: rotate(${bearing}deg)">R</div>`,
        iconSize: [34, 34],
        iconAnchor: [17, 17]
      });
    }
  }

  public createStaticMarker(latLng: L.LatLng, label: string, tone: 'shop' | 'dropoff'): L.Marker {
    return L.marker(latLng, {
      icon: L.divIcon({
        className: 'sim-marker',
        html: `<div class="sim-marker-core ${tone}">${label}</div>`,
        iconSize: [36, 36],
        iconAnchor: [18, 18]
      })
    });
  }

  public animateMarker(
    riderId: string, 
    assignedRiderId: string | null, 
    marker: L.Marker, 
    next: L.LatLng, 
    status = 'IDLE', 
    onComplete?: () => void
  ): void {
    // 🌟 สกัดพิษสยบบั๊กรั้งไอคอน: ล้างสไตล์ Transition ดั้งเดิมของ Leaflet ออกให้เกลี้ยงก่อนวาด
    const iconElement = marker.getElement();
    if (iconElement) {
      iconElement.style.transition = 'none'; // ห้าม CSS มารบกวน requestAnimationFrame เด็ดขาด!
    }

    // 1. นำข้อมูลพิกัดใหม่พร้อมประทับเวลาปัจจุบัน ยัดเข้าคลังเก็บคิว
    if (!this.riderPositionQueues.has(riderId)) {
      this.riderPositionQueues.set(riderId, []);
    }
    const queue = this.riderPositionQueues.get(riderId)!;
    queue.push({ lat: next.lat, lng: next.lng, timestamp: performance.now() });

    // จำกัดขนาดคิวสำรองไม่ให้หน่วยความจำบวมโต (คุมโหลดสูงสุด 5 จุดใน RAM)
    if (queue.length > 5) queue.shift();

    // 2. ปลุกตัวรันแอนิเมชันให้ตื่นขึ้นมาทำงาน หากคนขับรายนี้ยังไม่มีลูปวิ่ง
    if (!this.activeAnimationLoops.get(riderId)) {
      this.activeAnimationLoops.set(riderId, true);
      this.processQueueGlide(riderId, assignedRiderId, marker, status, onComplete);
    }
  }

  private processQueueGlide(
    riderId: string, 
    assignedRiderId: string | null, 
    marker: L.Marker, 
    status: string, 
    onComplete?: () => void
  ): void {
    const queue = this.riderPositionQueues.get(riderId);
    if (!queue || queue.length < 2) {
      // หากพิกัดในกล่องคิวหมดลง หรือเน็ตหลุด ให้ปิดลูปรอคอยสัญญาณรอบถัดไป
      this.activeAnimationLoops.set(riderId, false);
      return;
    }

    // หยิบจุดเริ่มต้น และจุดหมายถัดไปออกมาจากคิว
    const startPoint = queue[0];
    const targetPoint = queue[1];
    
    const startTime = performance.now();
    // 🌟 ความยืดหยุ่นขั้นสุด: คำนวณระยะเวลาสไลด์แบบไดนามิกตาม Ping สัญญาณจริงที่ยิงมาถึง
    const dynamicDuration = Math.max(120, Math.min(1000, targetPoint.timestamp - startPoint.timestamp));

    const startLatLng = L.latLng(startPoint.lat, startPoint.lng);
    const targetLatLng = L.latLng(targetPoint.lat, targetPoint.lng);
    const bearing = this.math.calculateBearing(startLatLng, targetLatLng);

    const tick = () => {
      const queueNow = this.riderPositionQueues.get(riderId);
      // หากมีการอัปเดตกระชากโครงสร้าง หรือล้างตารางกลางท่อให้ยุติการทำงาน
      if (!queueNow || queueNow.length < 2) {
        this.activeAnimationLoops.set(riderId, false);
        return;
      }

      const elapsed = performance.now() - startTime;
      const progress = Math.min(elapsed / dynamicDuration, 1);
      
      // 🌟 ใช้สมการคณิตศาสตร์ Easing (Linear Interpolation) ถักทอพิกัดนุ่มนวล
      const currentLat = startPoint.lat + (targetPoint.lat - startPoint.lat) * progress;
      const currentLng = startPoint.lng + (targetPoint.lng - startPoint.lng) * progress;
      const currentLatLng = L.latLng(currentLat, currentLng);
      
      marker.setLatLng(currentLatLng);
      marker.setIcon(this.createRiderIcon(riderId, assignedRiderId, bearing, status));
      this.updateIconStyle(marker, riderId, assignedRiderId, status, bearing);

      if (progress < 1) {
        // หากยังเคลื่อนที่ไปไม่ถึงจุดหมาย ให้ขอเฟรมถัดไปของเบราว์เซอร์ลุยต่อความเร็ว 60 FPS
        const frameId = requestAnimationFrame(tick);
        this.markerAnimations.set(riderId, frameId);
      } else {
        // เมื่อเคลื่อนที่ไปถึงจุดหมายสำเร็จ ให้ถอนจุดแรกทิ้ง และกระโดดรันจุดถัดไปในคิวทันที
        queue.shift();
        if (onComplete) onComplete();
        this.processQueueGlide(riderId, assignedRiderId, marker, status, onComplete);
      }
    };

    const frameId = requestAnimationFrame(tick);
    this.markerAnimations.set(riderId, frameId);
  }

  private updateIconStyle(marker: L.Marker, riderId: string, assignedRiderId: string | null, status: string, bearing: number): void {
    const iconElement = marker.getElement();
    if (!iconElement) return;

    // คุมทิศทางองศาการเลี้ยวรถมอเตอร์ไซค์ตามแนวพิกัด Bearing ถนนจริง
    iconElement.style.transformOrigin = 'center center';
    
    // ดักลอจิกระดับการแสดงผล หมุนหัวรถเอียงองศา และคงป้ายกำกับ CSS เรืองแสงนีออนตามแบบสเปก
    if (this.markerType === 'dashboard') {
      const currentTransform = iconElement.style.transform;
      // ล้างค่าหมุนเก่าออกชั่วคราวแล้วประกบคำสั่งเลี้ยวหัวรถตาม bearing จริงเข้าไป
      const baseTransform = currentTransform.replace(/rotate\([^)]*\)/g, '');
      iconElement.style.transform = `${baseTransform} rotate(${bearing}deg)`;
    }
  }

  public refreshMarkerIcons(assignedRiderId: string | null): void {
    this.markerMap.forEach((marker, riderId) => marker.setIcon(this.createRiderIcon(riderId, assignedRiderId)));
  }

  public drawOfferRoutes(pickupCoords: L.LatLng[], deliveryCoords: L.LatLng[], defaultPickup: L.LatLng | null, defaultDropoff: L.LatLng | null): void {
    if (!this.mapInstance) return;
    this.routeLines.pickup?.remove();
    this.routeLines.delivery?.remove();

    if (pickupCoords.length) {
      this.routeLines.pickup = L.polyline(pickupCoords, {
        color: '#ef4444',
        weight: 5,
        opacity: 0.9,
        dashArray: '10, 10'
      }).addTo(this.mapInstance);
    }

    const finalDeliveryCoords = deliveryCoords.length
      ? deliveryCoords
      : defaultPickup && defaultDropoff ? [defaultPickup, defaultDropoff] : [];

    if (finalDeliveryCoords.length) {
      this.routeLines.delivery = L.polyline(finalDeliveryCoords, {
        color: '#22c55e',
        weight: 5,
        opacity: 0.78
      }).addTo(this.mapInstance);
    }
  }

  public clearCandidateMarkers(): void {
    this.candidateMarkers.forEach(marker => marker.remove());
    this.candidateMarkers = [];
  }

  public clearActiveLayers(): void {
    this.radarCircle?.remove();
    this.routeLines.pickup?.remove();
    this.routeLines.delivery?.remove();
    this.routeLines.completed?.remove();
    this.activeMarkers.shop?.remove();
    this.activeMarkers.dropoff?.remove();
    this.clearCandidateMarkers();
    
    this.routeLines = {};
    this.activeMarkers = {};
  }
}
