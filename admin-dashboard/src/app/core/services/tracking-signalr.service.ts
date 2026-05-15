import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { BehaviorSubject, Observable } from 'rxjs';

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

  constructor(private authService: AuthService) {}

  public startConnection(): void {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      return; // Already connected
    }

    const token = this.authService.getToken();
    if (!token) {
      console.warn('No token found. Cannot start SignalR connection.');
      return;
    }

    const hubUrl = environment.config.baseConfig.apiUrl.replace('/api/v1', '/hubs/tracking');

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token,
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

    // Listen to rider location updates
    this.hubConnection.on('RiderLocationUpdated', (data: RiderLocationUpdate) => {
      const currentMap = this._riderLocations.getValue();
      currentMap.set(data.riderId, data);
      
      // Emit a new map to trigger change detection
      this._riderLocations.next(new Map(currentMap));
    });

    // Listen to dispatch events
    this.hubConnection.on('OfferSent', (riderId: string, offer: DispatchOffer) => {
      this.addAlert('AI Dispatcher', `Sent an offer to Rider ${riderId} (Order ${offer.order?.id || 'Unknown'})`, 'info');
    });
    
    this.hubConnection.on('OrderStatusChanged', (orderId: string, newStatus: string) => {
      this.addAlert('Order Update', `Order ${orderId} is now ${newStatus}`, 'info');
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
