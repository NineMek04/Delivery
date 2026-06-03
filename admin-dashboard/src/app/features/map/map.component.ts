import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, inject, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
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
  public simAutoFollow = false;
  private activeShopMarker: L.Marker | null = null;
  private activeCustomerMarker: L.Marker | null = null;
  private pickupRouteLine: L.Polyline | null = null;
  private activeRadarCircle: L.Circle | null = null;
  private candidateMarkers: L.CircleMarker[] = [];
  private currentRiderRoutePolyline: L.Polyline | null = null;

  // Order Markers and Polylines
  private orderMarkers: L.Marker[] = [];
  private orderPolylines: L.Polyline[] = [];
  public selectedOrder: OrderDto | null = null;
  public showOrderDetailModal = false;
  public filterStatus: string = 'ALL';

  // ── คุณลักษณะระบบจัดออเดอร์พ่วงด้วยมือ (Manual Order Batching) ──
  public isBatchMode = false;
  public selectedOrderIdsForBatch: string[] = [];
  private activeOrders: OrderDto[] = [];
  private batchPolylines: L.Polyline[] = [];

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
    (window as any).angularComponentReference = {
      zone: this.zone,
      showRiderRoute: (id: string) => this.showRiderRoute(id)
    };
    
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


  }

  ngAfterViewInit(): void {
    this.initMap();
    this.draw.initializeMap(this.map);
    this.draw.markerType = 'dashboard';
    this.loadExistingShops();
    this.loadActiveOrders();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.trackingService.stopConnection();
    this.draw.stopAllAnimations();
    this.clearBatchLines();
    if (this.currentRiderRoutePolyline) {
      this.currentRiderRoutePolyline.remove();
    }
    if (this.map) {
      this.map.remove();
    }
    delete (window as any).angularComponentReference;
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
    // Deprecated in favor of this.trackingService.fetchInitialLocations()
  }

  private loadActiveOrders(): void {
    this.orderService.getAll(1, 100).subscribe({
      next: (orders) => {
        const active = orders.filter(o => !['COMPLETED', 'CANCELLED'].includes(o.status || ''));
        this.activeOrders = active;
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
      const isSelected = this.selectedOrderIdsForBatch.includes(order.id || '');

      // Draw pickup (Shop) if not drawn
      if (order.pickupLat && order.pickupLng) {
        const bg = isSelected ? '#f59e0b' : '#ea580c';
        const shadow = isSelected ? 'box-shadow: 0 0 10px #f59e0b, 0 0 5px #f59e0b;' : '';
        const scale = isSelected ? 'transform: scale(1.25);' : '';
        const pMarker = L.marker([order.pickupLat, order.pickupLng], {
          icon: L.divIcon({
            className: 'order-pickup-marker',
            html: `<div style="background:${bg};width:20px;height:20px;border-radius:50%;border:2px solid #fff;display:flex;align-items:center;justify-content:center;font-size:10px;${shadow}${scale}">🏪</div>`,
            iconSize: [20,20]
          })
        }).addTo(this.map);
        pMarker.on('click', () => {
          if (this.isBatchMode) {
            this.toggleOrderSelectionForBatch(order);
          } else {
            this.openOrderDetails(order);
          }
        });
        this.orderMarkers.push(pMarker);
      }
      
      // Draw dropoff (Customer)
      if (order.dropoffLat && order.dropoffLng) {
        const bg = isSelected ? '#f59e0b' : '#3b82f6';
        const shadow = isSelected ? 'box-shadow: 0 0 10px #f59e0b, 0 0 5px #f59e0b;' : '';
        const scale = isSelected ? 'transform: scale(1.25);' : '';
        const dMarker = L.marker([order.dropoffLat, order.dropoffLng], {
          icon: L.divIcon({
            className: 'order-dropoff-marker',
            html: `<div style="background:${bg};width:24px;height:24px;border-radius:50%;border:2px solid #fff;display:flex;align-items:center;justify-content:center;font-size:12px;${shadow}${scale}">🏠</div>`,
            iconSize: [24,24]
          })
        }).addTo(this.map);
        dMarker.on('click', () => {
          if (this.isBatchMode) {
            this.toggleOrderSelectionForBatch(order);
          } else {
            this.openOrderDetails(order);
          }
        });
        this.orderMarkers.push(dMarker);
      }

      // Draw polyline connecting pickup and dropoff
      if (order.pickupLat && order.pickupLng && order.dropoffLat && order.dropoffLng) {
        const color = isSelected ? '#f59e0b' : '#8b5cf6';
        const weight = isSelected ? 5 : 3;
        const poly = L.polyline(
          [[order.pickupLat, order.pickupLng], [order.dropoffLat, order.dropoffLng]], 
          { color: color, weight: weight, dashArray: '5, 10', opacity: 0.7 }
        ).addTo(this.map);
        this.orderPolylines.push(poly);
      }
    });

    if (this.isBatchMode) {
      this.drawBatchRouteLines();
    }
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
            <b>อัปเดต:</b> ${new Date(loc.timestamp).toLocaleTimeString()}<br>
            <b>ความเร็ว:</b> ${loc.speedKmh ? loc.speedKmh.toFixed(1) : 0} km/h<br>
            <b>เกาะถนน:</b> ${loc.isSnapped ? '✅ ใช่' : '❌ ไม่'}
          </span>
          <hr style="margin: 6px 0; border: 0; border-top: 1px solid #e5e7eb;">
          <button style="width: 100%; background: #3b82f6; color: white; border: none; padding: 4px; border-radius: 4px; font-size: 11px; cursor: pointer;" onclick="window.angularComponentReference.zone.run(() => { window.angularComponentReference.showRiderRoute('${loc.riderId}'); })">ดูเส้นทางย้อนหลัง</button>
        </div>
      `;

      if (marker) {
        // Animate marker smoothly with 300ms continuous gliding!
        this.draw.animateMarker(riderId, this.assignedRiderId, marker, next, loc.status, loc.isSnapped, () => {
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
        const created = L.marker(next, { icon: this.draw.createRiderIcon(riderId, this.assignedRiderId, 0, loc.status, loc.isSnapped) })
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
      // update map
      this.updateRiderList(this.trackingService.getRiderLocations());
    }
  }

  showRiderRoute(riderId: string): void {
    if (!riderId) return;
    const url = `/api/v1/rider-locations/${riderId}/history`;
    this.http.get<any>(url).subscribe({
      next: (response) => {
        if (response?.isSuccess && Array.isArray(response.data) && response.data.length > 0) {
          if (this.currentRiderRoutePolyline) {
            this.currentRiderRoutePolyline.remove();
          }
          const coords = response.data.map((pt: any) => L.latLng(pt.lat || pt.Lat, pt.lng || pt.Lng));
          this.currentRiderRoutePolyline = L.polyline(coords, {
            color: '#3b82f6',
            weight: 4,
            opacity: 0.8,
            dashArray: '5, 10'
          }).addTo(this.map);
          this.map.fitBounds(this.currentRiderRoutePolyline.getBounds(), { padding: [50, 50] });
          Swal.fire({
            title: 'Rider Route',
            text: `Showing history for rider ${riderId.substring(0, 6)}`,
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
      }
    });
  }

  // ── คุณลักษณะระบบจัดออเดอร์พ่วงด้วยมือ (Manual Order Batching Methods) ──

  toggleBatchMode(): void {
    this.isBatchMode = !this.isBatchMode;
    if (!this.isBatchMode) {
      this.selectedOrderIdsForBatch = [];
      this.clearBatchLines();
    } else {
      this.closeOrderDetails();
      Swal.fire({
        title: 'เปิดโหมดจัดออเดอร์พ่วงด้วยมือ',
        text: 'กรุณาคลิกเลือกจุดออเดอร์ (ร้านค้าหรือลูกค้า) เพื่อพ่วงงานเข้าด้วยกัน',
        icon: 'info',
        toast: true,
        position: 'top-end',
        timer: 3000,
        showConfirmButton: false
      });
    }
    this.drawOrdersOnMap(this.activeOrders);
  }

  toggleOrderSelectionForBatch(order: OrderDto): void {
    const orderId = order.id;
    if (!orderId) return;

    if (order.status !== 'CREATED' && order.status !== 'MATCHING') {
      Swal.fire({
        title: 'ไม่สามารถเลือกได้',
        text: `ออเดอร์ ${order.trackingCode} อยู่ในสถานะ ${order.status} ไม่สามารถนำมาจัดกลุ่มพ่วงได้`,
        icon: 'warning',
        confirmButtonColor: '#f59e0b'
      });
      return;
    }

    const index = this.selectedOrderIdsForBatch.indexOf(orderId);
    if (index > -1) {
      this.selectedOrderIdsForBatch.splice(index, 1);
    } else {
      this.selectedOrderIdsForBatch.push(orderId);
    }

    this.drawOrdersOnMap(this.activeOrders);
  }

  private drawBatchRouteLines(): void {
    this.clearBatchLines();

    if (this.selectedOrderIdsForBatch.length < 2) return;

    const locations: any[] = [];
    this.selectedOrderIdsForBatch.forEach(id => {
      const order = this.activeOrders.find(o => o.id === id);
      if (order) {
        if (order.pickupLat && order.pickupLng) {
          locations.push({
            id: `pickup_${order.id}`,
            lat: order.pickupLat,
            lng: order.pickupLng
          });
        }
        if (order.dropoffLat && order.dropoffLng) {
          locations.push({
            id: `dropoff_${order.id}`,
            lat: order.dropoffLat,
            lng: order.dropoffLng
          });
        }
      }
    });

    if (locations.length < 2) return;

    // Call AI Routing VRP sequence optimization
    this.routeService.optimizeRoute({
      locations: locations,
      num_vehicles: 1,
      depot: 0
    }).subscribe({
      next: (res) => {
        const optimized = res.value?.optimizedRoute || res.optimizedRoute;
        if (optimized && Array.isArray(optimized) && optimized.length >= 2) {
          // Fetch real road routing path from OSRM between optimized waypoints
          const coordsStr = optimized.map((wp: any) => `${wp.lng},${wp.lat}`).join(';');
          const osrmUrl = `https://router.project-osrm.org/route/v1/driving/${coordsStr}?overview=full&geometries=geojson`;
          
          this.http.get<any>(osrmUrl).subscribe({
            next: (routeRes) => {
              if (routeRes?.routes?.[0]?.geometry?.coordinates) {
                const roadCoords = routeRes.routes[0].geometry.coordinates.map((coord: [number, number]) => 
                  L.latLng(coord[1], coord[0])
                );
                
                const poly = L.polyline(roadCoords, {
                  color: '#f59e0b',
                  weight: 6,
                  opacity: 0.9
                }).addTo(this.map);
                this.batchPolylines.push(poly);
              } else {
                const fallbackCoords = optimized.map((wp: any) => L.latLng(wp.lat, wp.lng));
                const poly = L.polyline(fallbackCoords, {
                  color: '#f59e0b',
                  weight: 6,
                  dashArray: '8, 12',
                  opacity: 0.9
                }).addTo(this.map);
                this.batchPolylines.push(poly);
              }
            },
            error: (err) => {
              console.warn('Failed to fetch OSRM road coordinates, falling back to straight lines:', err);
              const fallbackCoords = optimized.map((wp: any) => L.latLng(wp.lat, wp.lng));
              const poly = L.polyline(fallbackCoords, {
                color: '#f59e0b',
                weight: 6,
                dashArray: '8, 12',
                opacity: 0.9
              }).addTo(this.map);
              this.batchPolylines.push(poly);
            }
          });
        }
      },
      error: (err) => {
        console.error('Failed to optimize VRP route:', err);
      }
    });
  }

  private clearBatchLines(): void {
    this.batchPolylines.forEach(p => p.remove());
    this.batchPolylines = [];
  }

  submitBatchDispatch(): void {
    if (this.selectedOrderIdsForBatch.length < 2) {
      Swal.fire({
        title: 'เลือกออเดอร์ไม่พอ',
        text: 'กรุณาเลือกออเดอร์อย่างน้อย 2 รายการเพื่อทำการจัดพ่วงงาน',
        icon: 'warning',
        confirmButtonColor: '#f59e0b'
      });
      return;
    }

    Swal.fire({
      title: 'ยืนยันส่งงานพ่วง?',
      text: `คุณต้องการมัดรวมออเดอร์ ${this.selectedOrderIdsForBatch.length} รายการและจัดส่งไปยังไรเดอร์ผ่านระบบ AI Dispatch หรือไม่?`,
      icon: 'question',
      showCancelButton: true,
      confirmButtonColor: '#f59e0b',
      cancelButtonColor: '#6b7280',
      confirmButtonText: '🚀 ยืนยันส่งงานพ่วง',
      cancelButtonText: 'ยกเลิก'
    }).then((result) => {
      if (result.isConfirmed) {
        Swal.fire({
          title: 'กำลังประมวลผล...',
          allowOutsideClick: false,
          didOpen: () => {
            Swal.showLoading();
          }
        });

        this.orderService.batchDispatch(this.selectedOrderIdsForBatch).subscribe({
          next: (res) => {
            Swal.fire({
              title: 'สำเร็จ!',
              text: 'มัดรวมออเดอร์และสั่งเริ่มค้นหาไรเดอร์แบบพ่วงงานเรียบร้อยแล้ว',
              icon: 'success',
              timer: 3000,
              showConfirmButton: false
            });
            this.selectedOrderIdsForBatch = [];
            this.isBatchMode = false;
            this.clearBatchLines();
            this.loadActiveOrders();
          },
          error: (err) => {
            console.error('Failed to submit batch dispatch:', err);
            Swal.fire({
              title: 'เกิดข้อผิดพลาด',
              text: err?.error?.message || 'ไม่สามารถเริ่มจัดส่งออเดอร์พ่วงได้',
              icon: 'error',
              confirmButtonColor: '#ef4444'
            });
          }
        });
      }
    });
  }
}
