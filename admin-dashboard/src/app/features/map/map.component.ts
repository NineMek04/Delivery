import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as L from 'leaflet';
import { TrackingSignalRService, RiderLocationUpdate } from '../../core/services/tracking-signalr.service';
import { Subscription } from 'rxjs';

// Fix Leaflet icons issue
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
  imports: [CommonModule],
  templateUrl: './map.component.html',
  styleUrl: './map.component.scss'
})
export class MapComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('mapElement', { static: true }) mapElement!: ElementRef;
  readonly title = 'Live Fleet Map';

  private map!: L.Map;
  private markers: Map<string, L.Marker> = new Map();

  // ── ขอบเขตแผนที่ประเทศไทย (Thailand Bounding Box) ──
  private readonly THAILAND_CENTER: L.LatLngTuple = [13.7563, 100.5018]; // กรุงเทพฯ
  private readonly THAILAND_BOUNDS: L.LatLngBoundsExpression = [
    [5.5, 97.3],   // Southwest — ทิศตะวันตกเฉียงใต้ (สตูล/นราธิวาส)
    [20.5, 105.7]  // Northeast — ทิศตะวันออกเฉียงเหนือ (เชียงราย/อุบลราชธานี)
  ];

  private trackingService = inject(TrackingSignalRService);
  private subscriptions: Subscription = new Subscription();

  public alerts: any[] = [];
  public riders: any[] = [];

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
  }

  zoomIn(): void {
    this.map?.zoomIn();
  }

  zoomOut(): void {
    this.map?.zoomOut();
  }

  recenter(): void {
    // กลับไปศูนย์กลาง กรุงเทพฯ พร้อม Zoom ระดับเมือง
    this.map?.setView(this.THAILAND_CENTER, 12);
  }

  /** กดเพื่อซูมให้เห็นทั้งประเทศไทย */
  showFullThailand(): void {
    this.map?.fitBounds(this.THAILAND_BOUNDS);
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
