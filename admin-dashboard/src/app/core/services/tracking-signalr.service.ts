import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import { RealtimeTelemetryDto, RiderUtilizationDto } from './analytics.service';
import { ToastService } from './toast.service';

export interface RiderLocationUpdate {
  riderId: string;
  latitude: number;
  longitude: number;
  snappedLat?: number;
  snappedLng?: number;
  isSnapped?: boolean;
  speedKmh?: number;
  status: string; // "IDLE", "DELIVERING", "PICKING_UP", etc.
  timestamp: string;
}

export interface DispatchOffer {
  offerId: string;
  version: number;
  expiresAt: string;
  riderId?: string;
  pickupRoute?: any;
  order: any;
}

export interface DispatchScanStarted {
  order: any;
  pickupLat: number;
  pickupLng: number;
  searchRadiusKm: number;
  nearbyRiders: any[];
  startedAt: string;
}

@Injectable({
  providedIn: 'root'
})
export class TrackingSignalRService {
  private hubConnection: signalR.HubConnection | null = null;
  
  private _riderLocations = new BehaviorSubject<Map<string, RiderLocationUpdate>>(new Map());
  public riderLocations$ = this._riderLocations.asObservable();

  private _alerts = new BehaviorSubject<any[]>([]);
  public alerts$ = this._alerts.asObservable();

  // New Observables for Map component to track dispatch phases
  private _offerReceived = new Subject<DispatchOffer>();
  public offerReceived$ = this._offerReceived.asObservable();

  private _dispatchScanStarted = new Subject<DispatchScanStarted>();
  public dispatchScanStarted$ = this._dispatchScanStarted.asObservable();

  private _dispatchCandidatesRanked = new Subject<any>();
  public dispatchCandidatesRanked$ = this._dispatchCandidatesRanked.asObservable();

  private _orderAssigned = new Subject<{ id: string; riderId: string; assignedAt: string }>();
  public orderAssigned$ = this._orderAssigned.asObservable();

  private _orderStatusChanged = new Subject<{ orderId: string; status: string }>();
  public orderStatusChanged$ = this._orderStatusChanged.asObservable();

  private _telemetryUpdated = new BehaviorSubject<{ telemetry: RealtimeTelemetryDto; utilization: RiderUtilizationDto } | null>(null);
  public telemetryUpdated$ = this._telemetryUpdated.asObservable();

  constructor(
    private authService: AuthService,
    private toastService: ToastService,
    private http: HttpClient
  ) {}

  public fetchInitialLocations(): void {
    const url = environment.config.baseConfig.apiUrl.replace('/api/v1', '') + '/api/v1/rider-locations';
    this.http.get<any>(url).subscribe({
      next: (response) => {
        if (response?.isSuccess && Array.isArray(response.data)) {
          const currentMap = this._riderLocations.getValue();
          for (const rider of response.data) {
            currentMap.set(rider.riderId || rider.RiderId, {
              riderId: rider.riderId || rider.RiderId,
              latitude: rider.lat ?? rider.Lat ?? 0,
              longitude: rider.lng ?? rider.Lng ?? 0,
              snappedLat: rider.snappedLat ?? rider.SnappedLat,
              snappedLng: rider.snappedLng ?? rider.SnappedLng,
              isSnapped: rider.isSnapped ?? rider.IsSnapped ?? false,
              speedKmh: rider.speedKmh ?? rider.SpeedKmh ?? 0,
              status: rider.status ?? rider.Status ?? 'OFFLINE',
              timestamp: rider.updatedAt ?? rider.UpdatedAt ?? new Date().toISOString()
            });
          }
          this._riderLocations.next(new Map(currentMap));
        }
      },
      error: (err) => console.error('Failed to fetch initial rider locations from Redis API', err)
    });
  }

  public getRiderLocations(): Map<string, RiderLocationUpdate> {
    return this._riderLocations.getValue();
  }

  public startConnection(): void {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      return; // Already connected
    }

