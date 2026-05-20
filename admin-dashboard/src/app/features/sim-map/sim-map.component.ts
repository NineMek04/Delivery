import { AfterViewInit, Component, ElementRef, OnDestroy, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as L from 'leaflet';
import { Subscription } from 'rxjs';
import { DispatchScanStarted, RiderLocationUpdate, TrackingSignalRService } from '../../core/services/tracking-signalr.service';

type FlowPhase = 'idle' | 'scan' | 'offer' | 'assigned' | 'pickup' | 'delivery' | 'completed';

interface TimelineItem {
  id: number;
  title: string;
  text: string;
  time: string;
  tone: 'scan' | 'success' | 'warning' | 'info';
}

interface CandidateRow {
  rank: number;
  riderId: string;
  label: string;
  distanceKm: number;
}

interface RiderRow {
  id: string;
  label: string;
  short: string;
  status: string;
  updatedAt: string;
}

@Component({
  selector: 'app-sim-map',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './sim-map.component.html',
  styleUrl: './sim-map.component.scss'
})
export class SimMapComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('mapElement', { static: true }) mapElement!: ElementRef<HTMLElement>;

  private readonly trackingService = inject(TrackingSignalRService);
  private readonly subscriptions = new Subscription();
  private readonly udonCenter: L.LatLngTuple = [17.4138, 102.7872];
  private readonly thailandBounds: L.LatLngBoundsExpression = [[5.5, 97.3], [20.5, 105.7]];

  private map!: L.Map;
  private markerMap = new Map<string, L.Marker>();
  private markerPositions = new Map<string, L.LatLng>();
  private markerAnimations = new Map<string, number>();
  private routeCoords: { pickup: L.LatLng[]; delivery: L.LatLng[] } = { pickup: [], delivery: [] };
  private routeLines: { pickup?: L.Polyline; delivery?: L.Polyline; completed?: L.Polyline } = {};
  private activeMarkers: { shop?: L.Marker; dropoff?: L.Marker } = {};
  private radarCircle?: L.Circle;
  private candidateMarkers: L.CircleMarker[] = [];
  private followThrottleAt = 0;
  private timelineId = 0;

  activeOrder: any = null;
  activeOrderId = '';
  assignedRiderId: string | null = null;
  flowPhase: FlowPhase = 'idle';
  autoFollow = true;
  routeProgress = 0;
  routeDistanceLabel = '-';
  liveRouteLabel = 'Waiting for simulation';
  routeHint = 'Start the simulator to see scan, assignment, pickup, and delivery states.';
  candidateRows: CandidateRow[] = [];
  riderRows: RiderRow[] = [];
  timeline: TimelineItem[] = [];

  get flowTitle(): string {
    return {
      idle: 'Waiting for simulated order',
      scan: 'AI is scanning nearby riders',
      offer: 'Offer sent to best rider',
      assigned: 'Rider confirmed order',
      pickup: 'Rider heading to pickup',
      delivery: 'Rider delivering to dropoff',
      completed: 'Delivery completed'
    }[this.flowPhase];
  }

  get selectedRiderLabel(): string {
    return this.assignedRiderId ? `RID-${this.assignedRiderId.slice(0, 6).toUpperCase()}` : 'NONE';
  }

  get flowSteps(): Array<{ key: FlowPhase; label: string; done: boolean }> {
    const order: FlowPhase[] = ['scan', 'offer', 'assigned', 'pickup', 'delivery'];
    const currentIndex = order.indexOf(this.flowPhase);
    return [
      { key: 'scan', label: 'Scan', done: currentIndex > 0 || this.flowPhase === 'completed' },
      { key: 'offer', label: 'Offer', done: currentIndex > 1 || this.flowPhase === 'completed' },
      { key: 'assigned', label: 'Assign', done: currentIndex > 2 || this.flowPhase === 'completed' },
      { key: 'pickup', label: 'Pickup', done: currentIndex > 3 || this.flowPhase === 'completed' },
      { key: 'delivery', label: 'Dropoff', done: this.flowPhase === 'completed' }
    ];
  }

  ngOnInit(): void {
    this.trackingService.startConnection();

    this.subscriptions.add(
      this.trackingService.riderLocations$.subscribe(locationMap => {
        this.updateRiderMarkers(locationMap);
        this.updateRiderRows(locationMap);
      })
    );

    this.subscriptions.add(
      this.trackingService.dispatchScanStarted$.subscribe(data => this.handleDispatchScanStarted(data))
    );

    this.subscriptions.add(
      this.trackingService.dispatchCandidatesRanked$.subscribe(data => this.handleCandidatesRanked(data))
    );

    this.subscriptions.add(
      this.trackingService.offerReceived$.subscribe(offer => this.handleOfferSent(offer))
    );

    this.subscriptions.add(
      this.trackingService.orderAssigned$.subscribe(data => this.handleOrderAssigned(data))
    );

    this.subscriptions.add(
      this.trackingService.orderStatusChanged$.subscribe(data => this.handleOrderStatusChanged(data))
    );
  }

  ngAfterViewInit(): void {
    this.map = L.map(this.mapElement.nativeElement, {
      center: this.udonCenter,
      zoom: 13,
      minZoom: 6,
      maxZoom: 19,
      maxBounds: this.thailandBounds,
      maxBoundsViscosity: 1
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; OpenStreetMap contributors &copy; CARTO',
      subdomains: 'abcd',
      maxZoom: 19
    }).addTo(this.map);
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.trackingService.stopConnection();
    this.markerAnimations.forEach(frame => cancelAnimationFrame(frame));
    this.map?.remove();
  }

  zoomIn(): void {
    this.map?.zoomIn();
  }

  zoomOut(): void {
    this.map?.zoomOut();
  }

  resetView(): void {
    this.map?.setView(this.udonCenter, 13, { animate: true, duration: 0.8 });
  }

  toggleAutoFollow(): void {
    this.autoFollow = !this.autoFollow;
    if (this.autoFollow) this.focusActiveFlow();
  }

  focusActiveFlow(): void {
    const points = this.collectActivePoints();
    if (points.length === 1) {
      this.map.flyTo(points[0], 17, { duration: 0.85 });
      return;
    }
    if (points.length > 1) {
      this.map.flyToBounds(L.latLngBounds(points), { padding: [90, 90], maxZoom: 17, duration: 0.85 });
    }
  }

  private handleDispatchScanStarted(data: DispatchScanStarted): void {
    this.clearActiveLayers();
    this.flowPhase = 'scan';
    this.activeOrder = this.normalizeOrder(data.order);
    this.activeOrderId = this.shortOrder(this.activeOrder?.id);
    this.routeProgress = 0;
    this.liveRouteLabel = 'Scanning rider pool';
    this.routeHint = `Scanning within ${Number(data.searchRadiusKm || 0).toFixed(1)} km around pickup.`;
    this.routeDistanceLabel = this.activeOrder?.distanceKm ? `${Number(this.activeOrder.distanceKm).toFixed(2)} km` : '-';

    const pickup = this.getPickupLatLng();
    const dropoff = this.getDropoffLatLng();
    if (!pickup) return;

    this.activeMarkers.shop = this.createStaticMarker(pickup, 'S', 'shop').addTo(this.map);
    if (dropoff) this.activeMarkers.dropoff = this.createStaticMarker(dropoff, 'D', 'dropoff').addTo(this.map);

    this.radarCircle = L.circle(pickup, {
      radius: Math.max(300, Number(data.searchRadiusKm || 0.8) * 1000),
      color: '#38bdf8',
      fillColor: '#38bdf8',
      fillOpacity: 0.08,
      className: 'ai-radar-pulse'
    }).addTo(this.map);

    this.candidateRows = (data.nearbyRiders || []).map((candidate: any, index: number) => {
      const riderId = candidate.riderId || candidate.RiderId || '';
      const lat = candidate.lat ?? candidate.Lat;
      const lng = candidate.lng ?? candidate.Lng;
      const distanceKm = Number(candidate.distanceKm ?? candidate.DistanceKm ?? 0);
      if (lat != null && lng != null) {
        const marker = L.circleMarker([lat, lng], {
          radius: Math.max(6, 12 - index),
          color: '#38bdf8',
          fillColor: '#0ea5e9',
          fillOpacity: 0.72,
          weight: 2,
          className: 'scan-candidate-marker'
        }).addTo(this.map);
        marker.bindTooltip(`#${index + 1} RID-${riderId.slice(0, 6).toUpperCase()} / ${distanceKm.toFixed(2)} km`);
        this.candidateMarkers.push(marker);
      }
      return {
        rank: index + 1,
        riderId,
        label: `RID-${riderId.slice(0, 6).toUpperCase()}`,
        distanceKm
      };
    });

    this.addTimeline('AI scan started', `${this.candidateRows.length} riders found near pickup.`, 'scan');
    this.focusActiveFlow();
  }

  private handleCandidatesRanked(data: any): void {
    const ranked = data.rankedCandidates || data.RankedCandidates || [];
    this.candidateRows = ranked.map((candidate: any, index: number) => {
      const riderId = candidate.riderId || candidate.RiderId || '';
      return {
        rank: candidate.rank || candidate.Rank || index + 1,
        riderId,
        label: `RID-${riderId.slice(0, 6).toUpperCase()}`,
        distanceKm: Number(candidate.distanceKm ?? candidate.DistanceKm ?? 0)
      };
    });

    const winner = this.candidateRows[0];
    if (!winner) return;

    this.assignedRiderId = winner.riderId;
    this.liveRouteLabel = 'Best rider selected';
    this.routeHint = `${winner.label} has the best AI score for this pickup.`;
    this.addTimeline('AI ranking completed', `${winner.label} selected as best candidate.`, 'success');
    this.refreshMarkerIcons();
    this.focusActiveFlow();
  }

  private handleOfferSent(offer: any): void {
    this.flowPhase = 'offer';
    this.activeOrder = this.normalizeOrder(offer.order || this.activeOrder);
    this.activeOrderId = this.shortOrder(this.activeOrder?.id);
    this.assignedRiderId = offer.riderId || offer.RiderId || this.assignedRiderId;
    this.routeCoords.pickup = this.decodeRoute(offer.pickupRoute?.encodedPolyline || offer.PickupRoute?.EncodedPolyline);
    this.routeCoords.delivery = this.decodeRoute(this.activeOrder?.encodedPolyline);
    this.drawOfferRoutes();
    this.liveRouteLabel = 'Offer sent to rider';
    this.routeHint = this.assignedRiderId ? `${this.selectedRiderLabel} is confirming the order.` : 'Waiting for rider confirmation.';
    this.addTimeline('Offer sent', `${this.selectedRiderLabel} received the simulated order offer.`, 'scan');
    this.refreshMarkerIcons();
    this.focusActiveFlow();
  }

  private handleOrderAssigned(data: any): void {
    this.flowPhase = 'assigned';
    this.assignedRiderId = data.riderId || data.RiderId || this.assignedRiderId;
    this.liveRouteLabel = 'Rider accepted order';
    this.routeHint = 'Zooming to selected rider before pickup movement starts.';
    this.radarCircle?.remove();
    this.radarCircle = undefined;
    this.clearCandidateMarkers();
    this.addTimeline('Order assigned', `${this.selectedRiderLabel} accepted the order.`, 'success');
    this.refreshMarkerIcons();
    this.zoomToSelectedRider();
  }

  private handleOrderStatusChanged(data: { orderId: string; status: string }): void {
    const status = data.status;
    if (status === 'PICKING_UP') {
      this.flowPhase = 'pickup';
      this.liveRouteLabel = 'Rider heading to store';
      this.routeHint = 'Pickup route is being shortened as GPS updates arrive.';
      this.addTimeline('Pickup route started', `${this.selectedRiderLabel} is moving to the store.`, 'scan');
      this.zoomToSelectedRider();
    } else if (status === 'DELIVERING') {
      this.flowPhase = 'delivery';
      this.routeProgress = 0;
      this.routeLines.pickup?.remove();
      this.routeLines.pickup = undefined;
      this.liveRouteLabel = 'Rider heading to dropoff';
      this.routeHint = 'Delivery route is now active on the real road polyline.';
      this.addTimeline('Food picked up', `${this.selectedRiderLabel} is delivering to destination.`, 'success');
      this.zoomToSelectedRider();
    } else if (status === 'COMPLETED') {
      this.flowPhase = 'completed';
      this.routeProgress = 100;
      this.liveRouteLabel = 'Delivery completed';
      this.routeHint = 'Simulation flow completed. Layers will stay visible for review.';
      this.addTimeline('Delivery completed', `${this.shortOrder(data.orderId)} reached the dropoff.`, 'success');
      this.focusActiveFlow();
    }
  }

  private updateRiderMarkers(locationMap: Map<string, RiderLocationUpdate>): void {
    if (!this.map) return;

    locationMap.forEach((loc, riderId) => {
      const next = L.latLng(loc.latitude, loc.longitude);
      const marker = this.markerMap.get(riderId);
      if (marker) {
        this.animateMarker(riderId, marker, next);
      } else {
        const created = L.marker(next, { icon: this.createRiderIcon(riderId) })
          .bindTooltip(`RID-${riderId.slice(0, 6).toUpperCase()}`)
          .addTo(this.map);
        this.markerMap.set(riderId, created);
        this.markerPositions.set(riderId, next);
      }

      if (riderId === this.assignedRiderId) {
        this.updateRouteProgress(next);
        this.followSelectedRider(next);
      }
    });
  }

  private updateRiderRows(locationMap: Map<string, RiderLocationUpdate>): void {
    const rows: RiderRow[] = [];
    locationMap.forEach((loc, riderId) => {
      rows.push({
        id: riderId,
        label: `RID-${riderId.slice(0, 6).toUpperCase()}`,
        short: riderId.slice(0, 2).toUpperCase(),
        status: riderId === this.assignedRiderId ? `${loc.status} / SELECTED` : loc.status,
        updatedAt: new Date(loc.timestamp).toLocaleTimeString()
      });
    });
    this.riderRows = rows.sort((a, b) => Number(b.id === this.assignedRiderId) - Number(a.id === this.assignedRiderId));
  }

  private animateMarker(riderId: string, marker: L.Marker, next: L.LatLng): void {
    const previous = this.markerPositions.get(riderId) || marker.getLatLng();
    const distance = previous.distanceTo(next);
    if (distance < 1) return;

    const existingFrame = this.markerAnimations.get(riderId);
    if (existingFrame) cancelAnimationFrame(existingFrame);

    const duration = Math.min(950, Math.max(260, distance * 14));
    const startedAt = performance.now();
    const bearing = this.calculateBearing(previous, next);

    const tick = (now: number) => {
      const t = Math.min(1, (now - startedAt) / duration);
      const eased = 1 - Math.pow(1 - t, 3);
      const lat = previous.lat + (next.lat - previous.lat) * eased;
      const lng = previous.lng + (next.lng - previous.lng) * eased;
      marker.setLatLng([lat, lng]);
      marker.setIcon(this.createRiderIcon(riderId, bearing));

      if (t < 1) {
        this.markerAnimations.set(riderId, requestAnimationFrame(tick));
      } else {
        this.markerPositions.set(riderId, next);
        this.markerAnimations.delete(riderId);
      }
    };

    this.markerAnimations.set(riderId, requestAnimationFrame(tick));
  }

  private updateRouteProgress(position: L.LatLng): void {
    const coords = this.flowPhase === 'delivery' ? this.routeCoords.delivery : this.routeCoords.pickup;
    const line = this.flowPhase === 'delivery' ? this.routeLines.delivery : this.routeLines.pickup;
    if (!coords.length || !line) return;

    const nearest = this.findNearestRouteIndex(position, coords);
    const remaining = [position, ...coords.slice(Math.min(nearest + 1, coords.length - 1))];
    line.setLatLngs(remaining);
    this.routeProgress = Math.min(100, Math.round((nearest / Math.max(1, coords.length - 1)) * 100));
  }

  private followSelectedRider(position: L.LatLng): void {
    if (!this.autoFollow || !this.assignedRiderId) return;
    const now = Date.now();
    if (now - this.followThrottleAt < 900) return;
    this.followThrottleAt = now;

    if (this.flowPhase === 'assigned' || this.flowPhase === 'pickup' || this.flowPhase === 'delivery') {
      const zoom = Math.max(this.map.getZoom(), 16);
      this.map.flyTo(position, Math.min(18, zoom), { duration: 0.75 });
    }
  }

  private zoomToSelectedRider(): void {
    if (!this.assignedRiderId) return;
    const marker = this.markerMap.get(this.assignedRiderId);
    if (!marker) {
      this.focusActiveFlow();
      return;
    }
    this.map.flyTo(marker.getLatLng(), 17, { duration: 0.9 });
  }

  private drawOfferRoutes(): void {
    this.routeLines.pickup?.remove();
    this.routeLines.delivery?.remove();

    if (this.routeCoords.pickup.length) {
      this.routeLines.pickup = L.polyline(this.routeCoords.pickup, {
        color: '#ef4444',
        weight: 5,
        opacity: 0.9,
        dashArray: '10, 10'
      }).addTo(this.map);
    }

    const pickup = this.getPickupLatLng();
    const dropoff = this.getDropoffLatLng();
    const deliveryCoords = this.routeCoords.delivery.length
      ? this.routeCoords.delivery
      : pickup && dropoff ? [pickup, dropoff] : [];

    if (deliveryCoords.length) {
      this.routeLines.delivery = L.polyline(deliveryCoords, {
        color: '#22c55e',
        weight: 5,
        opacity: 0.78
      }).addTo(this.map);
    }
  }

  private clearActiveLayers(): void {
    this.radarCircle?.remove();
    this.routeLines.pickup?.remove();
    this.routeLines.delivery?.remove();
    this.routeLines.completed?.remove();
    this.activeMarkers.shop?.remove();
    this.activeMarkers.dropoff?.remove();
    this.clearCandidateMarkers();
    this.routeLines = {};
    this.activeMarkers = {};
    this.routeCoords = { pickup: [], delivery: [] };
    this.candidateRows = [];
    this.assignedRiderId = null;
  }

  private clearCandidateMarkers(): void {
    this.candidateMarkers.forEach(marker => marker.remove());
    this.candidateMarkers = [];
  }

  private collectActivePoints(): L.LatLng[] {
    const points: L.LatLng[] = [];
    const pickup = this.getPickupLatLng();
    const dropoff = this.getDropoffLatLng();
    if (pickup) points.push(pickup);
    if (dropoff) points.push(dropoff);
    if (this.assignedRiderId) {
      const marker = this.markerMap.get(this.assignedRiderId);
      if (marker) points.push(marker.getLatLng());
    }
    this.candidateMarkers.forEach(marker => points.push(marker.getLatLng()));
    return points;
  }

  private createRiderIcon(riderId: string, bearing = 0): L.DivIcon {
    const winner = riderId === this.assignedRiderId ? ' winner' : '';
    return L.divIcon({
      className: 'sim-marker',
      html: `<div class="sim-marker-core${winner}" style="transform: rotate(${bearing}deg)">R</div>`,
      iconSize: [34, 34],
      iconAnchor: [17, 17]
    });
  }

  private createStaticMarker(latLng: L.LatLng, label: string, tone: 'shop' | 'dropoff'): L.Marker {
    return L.marker(latLng, {
      icon: L.divIcon({
        className: 'sim-marker',
        html: `<div class="sim-marker-core ${tone}">${label}</div>`,
        iconSize: [36, 36],
        iconAnchor: [18, 18]
      })
    });
  }

  private refreshMarkerIcons(): void {
    this.markerMap.forEach((marker, riderId) => marker.setIcon(this.createRiderIcon(riderId)));
  }

  private decodeRoute(polyline?: string): L.LatLng[] {
    if (!polyline) return [];
    let index = 0;
    let lat = 0;
    let lng = 0;
    const coords: L.LatLng[] = [];

    while (index < polyline.length) {
      let shift = 0;
      let result = 0;
      let b: number;
      do {
        b = polyline.charCodeAt(index++) - 63;
        result |= (b & 0x1f) << shift;
        shift += 5;
      } while (b >= 0x20);
      lat += (result & 1) ? ~(result >> 1) : result >> 1;

      shift = 0;
      result = 0;
      do {
        b = polyline.charCodeAt(index++) - 63;
        result |= (b & 0x1f) << shift;
        shift += 5;
      } while (b >= 0x20);
      lng += (result & 1) ? ~(result >> 1) : result >> 1;

      coords.push(L.latLng(lat / 1e5, lng / 1e5));
    }

    return coords;
  }

  private normalizeOrder(order: any): any {
    if (!order) return null;
    return {
      id: order.id || order.Id,
      pickupLat: order.pickupLat ?? order.PickupLat,
      pickupLng: order.pickupLng ?? order.PickupLng,
      dropoffLat: order.dropoffLat ?? order.DropoffLat,
      dropoffLng: order.dropoffLng ?? order.DropoffLng,
      encodedPolyline: order.encodedPolyline || order.EncodedPolyline,
      distanceKm: order.distanceKm ?? order.DistanceKm,
      routeDistanceMeters: order.routeDistanceMeters ?? order.RouteDistanceMeters,
      routeDurationSeconds: order.routeDurationSeconds ?? order.RouteDurationSeconds
    };
  }

  private getPickupLatLng(): L.LatLng | null {
    if (!this.activeOrder?.pickupLat || !this.activeOrder?.pickupLng) return null;
    return L.latLng(this.activeOrder.pickupLat, this.activeOrder.pickupLng);
  }

  private getDropoffLatLng(): L.LatLng | null {
    if (!this.activeOrder?.dropoffLat || !this.activeOrder?.dropoffLng) return null;
    return L.latLng(this.activeOrder.dropoffLat, this.activeOrder.dropoffLng);
  }

  private findNearestRouteIndex(position: L.LatLng, coords: L.LatLng[]): number {
    let bestIndex = 0;
    let bestDistance = Number.MAX_SAFE_INTEGER;
    coords.forEach((coord, index) => {
      const distance = position.distanceTo(coord);
      if (distance < bestDistance) {
        bestDistance = distance;
        bestIndex = index;
      }
    });
    return bestIndex;
  }

  private calculateBearing(from: L.LatLng, to: L.LatLng): number {
    const fromLat = from.lat * Math.PI / 180;
    const toLat = to.lat * Math.PI / 180;
    const deltaLng = (to.lng - from.lng) * Math.PI / 180;
    const y = Math.sin(deltaLng) * Math.cos(toLat);
    const x = Math.cos(fromLat) * Math.sin(toLat) - Math.sin(fromLat) * Math.cos(toLat) * Math.cos(deltaLng);
    return (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;
  }

  private shortOrder(orderId?: string): string {
    return orderId ? `ORD-${orderId.slice(0, 6).toUpperCase()}` : '';
  }

  private addTimeline(title: string, text: string, tone: TimelineItem['tone']): void {
    this.timeline = [{
      id: ++this.timelineId,
      title,
      text,
      tone,
      time: new Date().toLocaleTimeString()
    }, ...this.timeline].slice(0, 12);
  }
}
