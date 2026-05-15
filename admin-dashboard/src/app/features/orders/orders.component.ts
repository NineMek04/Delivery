import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { OrderService } from '../../core/services/order.service';
import { OrderDto } from '../../api/generated/model/order-dto';
import Swal from 'sweetalert2';

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './orders.component.html',
  styleUrl: './orders.component.scss'
})
export class OrdersComponent implements OnInit {
  readonly title = 'FleetControl AI';
  
  private orderService = inject(OrderService);
  public orders: OrderDto[] = [];
  public isLoading = false;

  ngOnInit(): void {
    this.loadOrders();
  }

  loadOrders(): void {
    this.isLoading = true;
    this.orderService.getAll().subscribe({
      next: (res: any) => {
        // Backend returns PaginatedResult in value, or just a list. 
        // Need to check structure. Assume it's PaginatedResult wrapped in ApiResponse.
        this.orders = res.value?.items || res.value || [];
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
        // ErrorInterceptor will handle the SweetAlert
      }
    });
  }

  getStatusTone(status?: string): string {
    switch (status) {
      case 'PENDING': return 'gray';
      case 'ASSIGNED': return 'blue';
      case 'PICKING_UP': return 'amber';
      case 'DELIVERING': return 'blue';
      case 'DELIVERED': return 'green';
      case 'COMPLETED': return 'green';
      case 'CANCELLED': return 'red';
      default: return 'gray';
    }
  }

  cancelOrder(id?: string): void {
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

  retryDispatch(id?: string): void {
    if (!id) return;
    this.orderService.retryDispatch(id).subscribe(() => {
      Swal.fire('Dispatched!', 'The system is looking for a new rider.', 'success');
      this.loadOrders();
    });
  }
}
