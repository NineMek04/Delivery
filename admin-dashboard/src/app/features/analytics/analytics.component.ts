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
import { FormsModule } from '@angular/forms';
import {
  NgApexchartsModule,
  ApexAxisChartSeries,
  ApexChart,
  ApexXAxis,
  ApexStroke,
  ApexDataLabels,
  ApexYAxis,
  ApexLegend,
  ApexTooltip,
  ApexNonAxisChartSeries,
  ApexPlotOptions
} from 'ng-apexcharts';
import { Subscription, forkJoin } from 'rxjs';
import jsPDF from 'jspdf';
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
  imports: [CommonModule, FormsModule, NgApexchartsModule],
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

  dateFrom: string = '';
  dateTo: string = '';

  private map!: L.Map;
  private heatmapCircles: L.Circle[] = [];
  private readonly THAILAND_CENTER: L.LatLngTuple = [17.4138, 102.7872]; // Center around Udon Thani OSRM coverage

  // ── ApexCharts Line/Bar options ──────────────────────────
  public trendSeries: ApexAxisChartSeries = [];
  public trendChart: ApexChart = { type: 'line', height: 350, toolbar: { show: false }, background: 'transparent' };
  public trendXAxis: ApexXAxis = { type: 'category', categories: [], labels: { style: { colors: '#888' } } };
  public trendYAxis: ApexYAxis = { labels: { style: { colors: '#888' } } };
  public trendStroke: ApexStroke = { curve: 'smooth', width: 3 };
  public trendTooltip: ApexTooltip = { theme: 'dark' };
  public trendLegend: ApexLegend = { labels: { colors: '#888' } };

  // ── ApexCharts Pie options ──────────────────────────
  public utilSeries: ApexNonAxisChartSeries = [0, 0, 0];
  public utilChart: ApexChart = { type: 'donut', height: 300, background: 'transparent' };
  public utilLabels: string[] = ['Busy', 'Idle', 'Offline'];
  public utilColors: string[] = ['#FFC107', '#00FF66', '#6C757D'];
  public utilLegend: ApexLegend = { position: 'bottom', labels: { colors: '#888' } };
  public utilStroke: ApexStroke = { colors: ['#141414'] };
  public utilPlotOptions: ApexPlotOptions = { pie: { donut: { size: '70%' } } };

  // ── Revenue Bar Chart ──────────────────────────
  public revSeries: ApexAxisChartSeries = [];
  public revChart: ApexChart = { type: 'bar', height: 350, toolbar: { show: false }, background: 'transparent' };
  public revXAxis: ApexXAxis = { type: 'category', categories: [], labels: { style: { colors: '#888' } } };
  public revColors: string[] = ['#00E5FF'];

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

      this.trendXAxis = { ...this.trendXAxis, categories: labels };
      this.trendSeries = [
        { name: 'Total Orders', data: totalData, color: '#00E5FF' },
        { name: 'Completed Orders', data: completedData, color: '#00FF66' }
      ];

      // Simulated Revenue Data (multiply completed by avg fee ~50thb)
      const revData = completedData.map(c => c * 50);
      this.revXAxis = { ...this.revXAxis, categories: labels };
      this.revSeries = [{ name: 'Revenue', data: revData, color: '#00E5FF' }];
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

      this.utilSeries = [busy, idle, offline];
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

  // ── Exports ──────────────────────────────────────────────────
  exportCsv(): void {
    const csvContent = "data:text/csv;charset=utf-8," + 
      "Date,Total Orders,Completed Orders\n" +
      this.orderTrends.map(t => `${t.date},${t.totalOrders},${t.completedOrders}`).join("\n");
    const encodedUri = encodeURI(csvContent);
    const link = document.createElement("a");
    link.setAttribute("href", encodedUri);
    link.setAttribute("download", "analytics_report.csv");
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  }

  exportPdf(): void {
    const pdf = new jsPDF();
    pdf.text("Delivery Analytics Report", 20, 20);
    let y = 30;
    this.orderTrends.forEach(t => {
      pdf.text(`${t.date}: ${t.totalOrders} total / ${t.completedOrders} completed`, 20, y);
      y += 10;
    });
    pdf.save("analytics_report.pdf");
  }
}
