import {
  Component, EventEmitter, Input, Output, OnChanges, OnDestroy,
  SimpleChanges, AfterViewInit, ViewChild, ElementRef, inject, NgZone
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  LucideAngularModule, ArrowLeft, MapPin, Navigation, Clock,
  Package, Calendar, RefreshCcw, Layers, Flag
} from 'lucide-angular';
import * as L from 'leaflet';
import { RiderDto } from '../../../api/generated/model/rider-dto';
import {
  RiderHistoryService, RiderCompletedOrder, RiderGpsPoint, OrderRouteHistory
} from '../../../core/services/rider-history.service';
import { MapMathService } from '../../map/services/map-math.service';

// Fix Leaflet default icons
const iconDefault = L.icon({
  iconRetinaUrl: 'assets/marker-icon-2x.png',
  iconUrl: 'assets/marker-icon.png',
  shadowUrl: 'assets/marker-shadow.png',
  iconSize: [25, 41], iconAnchor: [12, 41],
  popupAnchor: [1, -34], shadowSize: [41, 41]
});
L.Marker.prototype.options.icon = iconDefault;

export type TimePreset = 'TODAY' | '6H' | '24H' | 'CUSTOM';

@Component({
  selector: 'app-rider-route-map',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  templateUrl: './rider-route-map.component.html',
  styleUrl: './rider-route-map.component.scss'
})
export class RiderRouteMapComponent implements OnChanges, AfterViewInit, OnDestroy {
  readonly icons = {
    ArrowLeft, MapPin, Navigation, Clock, Package, Calendar, RefreshCcw, Layers, Flag
  };

  /** Rider data if in Rider GPS History mode */
  @Input() rider: RiderDto | null = null;
  /** Order object if available */
  @Input() order: RiderCompletedOrder | null = null;
  /** Specific Order ID to fetch route-history */
  @Input() orderId: string | null = null;
  /** Visibility toggle */
  @Input() isVisible = false;

  @Output() back = new EventEmitter<void>();

  @ViewChild('routeMapEl', { static: false }) mapEl!: ElementRef<HTMLDivElement>;

  private readonly historyService = inject(RiderHistoryService);
  private readonly mapMath = inject(MapMathService);
  private readonly zone = inject(NgZone);

  private map: L.Map | null = null;
  actualRouteLine: L.Polyline | null = null;
  plannedRouteLine: L.Polyline | null = null;
  pickupMarker: L.Marker | null = null;
  dropoffMarker: L.Marker | null = null;
  startMarker: L.Marker | null = null;
  endMarker: L.Marker | null = null;

  isLoading = false;
  hasError  = false;
  errorMsg  = '';
  pointCount = 0;
  totalDistanceKm = 0;

  // Filter state for Rider mode
  selectedPreset: TimePreset = 'TODAY';
  customDateFrom = '';
  customTimeFrom = '00:00';
  customDateTo = '';
  customTimeTo = '23:59';

  // Loaded Order Route History (when in Order mode)
  orderRouteData: OrderRouteHistory | null = null;

  private mapReady = false;
  private pendingLoad = false;

  constructor() {
    const today = new Date();
    this.customDateFrom = today.toISOString().slice(0, 10);
    this.customDateTo = today.toISOString().slice(0, 10);
  }

  get isOrderMode(): boolean {
    return !!(this.orderId || this.order);
  }

  ngAfterViewInit(): void {
    if (this.isVisible) {
      setTimeout(() => this.initMap(), 0);
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isVisible']?.currentValue === true) {
      if (!this.mapReady) {
        setTimeout(() => this.initMap(), 0);
      } else if (this.pendingLoad) {
        this.loadData();
      }
    }

    if ((changes['order'] || changes['orderId'] || changes['rider']) && this.mapReady && this.isVisible) {
      this.loadData();
    }
  }

  ngOnDestroy(): void {
    this.destroyMap();
  }

  // ── Map lifecycle ─────────────────────────────────────────────────

