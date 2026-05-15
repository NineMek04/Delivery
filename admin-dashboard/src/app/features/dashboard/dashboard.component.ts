import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule, Plus, Truck, AlertTriangle, Warehouse, User, ChevronRight, ArrowUpRight, ArrowDownRight } from 'lucide-angular';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration } from 'chart.js';
import { Subscription, forkJoin } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
import { RiderService } from '../../core/services/rider.service';
import { TrackingSignalRService, RiderLocationUpdate } from '../../core/services/tracking-signalr.service';
import { OrderDto } from '../../api/generated/model/order-dto';
import { RiderDto } from '../../api/generated/model/rider-dto';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, LucideAngularModule, BaseChartDirective],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit, OnDestroy {
  icons = { Plus, Truck, AlertTriangle, Warehouse, User, ChevronRight, ArrowUpRight, ArrowDownRight };
  readonly title = 'Operational_Status';

  private readonly orderService = inject(OrderService);
  private readonly riderService = inject(RiderService);
  private readonly trackingService = inject(TrackingSignalRService);
  private readonly subscription = new Subscription();

  orders: OrderDto[] = [];
  riders: RiderDto[] = [];
  liveRiders: RiderLocationUpdate[] = [];
  isLoading = false;

  public chartData: ChartConfiguration<'line'>['data'] = {
    labels: ['00:00', '04:00', '08:00', '12:00', '16:00', '20:00', '23:59'],
    datasets: [
      {
        data: [0, 0, 0, 0, 0, 0, 0],
        label: 'Flux_Value',
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
    this.loadDashboardData();
    this.trackingService.startConnection();
    this.subscription.add(
      this.trackingService.riderLocations$.subscribe(locations => {
        this.liveRiders = Array.from(locations.values());
      })
    );
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }

  loadDashboardData(): void {
    this.isLoading = true;
    forkJoin({
      orders: this.orderService.getAll(),
      riders: this.riderService.getAll()
    }).subscribe({
      next: ({ orders, riders }: any) => {
        this.orders = this.unwrapList<OrderDto>(orders);
        this.riders = this.unwrapList<RiderDto>(riders);
        this.syncChart();
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
      }
    });
  }

  get activeOrders(): OrderDto[] {
    return this.orders.filter(order => !['DELIVERED', 'COMPLETED', 'CANCELLED'].includes(order.status || ''));
  }

  get pendingOrders(): OrderDto[] {
    return this.orders.filter(order => ['PENDING', 'ASSIGNED', 'PICKING_UP', 'DELIVERING'].includes(order.status || '')).slice(0, 4);
  }

  get routeCards(): OrderDto[] {
    return this.orders.slice(0, 5);
  }

  get riderUtilization(): number {
    if (!this.riders.length) return this.liveRiders.length ? 100 : 0;
    const busy = this.riders.filter(rider => ['DELIVERING', 'PICKING_UP', 'BUSY'].includes(rider.status || '')).length;
    return Math.round((busy / this.riders.length) * 100);
  }

  get totalDistanceKm(): number {
    return this.orders.reduce((sum, order) => sum + (order.distanceKm || 0), 0);
  }

  get totalFees(): number {
    return this.orders.reduce((sum, order) => sum + (order.deliveryFee || 0), 0);
  }

  statusClass(status?: string | null): string {
    const normalized = (status || 'PENDING').toLowerCase().replaceAll('_', '-');
    if (['delivered', 'completed'].includes(normalized)) return 'delivered';
    if (['delivering', 'picking-up', 'assigned'].includes(normalized)) return 'in-transit';
    if (normalized === 'cancelled') return 'cancelled';
    return 'pending';
  }

  shortId(id?: string | null): string {
    return id ? id.slice(0, 8).toUpperCase() : 'UNASSIGNED';
  }

  private syncChart(): void {
    const buckets = ['PENDING', 'ASSIGNED', 'PICKING_UP', 'DELIVERING', 'DELIVERED', 'COMPLETED', 'CANCELLED'];
    this.chartData = {
      ...this.chartData,
      labels: ['Pending', 'Assigned', 'Pickup', 'Delivering', 'Delivered', 'Complete', 'Cancel'],
      datasets: [{
        ...this.chartData.datasets[0],
        data: buckets.map(status => this.orders.filter(order => order.status === status).length)
      }]
    };
  }

  private unwrapList<T>(res: any): T[] {
    const value = res?.value ?? res;
    if (Array.isArray(value)) return value;
    if (Array.isArray(value?.items)) return value.items;
    return [];
  }
}
