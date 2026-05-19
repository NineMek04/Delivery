import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrderService } from '../../core/services/order.service';
import { OrderDto } from '../../api/generated/model/order-dto';
import Swal from 'sweetalert2';
import { LucideAngularModule, RefreshCcw, Search, Plus, XCircle, RotateCcw, Info } from 'lucide-angular';

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  templateUrl: './orders.component.html',
  styleUrl: './orders.component.scss'
})
export class OrdersComponent implements OnInit {
  readonly title = 'Order_Operations';
  readonly icons = { RefreshCcw, Search, Plus, XCircle, RotateCcw, Info };
  
  private orderService = inject(OrderService);
  public orders: OrderDto[] = [];
  public isLoading = false;
  public query = '';

  // Modal state
  selectedOrder: OrderDto | null = null;
  showDetailModal = false;

  ngOnInit(): void {
    this.loadOrders();
  }

  loadOrders(): void {
    this.isLoading = true;
    this.orderService.getAll().subscribe({
      next: (orders) => {
        this.orders = orders;
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
      }
    });
  }

  getStatusTone(status?: string | null): string {
    switch (status) {
      case 'CREATED': return 'gray';
      case 'MATCHING': return 'purple';
      case 'OFFERING': return 'amber';
      case 'ASSIGNED': return 'blue';
      case 'PICKING_UP': return 'amber';
      case 'DELIVERING': return 'blue';
      case 'COMPLETED': return 'green';
      case 'CANCELLED': return 'red';
      default: return 'gray';
    }
  }

  get filteredOrders(): OrderDto[] {
    const q = this.query.trim().toLowerCase();
    if (!q) return this.orders;
    return this.orders.filter(order =>
      (order.id || '').toLowerCase().includes(q) ||
      (order.status || '').toLowerCase().includes(q) ||
      (order.assignedRiderId || '').toLowerCase().includes(q)
    );
  }

  get activeCount(): number {
    return this.orders.filter(order => !['COMPLETED', 'CANCELLED'].includes(order.status || '')).length;
  }

  get pendingCount(): number {
    return this.orders.filter(order => ['CREATED', 'MATCHING', 'OFFERING'].includes(order.status || '')).length;
  }

  get completedCount(): number {
    return this.orders.filter(order => order.status === 'COMPLETED').length;
  }

  get totalFees(): number {
    return this.orders.reduce((sum, order) => sum + (order.deliveryFee || 0), 0);
  }

  shortId(id?: string | null): string {
    return id ? `${id.slice(0, 8).toUpperCase()}...` : 'UNASSIGNED';
  }

  openOrderDetail(order: OrderDto): void {
    this.selectedOrder = order;
    this.showDetailModal = true;
  }

  closeOrderDetail(): void {
    this.showDetailModal = false;
    this.selectedOrder = null;
  }

  cancelOrder(id?: string | null): void {
    if (!id) return;
    
    Swal.fire({
      title: 'Are you sure?',
      text: "You won't be able to revert this!",
      icon: 'warning',
      showCancelButton: true,
      confirmButtonColor: '#d33',
      cancelButtonColor: '#3085d6',
      confirmButtonText: 'Yes, cancel it!'
    }).then((result) => {
      if (result.isConfirmed) {
        this.orderService.cancelOrder(id).subscribe(() => {
          Swal.fire('Cancelled!', 'The order has been cancelled.', 'success');
          this.loadOrders();
        });
      }
    });
  }

  retryDispatch(id?: string | null): void {
    if (!id) return;
    this.orderService.retryDispatch(id).subscribe(() => {
      Swal.fire('Dispatched!', 'The system is looking for a new rider.', 'success');
      this.loadOrders();
    });
  }
}
