import {
  AfterViewInit, Component, ElementRef, OnDestroy, OnInit,
  ViewChild, inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import 'leaflet.markercluster';
import { Subscription } from 'rxjs';
import {
  DispatchScanStarted, RiderLocationUpdate,
  TrackingSignalRService
} from '../../core/services/tracking-signalr.service';
import { MapDrawingService, RiderStatus } from '../map/services/map-drawing.service';
import { MapMathService } from '../map/services/map-math.service';

type FlowPhase = 'idle' | 'scan' | 'offer' | 'assigned' | 'pickup' | 'delivery' | 'completed';

interface TimelineItem {
  id:    number;
  title: string;
  text:  string;
  time:  string;
  tone:  'scan' | 'success' | 'warning' | 'info';
}

interface CandidateRow {
  rank:                number;
  riderId:             string;
  label:               string;
  distanceKm:          number;
  score?:              number;
  etaMinutes?:         number;
  deliveryEtaMinutes?: number;
  totalEtaMinutes?:    number;
}

interface RiderRow {
  id:        string;
  label:     string;
  short:     string;
  status:    string;
  updatedAt: string;
}

@Component({
  selector:    'app-sim-map',
  standalone:  true,
  imports:     [CommonModule, FormsModule],
  providers:   [MapDrawingService, MapMathService],
  templateUrl: './sim-map.component.html',
  styleUrl:    './sim-map.component.scss'
})
export class SimMapComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('mapElement', { static: true }) mapElement!: ElementRef<HTMLElement>;

  private readonly trackingService = inject(TrackingSignalRService);
  public  readonly draw            = inject(MapDrawingService);
  private readonly math            = inject(MapMathService);
  private readonly subscriptions   = new Subscription();

  private readonly udonCenter:     L.LatLngTuple          = [17.4138, 102.7872];
  private readonly thailandBounds: L.LatLngBoundsExpression = [[5.5, 97.3], [20.5, 105.7]];

  private map!:                L.Map;
  private routeCoords:         { pickup: L.LatLng[]; delivery: L.LatLng[] } = { pickup: [], delivery: [] };
  private followThrottleAt   = 0;
  private timelineId         = 0;

  // live state
  activeOrder:    any    = null;
  activeOrderId   = '';
  assignedRiderId: string | null = null;
  flowPhase:       FlowPhase     = 'idle';
  autoFollow       = true;
  routeProgress    = 0;
  routeDistanceLabel = '-';
  aiDurationLabel    = '-';
  liveRouteLabel     = 'Waiting for simulation';
  routeHint          = 'Start the simulator to see scan, assignment, pickup, and delivery states.';
  candidateRows:   CandidateRow[] = [];
  riderRows:       RiderRow[]     = [];
  timeline:        TimelineItem[] = [];
  liveRidersMap    = new Map<string, RiderLocationUpdate>();

  // ── Filter state ──────────────────────────────────────────────────────────
  filterIdleVisible       = true;
  filterDeliveringVisible = true;
  filterOfflineVisible    = true;
  filterShowRoutes        = true;
  filterShowHeatmap       = false;
  searchQuery             = '';
  clusterMode             = true;

  // ─────────────────────────────────────────────────────────────────────────
  // Computed
  // ─────────────────────────────────────────────────────────────────────────

  get flowTitle(): string {
    return ({
      idle:      'Waiting for simulated order',
      scan:      'AI is scanning nearby riders',
      offer:     'Offer sent to best rider',
      assigned:  'Rider confirmed order',
      pickup:    'Rider heading to pickup',
      delivery:  'Rider delivering to dropoff',
      completed: 'Delivery completed',
    } as Record<FlowPhase, string>)[this.flowPhase];
  }

  get selectedRiderLabel(): string {
    return this.assignedRiderId
      ? `RID-${this.assignedRiderId.slice(0, 6).toUpperCase()}`
      : 'NONE';
  }

  get flowSteps(): Array<{ key: FlowPhase; label: string; done: boolean }> {
    const order: FlowPhase[] = ['scan', 'offer', 'assigned', 'pickup', 'delivery'];
    const idx = order.indexOf(this.flowPhase);
    return [
      { key: 'scan',     label: 'Scan',    done: idx > 0 || this.flowPhase === 'completed' },
      { key: 'offer',    label: 'Offer',   done: idx > 1 || this.flowPhase === 'completed' },
      { key: 'assigned', label: 'Assign',  done: idx > 2 || this.flowPhase === 'completed' },
      { key: 'pickup',   label: 'Pickup',  done: idx > 3 || this.flowPhase === 'completed' },
      { key: 'delivery', label: 'Dropoff', done: this.flowPhase === 'completed' },
    ];
  }

  get filteredRiderRows(): RiderRow[] {
    const q = this.searchQuery.trim().toLowerCase();
    return this.riderRows.filter(r => {
      const statusMatch = this.isStatusVisible(r.status);
      const searchMatch = !q || r.label.toLowerCase().includes(q) || r.id.toLowerCase().includes(q);
      return statusMatch && searchMatch;
    });
  }

  get heatmapPoints(): { lat: number; lng: number; intensity: number }[] {
    const pts: { lat: number; lng: number; intensity: number }[] = [];
    this.liveRidersMap.forEach(loc => pts.push({ lat: loc.latitude, lng: loc.longitude, intensity: 0.7 }));
    return pts;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Lifecycle
  // ─────────────────────────────────────────────────────────────────────────

  ngOnInit(): void {
    this.trackingService.startConnection();

    this.subscriptions.add(
      this.trackingService.riderLocations$.subscribe(locationMap => {
        this.liveRidersMap = locationMap;
        this.updateRiderMarkers(locationMap);
        this.updateRiderRows(locationMap);
        if (this.filterShowHeatmap) this.draw.showHeatmap(this.heatmapPoints);
      })
    );

    this.subscriptions.add(
      this.trackingService.dispatchScanStarted$.subscribe(d => this.handleDispatchScanStarted(d))
    );
    this.subscriptions.add(
      this.trackingService.dispatchCandidatesRanked$.subscribe(d => this.handleCandidatesRanked(d))
    );
    this.subscriptions.add(
      this.trackingService.offerReceived$.subscribe(o => this.handleOfferSent(o))
    );
    this.subscriptions.add(
      this.trackingService.orderAssigned$.subscribe(d => this.handleOrderAssigned(d))
    );
    this.subscriptions.add(
      this.trackingService.orderStatusChanged$.subscribe(d => this.handleOrderStatusChanged(d))
    );

    // Expose rider action callback for popup buttons (mock)
    (window as any)._riderAction = (action: string, riderId: string) => {
      this.addTimeline(
        action === 'accept' ? 'Rider Accepted (Mock)' : 'Rider Rejected (Mock)',
        `Action "${action}" sent to ${riderId.slice(0, 8).toUpperCase()}`,
        action === 'accept' ? 'success' : 'warning'
      );
    };
  }

  ngAfterViewInit(): void {
    this.map = L.map(this.mapElement.nativeElement, {
      center:             this.udonCenter,
      zoom:               13,
      minZoom:            6,
      maxZoom:            19,
      maxBounds:          this.thailandBounds,
      maxBoundsViscosity: 1,
      zoomControl:        false,
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; OpenStreetMap contributors &copy; CARTO',
      subdomains:  'abcd',
      maxZoom:     19,
    }).addTo(this.map);

    this.draw.initializeMap(this.map);
    this.draw.initCluster();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.trackingService.stopConnection();
    delete (window as any)._riderAction;
    this.map?.remove();
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Map controls
  // ─────────────────────────────────────────────────────────────────────────

  zoomIn():       void { this.map?.zoomIn(); }
  zoomOut():      void { this.map?.zoomOut(); }

  resetView(): void {
    this.map?.setView(this.udonCenter, 13, { animate: true, duration: 0.8 });
  }

  toggleAutoFollow(): void {
    this.autoFollow = !this.autoFollow;
    if (this.autoFollow) this.focusActiveFlow();
  }

  focusActiveFlow(): void {
    const points = this.collectActivePoints();
    if (points.length === 1) { this.map.flyTo(points[0], 17, { duration: 0.85 }); return; }
    if (points.length >  1)  { this.map.flyToBounds(L.latLngBounds(points), { padding: [90, 90], maxZoom: 17, duration: 0.85 }); }
  }

  clearSelections(): void {
    this.searchQuery = '';
    this.applyFilters();
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Filter & toggle handlers
  // ─────────────────────────────────────────────────────────────────────────

  applyFilters(): void {
    // Rebuild visible markers in cluster based on current filter state
    if (!this.clusterMode) return;

    const visible = new Set<string>();
    if (this.filterIdleVisible)       visible.add('IDLE');
    if (this.filterIdleVisible)       visible.add('AVAILABLE');
    if (this.filterDeliveringVisible) visible.add('DELIVERING');
    if (this.filterDeliveringVisible) visible.add('PICKING_UP');
    if (this.filterDeliveringVisible) visible.add('BUSY');
    if (this.filterOfflineVisible)    visible.add('OFFLINE');
    this.draw.filterClusterByStatus(visible);

    // Route visibility
    if (this.filterShowRoutes) {
      if (this.routeCoords.pickup.length)   this.draw.routeLines.pickup?.addTo(this.map);
      if (this.routeCoords.delivery.length) this.draw.routeLines.delivery?.addTo(this.map);
    } else {
      this.draw.routeLines.pickup?.remove();
      this.draw.routeLines.delivery?.remove();
    }
  }

  onHeatmapToggle(): void {
    if (this.filterShowHeatmap) {
      this.draw.showHeatmap(this.heatmapPoints);
    } else {
      this.draw.removeHeatmap();
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Rider markers
  // ─────────────────────────────────────────────────────────────────────────

  private updateRiderMarkers(locationMap: Map<string, RiderLocationUpdate>): void {
    if (!this.map) return;
    const activeIds = new Set(locationMap.keys());

    locationMap.forEach((loc, riderId) => {
      const next   = L.latLng(loc.latitude, loc.longitude);
      const status = (loc.status || 'IDLE') as RiderStatus;

      if (this.clusterMode) {
        // Cluster layer path
        if (!this.isStatusVisible(status)) return;
        const popupHtml = this.draw.buildRiderPopupHtml({
          riderId, status,
          label: `RID-${riderId.slice(0, 6).toUpperCase()}`,
          lat:   loc.latitude,
          lng:   loc.longitude,
        });
        this.draw.upsertClusterMarker(riderId, loc.latitude, loc.longitude, status, popupHtml);
      } else {
        // Non-cluster (sim mode) path
        const marker = this.draw.markerMap.get(riderId);
        if (marker) {
          this.draw.animateMarker(riderId, this.assignedRiderId, marker, next, status, () => {
            if (riderId === this.assignedRiderId) {
              this.updateRouteProgress(next);
              this.followSelectedRider(next);
            }
          });
        } else {
          const created = L.marker(next, { icon: this.draw.createRiderIcon(riderId, this.assignedRiderId) })
            .bindTooltip(`RID-${riderId.slice(0, 6).toUpperCase()}`)
            .on('dblclick', () => this.map.flyTo(next, 17, { animate: true, duration: 0.6 }))
            .addTo(this.map);
          this.draw.markerMap.set(riderId, created);
          this.draw.markerPositions.set(riderId, next);
        }
      }
    });

    if (this.clusterMode) {
      this.draw.pruneClusterMarkers(activeIds);
    }
  }

  private isStatusVisible(status: string): boolean {
    const s = status.toUpperCase();
    if (['IDLE', 'AVAILABLE'].includes(s))              return this.filterIdleVisible;
    if (['DELIVERING', 'PICKING_UP', 'BUSY'].includes(s)) return this.filterDeliveringVisible;
    return this.filterOfflineVisible;
  }

  private updateRiderRows(locationMap: Map<string, RiderLocationUpdate>): void {
    const rows: RiderRow[] = [];
    locationMap.forEach((loc, riderId) => {
      rows.push({
        id:        riderId,
        label:     `RID-${riderId.slice(0, 6).toUpperCase()}`,
        short:     riderId.slice(0, 2).toUpperCase(),
        status:    riderId === this.assignedRiderId ? `${loc.status} / SELECTED` : loc.status,
        updatedAt: new Date(loc.timestamp).toLocaleTimeString(),
      });
    });
    this.riderRows = rows.sort((a, b) => Number(b.id === this.assignedRiderId) - Number(a.id === this.assignedRiderId));
  }

  // ─────────────────────────────────────────────────────────────────────────
  // SignalR handlers (original, preserved)
  // ─────────────────────────────────────────────────────────────────────────

  private handleDispatchScanStarted(data: DispatchScanStarted): void {
    this.clearSimLayers();
    this.flowPhase        = 'scan';
    this.activeOrder      = this.normalizeOrder(data.order);
    this.activeOrderId    = this.shortOrder(this.activeOrder?.id);
    this.routeProgress    = 0;
    this.liveRouteLabel   = 'Scanning rider pool';
    this.routeHint        = `Scanning within ${Number(data.searchRadiusKm || 0).toFixed(1)} km around pickup.`;
    this.routeDistanceLabel = this.activeOrder?.distanceKm ? `${Number(this.activeOrder.distanceKm).toFixed(2)} km` : '-';
    this.aiDurationLabel  = 'AI calculating ETA...';

    const pickup  = this.getPickupLatLng();
    const dropoff = this.getDropoffLatLng();
    if (!pickup) return;

    this.draw.activeMarkers.shop   = this.draw.createStaticMarker(pickup, 'S', 'shop').addTo(this.map);
    if (dropoff) this.draw.activeMarkers.dropoff = this.draw.createStaticMarker(dropoff, 'D', 'dropoff').addTo(this.map);

    this.draw.radarCircle = L.circle(pickup, {
      radius:      Math.max(300, Number(data.searchRadiusKm || 0.8) * 1000),
      color:       '#38bdf8',
      fillColor:   '#38bdf8',
      fillOpacity: 0.08,
      className:   'ai-radar-pulse',
    }).addTo(this.map);

    this.candidateRows = (data.nearbyRiders || []).map((candidate: any, index: number) => {
      const riderId    = candidate.riderId || candidate.RiderId || '';
      const lat        = candidate.lat ?? candidate.Lat;
      const lng        = candidate.lng ?? candidate.Lng;
      const distanceKm = Number(candidate.distanceKm ?? candidate.DistanceKm ?? 0);
      if (lat != null && lng != null) {
        const marker = L.circleMarker([lat, lng], {
          radius: Math.max(6, 12 - index), color: '#38bdf8',
          fillColor: '#0ea5e9', fillOpacity: 0.72, weight: 2,
          className: 'scan-candidate-marker',
        }).addTo(this.map);
        marker.bindTooltip(`#${index + 1} RID-${riderId.slice(0, 6).toUpperCase()} / ${distanceKm.toFixed(2)} km`);
        this.draw.candidateMarkers.push(marker);
      }
      return { rank: index + 1, riderId, label: `RID-${riderId.slice(0, 6).toUpperCase()}`, distanceKm };
    });

    this.addTimeline('AI scan started', `${this.candidateRows.length} riders found near pickup.`, 'scan');
    this.focusActiveFlow();
  }

  private handleCandidatesRanked(data: any): void {
    const ranked = data.rankedCandidates || data.RankedCandidates || [];
    this.candidateRows = ranked.map((candidate: any, index: number) => {
      const riderId    = candidate.riderId || candidate.RiderId || '';
      const pickupEta  = candidate.etaMinutes ?? candidate.EtaMinutes ?? 0;
      let deliveryEta  = 0;
      if (this.activeOrder) {
        deliveryEta = this.activeOrder.routeDurationSeconds
          ? Math.ceil(this.activeOrder.routeDurationSeconds / 60)
          : Math.ceil((this.activeOrder.distanceKm || 0) * 2.0);
      }
      return {
        rank:                candidate.rank || candidate.Rank || index + 1,
        riderId,
        label:               `RID-${riderId.slice(0, 6).toUpperCase()}`,
        distanceKm:          Number(candidate.distanceKm ?? candidate.DistanceKm ?? 0),
        score:               candidate.score ?? candidate.Score,
        etaMinutes:          pickupEta,
        deliveryEtaMinutes:  deliveryEta,
        totalEtaMinutes:     pickupEta + deliveryEta,
      };
    });

    const winner = this.candidateRows[0];
    if (!winner) return;
    this.assignedRiderId  = winner.riderId;
    this.liveRouteLabel   = 'Best rider selected';
    this.routeHint        = `${winner.label} has the best AI score for this pickup.`;
    this.addTimeline('AI ranking completed', `${winner.label} selected as best candidate.`, 'success');
    this.draw.refreshMarkerIcons(this.assignedRiderId);
    this.focusActiveFlow();
  }

  private handleOfferSent(offer: any): void {
    this.flowPhase      = 'offer';
    this.activeOrder    = this.normalizeOrder(offer.order || this.activeOrder);
    this.activeOrderId  = this.shortOrder(this.activeOrder?.id);
    this.assignedRiderId = offer.riderId || offer.RiderId || this.assignedRiderId;

    this.routeCoords.pickup   = this.math.decodeRoute(offer.pickupRoute?.encodedPolyline || offer.PickupRoute?.EncodedPolyline);
    this.routeCoords.delivery = this.math.decodeRoute(this.activeOrder?.encodedPolyline);

    this.draw.drawOfferRoutes(this.routeCoords.pickup, this.routeCoords.delivery, this.getPickupLatLng(), this.getDropoffLatLng());

    const pickupDist   = offer.pickupRoute?.distanceMeters ?? offer.PickupRoute?.DistanceMeters ?? 0;
    const deliveryDist = this.activeOrder?.routeDistanceMeters ?? 0;
    const totalDistKm  = (pickupDist + deliveryDist) / 1000;
    if (totalDistKm > 0) this.routeDistanceLabel = `${totalDistKm.toFixed(2)} km (Dijkstra Road)`;

    const pickupDuration   = offer.pickupRoute?.durationSeconds ?? offer.PickupRoute?.DurationSeconds ?? 0;
    const deliveryDuration = this.activeOrder?.routeDurationSeconds ?? 0;
    const totalSeconds     = pickupDuration + deliveryDuration;
    if (totalSeconds > 0) {
      const totalMins    = Math.ceil(totalSeconds / 60);
      const pickupMins   = Math.ceil(pickupDuration / 60);
      const deliveryMins = Math.ceil(deliveryDuration / 60);
      this.aiDurationLabel = `${totalMins} mins (Rider→Store: ${pickupMins}m, Store→Dropoff: ${deliveryMins}m)`;
    } else {
      this.aiDurationLabel = '-';
    }

    this.liveRouteLabel = 'Offer sent to rider';
    this.routeHint      = this.assignedRiderId
      ? `${this.selectedRiderLabel} is confirming the order.`
      : 'Waiting for rider confirmation.';
    this.addTimeline('Offer sent', `${this.selectedRiderLabel} received the simulated order offer.`, 'scan');
    this.draw.refreshMarkerIcons(this.assignedRiderId);
    this.focusActiveFlow();
  }

  private handleOrderAssigned(data: any): void {
    this.flowPhase       = 'assigned';
    this.assignedRiderId = data.riderId || data.RiderId || this.assignedRiderId;
    this.liveRouteLabel  = 'Rider accepted order';
    this.routeHint       = 'Zooming to selected rider before pickup movement starts.';

    this.draw.radarCircle?.remove();
    this.draw.radarCircle = undefined;
    this.draw.clearCandidateMarkers();

    this.addTimeline('Order assigned', `${this.selectedRiderLabel} accepted the order.`, 'success');
    this.draw.refreshMarkerIcons(this.assignedRiderId);
    this.zoomToSelectedRider();
  }

  private handleOrderStatusChanged(data: { orderId: string; status: string }): void {
    const status = data.status;
    if (status === 'PICKING_UP') {
      this.flowPhase      = 'pickup';
      this.liveRouteLabel = 'Rider heading to store';
      this.routeHint      = 'Pickup route is being shortened as GPS updates arrive.';
      this.addTimeline('Pickup route started', `${this.selectedRiderLabel} is moving to the store.`, 'scan');
      this.zoomToSelectedRider();
    } else if (status === 'DELIVERING') {
      this.flowPhase      = 'delivery';
      this.routeProgress  = 0;
      this.draw.routeLines.pickup?.remove();
      this.draw.routeLines.pickup = undefined;
      this.liveRouteLabel = 'Rider heading to dropoff';
      this.routeHint      = 'Delivery route is now active on the real road polyline.';
      this.addTimeline('Food picked up', `${this.selectedRiderLabel} is delivering to destination.`, 'success');
      this.zoomToSelectedRider();
    } else if (status === 'COMPLETED') {
      this.flowPhase      = 'completed';
      this.routeProgress  = 100;
      this.liveRouteLabel = 'Delivery completed';
      this.routeHint      = 'Simulation flow completed. Layers will stay visible for review.';
      // Re-draw final delivery segment as completed (green)
      if (this.routeCoords.delivery.length) {
        this.draw.routeLines.completed = this.draw.drawColoredRoute(this.routeCoords.delivery, 'completed');
      }
      this.addTimeline('Delivery completed', `${this.shortOrder(data.orderId)} reached the dropoff.`, 'success');
      this.focusActiveFlow();
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Route progress / follow
  // ─────────────────────────────────────────────────────────────────────────

  private updateRouteProgress(position: L.LatLng): void {
    const coords = this.flowPhase === 'delivery' ? this.routeCoords.delivery : this.routeCoords.pickup;
    const line   = this.flowPhase === 'delivery' ? this.draw.routeLines.delivery : this.draw.routeLines.pickup;
    if (!coords.length || !line) return;

    const nearest  = this.math.findNearestRouteIndex(position, coords);
    const remaining = [position, ...coords.slice(Math.min(nearest + 1, coords.length - 1))];
    line.setLatLngs(remaining);
    this.routeProgress = Math.min(100, Math.round((nearest / Math.max(1, coords.length - 1)) * 100));
  }

  private followSelectedRider(position: L.LatLng): void {
    if (!this.autoFollow || !this.assignedRiderId) return;
    const now = Date.now();
    if (now - this.followThrottleAt < 900) return;
    this.followThrottleAt = now;
    if (['assigned', 'pickup', 'delivery'].includes(this.flowPhase)) {
      const zoom = Math.max(this.map.getZoom(), 16);
      this.map.flyTo(position, Math.min(18, zoom), { duration: 0.75 });
    }
  }

  private zoomToSelectedRider(): void {
    if (!this.assignedRiderId) return;
    const marker = this.draw.markerMap.get(this.assignedRiderId);
    if (!marker) { this.focusActiveFlow(); return; }
    this.map.flyTo(marker.getLatLng(), 17, { duration: 0.9 });
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Utility
  // ─────────────────────────────────────────────────────────────────────────

  private clearSimLayers(): void {
    this.draw.clearActiveLayers();
    this.routeCoords    = { pickup: [], delivery: [] };
    this.candidateRows  = [];
    this.assignedRiderId = null;
    this.aiDurationLabel = '-';
  }

  private collectActivePoints(): L.LatLng[] {
    const points: L.LatLng[] = [];
    const pickup  = this.getPickupLatLng();
    const dropoff = this.getDropoffLatLng();
    if (pickup)  points.push(pickup);
    if (dropoff) points.push(dropoff);
    if (this.assignedRiderId) {
      const marker = this.draw.markerMap.get(this.assignedRiderId);
      if (marker) points.push(marker.getLatLng());
    }
    this.draw.candidateMarkers.forEach(m => points.push(m.getLatLng()));
    return points;
  }

  private normalizeOrder(order: any): any {
    if (!order) return null;
    return {
      id:                   order.id || order.Id,
      pickupLat:            order.pickupLat  ?? order.PickupLat,
      pickupLng:            order.pickupLng  ?? order.PickupLng,
      dropoffLat:           order.dropoffLat ?? order.DropoffLat,
      dropoffLng:           order.dropoffLng ?? order.DropoffLng,
      encodedPolyline:      order.encodedPolyline    || order.EncodedPolyline,
      distanceKm:           order.distanceKm         ?? order.DistanceKm,
      routeDistanceMeters:  order.routeDistanceMeters ?? order.RouteDistanceMeters,
      routeDurationSeconds: order.routeDurationSeconds ?? order.RouteDurationSeconds,
    };
  }

  private getPickupLatLng():  L.LatLng | null {
    if (!this.activeOrder?.pickupLat || !this.activeOrder?.pickupLng) return null;
    return L.latLng(this.activeOrder.pickupLat, this.activeOrder.pickupLng);
  }

  private getDropoffLatLng(): L.LatLng | null {
    if (!this.activeOrder?.dropoffLat || !this.activeOrder?.dropoffLng) return null;
    return L.latLng(this.activeOrder.dropoffLat, this.activeOrder.dropoffLng);
  }

  private shortOrder(orderId?: string): string {
    return orderId ? `ORD-${orderId.slice(0, 6).toUpperCase()}` : '';
  }

  private addTimeline(title: string, text: string, tone: TimelineItem['tone']): void {
    this.timeline = [{
      id: ++this.timelineId, title, text, tone,
      time: new Date().toLocaleTimeString(),
    }, ...this.timeline].slice(0, 12);
  }
}
