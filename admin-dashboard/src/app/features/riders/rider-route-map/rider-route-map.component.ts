import {
  Component, EventEmitter, Input, Output, OnChanges, OnDestroy,
  SimpleChanges, AfterViewInit, ViewChild, ElementRef, inject, NgZone
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule, ArrowLeft, MapPin, Navigation, Clock, Package } from 'lucide-angular';
import * as L from 'leaflet';
import { RiderDto } from '../../../api/generated/model/rider-dto';
import { RiderHistoryService, RiderCompletedOrder, RiderGpsPoint } from '../../../core/services/rider-history.service';

// Fix Leaflet default icons
const iconDefault = L.icon({
  iconRetinaUrl: 'assets/marker-icon-2x.png',
  iconUrl: 'assets/marker-icon.png',
  shadowUrl: 'assets/marker-shadow.png',
  iconSize: [25, 41], iconAnchor: [12, 41],
  popupAnchor: [1, -34], shadowSize: [41, 41]
});
L.Marker.prototype.options.icon = iconDefault;

@Component({
  selector: 'app-rider-route-map',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  templateUrl: './rider-route-map.component.html',
  styleUrl: './rider-route-map.component.scss'
})
export class RiderRouteMapComponent implements OnChanges, AfterViewInit, OnDestroy {
  readonly icons = { ArrowLeft, MapPin, Navigation, Clock, Package };

  @Input() rider: RiderDto | null = null;
  @Input() order: RiderCompletedOrder | null = null;
  @Input() isVisible = false;

  @Output() back = new EventEmitter<void>();

  @ViewChild('routeMapEl', { static: false }) mapEl!: ElementRef<HTMLDivElement>;

  private readonly historyService = inject(RiderHistoryService);
  private readonly zone = inject(NgZone);

  private map: L.Map | null = null;
  private routeLine: L.Polyline | null = null;
  private pickupMarker: L.Marker | null = null;
  private dropoffMarker: L.Marker | null = null;

  isLoading = false;
  hasError  = false;
  errorMsg  = '';
  pointCount = 0;

  private mapReady = false;
  private pendingLoad = false;

  ngAfterViewInit(): void {
    // ถ้า isVisible = true มาก่อน AfterViewInit (เช่น ถูก render แบบ @if ทีเดียว)
    // ให้รอ 1 tick ก่อน init map เพื่อให้ DOM render เสร็จก่อน
    if (this.isVisible) {
      setTimeout(() => this.initMap(), 0);
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isVisible']?.currentValue === true) {
      if (!this.mapReady) {
        setTimeout(() => this.initMap(), 0);
      } else if (this.pendingLoad) {
        this.loadRoute();
      }
    }

    if (changes['order'] && this.mapReady && this.isVisible) {
      this.loadRoute();
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
    });

    if (this.order) {
      this.loadRoute();
    } else {
      this.pendingLoad = true;
    }
  }

  private destroyMap(): void {
    this.clearLayers();
    if (this.map) {
      this.map.remove();
      this.map = null;
      this.mapReady = false;
    }
  }

  // ── Route loading ─────────────────────────────────────────────────

  loadRoute(): void {
    if (!this.rider?.id || !this.order || !this.map) {
      this.pendingLoad = !this.map;
      return;
    }

    this.pendingLoad = false;
    this.isLoading   = true;
    this.hasError    = false;
    this.clearLayers();

    const { from, to } = RiderHistoryService.buildOrderGpsWindow(this.order);

    this.historyService.getGpsHistory(this.rider.id, from, to).subscribe({
      next: (points) => {
        this.zone.run(() => {
          this.isLoading  = false;
          this.pointCount = points.length;
          this.drawRoute(points);
        });
      },
      error: (err) => {
        this.zone.run(() => {
          this.isLoading = false;
          this.hasError  = true;
          this.errorMsg  = err?.error?.message ?? 'ไม่สามารถดึง GPS history ได้';
        });
      }
    });
  }

  private drawRoute(points: RiderGpsPoint[]): void {
    if (!this.map) return;

    this.zone.runOutsideAngular(() => {
      // ── วาด polyline ────────────────────────────────────────────
      if (points.length >= 2) {
        const latlngs = points.map(p => L.latLng(p.lat, p.lng));

        this.routeLine = L.polyline(latlngs, {
          color: '#00ff66',
          weight: 4,
          opacity: 0.85,
          lineJoin: 'round'
        }).addTo(this.map!);
      }

      // ── Pickup Marker (จุดรับสินค้า) ───────────────────────────
      if (this.order?.pickupLat && this.order?.pickupLng) {
        const pickupIcon = L.divIcon({
          className: '',
          html: `<div class="map-marker pickup-marker">P</div>`,
          iconSize: [32, 32], iconAnchor: [16, 32]
        });
        this.pickupMarker = L.marker(
          [this.order.pickupLat, this.order.pickupLng],
          { icon: pickupIcon }
        )
          .bindPopup(`<b>Pickup</b><br>${this.order.shopName ?? 'Shop'}`)
          .addTo(this.map!);
      }

      // ── Dropoff Marker (จุดส่งของ) ─────────────────────────────
      if (this.order?.dropoffLat && this.order?.dropoffLng) {
        const dropIcon = L.divIcon({
          className: '',
          html: `<div class="map-marker dropoff-marker">D</div>`,
          iconSize: [32, 32], iconAnchor: [16, 32]
        });
        this.dropoffMarker = L.marker(
          [this.order.dropoffLat, this.order.dropoffLng],
          { icon: dropIcon }
        )
          .bindPopup(`<b>Dropoff</b><br>${this.order.deliveryAddress ?? 'Destination'}`)
          .addTo(this.map!);
      }

      // ── Fit bounds ─────────────────────────────────────────────
      if (this.routeLine) {
        this.map!.fitBounds(this.routeLine.getBounds(), { padding: [60, 60] });
      } else if (this.pickupMarker || this.dropoffMarker) {
        const bounds = L.latLngBounds([]);
        if (this.pickupMarker)  bounds.extend(this.pickupMarker.getLatLng());
        if (this.dropoffMarker) bounds.extend(this.dropoffMarker.getLatLng());
        this.map!.fitBounds(bounds, { padding: [60, 60] });
      }
    });
  }

  private clearLayers(): void {
    this.zone.runOutsideAngular(() => {
      this.routeLine?.remove();
      this.pickupMarker?.remove();
      this.dropoffMarker?.remove();
      this.routeLine = null;
      this.pickupMarker = null;
      this.dropoffMarker = null;
    });
  }

  // ── Helpers ───────────────────────────────────────────────────────

  goBack(): void {
    this.back.emit();
  }

  get riderName(): string {
    return this.rider?.name ?? this.rider?.id?.slice(0, 8).toUpperCase() ?? '—';
  }

  formatDate(iso: string | null): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleString('th-TH', {
      day: '2-digit', month: 'short', year: '2-digit',
      hour: '2-digit', minute: '2-digit'
    });
  }

  formatDuration(from: string | null, to: string | null): string {
    if (!from || !to) return '—';
    const ms = new Date(to).getTime() - new Date(from).getTime();
    const mins = Math.round(ms / 60000);
    if (mins < 60) return `${mins} min`;
    return `${Math.floor(mins / 60)}h ${mins % 60}m`;
  }
}
