import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, inject, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { DispatchScanStarted, TrackingSignalRService, RiderLocationUpdate } from '../../core/services/tracking-signalr.service';
import { ShopService, ShopDto } from '../../core/services/shop.service';
import { RiderService } from '../../core/services/rider.service';
import { OrderService } from '../../core/services/order.service';
import { Subscription, forkJoin } from 'rxjs';
import Swal from 'sweetalert2';
import { OrderDetailComponent } from '../orders/order-detail/order-detail.component';
import { OrderDto } from '../../api/generated/model/order-dto';
import { req } from '../../core/http/delivery-http-request';

// Fix Leaflet default icons issue
const iconRetinaUrl = 'assets/marker-icon-2x.png';
const iconUrl = 'assets/marker-icon.png';
const shadowUrl = 'assets/marker-shadow.png';
const iconDefault = L.icon({
  iconRetinaUrl,
  iconUrl,
  shadowUrl,
  iconSize: [25, 41],
  iconAnchor: [12, 41],
  popupAnchor: [1, -34],
  tooltipAnchor: [16, -28],
  shadowSize: [41, 41]
});
L.Marker.prototype.options.icon = iconDefault;

import { MapMathService } from './services/map-math.service';
import { MapDrawingService } from './services/map-drawing.service';
import { RouteService } from '../../services/route.service';

