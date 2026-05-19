import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration } from 'chart.js';
import { OrderService } from '../../core/services/order.service';
import { RiderService } from '../../core/services/rider.service';
import { forkJoin } from 'rxjs';
import { OrderDto } from '../../api/generated/model/order-dto';
import { RiderDto } from '../../api/generated/model/rider-dto';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
  templateUrl: './analytics.component.html',
  styleUrl: './analytics.component.scss'
})
export class AnalyticsComponent implements OnInit {
  readonly title = 'Analytics_Performance';

  private readonly orderService = inject(OrderService);
  private readonly riderService = inject(RiderService);

  orders: OrderDto[] = [];
  riders: RiderDto[] = [];
  isLoading = false;

  // ── Chart config (pattern เดียวกับ DashboardComponent) ──────────
  public chartData: ChartConfiguration<'line'>['data'] = {
    labels: ['CREATED', 'MATCHING', 'OFFERING', 'ASSIGNED', 'PICKUP', 'DELIVERING', 'COMPLETED', 'CANCELLED'],
    datasets: [
      {
        data: [0, 0, 0, 0, 0, 0, 0, 0],
        label: 'Order_Volume',
        fill: true,
        tension: 0.4,
        borderColor: '#00FF66',
        backgroundColor: 'rgba(0, 255, 102, 0.1)',
        pointBackgroundColor: '#00FF66',
        pointBorderColor: '#000',
        pointHoverBackgroundColor: '#fff',
        pointHoverBorderColor: '#00FF66',
      }
    ]
  };

  public chartOptions: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    scales: {
      y: {
        beginAtZero: true,
        grid: { color: '#262626', drawTicks: false },
        border: { display: false },
        ticks: { color: '#888888', font: { family: 'JetBrains Mono', size: 10 } }
      },
      x: {
        grid: { display: false },
        border: { display: false },
        ticks: { color: '#888888', font: { family: 'JetBrains Mono', size: 10 } }
      }
    },
    plugins: {
      legend: { display: false },
      tooltip: {
        backgroundColor: '#141414',
        titleColor: '#00FF66',
        bodyColor: '#fff',
        borderColor: '#262626',
        borderWidth: 1,
        titleFont: { family: 'JetBrains Mono' },
        bodyFont: { family: 'JetBrains Mono' }
      }
    }
  };

  ngOnInit(): void {
    this.loadAnalytics();
  }

  loadAnalytics(): void {
    this.isLoading = true;
    forkJoin({
      orders: this.orderService.getAll(),
      riders: this.riderService.getAll()
    }).subscribe({
      next: ({ orders, riders }) => {
        this.orders = orders;
        this.riders = riders;
        this.syncChart();
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
      }
    });
  }

  private syncChart(): void {
    const buckets = ['CREATED', 'MATCHING', 'OFFERING', 'ASSIGNED', 'PICKING_UP', 'DELIVERING', 'COMPLETED', 'CANCELLED'];
    this.chartData = {
      ...this.chartData,
      datasets: [{
        ...this.chartData.datasets[0],
        data: buckets.map(status => this.orders.filter(order => order.status === status).length)
      }]
    };
  }

  get generatedRoutes(): number {
    return this.orders.length;
  }

  get kilometersOptimized(): number {
    return this.orders.reduce((sum, order) => sum + (order.distanceKm || 0), 0);
  }

  get successRate(): number {
    if (!this.orders.length) return 0;
    const successful = this.orders.filter(order => order.status === 'COMPLETED').length;
    return Math.round((successful / this.orders.length) * 1000) / 10;
  }

  get cancelledRate(): number {
    if (!this.orders.length) return 0;
    const cancelled = this.orders.filter(order => order.status === 'CANCELLED').length;
    return Math.round((cancelled / this.orders.length) * 1000) / 10;
  }

  get activeFleet(): number {
    return this.riders.filter(rider => rider.status !== 'OFFLINE').length;
  }

  get hubs() {
    // ใช้สถานะตรงตาม Backend State Machine
    const buckets = ['CREATED', 'ASSIGNED', 'DELIVERING', 'COMPLETED', 'CANCELLED'];
    return buckets.map(status => {
      const count = this.orders.filter(order => order.status === status).length;
      return {
        name: `${status}_STATE`,
        fleet: `${this.activeFleet} Riders`,
        rate: this.orders.length ? `${Math.round((count / this.orders.length) * 100)}%` : '0%',
        duration: `${count} Orders`,
        status: count ? 'ACTIVE' : 'CLEAR',
        tone: status === 'CANCELLED' ? 'red' : status === 'CREATED' ? 'amber' : 'blue'
      };
    });
  }
}
