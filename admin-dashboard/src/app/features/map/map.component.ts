import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { TrackingSignalRService, RiderLocationUpdate } from '../../core/services/tracking-signalr.service';
import { ShopService, ShopDto } from '../../core/services/shop.service';
import { RiderService } from '../../core/services/rider.service';
import { Subscription } from 'rxjs';
import Swal from 'sweetalert2';

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

@Component({
  selector: 'app-map',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
  private subscriptions: Subscription = new Subscription();

  public alerts: any[] = [];
  public riders: any[] = [];

  // ── Active Order & Dynamic VRP Routing Details ──
  public activeOrder: any = null;
  public assignedRiderId: string | null = null;
  private activeShopMarker: L.Marker | null = null;
  private activeCustomerMarker: L.Marker | null = null;
  private pickupRouteLine: L.Polyline | null = null;
  private deliveryRouteLine: L.Polyline | null = null;
  private activeRadarCircle: L.Circle | null = null;

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

    // Dynamic AI Dispatch Events Subscriptions
    this.subscriptions.add(
      this.trackingService.offerReceived$.subscribe(offer => {
        this.handleOfferReceived(offer);
      })
    );

    this.subscriptions.add(
      this.trackingService.orderAssigned$.subscribe(data => {
        this.handleOrderAssigned(data);
      })
    );

    this.subscriptions.add(
      this.trackingService.orderStatusChanged$.subscribe(data => {
        this.handleOrderStatusChanged(data);
      })
    );
  }

  ngAfterViewInit(): void {
    this.initMap();
    this.loadExistingShops();
    this.loadExistingRiders();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.trackingService.stopConnection();
    this.clearActiveOrderLayers();
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
    this.riderService.getAll(1, 150).subscribe({
      next: (riders) => {
        const initialMap = new Map<string, RiderLocationUpdate>();
        
        riders.forEach(rider => {
          if (rider.lat != null && rider.lng != null && rider.id) {
            initialMap.set(rider.id, {
              riderId: rider.id,
              latitude: rider.lat,
              longitude: rider.lng,
              status: rider.status || 'OFFLINE',
              timestamp: rider.lastUpdated || new Date().toISOString()
            });
          }
        });
        
        this.updateMapMarkers(initialMap);
        this.updateRiderList(initialMap);
      },
      error: (err) => {
        console.error('Failed to load existing mock riders:', err);
      }
    });
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

  private handleOfferReceived(offer: any): void {
    console.log('Live Map: Offer received', offer);
    this.clearActiveOrderLayers();
    this.activeOrder = offer.order;
    this.assignedRiderId = offer.riderId || offer.order?.assignedRiderId || null;

    const pickupLat = offer.order?.pickupLat;
    const pickupLng = offer.order?.pickupLng;
    const dropoffLat = offer.order?.dropoffLat;
    const dropoffLng = offer.order?.dropoffLng;

    if (!pickupLat || !pickupLng || !dropoffLat || !dropoffLng) return;

    // 1. Auto-focus bounds
    const points: L.LatLngExpression[] = [[pickupLat, pickupLng], [dropoffLat, dropoffLng]];
    
    let riderPos: L.LatLngExpression | null = null;
    if (this.assignedRiderId) {
      const riderMarker = this.markers.get(this.assignedRiderId);
      if (riderMarker) {
        riderPos = riderMarker.getLatLng();
        points.push(riderPos);
      }
    }
    
    this.map.fitBounds(L.latLngBounds(points), { padding: [80, 80] });

    // 2. Add glowing active markers
    const activeShopIcon = L.divIcon({
      className: 'active-shop-marker',
      html: `<div style="background-color: #ea580c; width: 34px; height: 34px; border-radius: 50%; border: 3px solid white; box-shadow: 0 0 15px rgba(234,88,12,0.8); display: flex; align-items: center; justify-content: center; font-size: 16px; animation: bounce 0.6s infinite alternate;">🏪</div>`,
      iconSize: [34, 34],
      iconAnchor: [17, 17]
    });
    this.activeShopMarker = L.marker([pickupLat, pickupLng], { icon: activeShopIcon })
      .bindPopup(`<b>🏪 ร้านคู่ค้าที่ AI เลือก (จุดรับ):</b><br>พิกัด: ${pickupLat.toFixed(5)}, ${pickupLng.toFixed(5)}`)
      .addTo(this.map);

    const activeCustomerIcon = L.divIcon({
      className: 'active-customer-marker',
      html: `<div style="background-color: #10b981; width: 34px; height: 34px; border-radius: 50%; border: 3px solid white; box-shadow: 0 0 15px rgba(16,185,129,0.8); display: flex; align-items: center; justify-content: center; font-size: 16px;">🏁</div>`,
      iconSize: [34, 34],
      iconAnchor: [17, 17]
    });
    this.activeCustomerMarker = L.marker([dropoffLat, dropoffLng], { icon: activeCustomerIcon })
      .bindPopup(`<b>🏁 จุดจัดส่งของลูกค้า:</b><br>พิกัด: ${dropoffLat.toFixed(5)}, ${dropoffLng.toFixed(5)}`)
      .addTo(this.map);

    // 3. Add pulsing AI Radar ring around Shop
    this.activeRadarCircle = L.circle([pickupLat, pickupLng], {
      radius: 600,
      color: '#3b82f6',
      fillColor: '#3b82f6',
      fillOpacity: 0.1,
      className: 'ai-radar-pulse'
    }).addTo(this.map);

    // 4. Plot paths
    // Dash Red (Rider -> Shop)
    if (riderPos) {
      this.pickupRouteLine = L.polyline([riderPos, [pickupLat, pickupLng]], {
        color: '#ef4444',
        weight: 4,
        dashArray: '8, 8',
        className: 'route-pickup-line'
      }).addTo(this.map);
    }

    // Solid Emerald Green (Shop -> Customer)
    this.deliveryRouteLine = L.polyline([[pickupLat, pickupLng], [dropoffLat, dropoffLng]], {
      color: '#10b981',
      weight: 5,
      className: 'route-delivery-line'
    }).addTo(this.map);

    // Force marker update to highlight targeted rider
    const currentLocs = this.trackingService.getRiderLocations();
    this.updateMapMarkers(currentLocs);
  }

  private handleOrderAssigned(data: any): void {
    console.log('Live Map: Order assigned to rider', data);
    this.assignedRiderId = data.riderId;

    if (this.activeRadarCircle) {
      this.activeRadarCircle.remove();
      this.activeRadarCircle = null;
    }

    const currentLocs = this.trackingService.getRiderLocations();
    this.updateMapMarkers(currentLocs);
  }

  private handleOrderStatusChanged(data: any): void {
    console.log('Live Map: Order status changed', data);
    
    if (data.status === 'PICKING_UP' || data.status === 'DELIVERING') {
      if (this.pickupRouteLine) {
        this.pickupRouteLine.remove();
        this.pickupRouteLine = null;
      }
    } else if (data.status === 'COMPLETED') {
      // Clear layers after 6 seconds so user can witness completion
      setTimeout(() => {
        this.clearActiveOrderLayers();
        const currentLocs = this.trackingService.getRiderLocations();
        this.updateMapMarkers(currentLocs);
      }, 6000);
    }
  }

  private clearActiveOrderLayers(): void {
    if (this.activeShopMarker) {
      this.activeShopMarker.remove();
      this.activeShopMarker = null;
    }
    if (this.activeCustomerMarker) {
      this.activeCustomerMarker.remove();
      this.activeCustomerMarker = null;
    }
    if (this.pickupRouteLine) {
      this.pickupRouteLine.remove();
      this.pickupRouteLine = null;
    }
    if (this.deliveryRouteLine) {
      this.deliveryRouteLine.remove();
      this.deliveryRouteLine = null;
    }
    if (this.activeRadarCircle) {
      this.activeRadarCircle.remove();
      this.activeRadarCircle = null;
    }
    this.activeOrder = null;
    this.assignedRiderId = null;
  }

  private updateMapMarkers(locationMap: Map<string, RiderLocationUpdate>): void {
    if (!this.map) return;

    locationMap.forEach((loc, riderId) => {
      let marker = this.markers.get(riderId);
      const isWinner = riderId === this.assignedRiderId;

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

      // Define color and styling based on active order Winner status
      const color = loc.status === 'IDLE' ? '#22c55e' : (isWinner ? '#2563eb' : '#f59e0b');
      const border = isWinner ? '3px solid #ffde21' : '3px solid white';
      const shadow = isWinner ? '0 0 15px rgba(37,99,235,0.8)' : '0 2px 5px rgba(0,0,0,0.3)';
      const animationClass = isWinner ? 'winner-pulse-marker' : '';

      const customIcon = L.divIcon({
        className: `custom-rider-marker-div ${animationClass}`,
        html: `<div style="background-color: ${color}; width: 28px; height: 28px; border-radius: 50%; border: ${border}; box-shadow: ${shadow}; display: flex; align-items: center; justify-content: center; font-size: 14px; color: white; transition: all 0.3s ease;">🛵</div>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14]
      });

      if (marker) {
        marker.setLatLng([loc.latitude, loc.longitude]);
        marker.setIcon(customIcon);
        marker.setPopupContent(popupContent);
      } else {
        marker = L.marker([loc.latitude, loc.longitude], { icon: customIcon })
          .bindPopup(popupContent)
          .addTo(this.map);

        this.markers.set(riderId, marker);
      }

      // Dynamically shorten pickup polyline as the winner rider moves
      if (this.activeOrder && this.pickupRouteLine && isWinner) {
        this.pickupRouteLine.setLatLngs([
          [loc.latitude, loc.longitude],
          [this.activeOrder.pickupLat, this.activeOrder.pickupLng]
        ]);
      }
    });
  }

  private updateRiderList(locationMap: Map<string, RiderLocationUpdate>): void {
    const list: any[] = [];
    locationMap.forEach((loc, riderId) => {
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
}
