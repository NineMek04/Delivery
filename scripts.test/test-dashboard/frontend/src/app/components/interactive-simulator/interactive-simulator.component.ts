import { Component, OnInit, OnDestroy, AfterViewInit, ViewChild, ElementRef, NgZone, ChangeDetectorRef, Output, EventEmitter } from '@angular/core';
import * as L from 'leaflet';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';

interface SimulatedRider {
  id: string;
  name: string;
  status: 'IDLE' | 'PICKING_UP' | 'DELIVERING';
  x: number;
  y: number;
  color: string;
}

@Component({
  selector: 'app-interactive-simulator',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
    <div class="simulator-panel glass-card">
      <div class="sim-header">
        <div class="title-area">
          <span class="pulse-dot"></span>
          <h3>🚀 Interactive Map Simulator Engine</h3>
        </div>
      </div>

      <div class="sim-body">
        <!-- 1. The Leaflet Map Container -->
        <div class="map-grid-container">
          <div class="map-grid">
            <div #simMapElement style="width: 100%; height: 100%;"></div>
          </div>
          
          <div class="playback-bar">
            <span>🚀 Live Simulator Network Status: Connected</span>
            <div class="progress-track">
              <div class="progress-fill" style="width: 100%"></div>
            </div>
          </div>
        </div>

        <!-- 2. Live Telemetry & Fleet Controller Sidebar -->
        <div class="telemetry-sidebar">
          <div class="sidebar-header">
            <h4>📡 Live Fleet Telemetry & Controller</h4>
            <p class="muted">ควบคุมจำลองสภาพแวดล้อมจริง ค้นหาไรเดอร์ และซ้อนรูทวิ่งออเดอร์</p>
          </div>

          <!-- A. Simulation Actions -->
          <div class="telemetry-section">
            <h5 class="section-title">🎮 Simulation Action</h5>
            <button class="action-btn primary" (click)="startSimulation()" style="width: 100%; padding: 10px; font-weight: bold; background: #238636; border: 1px solid #2ea44f; border-radius: 6px; color: white; cursor: pointer; transition: all 0.2s;">
              🔄 เริ่มทดสอบ (Start Test)
            </button>
          </div>

          <!-- B. Create Order Section (Only visible when riders are loaded) -->
          <div class="telemetry-section" *ngIf="riders.length > 0">
            <h5 class="section-title">📦 สร้างงาน (Create Order)</h5>
            <div class="task-card" style="display: flex; flex-direction: column; gap: 10px; padding: 12px; background: rgba(255, 255, 255, 0.02); border: 1px solid var(--border-glass); border-radius: 8px;">
              <div>
                <label style="font-size: 10px; color: var(--color-muted); display: block; margin-bottom: 4px;">ประเภทงาน / Order Type</label>
                <div style="display: flex; gap: 8px;">
                  <button (click)="orderType = 'SINGLE'" [style.background]="orderType === 'SINGLE' ? '#00e5ff' : 'rgba(255,255,255,0.05)'" [style.color]="orderType === 'SINGLE' ? '#000' : '#fff'" style="flex: 1; border: none; padding: 6px; border-radius: 4px; font-size: 11px; cursor: pointer; font-weight: 500; transition: all 0.2s;">เดี่ยว (Single)</button>
                  <button (click)="orderType = 'BATCH'" [style.background]="orderType === 'BATCH' ? '#00e5ff' : 'rgba(255,255,255,0.05)'" [style.color]="orderType === 'BATCH' ? '#000' : '#fff'" style="flex: 1; border: none; padding: 6px; border-radius: 4px; font-size: 11px; cursor: pointer; font-weight: 500; transition: all 0.2s;">แบช (Batch)</button>
                </div>
              </div>

              <!-- SINGLE Pickups / Dropoffs -->
              <div *ngIf="orderType === 'SINGLE'">
                <div>
                  <label style="font-size: 10px; color: var(--color-muted); display: block; margin-bottom: 4px;">จุดรับสินค้า (Pickup Location)</label>
                  <select [(ngModel)]="pickupSelection" (change)="onLocationSelectorChange('pickup')" style="width: 100%; background: #161b22; border: 1px solid #30363d; border-radius: 4px; color: #fff; padding: 6px; font-size: 11px;">
                    <option value="custom">📍 คลิกเลือกบนแผนที่เอง</option>
                    <option *ngFor="let l of landmarks; let idx = index" [value]="idx">{{ l.name }}</option>
                  </select>
                  <button *ngIf="pickupSelection === 'custom'" (click)="activateMapSelection('pickup')" [style.background]="isSelectingPickupCoords ? '#ff9800' : 'rgba(255,255,255,0.05)'" style="width: 100%; border: none; color: white; padding: 4px; margin-top: 4px; border-radius: 4px; font-size: 10px; cursor: pointer; transition: all 0.2s;">
                    {{ isSelectingPickupCoords ? '🟡 กรุณาคลิกบนแผนที่...' : '🎯 เปิดใช้งานโหมดคลิกแผนที่' }}
                  </button>
                </div>

                <div style="margin-top: 8px;">
                  <label style="font-size: 10px; color: var(--color-muted); display: block; margin-bottom: 4px;">จุดส่งสินค้า (Dropoff Location)</label>
                  <select [(ngModel)]="dropoffSelection" (change)="onLocationSelectorChange('dropoff')" style="width: 100%; background: #161b22; border: 1px solid #30363d; border-radius: 4px; color: #fff; padding: 6px; font-size: 11px;">
                    <option value="custom">📍 คลิกเลือกบนแผนที่เอง</option>
                    <option *ngFor="let l of landmarks; let idx = index" [value]="idx">{{ l.name }}</option>
                  </select>
                  <button *ngIf="dropoffSelection === 'custom'" (click)="activateMapSelection('dropoff')" [style.background]="isSelectingDropoffCoords ? '#e91e63' : 'rgba(255,255,255,0.05)'" style="width: 100%; border: none; color: white; padding: 4px; margin-top: 4px; border-radius: 4px; font-size: 10px; cursor: pointer; transition: all 0.2s;">
                    {{ isSelectingDropoffCoords ? '🟡 กรุณาคลิกบนแผนที่...' : '🎯 เปิดใช้งานโหมดคลิกแผนที่' }}
                  </button>
                </div>
              </div>

              <!-- BATCH Checklist Pickups / Dropoffs -->
              <div *ngIf="orderType === 'BATCH'" style="display: flex; flex-direction: column; gap: 8px;">
                <!-- Pickups Checklist -->
                <div style="display: flex; flex-direction: column; gap: 4px; background: rgba(255, 255, 255, 0.02); padding: 8px; border: 1px solid #30363d; border-radius: 6px;">
                  <label style="font-size: 10px; color: #ffc107; font-weight: 500; text-transform: uppercase;">📍 เลือกจุดรับสินค้า (สูงสุด 3 จุด)</label>
                  <div *ngFor="let l of landmarks; let idx = index" style="display: flex; align-items: center; gap: 6px; font-size: 11px;">
                    <input type="checkbox" [(ngModel)]="pickupChecklist[idx]" (change)="onChecklistChange('pickup', idx)" style="cursor: pointer;"/>
                    <span>{{ l.name }}</span>
                  </div>
                  <div *ngIf="customPickupStops.length > 0" style="display: flex; flex-direction: column; gap: 4px; border-top: 1px dashed rgba(255,255,255,0.05); padding-top: 4px; margin-top: 2px;">
                    <div style="font-size: 9px; color: var(--color-muted);">จุดรับเพิ่มจากแผนที่ (สูงสุด 3 จุด):</div>
                    <div *ngFor="let p of customPickupStops; let i = index" style="display: flex; justify-content: space-between; align-items: center; font-size: 10px; background: rgba(255,255,255,0.02); padding: 2px 6px; border-radius: 4px;">
                      <span>📍 #{{ i + 1 }}: [{{ p.lat.toFixed(4) }}, {{ p.lng.toFixed(4) }}]</span>
                      <span (click)="removeCustomStop('pickup', i)" style="color: #f85149; cursor: pointer;">❌</span>
                    </div>
                  </div>
                  <button (click)="activateMapSelection('pickup')" [style.background]="isSelectingPickupCoords ? '#ff9800' : 'rgba(255,255,255,0.05)'" style="width: 100%; border: none; color: white; padding: 4px; border-radius: 4px; font-size: 10px; cursor: pointer; transition: all 0.2s; font-weight: 500;">
                    {{ isSelectingPickupCoords ? '🟡 กรุณาคลิกบนแผนที่...' : '🎯 เปิดโหมดคลิกเพื่อเก็บพิกัดจุดรับ' }}
                  </button>
                </div>

                <!-- Dropoffs Checklist -->
                <div style="display: flex; flex-direction: column; gap: 4px; background: rgba(255, 255, 255, 0.02); padding: 8px; border: 1px solid #30363d; border-radius: 6px;">
                  <label style="font-size: 10px; color: #e91e63; font-weight: 500; text-transform: uppercase;">🚩 เลือกจุดส่งสินค้า (สูงสุด 3 จุด)</label>
                  <div *ngFor="let l of landmarks; let idx = index" style="display: flex; align-items: center; gap: 6px; font-size: 11px;">
                    <input type="checkbox" [(ngModel)]="dropoffChecklist[idx]" (change)="onChecklistChange('dropoff', idx)" style="cursor: pointer;"/>
                    <span>{{ l.name }}</span>
                  </div>
                  <div *ngIf="customDropoffStops.length > 0" style="display: flex; flex-direction: column; gap: 4px; border-top: 1px dashed rgba(255,255,255,0.05); padding-top: 4px; margin-top: 2px;">
                    <div style="font-size: 9px; color: var(--color-muted);">จุดส่งเพิ่มจากแผนที่ (สูงสุด 3 จุด):</div>
                    <div *ngFor="let d of customDropoffStops; let i = index" style="display: flex; justify-content: space-between; align-items: center; font-size: 10px; background: rgba(255,255,255,0.02); padding: 2px 6px; border-radius: 4px;">
                      <span>🚩 #{{ i + 1 }}: [{{ d.lat.toFixed(4) }}, {{ d.lng.toFixed(4) }}]</span>
                      <span (click)="removeCustomStop('dropoff', i)" style="color: #f85149; cursor: pointer;">❌</span>
                    </div>
                  </div>
                  <button (click)="activateMapSelection('dropoff')" [style.background]="isSelectingDropoffCoords ? '#e91e63' : 'rgba(255,255,255,0.05)'" style="width: 100%; border: none; color: white; padding: 4px; border-radius: 4px; font-size: 10px; cursor: pointer; transition: all 0.2s; font-weight: 500;">
                    {{ isSelectingDropoffCoords ? '🟡 กรุณาคลิกบนแผนที่...' : '🎯 เปิดโหมดคลิกเพื่อเก็บพิกัดจุดส่ง' }}
                  </button>
                </div>
              </div>

              <button class="action-btn secondary" (click)="confirmCreateOrder()" style="width: 100%; padding: 8px; font-weight: bold; background: #21262d; border: 1px solid #30363d; border-radius: 6px; color: #c9d1d9; cursor: pointer; margin-top: 4px; transition: all 0.2s;">
                🚀 ยืนยันสร้างงาน (Confirm Create)
              </button>
            </div>
          </div>

          <!-- C. Active Orders List -->
          <div class="telemetry-section" *ngIf="orders.length > 0">
            <h5 class="section-title">📋 Active Orders ({{ orders.length }})</h5>
            <div style="max-height: 120px; overflow-y: auto; display: flex; flex-direction: column; gap: 6px; padding-right: 4px;">
              <div *ngFor="let o of orders" style="background: rgba(255,255,255,0.02); border: 1px solid var(--border-glass); border-radius: 6px; padding: 6px 8px; font-size: 11px; display: flex; align-items: center; justify-content: space-between;">
                <div>
                  <span style="font-weight: bold; color: #ffc107;">{{ o.id }}</span>
                  <span style="margin-left: 6px; font-size: 9px; padding: 1px 4px; border-radius: 3px; background: rgba(255,255,255,0.1);">{{ o.type }}</span>
                </div>
                <span [style.color]="o.status === 'COMPLETED' ? '#4caf50' : o.status === 'DELIVERING' ? '#00e5ff' : '#ff9800'" style="font-family: monospace; font-size: 10px;">{{ o.status }}</span>
              </div>
            </div>
          </div>

          <!-- D. Rider Fleet Status List -->
          <div class="telemetry-section">
            <h5 class="section-title">🛵 Rider Fleet ({{ riders.length }} active)</h5>
            <div class="rider-list-scroll">
              <div *ngFor="let r of riders; trackBy: trackByRiderId" class="rider-status-card" [class.active]="r.status !== 'IDLE'">
                <div class="rider-info-main">
                  <span class="status-indicator" [class.idle]="r.status === 'IDLE'" [class.delivering]="r.status === 'PICKING_UP' || r.status === 'DELIVERING'"></span>
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
      display: flex;
      gap: 1.5rem;
    }

    @media (max-width: 992px) {
      .sim-body {
        flex-direction: column;
      }
    }

    .map-grid-container {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      min-width: 0;
    }

    .map-grid {
      position: relative;
      height: 480px;
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
      width: 450px;
      flex-shrink: 0;
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

    .rider-list-scroll {
      max-height: 220px;
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
export class InteractiveSimulatorComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('simMapElement') mapElement!: ElementRef;
  @Output() sessionStarted = new EventEmitter<string>();

  riders: SimulatedRider[] = [];
  orders: any[] = [];
  riderMarkers = new Map<string, L.Marker>();
  private activePolylines = new Map<string, L.Polyline>();

  map: L.Map | null = null;
  private socket: any = null;

  // Predefined Landmarks for fast picking
  landmarks = [
    { name: 'เซ็นทรัลอุดรธานี (Central Plaza)', lat: 17.4082, lng: 102.7984 },
    { name: 'ยูดี ทาวน์ (UD Town)', lat: 17.4038, lng: 102.8072 },
    { name: 'ตลาดรถไฟ (Train Market)', lat: 17.4042, lng: 102.8021 },
    { name: 'หนองประจักษ์ (Nong Prajak)', lat: 17.4215, lng: 102.7830 },
    { name: 'ทุ่งศรีเมือง (Tung Sri Muang)', lat: 17.4111, lng: 102.7885 }
  ];

  // Forms state
  orderType: 'SINGLE' | 'BATCH' = 'SINGLE';
  pickupSelection: string = '0';
  dropoffSelection: string = '3';
  batchPickups: { lat: number; lng: number }[] = [];
  batchDropoffs: { lat: number; lng: number }[] = [];
  pickupChecklist: boolean[] = [false, false, false, false, false];
  dropoffChecklist: boolean[] = [false, false, false, false, false];
  isCustomPickupChecked = false;
  isCustomDropoffChecked = false;
  isSelectingPickupCoords = false;
  isSelectingDropoffCoords = false;
  customPickupCoords: { lat: number; lng: number } | null = null;
  customDropoffCoords: { lat: number; lng: number } | null = null;
  customPickupStops: { lat: number; lng: number }[] = [];
  customDropoffStops: { lat: number; lng: number }[] = [];

  // Custom marker instances
  private customPickupMarker: L.Marker | null = null;
  private customDropoffMarker: L.Marker | null = null;
  private customPickupMarkers: L.Marker[] = [];
  private customDropoffMarkers: L.Marker[] = [];
  activeOrderMarkers = new Map<string, L.Marker[]>();

  constructor(private ngZone: NgZone, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.initSocketConnection();
    this.checkCurrentStatus();
  }

  async checkCurrentStatus() {
    try {
      const res = await fetch('http://localhost:3001/api/simulator/status');
      if (res.ok) {
        const data = await res.json();
        if (data.running && data.sessionId) {
          console.log('[Simulator] Found active simulator session:', data.sessionId);
          this.sessionStarted.emit(data.sessionId);
        }
      }
    } catch (err) {
      console.error('[Simulator] Failed to check status:', err);
    }
  }

  initSocketConnection() {
    import('socket.io-client').then(({ io }) => {
      this.socket = io('http://localhost:3001');
      
      this.socket.on('connect', () => {
        console.log('[Socket Interactive Simulator] Connection established');
      });

      this.socket.on('simulator-init', (data: any) => {
        this.resetTestTelemetry();
        this.updateRidersAndOrders(data);
      });

      this.socket.on('simulator-tick', (data: any) => {
        this.updateRidersAndOrders(data);
      });
    });
  }

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

    this.map.on('click', (e: L.LeafletMouseEvent) => {
      this.ngZone.run(() => {
        if (this.isSelectingPickupCoords) {
          if (this.orderType === 'BATCH') {
            if (this.batchPickups.length < 3) {
              const newPt = { lat: e.latlng.lat, lng: e.latlng.lng };
              this.customPickupStops.push(newPt);
              this.onChecklistChange();

              const pinIcon = L.divIcon({
                html: `<div style="display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:50%;background:#ff9800;border:2px solid #fff;box-shadow: 0 0 15px #ff9800;font-size:16px;">🏪</div>`,
                className: 'custom-shop-icon',
                iconSize: [28, 28],
                iconAnchor: [14, 14]
              });
              const m = L.marker([newPt.lat, newPt.lng], { icon: pinIcon })
                .bindTooltip(`จุดรับระบุเอง #${this.customPickupStops.length}`, { permanent: true, direction: 'top' })
                .addTo(this.map!);
              this.customPickupMarkers.push(m);

              if (this.batchPickups.length >= 3) {
                this.isSelectingPickupCoords = false;
              }
            } else {
              alert('คุณเลือกจุดรับสูงสุดได้ไม่เกิน 3 จุด');
              this.isSelectingPickupCoords = false;
            }
          } else {
            this.customPickupCoords = { lat: e.latlng.lat, lng: e.latlng.lng };
            this.isSelectingPickupCoords = false;
            this.isCustomPickupChecked = true;
            this.onChecklistChange();
            this.updateCustomMarker('pickup', e.latlng.lat, e.latlng.lng);
          }
        } else if (this.isSelectingDropoffCoords) {
          if (this.orderType === 'BATCH') {
            if (this.batchDropoffs.length < 3) {
              const newPt = { lat: e.latlng.lat, lng: e.latlng.lng };
              this.customDropoffStops.push(newPt);
              this.onChecklistChange();

              const pinIcon = L.divIcon({
                html: `<div style="display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:50%;background:#e91e63;border:2px solid #fff;box-shadow: 0 0 15px #e91e63;font-size:16px;">📍</div>`,
                className: 'custom-dropoff-icon',
                iconSize: [28, 28],
                iconAnchor: [14, 14]
              });
              const m = L.marker([newPt.lat, newPt.lng], { icon: pinIcon })
                .bindTooltip(`จุดส่งระบุเอง #${this.customDropoffStops.length}`, { permanent: true, direction: 'top' })
                .addTo(this.map!);
              this.customDropoffMarkers.push(m);

              if (this.batchDropoffs.length >= 3) {
                this.isSelectingDropoffCoords = false;
              }
            } else {
              alert('คุณเลือกจุดส่งสูงสุดได้ไม่เกิน 3 จุด');
              this.isSelectingDropoffCoords = false;
            }
          } else {
            this.customDropoffCoords = { lat: e.latlng.lat, lng: e.latlng.lng };
            this.isSelectingDropoffCoords = false;
            this.isCustomDropoffChecked = true;
            this.onChecklistChange();
            this.updateCustomMarker('dropoff', e.latlng.lat, e.latlng.lng);
          }
        }
      });
    });

    this.renderLandmarkPins();
    this.onLocationSelectorChange('pickup');
    this.onLocationSelectorChange('dropoff');
  }

  private renderLandmarkPins() {
    if (!this.map) return;
    this.landmarks.forEach(l => {
      const landmarkIcon = L.divIcon({
        html: `<div style="display:flex;align-items:center;justify-content:center;width:24px;height:24px;border-radius:50%;background:#4caf50;border:2px solid #fff;box-shadow: 0 0 10px #4caf50;font-size:12px;color:white;">🏢</div>`,
        className: 'custom-landmark-icon',
        iconSize: [24, 24],
        iconAnchor: [12, 12]
      });
      L.marker([l.lat, l.lng], { icon: landmarkIcon })
        .bindTooltip(l.name, { permanent: false, direction: 'top' })
        .addTo(this.map!);
    });
  }

  resetTestTelemetry() {
    this.ngZone.runOutsideAngular(() => {
      this.riders = [];
      this.orders = [];
      
      this.riderMarkers.forEach(m => m.remove());
      this.riderMarkers.clear();

      this.activePolylines.forEach(p => p.remove());
      this.activePolylines.clear();

      if (this.customPickupMarker) {
        this.customPickupMarker.remove();
        this.customPickupMarker = null;
      }
      if (this.customDropoffMarker) {
        this.customDropoffMarker.remove();
        this.customDropoffMarker = null;
      }

      this.customPickupMarkers.forEach(m => m.remove());
      this.customPickupMarkers = [];
      this.customDropoffMarkers.forEach(m => m.remove());
      this.customDropoffMarkers = [];

      this.activeOrderMarkers.forEach(markers => {
        markers.forEach(m => m.remove());
      });
      this.activeOrderMarkers.clear();

      this.customPickupStops = [];
      this.customDropoffStops = [];
      this.pickupChecklist = [false, false, false, false, false];
      this.dropoffChecklist = [false, false, false, false, false];
      this.isCustomPickupChecked = false;
      this.isCustomDropoffChecked = false;
      this.customPickupCoords = null;
      this.customDropoffCoords = null;

      this.cdr.detectChanges();
    });
  }

  updateRidersAndOrders(data: { riders: any[], orders: any[] }) {
    this.ngZone.runOutsideAngular(() => {
      if (!this.map) return;
      
      this.orders = data.orders || [];

      // Remove completed active order pins from Leaflet map
      this.orders.forEach(o => {
        if (o.status === 'COMPLETED') {
          const markers = this.activeOrderMarkers.get(o.id);
          if (markers) {
            markers.forEach(m => m.remove());
            this.activeOrderMarkers.delete(o.id);
            console.log(`[Simulator] Order ${o.id} is COMPLETED, removed active pins from map.`);
          }
        }
      });
      
      // Update Riders list
      this.riders = (data.riders || []).map(r => ({
        id: r.id,
        name: r.name,
        status: r.status,
        x: r.lat,
        y: r.lng,
        color: r.color
      }));

      // Update Rider Markers
      this.riders.forEach(rider => {
        let marker = this.riderMarkers.get(rider.id);
        const isActive = rider.status !== 'IDLE';

        if (!marker) {
          const customIcon = L.divIcon({
            html: `<div style="width:${isActive ? '24px' : '14px'};height:${isActive ? '24px' : '14px'};border-radius:50%;border:2px solid #fff;background-color:${rider.color};box-shadow: 0 0 ${isActive ? '20px' : '8px'} ${rider.color};transition:all 0.2s ease-in-out;"><div style="position:absolute;top:50%;left:50%;width:${isActive ? '10px' : '6px'};height:${isActive ? '10px' : '6px'};background:#fff;border-radius:50%;transform:translate(-50%,-50%);"></div></div>`,
            className: 'custom-rider-icon',
            iconSize: isActive ? [24, 24] : [14, 14],
            iconAnchor: isActive ? [12, 12] : [7, 7]
          });
          marker = L.marker([rider.x, rider.y], { icon: customIcon })
            .bindTooltip(rider.name + ' (' + rider.status + ')', { direction: 'top', offset: [0, -10] })
            .addTo(this.map!);
          this.riderMarkers.set(rider.id, marker);
        } else {
          marker.setLatLng([rider.x, rider.y]);
          const customIcon = L.divIcon({
            html: `<div style="width:${isActive ? '24px' : '14px'};height:${isActive ? '24px' : '14px'};border-radius:50%;border:2px solid #fff;background-color:${rider.color};box-shadow: 0 0 ${isActive ? '20px' : '8px'} ${rider.color};transition:all 0.2s ease-in-out;"><div style="position:absolute;top:50%;left:50%;width:${isActive ? '10px' : '6px'};height:${isActive ? '10px' : '6px'};background:#fff;border-radius:50%;transform:translate(-50%,-50%);"></div></div>`,
            className: 'custom-rider-icon',
            iconSize: isActive ? [24, 24] : [14, 14],
            iconAnchor: isActive ? [12, 12] : [7, 7]
          });
          marker.setIcon(customIcon);
          marker.setTooltipContent(rider.name + ' (' + rider.status + ')');
        }
      });

      // Update Polylines
      const currentRiderIds = new Set((data.riders || []).map(r => r.id));
      this.activePolylines.forEach((poly, id) => {
        if (!currentRiderIds.has(id)) {
          poly.remove();
          this.activePolylines.delete(id);
        }
      });

      (data.riders || []).forEach(r => {
        if (r.targetPath && r.targetPath.length > 0) {
          const remainingPath = r.targetPath.slice(r.pathIndex);
          if (remainingPath.length > 0) {
            const latlngs = remainingPath.map((pt: any) => [pt.lat, pt.lng] as [number, number]);
            let poly = this.activePolylines.get(r.id);
            if (poly) {
              poly.setLatLngs(latlngs);
            } else {
              poly = L.polyline(latlngs, {
                color: r.color,
                weight: 4,
                dashArray: '8, 8',
                className: 'path-animate'
              }).addTo(this.map!);
              this.activePolylines.set(r.id, poly);
            }
          } else {
            const poly = this.activePolylines.get(r.id);
            if (poly) {
              poly.remove();
              this.activePolylines.delete(r.id);
            }
          }
        } else {
          const poly = this.activePolylines.get(r.id);
          if (poly) {
            poly.remove();
            this.activePolylines.delete(r.id);
          }
        }
      });

      this.cdr.detectChanges();
    });
  }

  async startSimulation() {
    try {
      const res = await fetch('http://localhost:3001/api/simulator/start', { method: 'POST' });
      if (!res.ok) throw new Error('Failed to start simulation session');
      const data = await res.json();
      console.log('[Simulator] Simulation started successfully:', data);
      if (data.sessionId) {
        this.sessionStarted.emit(data.sessionId);
      }
    } catch (err: any) {
      console.error('[Simulator] Failed to start simulation:', err.message);
      alert('Failed to start simulation: ' + err.message);
    }
  }

  onLocationSelectorChange(type: 'pickup' | 'dropoff') {
    if (type === 'pickup') {
      this.isSelectingPickupCoords = false;
      if (this.pickupSelection !== 'custom') {
        const index = parseInt(this.pickupSelection, 10);
        const l = this.landmarks[index];
        this.updateCustomMarker('pickup', l.lat, l.lng);
      }
    } else {
      this.isSelectingDropoffCoords = false;
      if (this.dropoffSelection !== 'custom') {
        const index = parseInt(this.dropoffSelection, 10);
        const l = this.landmarks[index];
        this.updateCustomMarker('dropoff', l.lat, l.lng);
      }
    }
  }

  activateMapSelection(type: 'pickup' | 'dropoff') {
    if (this.orderType === 'BATCH') {
      if (type === 'pickup' && this.batchPickups.length >= 3) {
        alert('คุณเลือกจุดรับสูงสุดได้ไม่เกิน 3 จุด');
        return;
      }
      if (type === 'dropoff' && this.batchDropoffs.length >= 3) {
        alert('คุณเลือกจุดส่งสูงสุดได้ไม่เกิน 3 จุด');
        return;
      }
    }
    if (type === 'pickup') {
      this.isSelectingPickupCoords = true;
      this.isSelectingDropoffCoords = false;
    } else {
      this.isSelectingPickupCoords = false;
      this.isSelectingDropoffCoords = true;
    }
  }

  updateCustomMarker(type: 'pickup' | 'dropoff', lat: number, lng: number) {
    if (!this.map) return;
    if (type === 'pickup') {
      if (this.customPickupMarker) {
        this.customPickupMarker.setLatLng([lat, lng]);
      } else {
        const pinIcon = L.divIcon({
          html: `<div style="display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:50%;background:#ff9800;border:2px solid #fff;box-shadow: 0 0 15px #ff9800;font-size:16px;">🏪</div>`,
          className: 'custom-shop-icon',
          iconSize: [28, 28],
          iconAnchor: [14, 14]
        });
        this.customPickupMarker = L.marker([lat, lng], { icon: pinIcon })
          .bindTooltip('จุดรับสินค้า (Selected Pickup)', { permanent: true, direction: 'top' })
          .addTo(this.map);
      }
    } else {
      if (this.customDropoffMarker) {
        this.customDropoffMarker.setLatLng([lat, lng]);
      } else {
        const pinIcon = L.divIcon({
          html: `<div style="display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:50%;background:#e91e63;border:2px solid #fff;box-shadow: 0 0 15px #e91e63;font-size:16px;">📍</div>`,
          className: 'custom-dropoff-icon',
          iconSize: [28, 28],
          iconAnchor: [14, 14]
        });
        this.customDropoffMarker = L.marker([lat, lng], { icon: pinIcon })
          .bindTooltip('จุดส่งสินค้า (Selected Dropoff)', { permanent: true, direction: 'top' })
          .addTo(this.map);
      }
    }
  }

  onChecklistChange(type?: 'pickup' | 'dropoff', idx?: number) {
    if (type && idx !== undefined) {
      if (type === 'pickup') {
        const checkedCount = this.pickupChecklist.filter(c => c).length;
        if (checkedCount + this.customPickupStops.length > 3) {
          alert('คุณเลือกจุดรับสูงสุดได้ไม่เกิน 3 จุด (รวมจุดที่ระบุเอง)');
          this.pickupChecklist[idx] = false;
        }
      } else if (type === 'dropoff') {
        const checkedCount = this.dropoffChecklist.filter(c => c).length;
        if (checkedCount + this.customDropoffStops.length > 3) {
          alert('คุณเลือกจุดส่งสูงสุดได้ไม่เกิน 3 จุด (รวมจุดที่ระบุเอง)');
          this.dropoffChecklist[idx] = false;
        }
      }
    }

    this.batchPickups = [];
    this.pickupChecklist.forEach((checked, idx) => {
      if (checked) {
        this.batchPickups.push({ lat: this.landmarks[idx].lat, lng: this.landmarks[idx].lng });
      }
    });
    this.batchPickups.push(...this.customPickupStops);

    this.batchDropoffs = [];
    this.dropoffChecklist.forEach((checked, idx) => {
      if (checked) {
        this.batchDropoffs.push({ lat: this.landmarks[idx].lat, lng: this.landmarks[idx].lng });
      }
    });
    this.batchDropoffs.push(...this.customDropoffStops);
    this.cdr.detectChanges();
  }

  removeCustomStop(type: 'pickup' | 'dropoff', idx: number) {
    if (type === 'pickup') {
      if (this.customPickupMarkers[idx]) {
        this.customPickupMarkers[idx].remove();
      }
      this.customPickupMarkers.splice(idx, 1);
      this.customPickupStops.splice(idx, 1);
      
      // Relabel remaining markers
      this.customPickupMarkers.forEach((m, i) => {
        m.setTooltipContent(`จุดรับระบุเอง #${i + 1}`);
      });
    } else {
      if (this.customDropoffMarkers[idx]) {
        this.customDropoffMarkers[idx].remove();
      }
      this.customDropoffMarkers.splice(idx, 1);
      this.customDropoffStops.splice(idx, 1);

      // Relabel remaining markers
      this.customDropoffMarkers.forEach((m, i) => {
        m.setTooltipContent(`จุดส่งระบุเอง #${i + 1}`);
      });
    }
    this.onChecklistChange();
  }

  async confirmCreateOrder() {
    try {
      let bodyData: any = { type: this.orderType };

      if (this.orderType === 'BATCH') {
        const pCount = this.batchPickups.length;
        const dCount = this.batchDropoffs.length;

        if (pCount === 0 || dCount === 0) {
          alert('กรุณาเลือกจุดรับและจุดส่งอย่างน้อยฝั่งละ 1 จุด');
          return;
        }
        if (pCount > 3 || dCount > 3) {
          alert('คุณสามารถเลือกจุดรับหรือจุดส่งสูงสุดได้ไม่เกิน 3 จุด');
          return;
        }
        if (pCount < 2 && dCount < 2) {
          alert('ระบบงานแบชต้องมีจุดจอดอย่างน้อย 2 จุดในฝั่งใดฝั่งหนึ่ง (เช่น รับ 1 ส่ง 2, รับ 2 ส่ง 1 หรือมากกว่า)');
          return;
        }
        bodyData.pickups = this.batchPickups;
        bodyData.dropoffs = this.batchDropoffs;
      } else {
        let pickupCoords = null;
        let dropoffCoords = null;

        if (this.pickupSelection === 'custom') {
          pickupCoords = this.customPickupCoords;
        } else {
          const idx = parseInt(this.pickupSelection, 10);
          pickupCoords = this.landmarks[idx] ? { lat: this.landmarks[idx].lat, lng: this.landmarks[idx].lng } : null;
        }

        if (this.dropoffSelection === 'custom') {
          dropoffCoords = this.customDropoffCoords;
        } else {
          const idx = parseInt(this.dropoffSelection, 10);
          dropoffCoords = this.landmarks[idx] ? { lat: this.landmarks[idx].lat, lng: this.landmarks[idx].lng } : null;
        }

        if (!pickupCoords || !dropoffCoords) {
          alert('Please specify both pickup and dropoff points first.');
          return;
        }
        bodyData.pickup = pickupCoords;
        bodyData.dropoff = dropoffCoords;
      }

      const res = await fetch('http://localhost:3001/api/simulator/create-order', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(bodyData)
      });
      if (!res.ok) {
        const errData = await res.json();
        throw new Error(errData.error || 'Server error');
      }

      const data = await res.json();
      console.log('[Simulator] Order created and matched successfully:', data);

      if (data.order) {
        const orderId = data.order.id;
        const oPickups = data.order.pickups || (data.order.pickup ? [data.order.pickup] : []);
        const oDropoffs = data.order.dropoffs || (data.order.dropoff ? [data.order.dropoff] : []);
        
        const markers: L.Marker[] = [];
        
        oPickups.forEach((p: any, index: number) => {
          const pinIcon = L.divIcon({
            html: `<div style="display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:50%;background:#ff9800;border:2px solid #fff;box-shadow: 0 0 15px #ff9800;font-size:16px;">🏪</div>`,
            className: 'custom-shop-icon',
            iconSize: [28, 28],
            iconAnchor: [14, 14]
          });
          const m = L.marker([p.lat, p.lng], { icon: pinIcon })
            .bindTooltip(`จุดรับสำหรับ ${orderId} (#${index + 1})`, { permanent: true, direction: 'top' })
            .addTo(this.map!);
          markers.push(m);
        });

        oDropoffs.forEach((d: any, index: number) => {
          const pinIcon = L.divIcon({
            html: `<div style="display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:50%;background:#e91e63;border:2px solid #fff;box-shadow: 0 0 15px #e91e63;font-size:16px;">📍</div>`,
            className: 'custom-dropoff-icon',
            iconSize: [28, 28],
            iconAnchor: [14, 14]
          });
          const m = L.marker([d.lat, d.lng], { icon: pinIcon })
            .bindTooltip(`จุดส่งสำหรับ ${orderId} (#${index + 1})`, { permanent: true, direction: 'top' })
            .addTo(this.map!);
          markers.push(m);
        });

        this.activeOrderMarkers.set(orderId, markers);
      }

      // Clear draft states
      this.customPickupMarkers.forEach(m => m.remove());
      this.customPickupMarkers = [];
      this.customDropoffMarkers.forEach(m => m.remove());
      this.customDropoffMarkers = [];
      this.customPickupStops = [];
      this.customDropoffStops = [];

      this.batchPickups = [];
      this.batchDropoffs = [];
      this.pickupChecklist = [false, false, false, false, false];
      this.dropoffChecklist = [false, false, false, false, false];
      this.isCustomPickupChecked = false;
      this.isCustomDropoffChecked = false;
      this.customPickupCoords = null;
      this.customDropoffCoords = null;
      if (this.customPickupMarker) { this.customPickupMarker.remove(); this.customPickupMarker = null; }
      if (this.customDropoffMarker) { this.customDropoffMarker.remove(); this.customDropoffMarker = null; }
      this.cdr.detectChanges();
    } catch (err: any) {
      console.error('[Simulator] Failed to create order:', err.message);
      alert('Error creating order: ' + err.message);
    }
  }

  ngOnDestroy() {
    if (this.socket) {
      this.socket.disconnect();
    }
    if (this.map) {
      this.riderMarkers.forEach(m => m.remove());
      this.customPickupMarkers.forEach(m => m.remove());
      this.customDropoffMarkers.forEach(m => m.remove());
      this.activeOrderMarkers.forEach(markers => {
        markers.forEach(m => m.remove());
      });
      if (this.customPickupMarker) this.customPickupMarker.remove();
      if (this.customDropoffMarker) this.customDropoffMarker.remove();
      this.activePolylines.forEach(p => p.remove());
      this.map.remove();
    }
  }
}
