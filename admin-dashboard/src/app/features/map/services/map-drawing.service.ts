import { Injectable, OnDestroy } from '@angular/core';
import * as L from 'leaflet';
import { MapMathService } from './map-math.service';

@Injectable()
export class MapDrawingService implements OnDestroy {
  private mapInstance?: L.Map;
  
  public markerMap = new Map<string, L.Marker>();
  public markerPositions = new Map<string, L.LatLng>();
  public markerAnimations = new Map<string, number>();
  public routeLines: { pickup?: L.Polyline; delivery?: L.Polyline; completed?: L.Polyline } = {};
  public activeMarkers: { shop?: L.Marker; dropoff?: L.Marker } = {};
  public radarCircle?: L.Circle;
  public candidateMarkers: L.CircleMarker[] = [];

  constructor(private math: MapMathService) {}

  public initializeMap(map: L.Map) {
    this.mapInstance = map;
  }

  ngOnDestroy(): void {
    this.markerAnimations.forEach(frame => cancelAnimationFrame(frame));
    this.clearActiveLayers();
    this.mapInstance = undefined;
  }

  public get map(): L.Map | undefined {
    return this.mapInstance;
  }

  public createRiderIcon(riderId: string, assignedRiderId: string | null, bearing = 0): L.DivIcon {
    const winner = riderId === assignedRiderId ? ' winner' : '';
    return L.divIcon({
      className: 'sim-marker',
      html: `<div class="sim-marker-core${winner}" style="transform: rotate(${bearing}deg)">R</div>`,
      iconSize: [34, 34],
      iconAnchor: [17, 17]
    });
  }

  public createStaticMarker(latLng: L.LatLng, label: string, tone: 'shop' | 'dropoff'): L.Marker {
    return L.marker(latLng, {
      icon: L.divIcon({
        className: 'sim-marker',
        html: `<div class="sim-marker-core ${tone}">${label}</div>`,
        iconSize: [36, 36],
        iconAnchor: [18, 18]
      })
    });
  }

  public animateMarker(riderId: string, assignedRiderId: string | null, marker: L.Marker, next: L.LatLng, onComplete?: () => void): void {
    const previous = this.markerPositions.get(riderId) || marker.getLatLng();
    const distance = previous.distanceTo(next);
    if (distance < 1) return;

    const existingFrame = this.markerAnimations.get(riderId);
    if (existingFrame) cancelAnimationFrame(existingFrame);

    const duration = Math.min(950, Math.max(260, distance * 14));
    const startedAt = performance.now();
    const bearing = this.math.calculateBearing(previous, next);

    const tick = (now: number) => {
      const t = Math.min(1, (now - startedAt) / duration);
      const eased = 1 - Math.pow(1 - t, 3);
      const lat = previous.lat + (next.lat - previous.lat) * eased;
      const lng = previous.lng + (next.lng - previous.lng) * eased;
      marker.setLatLng([lat, lng]);
      marker.setIcon(this.createRiderIcon(riderId, assignedRiderId, bearing));

      if (t < 1) {
        this.markerAnimations.set(riderId, requestAnimationFrame(tick));
      } else {
        this.markerPositions.set(riderId, next);
        this.markerAnimations.delete(riderId);
        if (onComplete) onComplete();
      }
    };

    this.markerAnimations.set(riderId, requestAnimationFrame(tick));
  }

  public refreshMarkerIcons(assignedRiderId: string | null): void {
    this.markerMap.forEach((marker, riderId) => marker.setIcon(this.createRiderIcon(riderId, assignedRiderId)));
  }

  public drawOfferRoutes(pickupCoords: L.LatLng[], deliveryCoords: L.LatLng[], defaultPickup: L.LatLng | null, defaultDropoff: L.LatLng | null): void {
    if (!this.mapInstance) return;
    this.routeLines.pickup?.remove();
    this.routeLines.delivery?.remove();

    if (pickupCoords.length) {
      this.routeLines.pickup = L.polyline(pickupCoords, {
        color: '#ef4444',
        weight: 5,
        opacity: 0.9,
        dashArray: '10, 10'
      }).addTo(this.mapInstance);
    }

    const finalDeliveryCoords = deliveryCoords.length
      ? deliveryCoords
      : defaultPickup && defaultDropoff ? [defaultPickup, defaultDropoff] : [];

    if (finalDeliveryCoords.length) {
      this.routeLines.delivery = L.polyline(finalDeliveryCoords, {
        color: '#22c55e',
        weight: 5,
        opacity: 0.78
      }).addTo(this.mapInstance);
    }
  }

  public clearCandidateMarkers(): void {
    this.candidateMarkers.forEach(marker => marker.remove());
    this.candidateMarkers = [];
  }

  public clearActiveLayers(): void {
    this.radarCircle?.remove();
    this.routeLines.pickup?.remove();
    this.routeLines.delivery?.remove();
    this.routeLines.completed?.remove();
    this.activeMarkers.shop?.remove();
    this.activeMarkers.dropoff?.remove();
    this.clearCandidateMarkers();
    
    this.routeLines = {};
    this.activeMarkers = {};
  }
}