  private initMap(): void {
    if (!this.mapEl?.nativeElement || this.mapReady) return;

    this.zone.runOutsideAngular(() => {
      this.map = L.map(this.mapEl.nativeElement, {
        center: [17.4138, 102.7872], // อุดรธานี default
        zoom: 13,
        zoomControl: true
      });

      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors',
        maxZoom: 19
      }).addTo(this.map!);

      this.mapReady = true;
      setTimeout(() => {
        this.map?.invalidateSize();
      }, 150);
    });

    this.loadData();
  }

  private destroyMap(): void {
    this.clearLayers();
    if (this.map) {
      this.map.remove();
      this.map = null;
      this.mapReady = false;
    }
  }

  // ── Data loading ──────────────────────────────────────────────────

  loadData(): void {
    if (!this.map) {
      this.pendingLoad = true;
      return;
    }

    if (this.isOrderMode) {
      this.loadOrderRoute();
    } else if (this.rider?.id) {
      this.loadRiderGpsTrail();
    }
  }

  /** โหมดที่ 1: ประวัติเส้นทางของออเดอร์ (Planned route vs Actual GPS track) */
  private loadOrderRoute(): void {
    const targetOrderId = this.orderId ?? this.order?.id;
    if (!targetOrderId) return;

    this.pendingLoad = false;
    this.isLoading   = true;
    this.hasError    = false;
    this.clearLayers();

    this.historyService.getOrderRouteHistory(targetOrderId).subscribe({
      next: (data) => {
        this.zone.run(() => {
          this.isLoading = false;
          this.orderRouteData = data;
          this.pointCount = data.actualGpsPoints.length;
          this.totalDistanceKm = data.distanceKm;
          this.renderOrderRoute(data);
        });
      },
      error: (err) => {
        this.zone.run(() => {
          this.isLoading = false;
          this.hasError  = true;
          this.errorMsg  = err?.error?.message ?? 'ไม่สามารถดึงข้อมูลเส้นทางออเดอร์ได้';
        });
      }
    });
  }

  /** โหมดที่ 2: ประวัติการเดินทางย้อนหลังของไรเดอร์ (ตามช่วงเวลาที่กำหนด) */
  loadRiderGpsTrail(): void {
    if (!this.rider?.id) return;

    this.pendingLoad = false;
    this.isLoading   = true;
    this.hasError    = false;
    this.clearLayers();

    const { from, to } = this.calculateRiderTimeWindow();

    this.historyService.getGpsHistory(this.rider.id, from, to, 3000).subscribe({
      next: (points) => {
        this.zone.run(() => {
          this.isLoading  = false;
          this.pointCount = points.length;
          this.totalDistanceKm = this.calculatePathDistance(points);
          this.renderRiderGpsTrail(points);
        });
      },
      error: (err) => {
        this.zone.run(() => {
          this.isLoading = false;
          this.hasError  = true;
          this.errorMsg  = err?.error?.message ?? 'ไม่สามารถดึงข้อมูล GPS ของไรเดอร์ได้';
        });
      }
    });
  }

  // ── Rendering on Map ──────────────────────────────────────────────

  private renderOrderRoute(data: OrderRouteHistory): void {
    if (!this.map) return;

    this.zone.runOutsideAngular(() => {
      const bounds = L.latLngBounds([]);

      // 1. วาด Planned Route (เส้นประสีฟ้า/ม่วง)
      if (data.plannedPolyline) {
        const plannedCoords = this.mapMath.decodeRoute(data.plannedPolyline);
        if (plannedCoords.length > 0) {
          this.plannedRouteLine = L.polyline(plannedCoords, {
            color: '#38bdf8',
            weight: 4,
            opacity: 0.65,
            dashArray: '8, 8',
            lineJoin: 'round'
          }).addTo(this.map!);
          bounds.extend(this.plannedRouteLine.getBounds());
        }
      }

      // 2. วาด Actual GPS Track (เส้นทึบสีเขียวสว่าง)
      if (data.actualGpsPoints && data.actualGpsPoints.length >= 2) {
        const actualCoords = data.actualGpsPoints.map(p => L.latLng(p.lat, p.lng));
        this.actualRouteLine = L.polyline(actualCoords, {
          color: '#00ff66',
          weight: 4,
          opacity: 0.9,
          lineJoin: 'round'
        }).addTo(this.map!);
        bounds.extend(this.actualRouteLine.getBounds());
      }

      // 3. จุด Pickup
      if (data.pickupLat != null && data.pickupLng != null) {
        const pickupIcon = L.divIcon({
          className: '',
          html: `<div class="map-marker pickup-marker">P</div>`,
          iconSize: [32, 32], iconAnchor: [16, 32]
        });
        this.pickupMarker = L.marker([data.pickupLat, data.pickupLng], { icon: pickupIcon })
          .bindPopup(`<b>จุดรับสินค้า (Pickup)</b><br>${data.shopName ?? 'ร้านค้า'}`)
          .addTo(this.map!);
        bounds.extend(this.pickupMarker.getLatLng());
      }

      // 4. จุด Dropoff
      if (data.dropoffLat != null && data.dropoffLng != null) {
        const dropIcon = L.divIcon({
          className: '',
          html: `<div class="map-marker dropoff-marker">D</div>`,
          iconSize: [32, 32], iconAnchor: [16, 32]
        });
        this.dropoffMarker = L.marker([data.dropoffLat, data.dropoffLng], { icon: dropIcon })
          .bindPopup(`<b>จุดส่งสินค้า (Dropoff)</b><br>${data.deliveryAddress ?? 'ที่อยู่ลูกค้า'}`)
          .addTo(this.map!);
        bounds.extend(this.dropoffMarker.getLatLng());
      }

      // 5. Fit bounds
      if (bounds.isValid()) {
        this.map!.invalidateSize();
        this.map!.fitBounds(bounds, { padding: [60, 60] });
        setTimeout(() => this.map?.invalidateSize(), 200);
      }
    });
  }

  private renderRiderGpsTrail(points: RiderGpsPoint[]): void {
    if (!this.map) return;

    this.zone.runOutsideAngular(() => {
      // กรองจุด default placeholder (17.4138, 102.7872) ออก เพื่อป้องกันการวาร์ปข้ามเมือง
      const validPoints = points.filter(p =>
        !(Math.abs(p.lat - 17.4138) < 0.0005 && Math.abs(p.lng - 102.7872) < 0.0005)
      );
      const activePoints = validPoints.length > 0 ? validPoints : points;

      if (activePoints.length < 1) return;

      const bounds = L.latLngBounds([]);
      const latlngs = activePoints.map(p => L.latLng(p.lat, p.lng));

      // 1. วาดเส้นทาง GPS ทั้งหมด
      if (latlngs.length >= 2) {
        this.actualRouteLine = L.polyline(latlngs, {
          color: '#00ff66',
          weight: 4,
          opacity: 0.85,
          lineJoin: 'round'
        }).addTo(this.map!);
        bounds.extend(this.actualRouteLine.getBounds());
      }

      // 2. Start Marker (จุดแรก)
      const firstPt = activePoints[0];
      const startIcon = L.divIcon({
        className: '',
        html: `<div class="map-marker start-marker">S</div>`,
        iconSize: [30, 30], iconAnchor: [15, 30]
      });
      this.startMarker = L.marker([firstPt.lat, firstPt.lng], { icon: startIcon })
        .bindPopup(`<b>จุดเริ่มต้น (Start)</b><br>${this.formatDate(firstPt.recordedAt)}`)
        .addTo(this.map!);
      bounds.extend(this.startMarker.getLatLng());

      // 3. End / Latest Marker (จุดล่าสุด)
      if (activePoints.length > 1) {
        const lastPt = activePoints[activePoints.length - 1];
        const endIcon = L.divIcon({
          className: '',
          html: `<div class="map-marker end-marker">E</div>`,
          iconSize: [30, 30], iconAnchor: [15, 30]
        });
        this.endMarker = L.marker([lastPt.lat, lastPt.lng], { icon: endIcon })
          .bindPopup(`<b>จุดล่าสุด (Latest)</b><br>${this.formatDate(lastPt.recordedAt)}`)
          .addTo(this.map!);
        bounds.extend(this.endMarker.getLatLng());
      }

      // 4. Fit bounds
      if (bounds.isValid()) {
        this.map!.invalidateSize();
        this.map!.fitBounds(bounds, { padding: [60, 60] });
        setTimeout(() => this.map?.invalidateSize(), 200);
      }
    });
  }

  private clearLayers(): void {
    this.zone.runOutsideAngular(() => {
      this.actualRouteLine?.remove();
      this.plannedRouteLine?.remove();
      this.pickupMarker?.remove();
      this.dropoffMarker?.remove();
      this.startMarker?.remove();
      this.endMarker?.remove();

      this.actualRouteLine = null;
      this.plannedRouteLine = null;
      this.pickupMarker = null;
      this.dropoffMarker = null;
      this.startMarker = null;
      this.endMarker = null;
    });
  }

  // ── Calculation & Time Filter Helpers ─────────────────────────────

  selectPreset(preset: TimePreset): void {
    this.selectedPreset = preset;
    if (preset !== 'CUSTOM') {
      this.loadRiderGpsTrail();
    }
  }

  applyCustomRange(): void {
    this.loadRiderGpsTrail();
  }

  private calculateRiderTimeWindow(): { from: Date; to: Date } {
    const to = new Date();
    let from: Date;

    switch (this.selectedPreset) {
      case 'TODAY': {
        from = new Date(to);
        from.setHours(0, 0, 0, 0);
        break;
      }
      case '6H': {
        from = new Date(to.getTime() - 6 * 3600_000);
        break;
      }
      case '24H': {
        from = new Date(to.getTime() - 24 * 3600_000);
        break;
      }
      case 'CUSTOM': {
        const fromStr = `${this.customDateFrom}T${this.customTimeFrom}:00`;
        const toStr = `${this.customDateTo}T${this.customTimeTo}:59`;
        from = new Date(fromStr);
        const customTo = new Date(toStr);
        return {
          from: isNaN(from.getTime()) ? new Date(to.getTime() - 86400_000) : from,
          to: isNaN(customTo.getTime()) ? to : customTo
        };
      }
    }

    return { from, to };
  }

  private calculatePathDistance(points: RiderGpsPoint[]): number {
    if (points.length < 2) return 0;
    let totalKm = 0;
    for (let i = 0; i < points.length - 1; i++) {
      totalKm += this.haversineKm(
        points[i].lat, points[i].lng,
        points[i + 1].lat, points[i + 1].lng
      );
    }
    return Math.round(totalKm * 10) / 10;
  }

  private haversineKm(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371; // Earth radius in km
    const dLat = (lat2 - lat1) * Math.PI / 180;
    const dLon = (lon2 - lon1) * Math.PI / 180;
    const a =
      Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
      Math.sin(dLon / 2) * Math.sin(dLon / 2);
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    return R * c;
  }

  // ── View Helpers ──────────────────────────────────────────────────

  goBack(): void {
    this.back.emit();
  }

  get riderName(): string {
    return this.rider?.name ?? this.orderRouteData?.riderName ?? this.rider?.id?.slice(0, 8).toUpperCase() ?? '—';
  }

  get displayTrackingCode(): string {
    return this.orderRouteData?.trackingCode ?? this.order?.trackingCode ?? '—';
  }

  formatDate(iso: string | null): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleString('th-TH', {
      day: '2-digit', month: 'short', year: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit'
    });
  }

  formatDuration(from: string | null, to: string | null): string {
    if (!from || !to) return '—';
    const ms = new Date(to).getTime() - new Date(from).getTime();
    const mins = Math.round(ms / 60000);
    if (mins < 60) return `${mins} นาที`;
    return `${Math.floor(mins / 60)} ชม. ${mins % 60} นาที`;
  }
}
