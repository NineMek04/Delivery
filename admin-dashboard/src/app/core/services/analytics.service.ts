import { Injectable } from '@angular/core';
import { BaseApiService, unwrapValue } from './base-api.service';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { req } from '../http/delivery-http-request';

export interface DashboardStatsDto {
  activeRiders: number;
  idleRiders: number;
  ongoingOrders: number;
  pendingOrders: number;
  completedOrdersToday: number;
  totalRevenueToday: number;
}

export interface OrderTrendDto {
  date: string;
  totalOrders: number;
  completedOrders: number;
}

export interface RiderPerformanceDto {
  riderId: string;
  name: string;
  completedDeliveries: number;
  averageDeliveryTimeMinutes: number;
  totalEarned: number;
}

export interface AnalyticsSummaryDto {
  averageDeliveryTimeMinutes: number;
  successRatePercent: number;
  failedDispatchPercent: number;
  totalOrdersCount: number;
  completedOrdersCount: number;
  cancelledOrdersCount: number;
}

export interface RealtimeTelemetryDto {
  activeRidersCount: number;
  gpsUpdatesPerSecond: number;
  dispatchQueueSize: number;
}

export interface RiderUtilizationDto {
  ridersBusyCount: number;
  ridersIdleCount: number;
  ridersOfflineCount: number;
  averageDeliveriesPerRider: number;
}

export interface HeatmapPointDto {
  latitude: number;
  longitude: number;
  intensity: number;
}

@Injectable({
  providedIn: 'root'
})
export class AnalyticsService extends BaseApiService<any> {
  protected get endpoint(): string {
    return '/analytics';
  }

  public getDashboardStats(): Observable<DashboardStatsDto> {
    return req<any>(`${this.endpoint}/dashboard`)
      .get()
      .pipe(map(res => unwrapValue<DashboardStatsDto>(res)));
  }

  public getOrderTrends(days = 7): Observable<OrderTrendDto[]> {
    return req<any>(`${this.endpoint}/trends`)
      .queryString({ days })
      .get()
      .pipe(map(res => unwrapValue<OrderTrendDto[]>(res)));
  }

  public getTopRiders(count = 5): Observable<RiderPerformanceDto[]> {
    return req<any>(`${this.endpoint}/top-riders`)
      .queryString({ count })
      .get()
      .pipe(map(res => unwrapValue<RiderPerformanceDto[]>(res)));
  }

  public getSummary(): Observable<AnalyticsSummaryDto> {
    return req<any>(`${this.endpoint}/summary`)
      .get()
      .pipe(map(res => unwrapValue<AnalyticsSummaryDto>(res)));
  }

  public getRealtime(): Observable<RealtimeTelemetryDto> {
    return req<any>(`${this.endpoint}/realtime`)
      .get()
      .pipe(map(res => unwrapValue<RealtimeTelemetryDto>(res)));
  }

  public getRiderUtilization(): Observable<RiderUtilizationDto> {
    return req<any>(`${this.endpoint}/rider-utilization`)
      .get()
      .pipe(map(res => unwrapValue<RiderUtilizationDto>(res)));
  }

  public getHeatmap(): Observable<HeatmapPointDto[]> {
    return req<any>(`${this.endpoint}/heatmap`)
      .get()
      .pipe(map(res => unwrapValue<HeatmapPointDto[]>(res)));
  }
}
