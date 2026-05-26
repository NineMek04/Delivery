import { Injectable, OnDestroy } from '@angular/core';
import * as L from 'leaflet';
import 'leaflet.markercluster';
import { MapMathService } from './map-math.service';

export type RiderStatus = 'IDLE' | 'DELIVERING' | 'PICKING_UP' | 'BUSY' | 'OFFLINE' | string;

export interface RiderPopupData {
  riderId: string;
  label: string;
  status: RiderStatus;
  phone?: string;
  lat: number;
  lng: number;
}

/** Color palette for rider status */
const STATUS_COLORS: Record<string, string> = {
  IDLE:        '#22c55e',  // green
  AVAILABLE:   '#22c55e',
  DELIVERING:  '#f97316',  // orange
  PICKING_UP:  '#f97316',
  BUSY:        '#f97316',
  OFFLINE:     '#64748b',  // gray
};

/** Route color per order phase */
const ROUTE_COLORS = {
  notStarted: '#3b82f6',   // blue
  inProgress: '#f97316',   // orange
  completed:  '#22c55e',   // green
  pickup:     '#ef4444',   // red (dashed)
};

@Injectable()
export class MapDrawingService implements OnDestroy {
  private mapInstance?: L.Map;
  public markerType: 'sim' | 'dashboard' = 'sim';

  // ── Rider markers (non-clustered, for sim mode) ─────────────────────────
  public markerMap        = new Map<string, L.Marker>();
  public markerPositions  = new Map<string, L.LatLng>();
  public markerAnimations = new Map<string, number>();

  // ── Cluster layer (dashboard mode) ──────────────────────────────────────
  private clusterGroup?: L.MarkerClusterGroup;
  private clusterMarkers = new Map<string, L.Marker>();

  // ── Heatmap (optional canvas layer) ─────────────────────────────────────
  private heatmapLayer?: L.Layer;

  // ── Animation internals ──────────────────────────────────────────────────
  private riderPositionQueues  = new Map<string, { lat: number; lng: number; timestamp: number }[]>();
  private activeAnimationLoops = new Map<string, boolean>();

  // ── Route / sim layers ───────────────────────────────────────────────────
  public routeLines:    { pickup?: L.Polyline; delivery?: L.Polyline; completed?: L.Polyline } = {};
  public activeMarkers: { shop?: L.Marker; dropoff?: L.Marker } = {};
  public radarCircle?:  L.Circle;
  public candidateMarkers: L.CircleMarker[] = [];

  constructor(private math: MapMathService) {}

  // ─────────────────────────────────────────────────────────────────────────
  // Lifecycle
  // ─────────────────────────────────────────────────────────────────────────

  public initializeMap(map: L.Map): void {
    this.mapInstance = map;
  }

  ngOnDestroy(): void {
    this.markerAnimations.forEach(frame => cancelAnimationFrame(frame));
    this.clearActiveLayers();
    this.destroyCluster();
    this.mapInstance = undefined;
  }

