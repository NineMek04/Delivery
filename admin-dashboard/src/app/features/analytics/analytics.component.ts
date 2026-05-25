import {
  Component,
  OnInit,
  OnDestroy,
  AfterViewInit,
  inject,
  ElementRef,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration } from 'chart.js';
import { Subscription, forkJoin, interval } from 'rxjs';
import * as L from 'leaflet';
import {
  AnalyticsService,
  AnalyticsSummaryDto,
  RealtimeTelemetryDto,
  RiderUtilizationDto,
  HeatmapPointDto,
  RiderPerformanceDto,
  OrderTrendDto,
} from '../../core/services/analytics.service';
import { TrackingSignalRService } from '../../core/services/tracking-signalr.service';

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
  shadowSize: [41, 41],
});
L.Marker.prototype.options.icon = iconDefault;

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
  templateUrl: './analytics.component.html',
  styleUrl: './analytics.component.scss',
})
export class AnalyticsComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('mapElement', { static: true }) mapElement!: ElementRef;

  readonly title = 'Analytics_Performance';

  private readonly analyticsService = inject(AnalyticsService);
  private readonly signalRService = inject(TrackingSignalRService);
  private subscriptions = new Subscription();

  isLoading = false;
  summary: AnalyticsSummaryDto | null = null;
  telemetry: RealtimeTelemetryDto | null = null;
  riderUtilization: RiderUtilizationDto | null = null;
  heatmapPoints: HeatmapPointDto[] = [];
  topRiders: RiderPerformanceDto[] = [];
  orderTrends: OrderTrendDto[] = [];

  // Cache to prevent chart DOM thrashing / redundant redraws
  private lastRidersBusy = -1;
  private lastRidersIdle = -1;
  private lastRidersOffline = -1;
  private lastAvgDeliveries = -1.0;

  private map!: L.Map;
  private heatmapCircles: L.Circle[] = [];
  private readonly THAILAND_CENTER: L.LatLngTuple = [17.4138, 102.7872]; // Center around Udon Thani OSRM coverage

  // ── Delivery Trend Chart configuration ──────────────────────────
  public trendChartData: ChartConfiguration<'line'>['data'] = {
    labels: [],
    datasets: [
      {
        data: [],
        label: 'Total Orders',
        fill: true,
        tension: 0.4,
        borderColor: '#00E5FF', // Neon Blue
        backgroundColor: 'rgba(0, 229, 255, 0.05)',
        pointBackgroundColor: '#00E5FF',
        pointBorderColor: '#000',
        pointHoverBackgroundColor: '#fff',
        pointHoverBorderColor: '#00E5FF',
      },
      {
        data: [],
        label: 'Completed Orders',
        fill: true,
        tension: 0.4,
        borderColor: '#00FF66', // Neon Green
        backgroundColor: 'rgba(0, 255, 102, 0.05)',
        pointBackgroundColor: '#00FF66',
        pointBorderColor: '#000',
        pointHoverBackgroundColor: '#fff',
        pointHoverBorderColor: '#00FF66',
      },
    ],
  };

  public trendChartOptions: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    scales: {
      y: {
        beginAtZero: true,
        grid: { color: '#222222', drawTicks: false },
        border: { display: false },
        ticks: {
          color: '#888888',
          font: { family: 'JetBrains Mono', size: 10 },
        },
      },
      x: {
        grid: { display: false },
        border: { display: false },
        ticks: {
          color: '#888888',
          font: { family: 'JetBrains Mono', size: 10 },
        },
      },
    },
    plugins: {
      legend: {
        display: true,
        position: 'top',
        labels: {
          color: '#888888',
          font: { family: 'JetBrains Mono', size: 10 },
        },
      },
      tooltip: {
        backgroundColor: '#141414',
        titleColor: '#00FF66',
        bodyColor: '#fff',
        borderColor: '#222222',
        borderWidth: 1,
        titleFont: { family: 'JetBrains Mono' },
        bodyFont: { family: 'JetBrains Mono' },
      },
    },
  };

  // ── Rider Utilization Chart configuration ───────────────────────
  public utilizationChartData: ChartConfiguration<'doughnut'>['data'] = {
    labels: ['Busy', 'Idle', 'Offline'],
    datasets: [
      {
        data: [0, 0, 0],
        backgroundColor: [
          '#FFC107', // Busy - Amber
          '#00FF66', // Idle - Neon Green
          '#6C757D', // Offline - Slate Gray
        ],
        hoverBackgroundColor: ['#FFD54F', '#33FF88', '#8A959E'],
        borderColor: '#141414',
        borderWidth: 3,
      },
    ],
  };

  public utilizationChartOptions: ChartConfiguration<'doughnut'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    cutout: '70%',
    plugins: {
      legend: {
        display: true,
        position: 'bottom',
        labels: {
          color: '#888888',
          font: { family: 'JetBrains Mono', size: 10 },
        },
      },
      tooltip: {
        backgroundColor: '#141414',
        titleColor: '#00FF66',
        bodyColor: '#fff',
        borderColor: '#222222',
        borderWidth: 1,
        titleFont: { family: 'JetBrains Mono' },
        bodyFont: { family: 'JetBrains Mono' },
      },
    },
  };

  ngOnInit(): void {
    // Connect to real-time SignalR network
    this.signalRService.startConnection();

    // Backend Controlled Aggregation — Live Telemetry Push
    this.subscriptions.add(
      this.signalRService.telemetryUpdated$.subscribe((data) => {
        if (!data) return;
        this.telemetry = data.telemetry;
        this.riderUtilization = data.utilization;
        this.syncUtilizationChart();
      }),
    );

    // Dynamic reload when critical order state machine transitions happen
    this.subscriptions.add(
      this.signalRService.orderStatusChanged$.subscribe(() => {
        this.loadAnalytics();
      }),
    );
  }

  ngAfterViewInit(): void {
    this.initMap();
    this.loadAnalytics();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    if (this.map) {
      this.map.remove();
    }
  }

  private initMap(): void {
    if (!this.mapElement) return;

    this.map = L.map(this.mapElement.nativeElement, {
      center: this.THAILAND_CENTER,
      zoom: 12,
      minZoom: 6,
      maxZoom: 18,
    });

    // Dark sleek high-tech map style for presentation readiness
    L.tileLayer(
      'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',
      {
        attribution:
          '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/">CARTO</a>',
        subdomains: 'abcd',
        maxZoom: 18,
      },
    ).addTo(this.map);
  }

  loadAnalytics(): void {
    this.isLoading = true;
    forkJoin({
      summary: this.analyticsService.getSummary(),
      telemetry: this.analyticsService.getRealtime(),
      utilization: this.analyticsService.getRiderUtilization(),
      heatmap: this.analyticsService.getHeatmap(),
      trends: this.analyticsService.getOrderTrends(7),
      topRiders: this.analyticsService.getTopRiders(5),
    }).subscribe({
      next: ({
        summary,
        telemetry,
        utilization,
        heatmap,
        trends,
        topRiders,
      }) => {
        this.summary = summary;
        this.telemetry = telemetry;
        this.riderUtilization = utilization;
        this.heatmapPoints = heatmap;
        this.orderTrends = trends;
        this.topRiders = topRiders;

        this.syncTrendChart();
        this.syncUtilizationChart();
        this.updateHeatmapOnMap();
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Failed to load telemetry aggregates from backend:', err);
        this.isLoading = false;
      },
    });
  }

  private syncTrendChart(): void {
    if (this.orderTrends && this.orderTrends.length > 0) {
      const sortedTrends = [...this.orderTrends].sort(
        (a, b) => new Date(a.date).getTime() - new Date(b.date).getTime(),
      );

      const labels = sortedTrends.map((t) => {
        const dateObj = new Date(t.date);
        return `${dateObj.getMonth() + 1}/${dateObj.getDate()}`;
      });
      const totalData = sortedTrends.map((t) => t.totalOrders);
      const completedData = sortedTrends.map((t) => t.completedOrders);

      this.trendChartData = {
        labels: labels,
        datasets: [
          {
            ...this.trendChartData.datasets[0],
            data: totalData,
          },
          {
            ...this.trendChartData.datasets[1],
            data: completedData,
          },
        ],
      };
    }
  }

  private syncUtilizationChart(): void {
    if (this.riderUtilization) {
      const busy = this.riderUtilization.ridersBusyCount;
      const idle = this.riderUtilization.ridersIdleCount;
      const offline = this.riderUtilization.ridersOfflineCount;
      const avg = this.riderUtilization.averageDeliveriesPerRider;

      if (
        busy === this.lastRidersBusy &&
        idle === this.lastRidersIdle &&
        offline === this.lastRidersOffline &&
        avg === this.lastAvgDeliveries
      ) {
        return; // Skip recreation if data has not changed to avoid DOM thrashing
      }

      this.lastRidersBusy = busy;
      this.lastRidersIdle = idle;
      this.lastRidersOffline = offline;
      this.lastAvgDeliveries = avg;

      this.utilizationChartData = {
        labels: ['Busy', 'Idle', 'Offline'],
        datasets: [
          {
            ...this.utilizationChartData.datasets[0],
            data: [busy, idle, offline],
          },
        ],
      };
    }
  }

  private updateHeatmapOnMap(): void {
    if (!this.map) return;

    this.heatmapCircles.forEach((circle) => circle.remove());
    this.heatmapCircles = [];

    this.heatmapPoints.forEach((point) => {
      const radius = 100 + point.intensity * 200; // dynamic radius
      const fillOpacity = 0.15 + point.intensity * 0.25;
      const color =
        point.intensity > 0.7
          ? '#FF3333'
          : point.intensity > 0.4
            ? '#FF7700'
            : '#FFBB00';

      const circle = L.circle([point.latitude, point.longitude], {
        radius: radius,
        fillColor: color,
        fillOpacity: fillOpacity,
        color: color,
        weight: 1.5,
        opacity: 0.4,
      }).addTo(this.map);

      circle.bindTooltip(`Intensity: ${Math.round(point.intensity * 100)}%`, {
        direction: 'top',
        className: 'custom-shop-tooltip',
      });

      circle.bindPopup(`
        <div style="font-family: 'JetBrains Mono', sans-serif; font-size: 11px; min-width: 140px; color: #fff; background: #141414; padding: 4px; border-radius: 4px;">
          <b style="color: ${color}">🔥 HOTZONE DEMAND</b><br>
          <hr style="margin: 6px 0; border: 0; border-top: 1px solid #333;">
          Density Index: ${Math.round(point.intensity * 100)}%<br>
          Coordinates:<br>
          ${point.latitude.toFixed(5)}, ${point.longitude.toFixed(5)}
        </div>
      `);

      this.heatmapCircles.push(circle);
    });

    if (this.heatmapPoints.length > 0 && this.heatmapCircles.length > 0) {
      // Recenter only on first initialization if needed
      const topPoint = this.heatmapPoints.reduce(
        (max, p) => (p.intensity > max.intensity ? p : max),
        this.heatmapPoints[0],
      );
      this.map.panTo([topPoint.latitude, topPoint.longitude]);
    }
  }

  // ── Template Getters ────────────────────────────────────────────

  get averageDeliveryTime(): number {
    return this.summary
      ? Math.round(this.summary.averageDeliveryTimeMinutes * 10) / 10
      : 0;
  }

  get successRate(): number {
    return this.summary
      ? Math.round(this.summary.successRatePercent * 10) / 10
      : 0;
  }

  get failedDispatchRate(): number {
    return this.summary
      ? Math.round(this.summary.failedDispatchPercent * 10) / 10
      : 0;
  }

  get totalOrdersCount(): number {
    return this.summary ? this.summary.totalOrdersCount : 0;
  }

  get completedOrdersCount(): number {
    return this.summary ? this.summary.completedOrdersCount : 0;
  }

  get cancelledOrdersCount(): number {
    return this.summary ? this.summary.cancelledOrdersCount : 0;
  }

  get activeFleet(): number {
    if (!this.riderUtilization) return 0;
    return (
      this.riderUtilization.ridersBusyCount +
      this.riderUtilization.ridersIdleCount
    );
  }

  get telemetryUpdatesPerSecond(): number {
    return this.telemetry
      ? Math.round(this.telemetry.gpsUpdatesPerSecond * 10) / 10
      : 0;
  }

  get telemetryActiveRiders(): number {
    return this.telemetry ? this.telemetry.activeRidersCount : 0;
  }

  get telemetryQueueSize(): number {
    return this.telemetry ? this.telemetry.dispatchQueueSize : 0;
  }
}
