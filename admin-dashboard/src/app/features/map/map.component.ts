import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { DispatchScanStarted, TrackingSignalRService, RiderLocationUpdate } from '../../core/services/tracking-signalr.service';
import { ShopService, ShopDto } from '../../core/services/shop.service';
import { RiderService } from '../../core/services/rider.service';
import { OrderService } from '../../core/services/order.service';
import { Subscription } from 'rxjs';
import Swal from 'sweetalert2';
import { OrderDetailComponent } from '../orders/order-detail/order-detail.component';
import { OrderDto } from '../../api/generated/model/order-dto';

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
  private subscriptions: Subscription = new Subscription();

  public alerts: any[] = [];
  public riders: any[] = [];

  // ── Active Order & Dynamic VRP Routing Details ──
  public activeOrder: any = null;
  public assignedRiderId: string | null = null;
  public simAutoFollow = false;
  private activeShopMarker: L.Marker | null = null;
  private activeCustomerMarker: L.Marker | null = null;
  private pickupRouteLine: L.Polyline | null = null;
  private activeRadarCircle: L.Circle | null = null;
  private candidateMarkers: L.CircleMarker[] = [];

  // Order Markers and Polylines
  private orderMarkers: L.Marker[] = [];
  private orderPolylines: L.Polyline[] = [];
  public selectedOrder: OrderDto | null = null;
  public showOrderDetailModal = false;
  public filterStatus: string = 'ALL';

  // ── คุณลักษณะระบบร้านค้า (Shop Registration Features) ──
  public isAddShopMode = false;
  public showShopModal = false;
  public newShop = {
    name: '',
    menuName: '',
    menuPrice: 60,
    lat: 0,
    lng: 0
  };
  private shopMarkers: Map<string, L.Marker> = new Map();
  private tempShopMarker: L.Marker | null = null;

  get availableCount(): number {
    return this.riders.filter(rider => ['IDLE', 'AVAILABLE'].includes(rider.status)).length;
  }

  get deliveringCount(): number {
    return this.riders.filter(rider => ['DELIVERING', 'PICKING_UP', 'BUSY'].includes(rider.status)).length;
  }

  get offlineCount(): number {
    return this.riders.filter(rider => ['OFFLINE', 'LOW'].includes(rider.status)).length;
  }

  ngOnInit(): void {
    this.trackingService.startConnection();

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


  }

  ngAfterViewInit(): void {
    this.initMap();
    this.draw.initializeMap(this.map);
    this.draw.markerType = 'dashboard';
    this.loadExistingShops();
    this.loadExistingRiders();
    this.loadActiveOrders();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.trackingService.stopConnection();
    if (this.map) {
      this.map.remove();
    }
  }

  private initMap(): void {
    this.map = L.map(this.mapElement.nativeElement, {
      center: this.THAILAND_CENTER,
      zoom: 12,
      minZoom: 6,
      maxZoom: 18,
      maxBounds: this.THAILAND_BOUNDS,
      maxBoundsViscosity: 1.0
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 18
    }).addTo(this.map);

    this.map.on('click', (e: L.LeafletMouseEvent) => {
      if (this.isAddShopMode) {
        this.onMapClickForShop(e.latlng.lat, e.latlng.lng);
      }
    });
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

  showFullThailand(): void {
    this.map?.fitBounds(this.THAILAND_BOUNDS);
  }

  toggleSimAutoFollow(): void {
    this.simAutoFollow = !this.simAutoFollow;
    console.log('Live Map: Simulation Auto-Follow toggled to', this.simAutoFollow);
    if (this.simAutoFollow && this.assignedRiderId) {
      const riderMarker = this.markers.get(this.assignedRiderId);
      if (riderMarker && this.activeOrder) {
        const bounds = L.latLngBounds([
          riderMarker.getLatLng(),
          [this.activeOrder.pickupLat, this.activeOrder.pickupLng]
        ]);
        this.map.fitBounds(bounds, { padding: [80, 80], maxZoom: 16 });
      }
    }
  }

  private decodePolyline(str: string): L.LatLng[] {
    return this.math.decodeRoute(str);
  }

  // ── ระบบปักหมุดและสร้างร้านค้า (Shop Registration Logic) ──

  toggleAddShopMode(): void {
    this.isAddShopMode = !this.isAddShopMode;
    if (!this.isAddShopMode) {
      this.cancelShopCreation();
    } else {
      Swal.fire({
        title: 'เปิดโหมดปักหมุดร้านค้า',
        text: 'กรุณาคลิกเลือกพิกัดร้านค้าบนแผนที่จุดใดก็ได้',
        icon: 'info',
        toast: true,
        position: 'top-end',
        timer: 3000,
        showConfirmButton: false
      });
    }
  }

  onMapClickForShop(lat: number, lng: number): void {
    if (this.tempShopMarker) {
      this.tempShopMarker.remove();
    }

    const tempIcon = L.divIcon({
      className: 'temp-shop-marker',
      html: `<div style="background-color: #f59e0b; width: 28px; height: 28px; border-radius: 50%; border: 3px dashed white; box-shadow: 0 4px 8px rgba(0,0,0,0.4); display: flex; align-items: center; justify-content: center; font-size: 12px; animation: bounce 0.6s infinite alternate;">📍</div>`,
      iconSize: [28, 28],
      iconAnchor: [14, 14]
    });

    this.tempShopMarker = L.marker([lat, lng], { icon: tempIcon }).addTo(this.map);

    this.newShop = {
      name: '',
      menuName: '',
      menuPrice: 60,
      lat: lat,
      lng: lng
    };

    this.showShopModal = true;
  }

  cancelShopCreation(): void {
    if (this.tempShopMarker) {
      this.tempShopMarker.remove();
      this.tempShopMarker = null;
    }
    this.showShopModal = false;
  }

  saveShop(event: Event): void {
    event.preventDefault();

    if (!this.newShop.name.trim() || !this.newShop.menuName.trim() || this.newShop.menuPrice <= 0) {
      Swal.fire({
        title: 'ข้อมูลไม่ครบถ้วน',
        text: 'กรุณากรอกข้อมูลและกำหนดราคาเมนูให้ถูกต้อง',
        icon: 'warning',
        confirmButtonColor: '#f59e0b'
      });
      return;
    }

    this.shopService.create(this.newShop).subscribe({
      next: (savedShop) => {
        if (this.tempShopMarker) {
          this.tempShopMarker.remove();
          this.tempShopMarker = null;
        }

        this.addShopToMap(savedShop);
        this.showShopModal = false;
        this.isAddShopMode = false;

        Swal.fire({
          title: 'สำเร็จ!',
          text: `ร้าน "${savedShop.name}" ได้รับการบันทึกลงระบบ PostGIS และแสดงผลถาวรแล้ว`,
          icon: 'success',
          timer: 3000,
          timerProgressBar: true,
          showConfirmButton: false
        });
      },
      error: (err) => {
        console.error('Failed to create shop:', err);
        Swal.fire({
          title: 'เกิดข้อผิดพลาด',
          text: 'ไม่สามารถเชื่อมต่อฐานข้อมูลได้ กรุณาลองใหม่อีกครั้ง',
          icon: 'error',
          confirmButtonColor: '#ef4444'
        });
      }
    });
  }

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

  private loadExistingRiders(): void {
    this.riderService.getByEndpoint('/rider-locations').subscribe({
      next: (res) => {
        const riders = res?.value || [];
        const initialMap = new Map<string, RiderLocationUpdate>();
        
        riders.forEach((rider: any) => {
          if (rider.lat != null && rider.lng != null && rider.riderId) {
            // Use snapped location if available
            const lat = rider.isSnapped ? rider.snappedLat : rider.lat;
            const lng = rider.isSnapped ? rider.snappedLng : rider.lng;
            
            initialMap.set(rider.riderId, {
              riderId: rider.riderId,
              latitude: lat,
              longitude: lng,
              status: rider.status || 'OFFLINE',
              timestamp: rider.updatedAt || new Date().toISOString()
            });
          }
        });
        
        this.updateMapMarkers(initialMap);
        this.updateRiderList(initialMap);
      },
      error: (err) => {
        console.error('Failed to load existing mock riders from Redis:', err);
      }
    });
  }

  private loadActiveOrders(): void {
    this.orderService.getAll(1, 100).subscribe({
      next: (orders) => {
        const active = orders.filter(o => !['COMPLETED', 'CANCELLED'].includes(o.status || ''));
        this.drawOrdersOnMap(active);
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
            html: `<div style="background:#ea580c;width:20px;height:20px;border-radius:50%;border:2px solid #fff;display:flex;align-items:center;justify-content:center;font-size:10px;">🏪</div>`,
            iconSize: [20,20]
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
            html: `<div style="background:#3b82f6;width:24px;height:24px;border-radius:50%;border:2px solid #fff;display:flex;align-items:center;justify-content:center;font-size:12px;">🏠</div>`,
            iconSize: [24,24]
          })
        }).addTo(this.map);
        dMarker.on('click', () => this.openOrderDetails(order));
        this.orderMarkers.push(dMarker);
      }

      // Draw polyline connecting pickup and dropoff
      if (order.pickupLat && order.pickupLng && order.dropoffLat && order.dropoffLng) {
        const poly = L.polyline(
          [[order.pickupLat, order.pickupLng], [order.dropoffLat, order.dropoffLng]], 
          { color: '#8b5cf6', weight: 3, dashArray: '5, 10', opacity: 0.7 }
        ).addTo(this.map);
        this.orderPolylines.push(poly);
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

  private addShopToMap(shop: ShopDto): void {
    if (!this.map || !shop.lat || !shop.lng) return;

    const shopIcon = L.divIcon({
      className: 'custom-shop-marker',
      html: `<div style="background-color: #ea580c; width: 26px; height: 26px; border-radius: 50%; border: 3px solid white; box-shadow: 0 3px 6px rgba(0,0,0,0.4); display: flex; align-items: center; justify-content: center; font-size: 11px; color: white;">🏪</div>`,
      iconSize: [26, 26],
      iconAnchor: [13, 13]
    });

    const popupContent = `
      <div style="font-family: 'Inter', sans-serif; min-width: 180px;">
        <h4 style="margin: 0 0 6px; font-weight: 700; color: #ea580c; font-size: 13px;">🏪 ${shop.name}</h4>
        <div style="font-size: 11px; color: #4b5563; line-height: 1.5;">
          <b>เมนูแนะนำ:</b> ${shop.menuName}<br>
          <b>ราคา:</b> <span style="font-weight: 800; color: #10b981;">${shop.menuPrice} บาท</span>
        </div>
      </div>
    `;

    const marker = L.marker([shop.lat, shop.lng], { icon: shopIcon })
      .bindTooltip(shop.name, {
        permanent: false,
        direction: 'top',
        className: 'custom-shop-tooltip',
        offset: [0, -10]
      })
      .bindPopup(popupContent)
      .addTo(this.map);

    this.shopMarkers.set(shop.id || '', marker);
  }

  // ── Real-time Dispatch Event Handling ──

  private updateMapMarkers(locationMap: Map<string, RiderLocationUpdate>): void {
    if (!this.map) return;

    locationMap.forEach((loc, riderId) => {
      let marker = this.draw.markerMap.get(riderId);
      const isWinner = riderId === this.assignedRiderId;
      const next = L.latLng(loc.latitude, loc.longitude);

      const popupContent = `
        <div style="font-family: 'Inter', sans-serif; min-width: 150px;">
          <strong style="color: ${isWinner ? '#2563eb' : '#374151'}; font-size: 13px;">🛵 RID-${loc.riderId.substring(0, 6).toUpperCase()}</strong><br>
          <hr style="margin: 6px 0; border: 0; border-top: 1px solid #e5e7eb;">
          <span style="font-size: 11px; color: #4b5563; line-height: 1.5;">
            <b>สถานะ:</b> ${loc.status}<br>
            <b>พิกัด:</b> ${loc.latitude.toFixed(5)}, ${loc.longitude.toFixed(5)}<br>
            <b>อัปเดต:</b> ${new Date(loc.timestamp).toLocaleTimeString()}
          </span>
        </div>
      `;

      if (marker) {
        // Animate marker smoothly with 300ms continuous gliding!
        this.draw.animateMarker(riderId, this.assignedRiderId, marker, next, loc.status, () => {
          if (this.simAutoFollow && isWinner && this.activeOrder) {
            const bounds = L.latLngBounds([
              next,
              [this.activeOrder.pickupLat, this.activeOrder.pickupLng]
            ]);
            this.map.fitBounds(bounds, { padding: [80, 80], maxZoom: 16 });
          }
        });
        marker.setPopupContent(popupContent);
      } else {
        const created = L.marker(next, { icon: this.draw.createRiderIcon(riderId, this.assignedRiderId, 0, loc.status) })
          .bindPopup(popupContent)
          .addTo(this.map);

        this.draw.markerMap.set(riderId, created);
        this.draw.markerPositions.set(riderId, next);
      }
    });
  }

  private updateRiderList(locationMap: Map<string, RiderLocationUpdate>): void {
    const list: any[] = [];
    locationMap.forEach((loc, riderId) => {
      if (this.filterStatus !== 'ALL' && loc.status !== this.filterStatus) return;

      const isWinner = riderId === this.assignedRiderId;
      list.push({
        name: `Rider ${riderId.substring(0, 5).toUpperCase()}`,
        id: riderId,
        battery: isWinner ? '98%' : '84%',
        signal: 'Strong',
        status: isWinner ? 'DISPATCHED' : loc.status,
        avatar: isWinner ? '🏆' : loc.status.charAt(0),
        tone: loc.status === 'IDLE' ? 'online' : (isWinner || loc.status === 'DELIVERING' || loc.status === 'PICKING_UP' ? 'busy' : 'low')
      });
    });
    this.riders = list;
  }

  setFilterStatus(status: string): void {
    this.filterStatus = status;
    // trigger update
    if (this.riders.length > 0) {
      // re-trigger the map
      this.loadExistingRiders();
    }
  }
}