    const token = this.authService.getToken();
    const hubUrl = environment.config.baseConfig.apiUrl.replace('/api/v1', '/hubs/tracking');

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: token ? () => token : undefined,
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true // Important for pure WebSockets
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000]) // Retry logic
      .build();

    this.hubConnection.start()
      .then(() => {
        console.log('SignalR connected to TrackingHub');
        this.addAlert('System', 'Connected to real-time dispatch network.', 'success');
        this.addListeners();
      })
      .catch(err => {
        console.error('Error while starting connection: ' + err);
        this.addAlert('Error', 'Failed to connect to real-time server.', 'danger');
      });
      
    this.hubConnection.onreconnecting(error => {
      console.warn('SignalR Reconnecting...', error);
      this.addAlert('Warning', 'Connection lost. Reconnecting...', 'warning');
    });

    this.hubConnection.onreconnected(connectionId => {
      console.log('SignalR Reconnected.', connectionId);
      this.addAlert('System', 'Connection restored.', 'success');
    });

    this.hubConnection.onclose(error => {
      console.error('SignalR Connection closed.', error);
    });
  }

  private addListeners(): void {
    if (!this.hubConnection) return;

    this.hubConnection.on('TelemetryUpdated', (data: any) => {
      const telemetry = data.telemetry || data.Telemetry;
      const utilization = data.utilization || data.Utilization;
      if (telemetry && utilization) {
        this._telemetryUpdated.next({
          telemetry: {
            activeRidersCount: telemetry.activeRidersCount ?? telemetry.ActiveRidersCount ?? 0,
            gpsUpdatesPerSecond: telemetry.gpsUpdatesPerSecond ?? telemetry.GpsUpdatesPerSecond ?? 0,
            dispatchQueueSize: telemetry.dispatchQueueSize ?? telemetry.DispatchQueueSize ?? 0
          },
          utilization: {
            ridersBusyCount: utilization.ridersBusyCount ?? utilization.RidersBusyCount ?? 0,
            ridersIdleCount: utilization.ridersIdleCount ?? utilization.RidersIdleCount ?? 0,
            ridersOfflineCount: utilization.ridersOfflineCount ?? utilization.RidersOfflineCount ?? 0,
            averageDeliveriesPerRider: utilization.averageDeliveriesPerRider ?? utilization.AverageDeliveriesPerRider ?? 0
          }
        });
      }
    });

    // Listen to rider location updates with robust coordinate property mapping
    this.hubConnection.on('RiderLocationUpdated', (data: any) => {
      const currentMap = this._riderLocations.getValue();
      
      const mappedData: RiderLocationUpdate = {
        riderId: data.riderId || data.RiderId,
        latitude: data.latitude != null ? data.latitude : (data.lat != null ? data.lat : (data.Lat != null ? data.Lat : 0)),
        longitude: data.longitude != null ? data.longitude : (data.lng != null ? data.lng : (data.Lng != null ? data.Lng : 0)),
        snappedLat: data.snappedLat != null ? data.snappedLat : (data.SnappedLat != null ? data.SnappedLat : undefined),
        snappedLng: data.snappedLng != null ? data.snappedLng : (data.SnappedLng != null ? data.SnappedLng : undefined),
        isSnapped: data.isSnapped != null ? data.isSnapped : (data.IsSnapped != null ? data.IsSnapped : false),
        status: data.status || data.Status || 'OFFLINE',
        timestamp: data.timestamp || data.Timestamp || new Date().toISOString()
      };

      currentMap.set(mappedData.riderId, mappedData);
      this._riderLocations.next(new Map(currentMap));
    });

    // Listen to OSRM road-snapped updates
    this.hubConnection.on('RiderLocationSnapped', (data: any) => {
      const currentMap = this._riderLocations.getValue();
      const riderId = data.riderId || data.RiderId;
      const existing = currentMap.get(riderId);
      
      const mappedData: RiderLocationUpdate = {
        riderId: riderId,
        latitude: data.latitude != null ? data.latitude : (data.lat != null ? data.lat : (data.Lat != null ? data.Lat : 0)),
        longitude: data.longitude != null ? data.longitude : (data.lng != null ? data.lng : (data.Lng != null ? data.Lng : 0)),
        snappedLat: data.latitude != null ? data.latitude : (data.lat != null ? data.lat : (data.Lat != null ? data.Lat : 0)), // Map Snapped update overrides
        snappedLng: data.longitude != null ? data.longitude : (data.lng != null ? data.lng : (data.Lng != null ? data.Lng : 0)),
        isSnapped: true,
        status: data.status || data.Status || existing?.status || 'OFFLINE',
        timestamp: data.timestamp || data.Timestamp || new Date().toISOString()
      };

      currentMap.set(mappedData.riderId, mappedData);
      this._riderLocations.next(new Map(currentMap));
    });

    // OfferReceived — Backend ยิงไปหา Rider โดยตรง (group rider:{id})
    this.hubConnection.on('OfferReceived', (offer: DispatchOffer) => {
      this.addAlert('AI Dispatcher', `Offer sent to rider (Order ${offer.order?.id?.slice(0, 8) || 'Unknown'})`, 'info');
      this._offerReceived.next(offer);
    });

    this.hubConnection.on('DispatchScanStarted', (data: DispatchScanStarted) => {
      const count = data.nearbyRiders?.length ?? 0;
      this.addAlert('AI Scan', `Scanning ${count} nearby riders for Order ${data.order?.id?.slice(0, 8) || 'Unknown'}`, 'info');
      this._dispatchScanStarted.next(data);
    });

    this.hubConnection.on('DispatchCandidatesRanked', (data: any) => {
      const winner = data.rankedCandidates?.[0]?.riderId || data.RankedCandidates?.[0]?.RiderId;
      this.addAlert('AI Rank', winner ? `Best rider candidate: ${winner.slice(0, 8)}` : 'Ranking completed', 'info');
      this._dispatchCandidatesRanked.next(data);
    });

    this.hubConnection.on('DispatchOfferSent', (offer: DispatchOffer) => {
      this.addAlert('Dispatch Offer', `Offer sent to Rider ${offer.riderId?.slice(0, 8) || 'Unknown'}`, 'info');
      this._offerReceived.next(offer);
    });

    // OrderAssigned — Backend broadcast ไปหา group admins เมื่อ Rider รับงาน
    this.hubConnection.on('OrderAssigned', (data: { id: string; riderId: string; assignedAt: string }) => {
      this.addAlert('Dispatch', `Order ${data.id?.slice(0, 8)} assigned to Rider ${data.riderId?.slice(0, 8)}`, 'success');
      this._orderAssigned.next(data);
    });

    // OrderStatusChanged — broadcast สถานะ Order เปลี่ยน
    this.hubConnection.on('OrderStatusChanged', (orderId: string, newStatus: string) => {
      this.addAlert('Order Update', `Order ${orderId?.slice(0, 8)} → ${newStatus}`, 'info');
      this._orderStatusChanged.next({ orderId, status: newStatus });
    });
  }

  public stopConnection(): void {
    if (this.hubConnection) {
      this.hubConnection.stop();
      this.hubConnection = null;
    }
  }

  private addAlert(title: string, text: string, tone: string) {
    const currentAlerts = this._alerts.getValue();
    const newAlert = {
      title, text, tone, time: new Date().toLocaleTimeString()
    };
    
    // Map tone to toast type
    let type: 'success' | 'error' | 'warning' | 'info' = 'info';
    if (tone === 'success') type = 'success';
    if (tone === 'danger') type = 'error';
    if (tone === 'warning') type = 'warning';
    
    this.toastService.show(title, text, type);

    // Keep only last 10 alerts for local observable fallback
    this._alerts.next([newAlert, ...currentAlerts].slice(0, 10));
  }
}
