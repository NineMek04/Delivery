import {
  Component, OnInit, OnDestroy, inject, ChangeDetectorRef, ChangeDetectionStrategy
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
import { RiderService } from '../../core/services/rider.service';
import { TrackingSignalRService } from '../../core/services/tracking-signalr.service';
import { OrderDto } from '../../api/generated/model/order-dto';
import { RiderDto } from '../../api/generated/model/rider-dto';
import Swal from 'sweetalert2';
import {
  LucideAngularModule,
  RefreshCcw, Search, XCircle, RotateCcw, Info,
  ChevronUp, ChevronDown, ChevronsUpDown, Bell, Filter, X
} from 'lucide-angular';
import { OrderDetailComponent } from './order-detail/order-detail.component';

type SortDir = 'asc' | 'desc' | null;
interface SortState { field: keyof OrderDto | 'rider'; dir: SortDir; }

interface FilterState {
  statuses:  string[];
  dateFrom:  string;
  dateTo:    string;
  riderId:   string;
  priceMin:  number | null;
  priceMax:  number | null;
  search:    string;
}

@Component({
  selector:    'app-orders',
  standalone:  true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [
    CommonModule, FormsModule, LucideAngularModule, OrderDetailComponent
  ],
  templateUrl: './orders.component.html',
  styleUrl:    './orders.component.scss'
})
export class OrdersComponent implements OnInit, OnDestroy {
  readonly title = 'Order_Operations';
  readonly icons = { RefreshCcw, Search, XCircle, RotateCcw, Info, ChevronUp, ChevronDown, ChevronsUpDown, Bell, Filter, X };

  private orderService   = inject(OrderService);
  private riderService   = inject(RiderService);
  private trackingService = inject(TrackingSignalRService);
  private cdr = inject(ChangeDetectorRef);
  private sub = new Subscription();

  // ── Data ─────────────────────────────────────────────────────────────────
  allOrders:  OrderDto[] = [];
  riders:     RiderDto[] = [];
  isLoading   = false;
  hasError    = false;

  // ── Filter state ──────────────────────────────────────────────────────────
  readonly allStatuses = ['CREATED','MATCHING','OFFERING','ASSIGNED','PICKING_UP','DELIVERING','COMPLETED','CANCELLED'];
  filters: FilterState = {
    statuses:  [],
    dateFrom:  '',
    dateTo:    '',
    riderId:   '',
    priceMin:  null,
    priceMax:  null,
    search:    '',
  };
  filterPanelOpen = false;

  // ── Sort state ────────────────────────────────────────────────────────────
  sort: SortState = { field: 'createdAt', dir: 'desc' };

  // ── Pagination ────────────────────────────────────────────────────────────
  currentPage = 1;
  readonly pageSize = 20;

  // ── Real-time highlight set ───────────────────────────────────────────────
  recentlyUpdated = new Set<string>();

  // ── Modal ─────────────────────────────────────────────────────────────────
  selectedOrder:  OrderDto | null = null;
  showDetailModal = false;

  // ── Notifications badge ───────────────────────────────────────────────────
  newOrderCount = 0;

  // ─────────────────────────────────────────────────────────────────────────
  // Lifecycle
  // ─────────────────────────────────────────────────────────────────────────

  ngOnInit(): void {
    this.riderService.getAll(1, 500).subscribe(r => this.riders = r);
    this.loadOrders();

    // SignalR: start connection and subscribe
    this.trackingService.startConnection();
    this.sub.add(
      this.trackingService.orderStatusChanged$.subscribe(({ orderId, status }) => {
        const order = this.allOrders.find(o => o.id === orderId);
        if (order) {
          order.status = status;
          this.recentlyUpdated.add(orderId);
          setTimeout(() => { this.recentlyUpdated.delete(orderId); this.cdr.markForCheck(); }, 3000);
        } else {
          // New order arrived — reload and bump badge
          this.newOrderCount++;
          this.loadOrders();
        }
        this.cdr.markForCheck();
      })
    );
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Data loading — fetch ALL and filter locally (backend supports page+search)
  // ─────────────────────────────────────────────────────────────────────────

  loadOrders(): void {
    this.isLoading  = true;
    this.hasError   = false;
    this.newOrderCount = 0;

    // Fetch all pages to support local filtering/sorting.
    // Use a large pageSize so we don't need pagination calls.
    this.orderService.getAll(1, 500).subscribe({
      next: orders => {
        this.allOrders = orders;
        this.currentPage = 1;
        this.isLoading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.isLoading = false;
        this.hasError  = true;
        this.cdr.markForCheck();
      }
    });
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Filtering + Sorting pipeline (computed in real-time)
  // ─────────────────────────────────────────────────────────────────────────

  get filteredOrders(): OrderDto[] {
    let result = [...this.allOrders];

    // Status filter
    if (this.filters.statuses.length) {
      result = result.filter(o => this.filters.statuses.includes(o.status ?? ''));
    }
    // Date range
    if (this.filters.dateFrom) {
      const from = new Date(this.filters.dateFrom).getTime();
      result = result.filter(o => o.createdAt && new Date(o.createdAt).getTime() >= from);
    }
    if (this.filters.dateTo) {
      const to = new Date(this.filters.dateTo + 'T23:59:59').getTime();
      result = result.filter(o => o.createdAt && new Date(o.createdAt).getTime() <= to);
    }
    // Rider filter
    if (this.filters.riderId) {
      result = result.filter(o => o.assignedRiderId === this.filters.riderId);
    }
    // Price range
    if (this.filters.priceMin !== null) {
      result = result.filter(o => (o.deliveryFee ?? 0) >= this.filters.priceMin!);
    }
    if (this.filters.priceMax !== null) {
      result = result.filter(o => (o.deliveryFee ?? 0) <= this.filters.priceMax!);
    }
    // Text search (Order ID / tracking code / address)
    if (this.filters.search.trim()) {
      const q = this.filters.search.trim().toLowerCase();
      result = result.filter(o =>
        (o.id ?? '').toLowerCase().includes(q) ||
        (o.trackingCode ?? '').toLowerCase().includes(q)
      );
    }

    // Sort
    const { field, dir } = this.sort;
    if (dir) {
      result.sort((a, b) => {
        let av: any, bv: any;
        if (field === 'rider') {
          av = this.getRiderLabel(a.assignedRiderId);
          bv = this.getRiderLabel(b.assignedRiderId);
        } else {
          av = (a as any)[field] ?? '';
          bv = (b as any)[field] ?? '';
        }
        if (av < bv) return dir === 'asc' ? -1 : 1;
        if (av > bv) return dir === 'asc' ? 1 : -1;
        return 0;
      });
    }

    return result;
  }

  get pagedOrders(): OrderDto[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.filteredOrders.slice(start, start + this.pageSize);
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.filteredOrders.length / this.pageSize));
  }

  get pages(): number[] {
    const total = this.totalPages;
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const p = this.currentPage;
    const set = new Set([1, 2, p - 1, p, p + 1, total - 1, total].filter(n => n >= 1 && n <= total));
    return Array.from(set).sort((a, b) => a - b);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Sorting
  // ─────────────────────────────────────────────────────────────────────────

  setSort(field: SortState['field']): void {
    if (this.sort.field === field) {
      this.sort = { field, dir: this.sort.dir === 'asc' ? 'desc' : this.sort.dir === 'desc' ? null : 'asc' };
    } else {
      this.sort = { field, dir: 'asc' };
    }
    this.currentPage = 1;
  }

  getSortIcon(field: SortState['field']): any {
    if (this.sort.field !== field || !this.sort.dir) return this.icons.ChevronsUpDown;
    return this.sort.dir === 'asc' ? this.icons.ChevronUp : this.icons.ChevronDown;
  }

  isSortActive(field: SortState['field']): boolean {
    return this.sort.field === field && !!this.sort.dir;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Filter panel
  // ─────────────────────────────────────────────────────────────────────────

  toggleStatus(status: string): void {
    const idx = this.filters.statuses.indexOf(status);
    if (idx >= 0) this.filters.statuses.splice(idx, 1);
    else this.filters.statuses.push(status);
    this.currentPage = 1;
  }

  isStatusSelected(status: string): boolean {
    return this.filters.statuses.includes(status);
  }

  clearFilters(): void {
    this.filters = { statuses: [], dateFrom: '', dateTo: '', riderId: '', priceMin: null, priceMax: null, search: '' };
    this.currentPage = 1;
  }

  get activeFilterCount(): number {
    let n = 0;
    if (this.filters.statuses.length) n++;
    if (this.filters.dateFrom || this.filters.dateTo) n++;
    if (this.filters.riderId) n++;
    if (this.filters.priceMin !== null || this.filters.priceMax !== null) n++;
    if (this.filters.search.trim()) n++;
    return n;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Pagination
  // ─────────────────────────────────────────────────────────────────────────

  goToPage(page: number | string): void {
    const p = Number(page);
    if (!isNaN(p) && p >= 1 && p <= this.totalPages) {
      this.currentPage = p;
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Computed helpers
  // ─────────────────────────────────────────────────────────────────────────

  get pendingCount():   number { return this.allOrders.filter(o => ['CREATED','MATCHING','OFFERING'].includes(o.status ?? '')).length; }
  get completedCount(): number { return this.allOrders.filter(o => o.status === 'COMPLETED').length; }
  get totalFees():      number { return this.allOrders.filter(o => o.status === 'COMPLETED').reduce((s, o) => s + (o.deliveryFee ?? 0), 0); }

  getStatusTone(status?: string | null): string {
    const map: Record<string, string> = {
      CREATED:'gray', MATCHING:'purple', OFFERING:'amber', ASSIGNED:'blue',
      PICKING_UP:'amber', DELIVERING:'blue', COMPLETED:'green', CANCELLED:'red'
    };
    return map[status ?? ''] ?? 'gray';
  }

  shortId(id?: string | null): string {
    return id ? id.slice(0, 8).toUpperCase() : '—';
  }

  getOrderLabel(order: OrderDto): string {
    return order.trackingCode ?? this.shortId(order.id);
  }

  getRiderLabel(riderId?: string | null): string {
    if (!riderId) return '—';
    const r = this.riders.find(x => x.id === riderId);
    return r?.trackingCode ?? this.shortId(riderId);
  }

  formatDate(val?: string | null): string {
    if (!val) return '—';
    return new Date(val).toLocaleString('th-TH', {
      day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit'
    });
  }

  isRecentlyUpdated(id?: string | null): boolean {
    return !!id && this.recentlyUpdated.has(id);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Modal
  // ─────────────────────────────────────────────────────────────────────────

  openOrderDetail(order: OrderDto): void {
    this.selectedOrder  = order;
    this.showDetailModal = true;
  }

  closeOrderDetail(): void {
    this.showDetailModal = false;
    this.selectedOrder  = null;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Actions
  // ─────────────────────────────────────────────────────────────────────────

  cancelOrder(id?: string | null): void {
    if (!id) return;
    Swal.fire({
      title: 'ยืนยันการยกเลิก',
      html: `<p style="color:#94a3b8;font-size:14px">กรุณาระบุเหตุผลการยกเลิก</p>
             <input id="cancel-reason" class="swal2-input" placeholder="เหตุผล..." style="background:#0f172a;color:#fff;border:1px solid #334155">`,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonColor: '#ef4444',
      cancelButtonColor:  '#334155',
      confirmButtonText:  'ยกเลิกออเดอร์',
      cancelButtonText:   'ย้อนกลับ',
      background: '#1e293b',
      color: '#f8fafc',
      preConfirm: () => (document.getElementById('cancel-reason') as HTMLInputElement)?.value || 'ยกเลิกโดยแอดมิน'
    }).then(result => {
      if (!result.isConfirmed) return;
      Swal.fire({ title: 'กำลังยกเลิก...', allowOutsideClick: false, background: '#1e293b', color: '#f8fafc', didOpen: () => Swal.showLoading() });
      this.orderService.cancelOrder(id).subscribe({
        next: () => {
          Swal.fire({ icon:'success', title:'ยกเลิกสำเร็จ', timer:1500, showConfirmButton:false, background:'#1e293b', color:'#f8fafc' });
          this.loadOrders();
        },
        error: err => {
          const msg = err?.error?.message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
          Swal.fire({ icon:'error', title:'ยกเลิกไม่สำเร็จ', text: msg, background:'#1e293b', color:'#f8fafc' });
        }
      });
    });
  }

  retryDispatch(id?: string | null): void {
    if (!id) return;
    Swal.fire({
      title: 'ส่งออเดอร์ใหม่?',
      text: 'ระบบจะทำการค้นหาไรเดอร์ใหม่ทันที',
      icon: 'question',
      showCancelButton: true,
      confirmButtonColor: '#22c55e',
      cancelButtonColor: '#334155',
      confirmButtonText: 'ใช่, Retry',
      cancelButtonText: 'ยกเลิก',
      background: '#1e293b',
      color: '#f8fafc'
    }).then(result => {
      if (!result.isConfirmed) return;
      Swal.fire({ title: 'กำลัง Dispatch...', allowOutsideClick: false, background: '#1e293b', color: '#f8fafc', didOpen: () => Swal.showLoading() });
      this.orderService.retryDispatch(id).subscribe({
        next: () => {
          Swal.fire({ icon:'success', title:'Dispatch ใหม่สำเร็จ', text:'ระบบกำลังหาไรเดอร์', background:'#1e293b', color:'#f8fafc' });
          this.loadOrders();
        },
        error: err => {
          const msg = err?.error?.message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
          Swal.fire({ icon:'error', title:'Dispatch ล้มเหลว', text: msg, background:'#1e293b', color:'#f8fafc' });
        }
      });
    });
  }
}
