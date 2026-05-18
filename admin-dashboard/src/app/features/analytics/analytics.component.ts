import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { OrderService } from '../../core/services/order.service';
import { RiderService } from '../../core/services/rider.service';
import { forkJoin } from 'rxjs';
import { OrderDto } from '../../api/generated/model/order-dto';
import { RiderDto } from '../../api/generated/model/rider-dto';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule],
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

  ngOnInit(): void {
    this.loadAnalytics();
  }

  loadAnalytics(): void {
    this.isLoading = true;
    // BaseApiService.getAll() ทำการ unwrap ApiResponse + PaginatedResult ให้อัตโนมัติแล้ว
    forkJoin({
      orders: this.orderService.getAll(),
      riders: this.riderService.getAll()
    }).subscribe({
      next: ({ orders, riders }) => {
        this.orders = orders;
        this.riders = riders;
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
      }
    });
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
