import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { BehaviorSubject, Observable, Subject } from 'rxjs';

export interface RiderLocationUpdate {
  riderId: string;
  latitude: number;
  longitude: number;
  status: string; // "IDLE", "DELIVERING", "PICKING_UP", etc.
  timestamp: string;
}

export interface DispatchOffer {
  offerId: string;
  version: number;
  expiresAt: string;
  order: any;
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

  private _orderAssigned = new Subject<{ id: string; riderId: string; assignedAt: string }>();
  public orderAssigned$ = this._orderAssigned.asObservable();

  private _orderStatusChanged = new Subject<{ orderId: string; status: string }>();
  public orderStatusChanged$ = this._orderStatusChanged.asObservable();

  constructor(private authService: AuthService) {}

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

    // Listen to rider location updates with robust coordinate property mapping
    this.hubConnection.on('RiderLocationUpdated', (data: any) => {
      const currentMap = this._riderLocations.getValue();
      
      const mappedData: RiderLocationUpdate = {
        riderId: data.riderId || data.RiderId,
        latitude: data.latitude != null ? data.latitude : (data.lat != null ? data.lat : (data.Lat != null ? data.Lat : 0)),
        longitude: data.longitude != null ? data.longitude : (data.lng != null ? data.lng : (data.Lng != null ? data.Lng : 0)),
        status: data.status || data.Status || 'OFFLINE',
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
    // Keep only last 10 alerts
    this._alerts.next([newAlert, ...currentAlerts].slice(0, 10));
  }
}
