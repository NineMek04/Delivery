import {
  Component, Input, Output, EventEmitter,
  OnInit, OnDestroy, AfterViewInit, ViewChild, ElementRef, inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { OrderDto } from '../../../api/generated/model/order-dto';
import { OrderService } from '../../../core/services/order.service';
import { RiderService } from '../../../core/services/rider.service';
import { RiderDto } from '../../../api/generated/model/rider-dto';
import {
  LucideAngularModule,
  X, Check, RotateCcw, MapPin, Clock, User, Truck,
  AlertTriangle, Info, Plus, Search, Bell, Navigation
} from 'lucide-angular';
import Swal from 'sweetalert2';

@Component({
  selector:    'app-order-detail',
  standalone:  true,
  imports:     [CommonModule, FormsModule, LucideAngularModule],
  templateUrl: './order-detail.component.html',
  styleUrl:    './order-detail.component.scss'
})
export class OrderDetailComponent implements OnInit, AfterViewInit, OnDestroy {
  @Input({ required: true }) order!: OrderDto;
  @Input() siblingOrders: OrderDto[] = [];
  @Output() close = new EventEmitter<void>();
  @Output() actionSuccess = new EventEmitter<string>();

  @ViewChild('miniMap', { static: false }) miniMapEl?: ElementRef<HTMLElement>;

  private orderService  = inject(OrderService);
  private riderService  = inject(RiderService);
  private map?: L.Map;

  readonly icons = {
    X, Check, RotateCcw, MapPin, Clock, User, Truck,
    AlertTriangle, Info, Plus, Search, Bell, Navigation
  };

  activeTab: 'info' | 'map' | 'rider' = 'info';
  rider?: RiderDto;
  isLoadingRider = false;

  // Timeline steps
  readonly timelineSteps = [
    { status: 'CREATED',    label: 'Created',    icon: 'Plus'     },
    { status: 'MATCHING',   label: 'Matching',   icon: 'Search'   },
    { status: 'OFFERING',   label: 'Offering',   icon: 'Bell'     },
    { status: 'ASSIGNED',   label: 'Assigned',   icon: 'User'     },
    { status: 'PICKING_UP', label: 'Pickup',     icon: 'Truck'    },
    { status: 'DELIVERING', label: 'Delivering', icon: 'Navigation' },
    { status: 'COMPLETED',  label: 'Completed',  icon: 'Check'    },
    { status: 'CANCELLED',  label: 'Cancelled',  icon: 'X'        },
  ];

  ngOnInit(): void {
    if (this.order.assignedRiderId) {
      this.isLoadingRider = true;
      this.riderService.getById(this.order.assignedRiderId).subscribe({
        next:  r => { this.rider = r; this.isLoadingRider = false; },
        error: () => { this.isLoadingRider = false; }
      });
    }
  }

  ngAfterViewInit(): void {
    // Map is initialized when the tab becomes active
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  switchTab(tab: 'info' | 'map' | 'rider'): void {
    this.activeTab = tab;
    if (tab === 'map') {
      // Allow DOM to render the map container first
      setTimeout(() => this.initMiniMap(), 50);
    }
  }

  private initMiniMap(): void {
    if (this.map || !this.miniMapEl?.nativeElement) return;

    const hasPickup  = this.order.pickupLat != null && this.order.pickupLng != null;
    const hasDropoff = this.order.dropoffLat != null && this.order.dropoffLng != null;

    if (!hasPickup && !hasDropoff) return;

    const center: L.LatLngTuple = hasPickup
      ? [this.order.pickupLat!, this.order.pickupLng!]
      : [this.order.dropoffLat!, this.order.dropoffLng!];

    this.map = L.map(this.miniMapEl.nativeElement, {
      center, zoom: 14, zoomControl: false, scrollWheelZoom: true, preferCanvas: true
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution: '© CARTO',
      subdomains: 'abcd',
      maxZoom: 19
    }).addTo(this.map);

    const points: L.LatLng[] = [];

    if (hasPickup) {
      const pickup = L.latLng(this.order.pickupLat!, this.order.pickupLng!);
      points.push(pickup);
      L.marker(pickup, { icon: this.createIcon('🏪', '#ea580c') })
        .addTo(this.map)
        .bindPopup('<b>📍 Pickup</b>')
        .openPopup();
    }

    if (hasDropoff) {
      const dropoff = L.latLng(this.order.dropoffLat!, this.order.dropoffLng!);
      points.push(dropoff);
      L.marker(dropoff, { icon: this.createIcon('🏠', '#0f766e') })
        .addTo(this.map)
        .bindPopup('<b>🎯 Dropoff</b>');
    }

    // Draw straight-line route
    if (points.length === 2) {
      const color = this.order.status === 'COMPLETED' ? '#22c55e'
        : ['DELIVERING', 'PICKING_UP'].includes(this.order.status ?? '') ? '#f97316'
        : '#3b82f6';

      L.polyline(points, { color, weight: 4, opacity: 0.8, dashArray: '8, 6' })
        .addTo(this.map);

      this.map.fitBounds(L.latLngBounds(points), { padding: [40, 40] });
    }

    // Zoom controls
    L.control.zoom({ position: 'bottomright' }).addTo(this.map);
    setTimeout(() => this.map?.invalidateSize(), 100);
  }

  private createIcon(emoji: string, color: string): L.DivIcon {
    return L.divIcon({
      className: '',
      html: `<div style="background:${color};width:32px;height:32px;border-radius:50%;border:3px solid #fff;display:flex;align-items:center;justify-content:center;font-size:15px;box-shadow:0 3px 10px rgba(0,0,0,0.4)">${emoji}</div>`,
      iconSize:   [32, 32],
      iconAnchor: [16, 16],
    });
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Helpers
  // ─────────────────────────────────────────────────────────────────────────

  getStatusTone(status?: string | null): string {
    const map: Record<string, string> = {
      CREATED:'gray', MATCHING:'purple', OFFERING:'amber', ASSIGNED:'blue',
      PICKING_UP:'amber', DELIVERING:'blue', COMPLETED:'green', CANCELLED:'red'
    };
    return map[status ?? ''] ?? 'gray';
  }

  getTimelineIndex(): number {
    return this.timelineSteps.findIndex(s => s.status === this.order.status);
  }

  getIcon(name: string): any {
    const m: any = { Plus: this.icons.Plus, Search: this.icons.Search, Bell: this.icons.Bell,
      User: this.icons.User, Truck: this.icons.Truck, Check: this.icons.Check,
      X: this.icons.X, Navigation: this.icons.Navigation };
    return m[name] ?? this.icons.Info;
  }

  shortId(id?: string | null): string { return id ? id.slice(0, 8).toUpperCase() : '—'; }

  getOrderLabel(): string { return this.order.trackingCode ?? this.shortId(this.order.id); }

  formatCoord(v?: number | null): string { return v != null ? v.toFixed(5) : '—'; }

  formatDate(v?: string | null): string {
    if (!v) return '—';
    return new Date(v).toLocaleString('th-TH', {
      day: '2-digit', month: 'short', year: 'numeric',
      hour: '2-digit', minute: '2-digit'
    });
  }

  get duration(): string {
    if (!this.order.assignedAt || !this.order.completedAt) return '—';
    const ms = new Date(this.order.completedAt).getTime() - new Date(this.order.assignedAt).getTime();
    const min = Math.floor(ms / 60000);
    const sec = Math.floor((ms % 60000) / 1000);
    return `${min}m ${sec}s`;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Actions
  // ─────────────────────────────────────────────────────────────────────────

  cancelOrder(): void {
    if (!this.order.id) return;
    Swal.fire({
      title: 'ยกเลิกออเดอร์?',
      text: `ออเดอร์ ${this.getOrderLabel()} จะถูกยกเลิก`,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonColor: '#dc2626',
      cancelButtonColor: '#334155',
      confirmButtonText: 'ยืนยัน',
      cancelButtonText: 'ยกเลิก',
      background: '#1e293b',
      color: '#f8fafc',
    }).then(result => {
      if (!result.isConfirmed) return;
      this.orderService.cancelOrder(this.order.id!).subscribe({
        next:  () => {
          Swal.fire({ icon:'success', title:'ยกเลิกสำเร็จ', timer:1500, showConfirmButton:false, background:'#1e293b', color:'#f8fafc' });
          this.actionSuccess.emit(this.order.id!);
          this.close.emit();
        },
        error: () => { Swal.fire({ icon:'error', title:'ผิดพลาด', text:'ไม่สามารถยกเลิกได้', background:'#1e293b', color:'#f8fafc' }); }
      });
    });
  }

  retryDispatch(): void {
    if (!this.order.id) return;
    this.orderService.retryDispatch(this.order.id).subscribe({
      next:  () => {
        Swal.fire({ icon:'success', title:'Dispatch ใหม่แล้ว', timer:1500, showConfirmButton:false, background:'#1e293b', color:'#f8fafc' });
        this.actionSuccess.emit(this.order.id!);
        this.close.emit();
      },
      error: () => { Swal.fire({ icon:'error', title:'ผิดพลาด', text:'ไม่สามารถ Dispatch ได้', background:'#1e293b', color:'#f8fafc' }); }
    });
  }
}
