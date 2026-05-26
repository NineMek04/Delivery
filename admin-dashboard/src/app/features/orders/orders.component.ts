import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrderService } from '../../core/services/order.service';
import { RiderService } from '../../core/services/rider.service';
import { OrderDto } from '../../api/generated/model/order-dto';
import { RiderDto } from '../../api/generated/model/rider-dto';
import { forkJoin } from 'rxjs';
import Swal from 'sweetalert2';
import { LucideAngularModule, RefreshCcw, Search, Plus, XCircle, RotateCcw, Info } from 'lucide-angular';
import { OrderDetailComponent } from './order-detail/order-detail.component';
import { DataTableComponent, TableColumn } from '../../component/data-table/data-table.component';

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule, OrderDetailComponent, DataTableComponent],
  templateUrl: './orders.component.html',
  styleUrl: './orders.component.scss'
})
export class OrdersComponent implements OnInit {
  readonly title = 'Order_Operations';
  readonly icons = { RefreshCcw, Search, Plus, XCircle, RotateCcw, Info };
  readonly Math = Math;
  
  private orderService = inject(OrderService);
  private riderService = inject(RiderService);
  
  public orders: OrderDto[] = [];
  public riders: RiderDto[] = [];
  public isLoading = false;
  public hasError = false;
  public query = '';

  // Pagination
  currentPage = 1;
  pageSize = 10;
  totalCount = 0;

  columns: TableColumn[] = [
    { field: 'id', header: 'ORDER_ID' },
    { field: 'distanceKm', header: 'DISTANCE' },
    { field: 'pickup', header: 'PICKUP_POINT' },
    { field: 'dropoff', header: 'DROPOFF_POINT' },
    { field: 'status', header: 'STATUS' },
    { field: 'rider', header: 'RIDER' }
  ];

  // Modal state
  selectedOrder: OrderDto | null = null;
  showDetailModal = false;

  ngOnInit(): void {
    // Load riders once or periodically, not strictly paginated since we need them for mapping
    this.riderService.getAll(1, 1000).subscribe(riders => this.riders = riders);
    this.loadOrders();
  }

  loadOrders(): void {
    this.isLoading = true;
    this.hasError = false;
    this.orderService.getAllPaginated(this.currentPage, this.pageSize, this.query).subscribe({
      next: (res) => {
        this.orders = res.items;
        this.totalCount = res.totalCount;
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
        this.hasError = true;
      }
    });
  }

  onPageChange(page: number) {
    this.currentPage = page;
    this.loadOrders();
  }

  onSearch(query: string) {
    this.query = query;
    this.currentPage = 1;
    this.loadOrders();
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

  getOrderTrackingCode(order?: OrderDto | null): string {
    if (!order) return 'UNASSIGNED';
    return order.trackingCode ? order.trackingCode : this.shortId(order.id);
  }

  getRiderTrackingCode(riderId?: string | null): string {
    if (!riderId) return 'UNASSIGNED';
    const rider = this.riders.find(r => r.id === riderId);
    return rider && rider.trackingCode ? rider.trackingCode : this.shortId(riderId);
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
      title: 'ยกเลิกออเดอร์?',
      text: 'คุณต้องการยกเลิกคำสั่งซื้อนี้จากระบบใช่หรือไม่',
      icon: 'warning',
      showCancelButton: true,
      confirmButtonColor: '#d33',
      cancelButtonColor: '#3085d6',
      confirmButtonText: 'ใช่, ยกเลิกออเดอร์',
      cancelButtonText: 'ยกเลิก',
      background: '#141414',
      color: '#FFFFFF'
    }).then((result) => {
      if (!result.isConfirmed) return;

      Swal.fire({
        title: 'กำลังยกเลิก...',
        allowOutsideClick: false,
        background: '#141414',
        color: '#FFFFFF',
        didOpen: () => {
          Swal.showLoading();
        }
      });

      this.orderService.cancelOrder(id).subscribe({
        next: () => {
          Swal.fire({
            icon: 'success',
            title: 'ยกเลิกออเดอร์สำเร็จ',
            timer: 1500,
            showConfirmButton: false,
            background: '#141414',
            color: '#FFFFFF'
          });
          this.loadOrders();
        },
        error: (err) => {
          const serverMessage = err?.error?.message ?? err?.error?.Message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
          Swal.fire({
            icon: 'error',
            title: 'ยกเลิกคำสั่งซื้อไม่สำเร็จ',
            text: serverMessage,
            background: '#141414',
            color: '#FFFFFF'
          });
        }
      });
    });
  }

  retryDispatch(id?: string | null): void {
    if (!id) return;

    Swal.fire({
      title: 'ส่งออเดอร์ใหม่?',
      text: 'คุณต้องการทำการกระจายไรเดอร์สำหรับออเดอร์นี้ใหม่อีกครั้งใช่หรือไม่',
      icon: 'question',
      showCancelButton: true,
      confirmButtonColor: '#00FF66',
      cancelButtonColor: '#3085d6',
      confirmButtonText: 'ใช่, ส่งใหม่',
      cancelButtonText: 'ยกเลิก',
      background: '#141414',
      color: '#FFFFFF'
    }).then((result) => {
      if (!result.isConfirmed) return;

      Swal.fire({
        title: 'กำลังกระตุ้นการกระจาย...',
        allowOutsideClick: false,
        background: '#141414',
        color: '#FFFFFF',
        didOpen: () => {
          Swal.showLoading();
        }
      });

      this.orderService.retryDispatch(id).subscribe({
        next: () => {
          Swal.fire({
            icon: 'success',
            title: 'กระจายออเดอร์ใหม่สำเร็จ',
            text: 'ระบบกำลังหาตัวผู้ขับไรเดอร์รายถัดไป',
            background: '#141414',
            color: '#FFFFFF'
          });
          this.loadOrders();
        },
        error: (err) => {
          const serverMessage = err?.error?.message ?? err?.error?.Message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
          Swal.fire({
            icon: 'error',
            title: 'การกระจายใหม่ล้มเหลว',
            text: serverMessage,
            background: '#141414',
            color: '#FFFFFF'
          });
        }
      });
    });
  }
}
