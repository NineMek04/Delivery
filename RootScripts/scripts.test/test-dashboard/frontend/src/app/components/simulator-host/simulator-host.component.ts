import { Component, OnInit, OnDestroy, AfterViewInit, ViewChild, ElementRef, NgZone, ChangeDetectorRef } from '@angular/core';
import * as L from 'leaflet';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';

interface SimulationPlugin {
  id: string;
  name: string;
  description: string;
  icon: string;
  active: boolean;
  intensity: number; // 0-100%
}

interface SimulatedRider {
  id: string;
  name: string;
  status: 'IDLE' | 'DELIVERING' | 'OFFLINE' | 'COMPLETED';
  x: number; // latitude
  y: number; // longitude
  color: string;
  _lastIsActive?: boolean;
  _lastStatus?: string;
  _lastName?: string;
}

@Component({
  selector: 'app-simulator-host',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
    <div class="simulator-panel glass-card">
      <div class="sim-header">
        <div class="title-area">
          <span class="pulse-dot"></span>
          <h3>🚀 Core E2E Simulator Engine</h3>
        </div>
      </div>

      <div class="sim-body">
        <!-- 1. The Leaflet Map Container -->
        <div class="map-grid-container">
          <div class="map-grid">
            <div #simMapElement style="width: 100%; height: 100%;"></div>
          </div>
          
          <div class="playback-bar">
            <span>Simulation Progress: {{ playbackProgress }}%</span>
            <div class="progress-track">
              <div class="progress-fill" [style.width.%]="playbackProgress"></div>
            </div>
          </div>
        </div>

        <!-- 2. Live Telemetry & Fleet Controller Sidebar -->
        <div class="telemetry-sidebar">
          <div class="sidebar-header">
            <h4>📡 Live Fleet Telemetry & Controller</h4>
            <p class="muted">Real-time status of backend services and rider fleet.</p>
          </div>

          <!-- A. Backend & Services Health Indicators -->
          <div class="telemetry-section">
            <h5 class="section-title">🏛️ System Infrastructure</h5>
            <div class="infra-grid">
              <div class="infra-item">
                <span class="dot green"></span>
                <span class="label">PostgreSQL / PostGIS:</span>
                <span class="val">Connected (SRID 4326)</span>
              </div>
              <div class="infra-item">
                <span class="dot green"></span>
                <span class="label">Redis Presence:</span>
                <span class="val">Active (Hot Cache)</span>
              </div>
              <div class="infra-item">
                <span class="dot green"></span>
                <span class="label">RabbitMQ Broker:</span>
                <span class="val">Active (Idle)</span>
              </div>
              <div class="infra-item">
                <span class="dot blue"></span>
                <span class="label">SignalR Hub connections:</span>
                <span class="val">{{ riders.length }} WS active</span>
              </div>
              <div class="infra-item">
                <span class="dot green"></span>
                <span class="label">VRP AI Optimizer load:</span>
                <span class="val">0.02s latency</span>
              </div>
            </div>
          </div>

          <!-- B. Active Order & Scenario Info -->
          <div class="telemetry-section" *ngIf="testShop">
            <h5 class="section-title">🛵 Active Dispatch Task</h5>
            <div class="task-card">
              <div class="task-row">
                <span class="label">🏪 Shop:</span>
                <span class="val text-gold">{{ testShop.name }}</span>
              </div>
              <div class="task-row" *ngIf="activeRiderName">
                <span class="label">👤 Assigned Rider:</span>
                <span class="val text-cyan">{{ activeRiderName }}</span>
              </div>
              <div class="task-row">
                <span class="label">🎯 Dropoff Target:</span>
                <span class="val">{{ testDropoff ? testDropoff.lat.toFixed(4) + ', ' + testDropoff.lng.toFixed(4) : 'N/A' }}</span>
              </div>
            </div>
          </div>

          <!-- C. Rider Fleet Status List -->
          <div class="telemetry-section">
            <h5 class="section-title">🛵 Rider Fleet ({{ riders.length }} active)</h5>
            <div class="rider-list-scroll">
              <div *ngFor="let r of riders; trackBy: trackByRiderId" class="rider-status-card" [class.active]="activeRiderName && r.name.toLowerCase().includes(activeRiderName.toLowerCase())">
                <div class="rider-info-main">
                  <span class="status-indicator" [class.idle]="r.status === 'IDLE'" [class.delivering]="r.status === 'DELIVERING'" [class.completed]="r.status === 'COMPLETED'"></span>
                  <span class="name">{{ r.name }}</span>
                  <span class="badge" [style.background-color]="r.color">{{ r.status }}</span>
                </div>
                <div class="rider-coords">
                  <span>📍 {{ r.x.toFixed(5) }}, {{ r.y.toFixed(5) }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .simulator-panel {
      display: flex;
      flex-direction: column;
      gap: 1.25rem;
      background: var(--bg-card);
      border: 1px solid var(--border-glass);
      padding: 1.5rem;
      border-radius: 16px;
    }

    .sim-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      border-bottom: 1px solid var(--border-glass);
      padding-bottom: 0.75rem;

      .title-area {
        display: flex;
        align-items: center;
        gap: 0.5rem;

        h3 {
          font-weight: 600;
          letter-spacing: 0.5px;
        }

        .pulse-dot {
          width: 8px;
          height: 8px;
          border-radius: 50%;
          background-color: var(--color-success);
          box-shadow: 0 0 8px var(--color-success);
          animation: pulse 1.5s infinite;
        }
      }
    }

    .sim-body {
      display: grid;
      grid-template-columns: 1.6fr 1fr;
      gap: 1.5rem;
    }

    @media (max-width: 992px) {
      .sim-body {
        grid-template-columns: 1fr;
      }
    }

    .map-grid-container {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    .map-grid {
      position: relative;
      height: 320px;
      background: #06090e;
      border: 1px solid var(--border-glass);
      border-radius: 12px;
      overflow: hidden;
    }
    
    ::ng-deep .custom-rider-icon {
      transition: transform 0.35s linear !important;
    }

    .playback-bar {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      font-size: 11px;
      color: var(--color-muted);

      .progress-track {
        height: 6px;
        background: rgba(255, 255, 255, 0.05);
        border-radius: 3px;
        overflow: hidden;
      }

      .progress-fill {
        height: 100%;
        background: linear-gradient(90deg, var(--color-primary), var(--color-secondary));
        border-radius: 3px;
        transition: width 0.3s ease;
      }
    }

    .telemetry-sidebar {
      display: flex;
      flex-direction: column;
      gap: 1.25rem;
      border-left: 1px solid var(--border-glass);
      padding-left: 1.5rem;
    }

    .sidebar-header {
      h4 {
        font-size: 13px;
        font-weight: 600;
        margin-bottom: 4px;
      }
    }

    .telemetry-section {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;

      .section-title {
        font-size: 10px;
        text-transform: uppercase;
        letter-spacing: 0.75px;
        color: var(--color-muted);
        font-weight: 600;
      }
    }

    .infra-grid {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }

    .infra-item {
      display: flex;
      align-items: center;
      font-size: 11px;
      gap: 6px;

      .dot {
        width: 6px;
        height: 6px;
        border-radius: 50%;

        &.green {
          background-color: var(--color-success);
          box-shadow: 0 0 6px var(--color-success);
        }
        &.blue {
          background-color: var(--color-primary);
          box-shadow: 0 0 6px var(--color-primary);
        }
      }

      .label {
        color: var(--color-muted);
      }

      .val {
        margin-left: auto;
        font-family: monospace;
      }
    }

    .task-card {
      background: rgba(255,255,255,0.02);
      border: 1px solid var(--border-glass);
      border-radius: 8px;
      padding: 0.75rem;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      font-size: 11px;
    }

    .task-row {
      display: flex;
      justify-content: space-between;

      .label {
        color: var(--color-muted);
      }

      .text-gold {
        color: #ff9800;
        font-weight: 500;
      }

      .text-cyan {
        color: #00e5ff;
        font-weight: 500;
      }
    }

    .rider-list-scroll {
      max-height: 180px;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      padding-right: 4px;
    }

    .rider-status-card {
      background: rgba(255,255,255,0.01);
      border: 1px solid var(--border-glass);
      border-radius: 8px;
      padding: 0.6rem 0.75rem;
      display: flex;
      flex-direction: column;
      gap: 4px;
      transition: all 0.2s ease;

      &.active {
        background: rgba(0, 229, 255, 0.03);
        border-color: rgba(0, 229, 255, 0.2);
      }

      .rider-info-main {
        display: flex;
        align-items: center;
        gap: 6px;
        font-size: 11px;

        .name {
          font-weight: 500;
        }

        .status-indicator {
          width: 6px;
          height: 6px;
          border-radius: 50%;

          &.idle { background-color: var(--color-primary); }
          &.delivering { background-color: var(--color-warning); }
          &.completed { background-color: var(--color-success); }
        }

        .badge {
          margin-left: auto;
          font-size: 9px;
          padding: 1px 4px;
          border-radius: 4px;
          color: #fff;
          font-family: monospace;
          background: rgba(255,255,255,0.1);
        }
      }

      .rider-coords {
        font-size: 9px;
        color: var(--color-muted);
        font-family: monospace;
      }
    }

    .muted {
      font-size: 11px;
      color: var(--color-muted);
    }

    @keyframes pulse {
      0% { transform: scale(1); opacity: 0.8; }
      50% { transform: scale(1.2); opacity: 1; box-shadow: 0 0 8px var(--color-success); }
      100% { transform: scale(1); opacity: 0.8; }
    }

    @keyframes path-animate {
      to {
        stroke-dashoffset: -20;
      }
    }

    .path-animate {
      animation: path-animate 2s linear infinite;
    }
  `]
})
export class SimulatorHostComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('simMapElement') mapElement!: ElementRef;

  playbackProgress = 0;
  riders: SimulatedRider[] = [];
  riderMappings = new Map<string, string>();
  riderMarkers = new Map<string, L.Marker>();

  map: L.Map | null = null;
  shopMarker: L.Marker | null = null;
  dropoffMarker: L.Marker | null = null;
  private pickupRoutePolyline: L.Polyline | null = null;
  private deliveryRoutePolyline: L.Polyline | null = null;
  private lastBoundsRecalcTime = 0;
  private lastCdrTime = 0;

  activeRiderName: string | null = null;
  testShop: { name: string, lat: number, lng: number } | null = null;
  testDropoff: { lat: number, lng: number } | null = null;

  constructor(private ngZone: NgZone, private cdr: ChangeDetectorRef) {}

  ngOnInit() {}

  trackByRiderId(index: number, rider: SimulatedRider): string {
    return rider.id;
  }

  ngAfterViewInit() {
    this.initMap();
  }

  private initMap() {
    if (!this.mapElement) return;
    
    this.map = L.map(this.mapElement.nativeElement, {
      center: [17.4138, 102.7872],
      zoom: 14,
      zoomControl: false,
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; OpenStreetMap contributors &copy; CARTO',
      subdomains: 'abcd',
      maxZoom: 19,
    }).addTo(this.map);

    this.updateMarkers();
  }

  resetTestTelemetry() {
    this.ngZone.runOutsideAngular(() => {
      if (this.shopMarker) {
        this.shopMarker.remove();
        this.shopMarker = null;
      }
      if (this.dropoffMarker) {
        this.dropoffMarker.remove();
        this.dropoffMarker = null;
      }
      if (this.pickupRoutePolyline) {
        this.pickupRoutePolyline.remove();
        this.pickupRoutePolyline = null;
      }
      if (this.deliveryRoutePolyline) {
        this.deliveryRoutePolyline.remove();
        this.deliveryRoutePolyline = null;
      }
      this.testShop = null;
      this.testDropoff = null;
      this.activeRiderName = null;
      this.riderMappings.clear();
      this.riders = [];
      this.playbackProgress = 0;
      this.riderMarkers.forEach(m => m.remove());
      this.riderMarkers.clear();

      if (this.map) {
        this.map.setView([17.4138, 102.7872], 14);
      }
      this.cdr.detectChanges();
    });
  }

  updateTestTelemetry(data: {
    shop?: { name: string, lat: number, lng: number },
    dropoff?: { lat: number, lng: number },
    route?: { label: string, coords: any[] },
    activeRider?: string,
    riderMapping?: { name: string, id: string },
    riderGps?: { id: string, name: string, lat: number, lng: number, status: 'IDLE' | 'DELIVERING' | 'OFFLINE' | 'COMPLETED' },
    progress?: number
  }) {
    if (!this.map) return;

    this.ngZone.runOutsideAngular(() => {
      if (data.progress !== undefined) {
        this.playbackProgress = data.progress;
      }

      const gps = data.riderGps;
      if (gps) {
        const existing = this.riders.find(r => r.id === gps.id);
        if (existing) {
          existing.x = gps.lat;
          existing.y = gps.lng;
          existing.status = gps.status;
          if (gps.name && gps.name !== gps.id) {
            existing.name = gps.name;
          }
        } else {
          const color = `hsl(${Math.floor(Math.random() * 360)}, 80%, 60%)`;
          this.riders.push({
            id: gps.id,
            name: gps.name || gps.id,
            status: gps.status,
            x: gps.lat,
            y: gps.lng,
            color: color
          });
        }
        this.updateMarkers();
      }

      const mapping = data.riderMapping;
      if (mapping) {
        this.riderMappings.set(mapping.name, mapping.id);
        const rider = this.riders.find(r => r.id === mapping.id);
        if (rider) {
          rider.name = mapping.name;
        }
        this.updateMarkers();
      }

      if (data.shop) {
        this.testShop = data.shop;
        if (this.shopMarker) {
          this.shopMarker.setLatLng([data.shop.lat, data.shop.lng]);
          this.shopMarker.setTooltipContent(`🏪 Shop: ${data.shop.name}`);
        } else {
          const shopIcon = L.divIcon({
            html: `<div style="display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:50%;background:#ff9800;border:2px solid #fff;box-shadow: 0 0 15px #ff9800;font-size:16px;">🏪</div>`,
            className: 'custom-shop-icon',
            iconSize: [28, 28],
            iconAnchor: [14, 14]
          });
          this.shopMarker = L.marker([data.shop.lat, data.shop.lng], { icon: shopIcon })
            .bindTooltip(`🏪 Shop: ${data.shop.name}`, { permanent: true, direction: 'top', className: 'glowing-tooltip' })
            .addTo(this.map!);
        }
      }

      if (data.dropoff) {
        this.testDropoff = data.dropoff;
        if (this.dropoffMarker) {
          this.dropoffMarker.setLatLng([data.dropoff.lat, data.dropoff.lng]);
        } else {
          const dropoffIcon = L.divIcon({
            html: `<div style="display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:50%;background:#e91e63;border:2px solid #fff;box-shadow: 0 0 15px #e91e63;font-size:16px;">📍</div>`,
            className: 'custom-dropoff-icon',
            iconSize: [28, 28],
            iconAnchor: [14, 14]
          });
          this.dropoffMarker = L.marker([data.dropoff.lat, data.dropoff.lng], { icon: dropoffIcon })
            .bindTooltip(`📍 Dropoff Point`, { permanent: true, direction: 'top', className: 'glowing-tooltip' })
            .addTo(this.map!);
        }
      }

      if (data.route) {
        const latlngs = data.route.coords.map(c => [c.lat ?? c[1], c.lng ?? c[0]] as [number, number]);
        if (data.route.label.toLowerCase().includes('store')) {
          if (this.pickupRoutePolyline) {
            this.pickupRoutePolyline.setLatLngs(latlngs);
          } else {
            this.pickupRoutePolyline = L.polyline(latlngs, {
              color: '#ffc107',
              weight: 4,
              dashArray: '8, 8',
              className: 'path-animate'
            }).addTo(this.map!);
          }
        } else if (data.route.label.toLowerCase().includes('dropoff')) {
          if (this.deliveryRoutePolyline) {
            this.deliveryRoutePolyline.setLatLngs(latlngs);
          } else {
            this.deliveryRoutePolyline = L.polyline(latlngs, {
              color: '#00e5ff',
              weight: 4,
              dashArray: '8, 8',
              className: 'path-animate'
            }).addTo(this.map!);
          }
        }
      }

      if (data.activeRider) {
        this.activeRiderName = data.activeRider;
        this.updateMarkers();
      }

      this.recalculateBounds();

      const now = Date.now();
      if (now - this.lastCdrTime > 500) {
        this.lastCdrTime = now;
        this.cdr.detectChanges();
      }
    });
  }

  private recalculateBounds() {
    if (!this.map) return;

    const now = Date.now();
    if (now - this.lastBoundsRecalcTime < 2000) {
      return;
    }
    this.lastBoundsRecalcTime = now;

    const boundsPoints: L.LatLngExpression[] = [];
    if (this.testShop) boundsPoints.push([this.testShop.lat, this.testShop.lng]);
    if (this.testDropoff) boundsPoints.push([this.testDropoff.lat, this.testDropoff.lng]);
    
    if (this.activeRiderName) {
      const activeRiderObj = this.riders.find(r => {
        if (r.name.toLowerCase().includes(this.activeRiderName!.toLowerCase())) return true;
        const mappedId = this.riderMappings.get(this.activeRiderName!);
        return mappedId && r.id === mappedId;
      });
      if (activeRiderObj) {
        boundsPoints.push([activeRiderObj.x, activeRiderObj.y]);
      }
    }

    if (boundsPoints.length >= 2) {
      const bounds = L.latLngBounds(boundsPoints);
      this.map!.fitBounds(bounds, { padding: [50, 50] });
    }
  }

  private updateMarkers() {
    const map = this.map;
    if (!map) return;

    this.riders.forEach(rider => {
      let marker = this.riderMarkers.get(rider.id);
      
      let isActive = false;
      if (this.activeRiderName) {
        if (rider.name.toLowerCase().includes(this.activeRiderName.toLowerCase())) {
          isActive = true;
        } else {
          const mappedId = this.riderMappings.get(this.activeRiderName);
          if (mappedId && rider.id === mappedId) {
            isActive = true;
          }
        }
      }
      
      const needsIconUpdate = !marker || rider._lastIsActive !== isActive;
      const needsTooltipUpdate = !marker || rider._lastStatus !== rider.status || rider._lastName !== rider.name;

      if (!marker) {
        const customIcon = L.divIcon({
          html: `<div style="width:${isActive ? '24px' : '14px'};height:${isActive ? '24px' : '14px'};border-radius:50%;border:2px solid #fff;background-color:${rider.color};box-shadow: 0 0 ${isActive ? '20px' : '8px'} ${rider.color};transition:all 0.2s ease-in-out;"><div style="position:absolute;top:50%;left:50%;width:${isActive ? '10px' : '6px'};height:${isActive ? '10px' : '6px'};background:#fff;border-radius:50%;transform:translate(-50%,-50%);"></div></div>`,
          className: 'custom-rider-icon',
          iconSize: isActive ? [24, 24] : [14, 14],
          iconAnchor: isActive ? [12, 12] : [7, 7]
        });

        marker = L.marker([rider.x, rider.y], { icon: customIcon })
          .bindTooltip(rider.name + ' (' + rider.status + ')', { direction: 'top', offset: [0, -10] })
          .addTo(map);
        this.riderMarkers.set(rider.id, marker);
      } else {
        marker.setLatLng([rider.x, rider.y]);
        
        if (needsIconUpdate) {
          const customIcon = L.divIcon({
            html: `<div style="width:${isActive ? '24px' : '14px'};height:${isActive ? '24px' : '14px'};border-radius:50%;border:2px solid #fff;background-color:${rider.color};box-shadow: 0 0 ${isActive ? '20px' : '8px'} ${rider.color};transition:all 0.2s ease-in-out;"><div style="position:absolute;top:50%;left:50%;width:${isActive ? '10px' : '6px'};height:${isActive ? '10px' : '6px'};background:#fff;border-radius:50%;transform:translate(-50%,-50%);"></div></div>`,
            className: 'custom-rider-icon',
            iconSize: isActive ? [24, 24] : [14, 14],
            iconAnchor: isActive ? [12, 12] : [7, 7]
          });
          marker.setIcon(customIcon);
        }

        if (needsTooltipUpdate) {
          marker.setTooltipContent(rider.name + ' (' + rider.status + ')');
        }
      }

      rider._lastIsActive = isActive;
      rider._lastStatus = rider.status;
      rider._lastName = rider.name;
    });
  }

  ngOnDestroy() {
    if (this.map) {
      this.map.remove();
    }
  }
}
