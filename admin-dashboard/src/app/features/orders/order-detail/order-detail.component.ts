import { Component, Input, Output, EventEmitter, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrderDto } from '../../../api/generated/model/order-dto';
import { OrderService } from '../../../core/services/order.service';
import { LucideAngularModule, X, Check, RotateCcw, MapPin, Clock, User, Truck, AlertTriangle, Info, Plus, Search, Bell } from 'lucide-angular';
import Swal from 'sweetalert2';

@Component({
  selector: 'app-order-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  templateUrl: './order-detail.component.html',
  styleUrl: './order-detail.component.scss'
})
export class OrderDetailComponent implements OnInit {
  @Input({ required: true }) order!: OrderDto;
  @Output() close = new EventEmitter<void>();

  private orderService = inject(OrderService);

  readonly icons = { X, Check, RotateCcw, MapPin, Clock, User, Truck, AlertTriangle, Info, Plus, Search, Bell };

  // Timeline states
  timelineSteps = [
    { status: 'CREATED', label: 'Created', icon: 'Plus' },
    { status: 'MATCHING', label: 'Matching', icon: 'Search' },
    { status: 'OFFERING', label: 'Offering', icon: 'Bell' },
    { status: 'ASSIGNED', label: 'Assigned', icon: 'User' },
    { status: 'PICKING_UP', label: 'Picking Up', icon: 'Truck' },
    { status: 'DELIVERING', label: 'Delivering', icon: 'Truck' },
    { status: 'COMPLETED', label: 'Completed', icon: 'Check' },
    { status: 'CANCELLED', label: 'Cancelled', icon: 'X' }
  ];

  getIcon(iconName: string): any {
    const icons: any = {
      Plus: this.icons.Plus,
      Search: this.icons.Search,
      Bell: this.icons.Bell,
      User: this.icons.User,
      Truck: this.icons.Truck,
      Check: this.icons.Check,
      X: this.icons.X,
      MapPin: this.icons.MapPin,
      Clock: this.icons.Clock,
      Info: this.icons.Info
    };
    return icons[iconName] || this.icons.Info;
  }

  ngOnInit(): void {
    // Initialize any needed state
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

  getTimelineIndex(): number {
    const status = this.order.status || 'CREATED';
    return this.timelineSteps.findIndex(s => s.status === status);
  }

  shortId(id?: string | null): string {
    return id ? id.slice(0, 8).toUpperCase() : '—';
  }

  getOrderTrackingCode(): string {
    return this.order.trackingCode ? this.order.trackingCode : this.shortId(this.order.id);
  }

  formatCoord(val?: number | null): string {
    return val != null ? val.toFixed(5) : '—';
  }

  formatDate(val?: string | null): string {
    if (!val) return '—';
    return new Date(val).toLocaleString('th-TH', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  cancelOrder(): void {
    if (!this.order.id) return;
    Swal.fire({
      title: 'ยกเลิกออเดอร์?',
      text: `ออเดอร์ ${this.getOrderTrackingCode()} จะถูกยกเลิก`,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonColor: '#d33',
      cancelButtonColor: '#3085d6',
      confirmButtonText: 'ใช่, ยกเลิกเลย',
      cancelButtonText: 'ยกเลิก'
    }).then(result => {
      if (result.isConfirmed) {
        this.orderService.cancelOrder(this.order.id!).subscribe({
          next: () => {
            Swal.fire('ยกเลิกแล้ว!', 'ออเดอร์ถูกยกเลิกเรียบร้อย', 'success');
            this.close.emit();
          },
          error: () => {
            Swal.fire('ผิดพลาด!', 'ไม่สามารถยกเลิกออเดอร์ได้', 'error');
          }
        });
      }
    });
  }

  retryDispatch(): void {
    if (!this.order.id) return;
    this.orderService.retryDispatch(this.order.id).subscribe({
      next: () => {
        Swal.fire('สั่ง Dispatch ใหม่!', 'ระบบกำลังหาไรเดอร์ให้ใหม่', 'success');
        this.close.emit();
      },
      error: () => {
        Swal.fire('ผิดพลาด!', 'ไม่สามารถสั่ง Dispatch ได้', 'error');
      }
    });
  }
}