@Component({
  selector: 'app-map',
  standalone: true,
  imports: [CommonModule, FormsModule, OrderDetailComponent],
  providers: [MapMathService, MapDrawingService],
  templateUrl: './map.component.html',
  styleUrl: './map.component.scss'
})
export class MapComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('mapElement', { static: true }) mapElement!: ElementRef;
  readonly title = 'Live Fleet Map';

  private map!: L.Map;
  private markers: Map<string, L.Marker> = new Map();

  // ── ขอบเขตแผนที่ประเทศไทย (Thailand Bounding Box) ──
  private readonly THAILAND_CENTER: L.LatLngTuple = [17.4138, 102.7872]; // อุดรธานี
  private readonly THAILAND_BOUNDS: L.LatLngBoundsExpression = [
    [5.5, 97.3],   // Southwest
    [20.5, 105.7]  // Northeast
  ];

  private trackingService = inject(TrackingSignalRService);
  private shopService = inject(ShopService);
  private riderService = inject(RiderService);
  private orderService = inject(OrderService);
  private math = inject(MapMathService);
  public draw = inject(MapDrawingService);
  private routeService = inject(RouteService);
  private http = inject(HttpClient);
  private zone = inject(NgZone);
  private subscriptions: Subscription = new Subscription();

  public alerts: any[] = [];
  public riders: any[] = [];

  // ── Active Order & Dynamic VRP Routing Details ──
  public activeOrder: any = null;
  public assignedRiderId: string | null = null;
  public simAutoFollow = true;
  
  private pickupRouteLine: L.Polyline | null = null;
  private deliveryRouteLine: L.Polyline | null = null;
  private activeRadarCircle: L.Circle | null = null;
  private candidateMarkers: L.CircleMarker[] = [];

  // Order Markers and Polylines
  private orderMarkers: L.Marker[] = [];
  private orderPolylines: L.Polyline[] = [];
  public selectedOrder: OrderDto | null = null;
  public showOrderDetailModal = false;
  public filterStatus: string = 'ALL';

  private activeOrders: OrderDto[] = [];
  private shopMarkers: Map<string, L.Marker> = new Map();

  get availableCount(): number {
    return [...this.trackingService.getRiderLocations().values()]
      .filter(rider => rider.status === 'IDLE').length;
  }

  get busyCount(): number {
    return [...this.trackingService.getRiderLocations().values()]
      .filter(rider => rider.status === 'BUSY').length;
  }

  get offlineCount(): number {
    return [...this.trackingService.getRiderLocations().values()]
      .filter(rider => ['OFFLINE', 'STALE'].includes(rider.status)).length;
  }

  ngOnInit(): void {
    this.trackingService.startConnection();
    this.trackingService.fetchInitialLocations();

    this.subscriptions.add(
      this.trackingService.alerts$.subscribe(newAlerts => {
        this.alerts = newAlerts;
      })
    );

    this.subscriptions.add(
      this.trackingService.riderLocations$.subscribe(locationMap => {
        this.updateMapMarkers(locationMap);
        this.updateRiderList(locationMap);
      })
    );

    // Subscribe to AI Scan / Dispatch events for route drawing
    this.subscriptions.add(
      this.trackingService.dispatchScanStarted$.subscribe(data => {
        this.zone.run(() => {
          this.handleDispatchScanStarted(data);
        });
      })
    );

    this.subscriptions.add(
      this.trackingService.offerReceived$.subscribe(offer => {
        this.zone.run(() => {
          this.handleOfferReceived(offer);
        });
      })
    );

    this.subscriptions.add(
      this.trackingService.orderAssigned$.subscribe(data => {
        this.zone.run(() => {
          this.handleOrderAssigned(data);
        });
      })
    );

    this.subscriptions.add(
      this.trackingService.orderStatusChanged$.subscribe(data => {
        this.zone.run(() => {
          this.handleOrderStatusChanged(data);
        });
      })
    );
  }

  ngAfterViewInit(): void {
    this.initMap();
    this.draw.initializeMap(this.map);
    const currentLocations = this.trackingService.getRiderLocations();
    this.updateMapMarkers(currentLocations);
    this.updateRiderList(currentLocations);
    this.loadExistingShops();
    this.loadActiveOrders();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.trackingService.stopConnection();
    this.draw.stopAllAnimations();
    
    if (this.pickupRouteLine) this.pickupRouteLine.remove();
    if (this.deliveryRouteLine) this.deliveryRouteLine.remove();
    if (this.activeRadarCircle) this.activeRadarCircle.remove();
    this.candidateMarkers.forEach(m => m.remove());

    if (this.map) {
      this.map.remove();
    }
  }

  private initMap(): void {
    this.map = L.map(this.mapElement.nativeElement, {
      center: this.THAILAND_CENTER,
      zoom: 14,
      minZoom: 6,
      maxZoom: 18,
      maxBounds: this.THAILAND_BOUNDS,
      maxBoundsViscosity: 1.0,
      zoomControl: false,
      preferCanvas: true
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 18
    }).addTo(this.map);
  }

  zoomIn(): void {
    this.map?.zoomIn();
  }

  zoomOut(): void {
    this.map?.zoomOut();
  }

  recenter(): void {
    this.map?.setView(this.THAILAND_CENTER, 12);
  }

  toggleSimAutoFollow(): void {
    this.simAutoFollow = !this.simAutoFollow;
    console.log('Live Map: Auto-Follow toggled to', this.simAutoFollow);
    if (this.simAutoFollow && this.assignedRiderId) {
      const riderMarker = this.markers.get(this.assignedRiderId);
      if (riderMarker) {
        this.map.setView(riderMarker.getLatLng(), 15);
      }
    }
  }

  private decodePolyline(str: string): L.LatLng[] {
    return this.math.decodeRoute(str);
  }

  // ── Loaders ──

  private loadExistingShops(): void {
    this.shopService.getAll(1, 150).subscribe({
      next: (shops) => {
        shops.forEach(shop => this.addShopToMap(shop));
      },
      error: (err) => {
        console.error('Failed to load existing shops:', err);
      }
    });
  }

  private loadActiveOrders(): void {
    this.orderService.getAll(1, 100).subscribe({
      next: (orders) => {
        // เก็บ activeOrders ทั้งหมดที่ยังไม่ COMPLETED/CANCELLED สำหรับ logic อื่น (เช่น cancelRiderOrder)
        this.activeOrders = orders.filter(o => !['COMPLETED', 'CANCELLED'].includes(o.status || ''));
        // วาดบนแผนที่เฉพาะออเดอร์ที่ Assigned/Delivering (มีไรเดอร์แล้ว) เพื่อไม่ให้ mock/pending orders โผล่
        const drawableOrders = orders.filter(o => ['ASSIGNED', 'DELIVERING', 'PICKING_UP'].includes(o.status || ''));
        this.drawOrdersOnMap(drawableOrders);
      }
    });
  }

  private drawOrdersOnMap(orders: OrderDto[]): void {
    if (!this.map) return;

    this.orderMarkers.forEach(m => m.remove());
    this.orderPolylines.forEach(p => p.remove());
    this.orderMarkers = [];
    this.orderPolylines = [];

    orders.forEach(order => {
      // Draw pickup (Shop) if not drawn
      if (order.pickupLat && order.pickupLng) {
        const pMarker = L.marker([order.pickupLat, order.pickupLng], {
          icon: L.divIcon({
            className: 'order-pickup-marker',
            html: `<div style="background:#ea580c;width:24px;height:24px;border-radius:50%;border:2px solid #fff;display:flex;align-items:center;justify-content:center;font-size:12px;box-shadow: 0 0 10px #ea580c;">🏪</div>`,
            iconSize: [24,24],
            iconAnchor: [12,12]
          })
        }).addTo(this.map);
        pMarker.on('click', () => this.openOrderDetails(order));
        this.orderMarkers.push(pMarker);
      }
      
      // Draw dropoff (Customer)
      if (order.dropoffLat && order.dropoffLng) {
        const dMarker = L.marker([order.dropoffLat, order.dropoffLng], {
          icon: L.divIcon({
            className: 'order-dropoff-marker',
            html: `<div style="background:#3b82f6;width:24px;height:24px;border-radius:50%;border:2px solid #fff;display:flex;align-items:center;justify-content:center;font-size:12px;box-shadow: 0 0 10px #3b82f6;">🏠</div>`,
            iconSize: [24,24],
            iconAnchor: [12,12]
          })
        }).addTo(this.map);
        dMarker.on('click', () => this.openOrderDetails(order));
        this.orderMarkers.push(dMarker);
      }

      // Draw polyline connecting pickup and dropoff if not assigned active order
      if (order.pickupLat && order.pickupLng && order.dropoffLat && order.dropoffLng) {
        if (order.assignedRiderId !== this.assignedRiderId) {
          const color = '#8b5cf6';
          const weight = 3;
          const poly = L.polyline(
            [[order.pickupLat, order.pickupLng], [order.dropoffLat, order.dropoffLng]], 
            { color: color, weight: weight, dashArray: '5, 10', opacity: 0.7 }
          ).addTo(this.map);
          this.orderPolylines.push(poly);
        }
      }
    });
  }

  openOrderDetails(order: OrderDto): void {
    this.selectedOrder = order;
    this.showOrderDetailModal = true;
  }

  closeOrderDetails(): void {
    this.selectedOrder = null;
    this.showOrderDetailModal = false;
  }

  private escapeHtml(str: string): string {
    const div = document.createElement('div');
    div.appendChild(document.createTextNode(str));
    return div.innerHTML;
  }

  private addShopToMap(shop: ShopDto): void {
    if (!this.map || !shop.lat || !shop.lng) return;

    const shopIcon = L.divIcon({
      className: 'custom-shop-marker',
      html: `<div style="background-color: #ea580c; width: 26px; height: 26px; border-radius: 50%; border: 3px solid white; box-shadow: 0 3px 6px rgba(0,0,0,0.4); display: flex; align-items: center; justify-content: center; font-size: 11px; color: white;">🏪</div>`,
      iconSize: [26, 26],
      iconAnchor: [13, 13]
    });

    const escapedName = this.escapeHtml(shop.name || '');
    const escapedMenuName = this.escapeHtml(shop.menuName || '');
    const escapedMenuPrice = shop.menuPrice ?? 0;

    const popupContent = `
      <div style="font-family: 'Inter', sans-serif; min-width: 180px;">
        <h4 style="margin: 0 0 6px; font-weight: 700; color: #ea580c; font-size: 13px;">🏪 ${escapedName}</h4>
        <div style="font-size: 11px; color: #4b5563; line-height: 1.5;">
          <b>เมนูแนะนำ:</b> ${escapedMenuName}<br>
          <b>ราคา:</b> <span style="font-weight: 800; color: #10b981;">${escapedMenuPrice} บาท</span>
        </div>
      </div>
    `;

    const marker = L.marker([shop.lat, shop.lng], { icon: shopIcon })
      .bindTooltip(escapedName, {
        permanent: false,
        direction: 'top',
        className: 'custom-shop-tooltip',
        offset: [0, -10]
      })
      .bindPopup(popupContent)
      .addTo(this.map);

    this.shopMarkers.set(shop.id || '', marker);
  }

  // ── Dispatch Scan and Route Handlers ──

  private handleDispatchScanStarted(data: any): void {
    if (this.activeRadarCircle) {
      this.activeRadarCircle.remove();
      this.activeRadarCircle = null;
    }
    this.candidateMarkers.forEach(m => m.remove());
    this.candidateMarkers = [];

    if (data.pickupLat && data.pickupLng) {
      this.activeRadarCircle = L.circle([data.pickupLat, data.pickupLng], {
        radius: (data.searchRadiusKm || 3) * 1000,
        color: '#3b82f6',
        fillColor: '#3b82f6',
        fillOpacity: 0.1,
        className: 'ai-radar-pulse'
      }).addTo(this.map);

      this.map.setView([data.pickupLat, data.pickupLng], 14);

      if (Array.isArray(data.nearbyRiders)) {
        data.nearbyRiders.forEach((rider: any) => {
          const lat = rider.lat ?? rider.Lat ?? rider.latitude ?? 0;
          const lng = rider.lng ?? rider.Lng ?? rider.longitude ?? 0;
          if (lat && lng) {
            const candidate = L.circleMarker([lat, lng], {
              radius: 8,
              color: '#3b82f6',
              fillColor: '#60a5fa',
              fillOpacity: 0.8,
              className: 'scan-candidate-marker'
            }).bindTooltip(`Rider: ${rider.name || rider.riderId || 'Candidate'}`, { direction: 'top' })
              .addTo(this.map);
            this.candidateMarkers.push(candidate);
          }
        });
      }
    }
  }

  private handleOfferReceived(offer: any): void {
    if (!offer || !offer.order) return;

    this.activeOrder = offer.order;
    const order = offer.order;
    this.assignedRiderId = offer.riderId || null;

    if (this.pickupRouteLine) this.pickupRouteLine.remove();
    if (this.deliveryRouteLine) this.deliveryRouteLine.remove();
    this.pickupRouteLine = null;
    this.deliveryRouteLine = null;

    if (offer.pickupRoute) {
      const coords = this.decodePolyline(offer.pickupRoute);
      this.pickupRouteLine = L.polyline(coords, {
        color: '#ffc107',
        weight: 4,
        dashArray: '8, 8',
        className: 'path-animate'
      }).addTo(this.map);
    } else if (offer.riderId && order.pickupLat && order.pickupLng) {
      const locMap = this.trackingService.getRiderLocations();
      const riderLoc = locMap.get(offer.riderId);
      if (riderLoc) {
        const coordsStr = `${riderLoc.longitude},${riderLoc.latitude};${order.pickupLng},${order.pickupLat}`;
        const osrmUrl = `https://router.project-osrm.org/route/v1/driving/${coordsStr}?overview=full&geometries=geojson`;
        this.http.get<any>(osrmUrl).subscribe({
          next: (res) => {
            if (res?.routes?.[0]?.geometry?.coordinates) {
              const roadCoords = res.routes[0].geometry.coordinates.map((c: any) => L.latLng(c[1], c[0]));
              this.pickupRouteLine = L.polyline(roadCoords, {
                color: '#ffc107',
                weight: 4,
                dashArray: '8, 8',
                className: 'path-animate'
              }).addTo(this.map);
            }
          }
        });
      }
    }

    if (order.pickupLat && order.pickupLng && order.dropoffLat && order.dropoffLng) {
      const coordsStr = `${order.pickupLng},${order.pickupLat};${order.dropoffLng},${order.dropoffLat}`;
      const osrmUrl = `https://router.project-osrm.org/route/v1/driving/${coordsStr}?overview=full&geometries=geojson`;
      this.http.get<any>(osrmUrl).subscribe({
        next: (res) => {
          if (res?.routes?.[0]?.geometry?.coordinates) {
            const roadCoords = res.routes[0].geometry.coordinates.map((c: any) => L.latLng(c[1], c[0]));
            this.deliveryRouteLine = L.polyline(roadCoords, {
              color: '#00e5ff',
              weight: 4,
              dashArray: '8, 8',
              className: 'path-animate'
            }).addTo(this.map);

            if (this.simAutoFollow) {
              const bounds = L.latLngBounds(roadCoords);
              this.map.fitBounds(bounds, { padding: [50, 50] });
            }
          }
        }
      });
    }
  }

  private handleOrderAssigned(data: any): void {
    if (this.activeRadarCircle) {
      this.activeRadarCircle.remove();
      this.activeRadarCircle = null;
    }
    this.candidateMarkers.forEach(m => m.remove());
    this.candidateMarkers = [];

    this.assignedRiderId = data.riderId;

    const order = this.activeOrders.find(o => o.id === data.id);
    if (order) {
      this.activeOrder = order;
      this.drawActiveOrderRoute(order, data.riderId);
    }
  }

  private handleOrderStatusChanged(data: any): void {
    if (data.status === 'DELIVERING') {
      if (this.pickupRouteLine) {
        this.pickupRouteLine.remove();
        this.pickupRouteLine = null;
      }
    }
    if (['COMPLETED', 'CANCELLED'].includes(data.status)) {
      if (this.pickupRouteLine) {
        this.pickupRouteLine.remove();
        this.pickupRouteLine = null;
      }
      if (this.deliveryRouteLine) {
        this.deliveryRouteLine.remove();
        this.deliveryRouteLine = null;
      }
      const orderId = data.orderId || data.OrderId;
      if (this.activeOrder && (this.activeOrder.id === orderId)) {
        this.assignedRiderId = null;
        this.activeOrder = null;
      }
    }
    this.loadActiveOrders();
  }

  private drawActiveOrderRoute(order: any, riderId: string): void {
    if (this.pickupRouteLine) this.pickupRouteLine.remove();
    if (this.deliveryRouteLine) this.deliveryRouteLine.remove();
    this.pickupRouteLine = null;
    this.deliveryRouteLine = null;

    const locMap = this.trackingService.getRiderLocations();
    const riderLoc = locMap.get(riderId);

    if (riderLoc && order.pickupLat && order.pickupLng) {
      const coordsStr = `${riderLoc.longitude},${riderLoc.latitude};${order.pickupLng},${order.pickupLat}`;
      const osrmUrl = `https://router.project-osrm.org/route/v1/driving/${coordsStr}?overview=full&geometries=geojson`;
      this.http.get<any>(osrmUrl).subscribe({
        next: (res) => {
          if (res?.routes?.[0]?.geometry?.coordinates) {
            const roadCoords = res.routes[0].geometry.coordinates.map((c: any) => L.latLng(c[1], c[0]));
            this.pickupRouteLine = L.polyline(roadCoords, {
              color: '#ffc107',
              weight: 4,
              dashArray: '8, 8',
              className: 'path-animate'
            }).addTo(this.map);
          }
        }
      });
    }

    if (order.pickupLat && order.pickupLng && order.dropoffLat && order.dropoffLng) {
      const coordsStr = `${order.pickupLng},${order.pickupLat};${order.dropoffLng},${order.dropoffLat}`;
      const osrmUrl = `https://router.project-osrm.org/route/v1/driving/${coordsStr}?overview=full&geometries=geojson`;
      this.http.get<any>(osrmUrl).subscribe({
        next: (res) => {
          if (res?.routes?.[0]?.geometry?.coordinates) {
            const roadCoords = res.routes[0].geometry.coordinates.map((c: any) => L.latLng(c[1], c[0]));
            this.deliveryRouteLine = L.polyline(roadCoords, {
              color: '#00e5ff',
              weight: 4,
              dashArray: '8, 8',
              className: 'path-animate'
            }).addTo(this.map);

            if (this.simAutoFollow) {
              const bounds = L.latLngBounds(roadCoords);
              if (riderLoc) bounds.extend([riderLoc.latitude, riderLoc.longitude]);
              this.map.fitBounds(bounds, { padding: [50, 50] });
            }
          }
        }
      });
    }
  }

  // ── Real-time Rider Marker and Popup Management ──

  private updateMapMarkers(locationMap: Map<string, RiderLocationUpdate>): void {
    if (!this.map) return;

    locationMap.forEach((loc, riderId) => {
      let marker = this.markers.get(riderId);
      if (!this.matchesRiderFilter(loc)) {
        marker?.remove();
        return;
      }

      const isWinner = riderId === this.assignedRiderId;
      const hasSnappedPosition = loc.isSnapped &&
        Number.isFinite(loc.snappedLat) &&
        Number.isFinite(loc.snappedLng) &&
        !(loc.snappedLat === 0 && loc.snappedLng === 0);
      const displayLat = hasSnappedPosition
        ? loc.snappedLat!
        : loc.latitude;
      const displayLng = hasSnappedPosition
        ? loc.snappedLng!
        : loc.longitude;
      if (!Number.isFinite(displayLat) || !Number.isFinite(displayLng) ||
          (displayLat === 0 && displayLng === 0)) return;
      const next = L.latLng(displayLat, displayLng);

      const isActive = isWinner || ['RESERVED', 'BUSY'].includes(loc.status);
      const statusColor = isWinner
        ? '#3b82f6'
        : loc.status === 'IDLE'
          ? '#22c55e'
          : loc.status === 'RESERVED'
            ? '#eab308'
            : loc.status === 'BUSY'
              ? '#f97316'
              : '#64748b';

      const customIcon = L.divIcon({
        html: `<div style="width:${isActive ? '24px' : '16px'};height:${isActive ? '24px' : '16px'};border-radius:50%;border:2px solid #fff;background-color:${statusColor};box-shadow: 0 0 ${isActive ? '24px' : '8px'} ${statusColor};transition:all 0.2s ease-in-out;"><div style="position:absolute;top:50%;left:50%;width:${isActive ? '10px' : '6px'};height:${isActive ? '10px' : '6px'};background:#fff;border-radius:50%;transform:translate(-50%,-50%);"></div></div>`,
        className: 'custom-rider-icon',
        iconSize: isActive ? [24, 24] : [16, 16],
        iconAnchor: isActive ? [12, 12] : [8, 8]
      });

      const escapedRiderId = this.escapeHtml(loc.riderId || '');
      const escapedStatus = this.escapeHtml(loc.status || '');

      const popupContent = `
        <div style="font-family: 'Inter', sans-serif; min-width: 180px; padding: 5px;">
          <strong style="color: ${isWinner ? '#3b82f6' : '#f3f4f6'}; font-size: 13px;">🛵 ไรเดอร์: RID-${escapedRiderId.substring(0, 6).toUpperCase()}</strong><br>
          <hr style="margin: 6px 0; border: 0; border-top: 1px solid rgba(255,255,255,0.1);">
          <span style="font-size: 11px; color: #9ca3af; line-height: 1.5;">
            <b>สถานะ:</b> <span style="color: ${statusColor}; font-weight: bold;">${escapedStatus}</span><br>
            <b>พิกัด:</b> ${displayLat.toFixed(5)}, ${displayLng.toFixed(5)}<br>
            <b>ความเร็ว:</b> ${loc.speedKmh ? loc.speedKmh.toFixed(1) : 0} km/h
          </span>
          <hr style="margin: 6px 0; border: 0; border-top: 1px solid rgba(255,255,255,0.1);">
          <div style="display: grid; gap: 6px;">
            <button class="btn-contact" style="width: 100%; background: #22c55e; color: black; border: none; padding: 6px; border-radius: 4px; font-size: 11px; font-weight: bold; cursor: pointer;">📞 ติดต่อไรเดอร์</button>
            <button class="btn-cancel" style="width: 100%; background: #ef4444; color: white; border: none; padding: 6px; border-radius: 4px; font-size: 11px; font-weight: bold; cursor: pointer;">🛑 ยกเลิกออร์เดอร์</button>
            <button class="btn-route" style="width: 100%; background: #3b82f6; color: white; border: none; padding: 6px; border-radius: 4px; font-size: 11px; cursor: pointer;">🔍 เส้นทางย้อนหลัง</button>
          </div>
        </div>
      `;

      if (marker) {
        if (!this.map.hasLayer(marker)) {
          marker.addTo(this.map);
        }
        this.draw.animateMarker(riderId, this.assignedRiderId, marker, next, loc.status, loc.isSnapped, () => {
          if (this.simAutoFollow && isWinner) {
            this.map.setView(next);
          }
        });
        marker.setPopupContent(popupContent);
      } else {
        const created = L.marker(next, { icon: customIcon })
          .bindPopup(popupContent)
          .addTo(this.map);

        created.on('popupopen', () => {
          const popupEl = created.getPopup()?.getElement();
          if (!popupEl) return;
          popupEl.querySelector('.btn-contact')?.addEventListener('click', () => {
            this.zone.run(() => this.contactRider(riderId));
          });
          popupEl.querySelector('.btn-cancel')?.addEventListener('click', () => {
            this.zone.run(() => this.cancelRiderOrder(riderId));
          });
          popupEl.querySelector('.btn-route')?.addEventListener('click', () => {
            this.zone.run(() => this.showRiderRoute(riderId));
          });
        });

        this.markers.set(riderId, created);
        this.draw.markerMap.set(riderId, created);
        this.draw.markerPositions.set(riderId, next);
      }
    });
  }

  private updateRiderList(locationMap: Map<string, RiderLocationUpdate>): void {
    const list: any[] = [];
    locationMap.forEach((loc, riderId) => {
      if (!this.matchesRiderFilter(loc)) return;

      const isWinner = riderId === this.assignedRiderId;
      list.push({
        name: `Rider ${riderId.substring(0, 5).toUpperCase()}`,
        id: riderId,
        battery: isWinner ? '98%' : '84%',
        signal: 'Strong',
        status: isWinner ? 'DISPATCHED' : loc.status,
        avatar: isWinner ? '🏆' : loc.status.charAt(0),
        tone: loc.status === 'IDLE'
          ? 'online'
          : (isWinner || loc.status === 'RESERVED' || loc.status === 'BUSY' ? 'busy' : 'low')
      });
    });
    this.riders = list;
  }

  private matchesRiderFilter(loc: RiderLocationUpdate): boolean {
    return this.filterStatus === 'ALL' ||
      loc.status === this.filterStatus ||
      (this.filterStatus === 'OFFLINE' && loc.status === 'STALE');
  }

  setFilterStatus(status: string): void {
    this.filterStatus = status;
    const locations = this.trackingService.getRiderLocations();
    this.updateMapMarkers(locations);
    this.updateRiderList(locations);
  }

  showRiderRoute(riderId: string): void {
    if (!riderId) return;
    const to = new Date();
    const from = new Date(to.getTime() - 24 * 60 * 60 * 1000);
    const query = new URLSearchParams({
      from: from.toISOString(),
      to: to.toISOString(),
      limit: '2000'
    });

    req<any>(`rider-locations/${encodeURIComponent(riderId)}/history?${query}`).get().subscribe({
      next: (response) => {
        const points = response?.data ?? response?.value ?? response;
        if (Array.isArray(points) && points.length > 0) {
          if (this.deliveryRouteLine) {
            this.deliveryRouteLine.remove();
            this.deliveryRouteLine = null;
          }
          const coords = points
            .map((pt: any) => ({
              lat: Number(pt.lat ?? pt.Lat),
              lng: Number(pt.lng ?? pt.Lng)
            }))
            .filter((point: { lat: number; lng: number }) =>
              Number.isFinite(point.lat) &&
              Number.isFinite(point.lng) &&
              !(point.lat === 0 && point.lng === 0))
            .map((point: { lat: number; lng: number }) =>
              L.latLng(point.lat, point.lng));

          if (coords.length === 0) {
            Swal.fire({
              title: 'No Valid GPS Data',
              text: 'History was found, but it did not contain valid coordinates.',
              icon: 'warning'
            });
            return;
          }

          this.deliveryRouteLine = L.polyline(coords, {
            color: '#3b82f6',
            weight: 4,
            opacity: 0.8,
            dashArray: '5, 10'
          }).addTo(this.map);
          this.map.fitBounds(this.deliveryRouteLine.getBounds(), { padding: [50, 50] });
          Swal.fire({
            title: 'Rider Route',
            text: `Showing ${coords.length} GPS points from the last 24 hours for rider ${riderId.substring(0, 6)}`,
            icon: 'info',
            toast: true,
            position: 'top-end',
            timer: 3000,
            showConfirmButton: false
          });
        } else {
          Swal.fire({
            title: 'No Data',
            text: 'No history found for this rider.',
            icon: 'warning',
            toast: true,
            position: 'top-end',
            timer: 3000,
            showConfirmButton: false
          });
        }
      },
      error: (err) => {
        console.error('Failed to fetch rider history:', err);
        Swal.fire({
          title: 'Unable to Load GPS History',
          text: err?.error?.message ?? 'Please try again.',
          icon: 'error'
        });
      }
    });
  }

  contactRider(riderId: string): void {
    this.riderService.getById(riderId).subscribe({
      next: (rider) => {
        Swal.fire({
          title: `📞 ติดต่อไรเดอร์ ${rider.name || riderId.substring(0, 6).toUpperCase()}`,
          html: `
            <div style="font-family: 'Inter', sans-serif; text-align: left; padding: 10px;">
              <b>เบอร์โทรศัพท์:</b> <a href="tel:${rider.phone || '089-999-9999'}" style="color: #22c55e; font-weight: bold; font-size: 16px;">${rider.phone || '089-999-9999'}</a><br>
              <b>รหัสอ้างอิง:</b> ${rider.trackingCode || 'N/A'}<br>
              <b>ระดับคะแนน:</b> ⭐ ${rider.rating || '5.0'}
            </div>
          `,
          icon: 'info',
          confirmButtonColor: '#3b82f6'
        });
      },
      error: (err) => {
        console.error('Failed to fetch rider details:', err);
        Swal.fire({
          title: 'ติดต่อไรเดอร์',
          text: `ไรเดอร์ ID: ${riderId.substring(0, 6).toUpperCase()} (ไม่พบข้อมูลในระบบ)`,
          icon: 'warning',
          confirmButtonColor: '#f59e0b'
        });
      }
    });
  }

  cancelRiderOrder(riderId: string): void {
    const riderOrders = this.activeOrders.filter(o => o.assignedRiderId === riderId);
    if (riderOrders.length === 0) {
      Swal.fire({
        title: 'ไม่สามารถยกเลิกออร์เดอร์ได้',
        text: 'ไรเดอร์คนนี้ไม่มีออร์เดอร์ที่กำลังจัดส่งอยู่ในขณะนี้',
        icon: 'warning',
        confirmButtonColor: '#f59e0b'
      });
      return;
    }

    if (riderOrders.length === 1) {
      const activeOrder = riderOrders[0];
      Swal.fire({
        title: 'ยืนยันการยกเลิกออร์เดอร์?',
        text: `คุณต้องการยกเลิกออร์เดอร์ ${activeOrder.trackingCode} ที่ไรเดอร์กำลังจัดส่งใช่หรือไม่?`,
        icon: 'warning',
        showCancelButton: true,
        confirmButtonColor: '#ef4444',
        cancelButtonColor: '#6b7280',
        confirmButtonText: '🛑 ยืนยันการยกเลิก',
        cancelButtonText: 'ยกเลิก'
      }).then((result) => {
        if (result.isConfirmed) {
          this.executeOrderCancellation([activeOrder]);
        }
      });
    } else {
      // Multiple orders: build choices dictionary for Swal inputOptions
      const inputOptions: { [key: string]: string } = {
        'all': 'ยกเลิกทั้งหมด (ทุกออเดอร์ที่พ่วงอยู่)'
      };
      riderOrders.forEach(o => {
        inputOptions[o.id!] = `ยกเลิกเฉพาะออเดอร์ ${o.trackingCode}`;
      });

      Swal.fire({
        title: 'เลือกออเดอร์ที่ต้องการยกเลิก',
        input: 'select',
        inputOptions: inputOptions,
        inputPlaceholder: 'เลือกตัวเลือกสำหรับการยกเลิก',
        showCancelButton: true,
        confirmButtonColor: '#ef4444',
        cancelButtonColor: '#6b7280',
        confirmButtonText: '🛑 ยืนยันการยกเลิก',
        cancelButtonText: 'ยกเลิก',
        inputValidator: (value) => {
          return new Promise((resolve) => {
            if (value) {
              resolve();
            } else {
              resolve('กรุณาเลือกตัวเลือกที่ต้องการยกเลิก');
            }
          });
        }
      }).then((result) => {
        if (result.isConfirmed) {
          const selectedValue = result.value;
          let ordersToCancel: OrderDto[] = [];
          if (selectedValue === 'all') {
            ordersToCancel = riderOrders;
          } else {
            const singleOrder = riderOrders.find(o => o.id === selectedValue);
            if (singleOrder) {
              ordersToCancel = [singleOrder];
            }
          }

          if (ordersToCancel.length > 0) {
            this.executeOrderCancellation(ordersToCancel);
          }
        }
      });
    }
  }

  private executeOrderCancellation(orders: OrderDto[]): void {
    Swal.fire({
      title: 'กำลังยกเลิกออร์เดอร์...',
      allowOutsideClick: false,
      didOpen: () => {
        Swal.showLoading();
      }
    });

    const cancelRequests = orders.map(o => this.orderService.cancelOrder(o.id!));
    forkJoin(cancelRequests).subscribe({
      next: () => {
        const codes = orders.map(o => o.trackingCode).join(', ');
        Swal.fire({
          title: 'สำเร็จ!',
          text: `ยกเลิกออร์เดอร์ ${codes} เรียบร้อยแล้ว`,
          icon: 'success',
          timer: 3000,
          showConfirmButton: false
        });

        // Only clear active selection if all of them are cancelled
        const riderId = orders[0].assignedRiderId;
        if (riderId) {
          const remainingRiderOrders = this.activeOrders.filter(o => o.assignedRiderId === riderId && !orders.some(canceled => canceled.id === o.id));
          if (remainingRiderOrders.length === 0) {
            this.assignedRiderId = null;
            this.activeOrder = null;
            if (this.pickupRouteLine) this.pickupRouteLine.remove();
            if (this.deliveryRouteLine) this.deliveryRouteLine.remove();
            this.pickupRouteLine = null;
            this.deliveryRouteLine = null;
          }
        }

        this.loadActiveOrders();
      },
      error: (err) => {
        console.error('Failed to cancel order(s):', err);
        Swal.fire({
          title: 'เกิดข้อผิดพลาด',
          text: err?.error?.message || 'ไม่สามารถยกเลิกออร์เดอร์ได้',
          icon: 'error',
          confirmButtonColor: '#ef4444'
        });
      }
    });
  }
}
