import {
  Component, OnInit, inject, ChangeDetectorRef, ChangeDetectionStrategy, DestroyRef
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { OrderService } from '../../core/services/order.service';
import { RiderService } from '../../core/services/rider.service';
import { TrackingSignalRService } from '../../core/services/tracking-signalr.service';
import { OrderDto } from '../../api/generated/model/order-dto';
import { RiderDto } from '../../api/generated/model/rider-dto';
import Swal from 'sweetalert2';
import {
  LucideAngularModule,
  RefreshCcw, Search, XCircle, RotateCcw, Info,
  ChevronUp, ChevronDown, ChevronsUpDown, Bell, Filter, X, MapPin
} from 'lucide-angular';
import { OrderDetailComponent } from './order-detail/order-detail.component';
import { DispatchQueueComponent } from './dispatch-queue/dispatch-queue.component';
import { DataTableComponent, TableColumn } from '../../component/data-table/data-table.component';

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
    CommonModule, FormsModule, LucideAngularModule, OrderDetailComponent, DispatchQueueComponent, DataTableComponent
  ],
  templateUrl: './orders.component.html',
  styleUrl:    './orders.component.scss'
})
export class OrdersComponent implements OnInit {
  readonly title = 'Order_Operations';
  readonly icons = { RefreshCcw, Search, XCircle, RotateCcw, Info, ChevronUp, ChevronDown, ChevronsUpDown, Bell, Filter, X, MapPin };

  columns: TableColumn[] = [
    { field: 'id', header: 'ORDER_ID', isSortable: true },
    { field: 'batch', header: 'BATCH' },
    { field: 'pickup', header: 'PICKUP' },
    { field: 'dropoff', header: 'DROPOFF' },
    { field: 'rider', header: 'RIDER', isSortable: true },
    { field: 'status', header: 'STATUS', isSortable: true },
    { field: 'distanceKm', header: 'DIST.', isSortable: true },
    { field: 'deliveryFee', header: 'FEE', isSortable: true },
    { field: 'createdAt', header: 'CREATED', isSortable: true }
  ];

  private orderService   = inject(OrderService);
  private riderService   = inject(RiderService);
  private trackingService = inject(TrackingSignalRService);
  private cdr = inject(ChangeDetectorRef);
  private destroyRef = inject(DestroyRef);

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
    this.riderService.getAll(1, 500).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(r => this.riders = r);
    this.loadOrders();

    // SignalR: start connection and subscribe
    this.trackingService.startConnection();
    this.trackingService.orderStatusChanged$.pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(({ orderId, status }) => {
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
    });
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Data loading — fetch ALL and filter locally (backend supports page+search)
  // ─────────────────────────────────────────────────────────────────────────

  loadOrders(keepPage = false): void {
    this.isLoading  = true;
    this.hasError   = false;
    this.newOrderCount = 0;

    // Fetch all pages to support local filtering/sorting.
    // Use a large pageSize so we don't need pagination calls.
    this.orderService.getAll(1, 500).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: orders => {
        this.allOrders = orders;
        if (!keepPage) {
          this.currentPage = 1;
        }
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

  refreshSingleOrder(id: string): void {
    this.orderService.getById(id).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: updatedOrder => {
        const index = this.allOrders.findIndex(o => o.id === id);
        if (index !== -1) {
          this.allOrders[index] = updatedOrder;
          this.recentlyUpdated.add(id);
          setTimeout(() => {
            this.recentlyUpdated.delete(id);
            this.cdr.markForCheck();
          }, 3000);
          this.cdr.markForCheck();
        }
      },
      error: err => {
        console.error('Failed to refresh order', id, err);
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

  // ─────────────────────────────────────────────────────────────────────────
  // Table event handlers & Pagination
  // ─────────────────────────────────────────────────────────────────────────

  onPageChange(page: number) {
    this.currentPage = page;
  }

  onSearch(query: string) {
    this.filters.search = query;
    this.currentPage = 1;
  }

  onSortChange(event: {field: string | null, dir: 'asc'|'desc'|null}) {
    if (!event.field || !event.dir) {
      this.sort = { field: 'createdAt', dir: 'desc' };
    } else {
      this.sort = { field: event.field as keyof OrderDto | 'rider', dir: event.dir };
    }
    this.currentPage = 1;
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

  formatCoord(val?: number | null): string {
    return val != null ? val.toFixed(4) : '—';
  }

  formatDistance(val?: number | null): string {
    return val != null ? `${val.toFixed(1)} km` : '—';
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

  getSiblingOrders(order: OrderDto): OrderDto[] {
    if (!order.batchGroupId) return [];
    return this.allOrders
      .filter(o => o.batchGroupId === order.batchGroupId && o.id !== order.id)
      .sort((a, b) => (a.batchSequence ?? 0) - (b.batchSequence ?? 0));
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
      this.orderService.cancelOrder(id).pipe(
        takeUntilDestroyed(this.destroyRef)
      ).subscribe({
        next: () => {
          Swal.fire({ icon:'success', title:'ยกเลิกสำเร็จ', timer:1500, showConfirmButton:false, background:'#1e293b', color:'#f8fafc' });
          this.refreshSingleOrder(id);
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
      this.orderService.retryDispatch(id).pipe(
        takeUntilDestroyed(this.destroyRef)
      ).subscribe({
        next: () => {
          Swal.fire({ icon:'success', title:'Dispatch ใหม่สำเร็จ', text:'ระบบกำลังหาไรเดอร์', background:'#1e293b', color:'#f8fafc' });
          this.refreshSingleOrder(id);
        },
        error: err => {
          const msg = err?.error?.message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
          Swal.fire({ icon:'error', title:'Dispatch ล้มเหลว', text: msg, background:'#1e293b', color:'#f8fafc' });
        }
      });
    });
  }
}
