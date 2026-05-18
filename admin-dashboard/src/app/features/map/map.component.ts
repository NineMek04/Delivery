import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { TrackingSignalRService, RiderLocationUpdate } from '../../core/services/tracking-signalr.service';
import { ShopService, ShopDto } from '../../core/services/shop.service';
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
    [5.5, 97.3],   // Southwest — ทิศตะวันตกเฉียงใต้ (สตูล/นราธิวาส)
    [20.5, 105.7]  // Northeast — ทิศตะวันออกเฉียงเหนือ (เชียงราย/อุบลราชธานี)
  ];

  private trackingService = inject(TrackingSignalRService);
  private shopService = inject(ShopService);
  private subscriptions: Subscription = new Subscription();

  public alerts: any[] = [];
  public riders: any[] = [];

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
    this.loadExistingShops();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.trackingService.stopConnection();
    if (this.map) {
      this.map.remove();
    }
  }

  private initMap(): void {
    // สร้างแผนที่พร้อมจำกัดขอบเขตเฉพาะประเทศไทย
    this.map = L.map(this.mapElement.nativeElement, {
      center: this.THAILAND_CENTER,
      zoom: 12,
      minZoom: 6,                        // ไม่ให้ซูมออกจนเห็นทั้งโลก
      maxZoom: 18,
      maxBounds: this.THAILAND_BOUNDS,    // จำกัดขอบเขตแผนที่
      maxBoundsViscosity: 1.0             // ป้องกันลากแผนที่หลุดนอกประเทศไทย
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 18
    }).addTo(this.map);

    // ดักฟังการคลิกแผนที่สำหรับโหมดลงทะเบียนร้านค้า
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
    // กลับไปศูนย์กลาง อุดรธานี
    this.map?.setView(this.THAILAND_CENTER, 12);
  }

  /** กดเพื่อซูมให้เห็นทั้งประเทศไทย */
  showFullThailand(): void {
    this.map?.fitBounds(this.THAILAND_BOUNDS);
  }

  // ── ระบบปักหมุดและสร้างร้านค้า (Shop Registration Logic) ──

  /** สลับโหมดการสร้างร้านค้า */
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

  /** เมื่อคลิกแผนที่เพื่อเลือกตำแหน่งสร้างร้าน */
  onMapClickForShop(lat: number, lng: number): void {
    // ลบหมุดจำลองเดิมออกถ้ามีอยู่
    if (this.tempShopMarker) {
      this.tempShopMarker.remove();
    }

    // สร้างหมุดจำลองสีเหลืองมีลูกเล่น animation กระดอน
    const tempIcon = L.divIcon({
      className: 'temp-shop-marker',
      html: `<div style="background-color: #f59e0b; width: 28px; height: 28px; border-radius: 50%; border: 3px dashed white; box-shadow: 0 4px 8px rgba(0,0,0,0.4); display: flex; align-items: center; justify-content: center; font-size: 12px; animation: bounce 0.6s infinite alternate;">📍</div>`,
      iconSize: [28, 28],
      iconAnchor: [14, 14]
    });

    this.tempShopMarker = L.marker([lat, lng], { icon: tempIcon }).addTo(this.map);

    // เตรียมฟอร์มสร้างร้านค้า
    this.newShop = {
      name: '',
      menuName: '',
      menuPrice: 60,
      lat: lat,
      lng: lng
    };

    this.showShopModal = true;
  }

  /** ยกเลิกการสร้างร้านค้ากลางคัน */
  cancelShopCreation(): void {
    if (this.tempShopMarker) {
      this.tempShopMarker.remove();
      this.tempShopMarker = null;
    }
    this.showShopModal = false;
  }

  /** บันทึกข้อมูลร้านค้าลงฐานข้อมูล PostGIS */
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
        // ลบหมุดชั่วคราวออก
        if (this.tempShopMarker) {
          this.tempShopMarker.remove();
          this.tempShopMarker = null;
        }

        // ปักหมุดร้านค้าของจริงลงแผนที่
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

  /** โหลดร้านค้าทั้งหมดที่มีอยู่ในฐานข้อมูลเพื่อมาแสดง */
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

  /** นำหมุดร้านค้าไปวาดบนแผนที่ */
  private addShopToMap(shop: ShopDto): void {
    if (!this.map || !shop.lat || !shop.lng) return;

    // ไอคอนร้านค้าสีส้มอิฐพรีเมียม
    const shopIcon = L.divIcon({
      className: 'custom-shop-marker',
      html: `<div style="background-color: #ea580c; width: 26px; height: 26px; border-radius: 50%; border: 3px solid white; box-shadow: 0 3px 6px rgba(0,0,0,0.4); display: flex; align-items: center; justify-content: center; font-size: 11px; color: white;">🏪</div>`,
      iconSize: [26, 26],
      iconAnchor: [13, 13]
    });

    const popupContent = `
      <div style="font-family: 'Inter', sans-serif; min-width: 180px;">
        <h4 style="margin: 0 0 6px; font-weight: 700; color: #ea580c; font-size: 13px;">🏪 ${shop.name}</h4>
        <div style="font-size: 11px; color: #d1d5db; line-height: 1.5;">
          <b>เมนูแนะนำ:</b> ${shop.menuName}<br>
          <b>ราคา:</b> <span style="font-weight: 800; color: #10b981;">${shop.menuPrice} บาท</span>
        </div>
      </div>
    `;

    // Tooltip แสดงชื่อร้านค้าเวลานำเมาส์ไปชี้ (Hover)
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

  private updateMapMarkers(locationMap: Map<string, RiderLocationUpdate>): void {
    if (!this.map) return;

    locationMap.forEach((loc, riderId) => {
      let marker = this.markers.get(riderId);

      const popupContent = `
        <div style="font-family: 'Inter', sans-serif;">
          <strong>Rider: ${riderId.substring(0, 8)}...</strong><br>
          Status: <b>${loc.status}</b><br>
          Updated: ${new Date(loc.timestamp).toLocaleTimeString()}
        </div>
      `;

      if (marker) {
        marker.setLatLng([loc.latitude, loc.longitude]);
        marker.setPopupContent(popupContent);
      } else {
        const customIcon = L.divIcon({
          className: 'custom-rider-marker',
          html: `<div style="background-color: ${loc.status === 'IDLE' ? '#22c55e' : '#3b82f6'}; width: 24px; height: 24px; border-radius: 50%; border: 3px solid white; box-shadow: 0 2px 4px rgba(0,0,0,0.3);"></div>`,
          iconSize: [24, 24],
          iconAnchor: [12, 12]
        });

        marker = L.marker([loc.latitude, loc.longitude], { icon: customIcon })
          .bindPopup(popupContent)
          .addTo(this.map);

        this.markers.set(riderId, marker);
      }
    });
  }

  private updateRiderList(locationMap: Map<string, RiderLocationUpdate>): void {
    const list: any[] = [];
    locationMap.forEach((loc, riderId) => {
      list.push({
        name: `Rider ${riderId.substring(0, 5)}`,
        id: riderId,
        battery: 'N/A',
        signal: 'Strong',
        status: loc.status,
        avatar: loc.status.charAt(0),
        tone: loc.status === 'IDLE' ? 'online' : (loc.status === 'DELIVERING' || loc.status === 'PICKING_UP' ? 'busy' : 'low')
      });
    });
    this.riders = list;
  }
}