  public get map(): L.Map | undefined {
    return this.mapInstance;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Marker Clustering
  // ─────────────────────────────────────────────────────────────────────────

  /** Initialize a MarkerClusterGroup and add it to the map */
  public initCluster(): void {
    if (!this.mapInstance || this.clusterGroup) return;

    this.clusterGroup = (L as any).markerClusterGroup({
      chunkedLoading: true,
      maxClusterRadius: 60,
      showCoverageOnHover: false,
      iconCreateFunction: (cluster: any) => this.createClusterIcon(cluster),
      spiderfyOnMaxZoom: true,
      removeOutsideVisibleBounds: true,
      animate: true,
      animateAddingMarkers: false,
    }) as L.MarkerClusterGroup;

    this.mapInstance.addLayer(this.clusterGroup);
  }

  private createClusterIcon(cluster: any): L.DivIcon {
    const count = cluster.getChildCount();
    const size  = count < 10 ? 36 : count < 50 ? 44 : 52;
    const color = count < 10 ? '#22c55e' : count < 30 ? '#f97316' : '#ef4444';

    return L.divIcon({
      html: `
        <div class="cluster-bubble" style="
          width:${size}px; height:${size}px;
          background: ${color};
          border: 3px solid rgba(255,255,255,0.9);
          border-radius: 50%;
          display: flex; align-items: center; justify-content: center;
          font: 900 ${count < 100 ? 13 : 11}px 'JetBrains Mono', monospace;
          color: #fff;
          box-shadow: 0 4px 16px rgba(0,0,0,0.35), 0 0 0 6px ${color}33;
        ">${count}</div>`,
      className: 'cluster-icon',
      iconSize:   [size, size],
      iconAnchor: [size / 2, size / 2],
    });
  }

  /** Upsert a rider into the cluster layer */
  public upsertClusterMarker(
    riderId: string,
    lat: number,
    lng: number,
    status: RiderStatus,
    popupHtml: string,
  ): void {
    if (!this.clusterGroup) return;

    const latlng = L.latLng(lat, lng);
    const existing = this.clusterMarkers.get(riderId);

    if (existing) {
      existing.setLatLng(latlng);
      existing.setIcon(this.createStatusIcon(riderId, status));
      existing.setPopupContent(popupHtml);
    } else {
      const marker = L.marker(latlng, { icon: this.createStatusIcon(riderId, status) });
      marker.bindPopup(popupHtml, { maxWidth: 280, className: 'rider-popup' });
      marker.on('dblclick', () => {
        this.mapInstance?.flyTo(latlng, 17, { animate: true, duration: 0.6 });
      });
      this.clusterMarkers.set(riderId, marker);
      this.clusterGroup.addLayer(marker);
    }
  }

  /** Remove riders from cluster that are not in the given set */
  public pruneClusterMarkers(activeIds: Set<string>): void {
    if (!this.clusterGroup) return;
    this.clusterMarkers.forEach((marker, id) => {
      if (!activeIds.has(id)) {
        this.clusterGroup!.removeLayer(marker);
        this.clusterMarkers.delete(id);
      }
    });
  }

  /** Show/hide riders by status in the cluster layer */
  public filterClusterByStatus(visibleStatuses: Set<string>): void {
    if (!this.clusterGroup || !this.mapInstance) return;
    this.clusterGroup.clearLayers();
    this.clusterMarkers.forEach((marker, _id) => {
      const popup = marker.getPopup();
      const statusMatch = popup?.getContent()?.toString() ?? '';
      const show = [...visibleStatuses].some(s => statusMatch.includes(s));
      if (show) this.clusterGroup!.addLayer(marker);
    });
  }

  public destroyCluster(): void {
    if (this.clusterGroup && this.mapInstance) {
      this.mapInstance.removeLayer(this.clusterGroup);
    }
    this.clusterGroup = undefined;
    this.clusterMarkers.clear();
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Status-coded Icons
  // ─────────────────────────────────────────────────────────────────────────

  public createStatusIcon(riderId: string, status: RiderStatus, bearing = 0): L.DivIcon {
    const color  = STATUS_COLORS[status.toUpperCase()] ?? STATUS_COLORS['OFFLINE'];
    const emoji  = status === 'OFFLINE' ? '⚫' : '🛵';
    const pulse  = ['DELIVERING', 'PICKING_UP', 'BUSY'].includes(status.toUpperCase())
      ? `box-shadow: 0 0 0 6px ${color}33, 0 4px 14px rgba(0,0,0,0.4);` : '';

    return L.divIcon({
      className: 'custom-status-marker',
      html: `
        <div style="
          background:${color};
          width:32px; height:32px;
          border-radius:50%;
          border:3px solid rgba(255,255,255,0.95);
          display:flex; align-items:center; justify-content:center;
          font-size:15px;
          transform:rotate(${bearing}deg);
          transition:transform 0.1s linear;
          ${pulse}
        ">${emoji}</div>`,
      iconSize:   [32, 32],
      iconAnchor: [16, 16],
    });
  }

  /** Build the popup HTML for a rider */
  public buildRiderPopupHtml(data: RiderPopupData): string {
    const color   = STATUS_COLORS[data.status.toUpperCase()] ?? '#64748b';
    const initials = data.label.slice(-2).toUpperCase();
    return `
      <div class="rp-wrap">
        <div class="rp-header" style="border-left:4px solid ${color}">
          <div class="rp-avatar" style="background:${color}">${initials}</div>
          <div>
            <div class="rp-name">${data.label}</div>
            <div class="rp-status" style="color:${color}">${data.status}</div>
          </div>
        </div>
        <div class="rp-coords">${data.lat.toFixed(5)}, ${data.lng.toFixed(5)}</div>
        ${data.phone ? `<div class="rp-phone">📞 ${data.phone}</div>` : ''}
        <div class="rp-actions">
          <button class="rp-btn accept" onclick="window._riderAction?.('accept','${data.riderId}')">✔ Accept</button>
          <button class="rp-btn reject" onclick="window._riderAction?.('reject','${data.riderId}')">✖ Reject</button>
        </div>
      </div>`;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Route Drawing (color-coded by phase)
  // ─────────────────────────────────────────────────────────────────────────

  /** Draw a polyline with color based on delivery phase */
  public drawColoredRoute(
    coords:  L.LatLng[],
    phase:   'notStarted' | 'inProgress' | 'completed' | 'pickup',
    dashed = false,
  ): L.Polyline | undefined {
    if (!this.mapInstance || !coords.length) return undefined;
    const color  = ROUTE_COLORS[phase];
    const weight = phase === 'completed' ? 3 : 5;
    return L.polyline(coords, {
      color,
      weight,
      opacity: phase === 'completed' ? 0.65 : 0.9,
      dashArray: dashed ? '10, 8' : undefined,
      lineCap:   'round',
      lineJoin:  'round',
    }).addTo(this.mapInstance);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Heatmap (canvas-based, no external lib needed)
  // ─────────────────────────────────────────────────────────────────────────

  /** Render a simple canvas heatmap using a Leaflet custom layer */
  public showHeatmap(points: { lat: number; lng: number; intensity?: number }[]): void {
    if (!this.mapInstance) return;
    this.removeHeatmap();

    const CanvasHeatLayer = (L as any).Layer.extend({
      initialize(pts: typeof points) { this._points = pts; },
      onAdd(map: L.Map) {
        this._map = map;
        this._canvas = document.createElement('canvas');
        this._canvas.style.cssText = 'position:absolute;top:0;left:0;z-index:400;pointer-events:none;';
        map.getPanes().overlayPane!.appendChild(this._canvas);
        map.on('viewreset moveend zoomend', this._draw, this);
        this._draw();
      },
      onRemove(map: L.Map) {
        map.off('viewreset moveend zoomend', this._draw, this);
        this._canvas?.parentNode?.removeChild(this._canvas);
      },
      _draw() {
        const map    = this._map;
        const canvas = this._canvas as HTMLCanvasElement;
        const size   = map.getSize();
        canvas.width  = size.x;
        canvas.height = size.y;
        const ctx  = canvas.getContext('2d')!;
        const pane = map.getPanes().overlayPane as HTMLElement;
        const topLeft = map.containerPointToLayerPoint([0, 0]);
        L.DomUtil.setPosition(canvas, topLeft);

        ctx.clearRect(0, 0, canvas.width, canvas.height);
        (this._points as typeof points).forEach(p => {
          const pt = map.latLngToContainerPoint([p.lat, p.lng]);
          const r  = 32;
          const gr = ctx.createRadialGradient(pt.x, pt.y, 0, pt.x, pt.y, r);
          const a  = Math.min(1, (p.intensity ?? 0.6));
          gr.addColorStop(0,   `rgba(249,115,22,${a})`);
          gr.addColorStop(0.5, `rgba(234,179,8,${a * 0.4})`);
          gr.addColorStop(1,   'rgba(0,0,0,0)');
          ctx.beginPath();
          ctx.fillStyle = gr;
          ctx.arc(pt.x, pt.y, r, 0, Math.PI * 2);
          ctx.fill();
        });
        // Multiply blend for richer density color
        ctx.globalCompositeOperation = 'source-over';
      }
    });

    this.heatmapLayer = new CanvasHeatLayer(points);
    this.mapInstance.addLayer(this.heatmapLayer!);
  }

  public removeHeatmap(): void {
    if (this.heatmapLayer && this.mapInstance) {
      this.mapInstance.removeLayer(this.heatmapLayer);
      this.heatmapLayer = undefined;
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Original sim-mode API (preserved)
  // ─────────────────────────────────────────────────────────────────────────

  public createRiderIcon(riderId: string, assignedRiderId: string | null, bearing = 0, status = 'IDLE'): L.DivIcon {
    const isWinner = riderId === assignedRiderId;
    if (this.markerType === 'dashboard') {
      return this.createStatusIcon(riderId, status as RiderStatus, bearing);
    }
    // sim mode
    const winner = isWinner ? ' winner' : '';
    return L.divIcon({
      className: 'sim-marker',
      html: `<div class="sim-marker-core${winner}" style="transform:rotate(${bearing}deg)">R</div>`,
      iconSize:   [34, 34],
      iconAnchor: [17, 17],
    });
  }

  public createStaticMarker(latLng: L.LatLng, label: string, tone: 'shop' | 'dropoff'): L.Marker {
    return L.marker(latLng, {
      icon: L.divIcon({
        className: 'sim-marker',
        html: `<div class="sim-marker-core ${tone}">${label}</div>`,
        iconSize:   [36, 36],
        iconAnchor: [18, 18],
      })
    });
  }

  public animateMarker(
    riderId:         string,
    assignedRiderId: string | null,
    marker:          L.Marker,
    next:            L.LatLng,
    status           = 'IDLE',
    onComplete?:     () => void,
  ): void {
    const iconElement = marker.getElement();
    if (iconElement) iconElement.style.transition = 'none';

    if (!this.riderPositionQueues.has(riderId)) this.riderPositionQueues.set(riderId, []);
    const queue = this.riderPositionQueues.get(riderId)!;
    queue.push({ lat: next.lat, lng: next.lng, timestamp: performance.now() });
    if (queue.length > 5) queue.shift();

    if (!this.activeAnimationLoops.get(riderId)) {
      this.activeAnimationLoops.set(riderId, true);
      this.processQueueGlide(riderId, assignedRiderId, marker, status, onComplete);
    }
  }

  private processQueueGlide(
    riderId:         string,
    assignedRiderId: string | null,
    marker:          L.Marker,
    status:          string,
    onComplete?:     () => void,
  ): void {
    const queue = this.riderPositionQueues.get(riderId);
    if (!queue || queue.length < 2) {
      this.activeAnimationLoops.set(riderId, false);
      return;
    }

    const startPoint  = queue[0];
    const targetPoint = queue[1];
    const startTime   = performance.now();
    const dynamicDuration = Math.max(120, Math.min(1000, targetPoint.timestamp - startPoint.timestamp));

    const startLatLng  = L.latLng(startPoint.lat, startPoint.lng);
    const targetLatLng = L.latLng(targetPoint.lat, targetPoint.lng);
    const bearing      = this.math.calculateBearing(startLatLng, targetLatLng);

    const tick = () => {
      const queueNow = this.riderPositionQueues.get(riderId);
      if (!queueNow || queueNow.length < 2) {
        this.activeAnimationLoops.set(riderId, false);
        return;
      }

      const elapsed  = performance.now() - startTime;
      const progress = Math.min(elapsed / dynamicDuration, 1);
      const currentLat  = startPoint.lat + (targetPoint.lat - startPoint.lat) * progress;
      const currentLng  = startPoint.lng + (targetPoint.lng - startPoint.lng) * progress;
      const currentLatLng = L.latLng(currentLat, currentLng);

      marker.setLatLng(currentLatLng);
      marker.setIcon(this.createRiderIcon(riderId, assignedRiderId, bearing, status));
      this.updateIconStyle(marker, riderId, assignedRiderId, status, bearing);

      if (progress < 1) {
        const frameId = requestAnimationFrame(tick);
        this.markerAnimations.set(riderId, frameId);
      } else {
        queue.shift();
        if (onComplete) onComplete();
        this.processQueueGlide(riderId, assignedRiderId, marker, status, onComplete);
      }
    };

    const frameId = requestAnimationFrame(tick);
    this.markerAnimations.set(riderId, frameId);
  }

  private updateIconStyle(marker: L.Marker, riderId: string, assignedRiderId: string | null, status: string, bearing: number): void {
    const iconElement = marker.getElement();
    if (!iconElement) return;
    iconElement.style.transformOrigin = 'center center';
    if (this.markerType === 'dashboard') {
      const base = iconElement.style.transform.replace(/rotate\([^)]*\)/g, '');
      iconElement.style.transform = `${base} rotate(${bearing}deg)`;
    }
  }

  public refreshMarkerIcons(assignedRiderId: string | null): void {
    this.markerMap.forEach((marker, riderId) => marker.setIcon(this.createRiderIcon(riderId, assignedRiderId)));
  }

  public drawOfferRoutes(
    pickupCoords:    L.LatLng[],
    deliveryCoords:  L.LatLng[],
    defaultPickup:   L.LatLng | null,
    defaultDropoff:  L.LatLng | null,
  ): void {
    if (!this.mapInstance) return;
    this.routeLines.pickup?.remove();
    this.routeLines.delivery?.remove();

    if (pickupCoords.length) {
      this.routeLines.pickup = this.drawColoredRoute(pickupCoords, 'pickup', true);
    }

    const finalCoords = deliveryCoords.length
      ? deliveryCoords
      : defaultPickup && defaultDropoff ? [defaultPickup, defaultDropoff] : [];

    if (finalCoords.length) {
      this.routeLines.delivery = this.drawColoredRoute(finalCoords, 'notStarted');
    }
  }

  public clearCandidateMarkers(): void {
    this.candidateMarkers.forEach(m => m.remove());
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
    this.routeLines    = {};
    this.activeMarkers = {};
  }
}
