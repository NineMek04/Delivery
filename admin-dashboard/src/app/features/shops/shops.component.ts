import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, RefreshCcw, Search, Pencil, Trash2, X, Check, Plus, Menu } from 'lucide-angular';
import { ShopService, ShopDto } from '../../core/services/shop.service';
import { StoreService, MenuItem } from '../../core/services/store.service';
import { DataTableComponent, TableColumn } from '../../component/data-table/data-table.component';
import Swal from 'sweetalert2';

@Component({
  selector: 'app-shops',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule, DataTableComponent],
  templateUrl: './shops.component.html',
  styleUrl: './shops.component.scss'
})
export class ShopsComponent implements OnInit {
  readonly title = 'Shop_Management';
  readonly icons = { RefreshCcw, Search, Pencil, Trash2, X, Check, Plus, Menu };
  readonly Math = Math;

  private readonly shopService = inject(ShopService);
  private readonly storeService = inject(StoreService);

  shops: ShopDto[] = [];
  isLoading = false;
  hasError = false;
  query = '';
  
  // Pagination
  currentPage = 1;
  pageSize = 10;
  totalCount = 0;

  columns: TableColumn[] = [
    { field: 'id', header: 'SHOP_ID', isSortable: true },
    { field: 'name', header: 'NAME', isSortable: true },
    { field: 'menuName', header: 'MENU' },
    { field: 'menuItems', header: 'VIEW MENU' },
    { field: 'menuPrice', header: 'PRICE (฿)', isSortable: true },
    { field: 'lat', header: 'LATITUDE' },
    { field: 'lng', header: 'LONGITUDE' },
    { field: 'createdAt', header: 'CREATED', isSortable: true }
  ];

  // inline edit state
  editingId: string | null = null;
  editSnapshot: Partial<ShopDto> = {};

  // menu management state
  selectedShopId: string | null = null;
  menuItems: MenuItem[] = [];
  isMenuModalOpen = false;

  ngOnInit(): void {
    this.loadShops();
  }

  loadShops(): void {
    this.isLoading = true;
    this.hasError = false;
    this.shopService.getAllPaginated(this.currentPage, this.pageSize, this.query).subscribe({
      next: (res) => {
        this.shops = res.items;
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
    this.loadShops();
  }

  onSearch(query: string) {
    this.query = query;
    this.currentPage = 1; // reset to first page on search
    this.loadShops();
  }

  onSortChange(event: {field: string | null, dir: 'asc'|'desc'|null}) {
    if (!event.dir || !event.field) {
      this.loadShops(); // reset to default server order
      return;
    }
    
    this.shops.sort((a, b) => {
      let valA: any = a[event.field as keyof ShopDto];
      let valB: any = b[event.field as keyof ShopDto];
      
      if (valA == null) valA = '';
      if (valB == null) valB = '';
      
      if (typeof valA === 'string') valA = valA.toLowerCase();
      if (typeof valB === 'string') valB = valB.toLowerCase();
      
      if (valA < valB) return event.dir === 'asc' ? -1 : 1;
      if (valA > valB) return event.dir === 'asc' ? 1 : -1;
      return 0;
    });
  }

  loadMenuItems(shopId: string): void {
    this.selectedShopId = shopId;
    this.storeService.loadMenusFromApi(shopId).subscribe({
      next: (menus) => {
        this.menuItems = menus;
        this.isMenuModalOpen = true;
      },
      error: (err) => {
        const serverMessage = err?.error?.message ?? err?.error?.Message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
        Swal.fire({ 
          icon: 'error', 
          title: 'โหลดเมนูไม่สำเร็จ', 
          text: serverMessage,
          background: '#141414',
          color: '#FFFFFF'
        });
      }
    });
  }

  closeMenuModal(): void {
    this.isMenuModalOpen = false;
    this.selectedShopId = null;
    this.menuItems = [];
  }

  // ── Inline Edit ──────────────────────────────────────────────────

  startEdit(shop: ShopDto): void {
    this.editingId = shop.id ?? null;
    this.editSnapshot = { name: shop.name, menuName: shop.menuName, menuPrice: shop.menuPrice };
  }

  cancelEdit(): void {
    this.editingId = null;
    this.editSnapshot = {};
  }

  saveEdit(shop: ShopDto): void {
    if (!shop.id) return;
    
    Swal.fire({
      title: 'ยืนยันการแก้ไขร้านค้า?',
      text: 'คุณต้องการบันทึกการเปลี่ยนแปลงของร้านนี้ใช่หรือไม่',
      icon: 'question',
      showCancelButton: true,
      confirmButtonColor: '#00FF66',
      cancelButtonColor: '#3085d6',
      confirmButtonText: 'ใช่, บันทึก',
      cancelButtonText: 'ยกเลิก',
      background: '#141414',
      color: '#FFFFFF'
    }).then(result => {
      if (!result.isConfirmed) return;

      Swal.fire({
        title: 'กำลังบันทึก...',
        allowOutsideClick: false,
        background: '#141414',
        color: '#FFFFFF',
        didOpen: () => {
          Swal.showLoading();
        }
      });

      const payload: Partial<ShopDto> = {
        name: this.editSnapshot.name,
        menuName: this.editSnapshot.menuName,
        menuPrice: this.editSnapshot.menuPrice,
        lat: shop.lat,
        lng: shop.lng
      };
      
      this.shopService.update(shop.id!, payload).subscribe({
        next: () => {
          shop.name = payload.name!;
          shop.menuName = payload.menuName!;
          shop.menuPrice = payload.menuPrice!;
          this.cancelEdit();
          Swal.fire({ 
            icon: 'success', 
            title: 'บันทึกสำเร็จ', 
            timer: 1500, 
            showConfirmButton: false,
            background: '#141414',
            color: '#FFFFFF'
          });
        },
        error: (err) => {
          const serverMessage = err?.error?.message ?? err?.error?.Message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
          Swal.fire({ 
            icon: 'error', 
            title: 'บันทึกไม่สำเร็จ', 
            text: serverMessage,
            background: '#141414',
            color: '#FFFFFF'
          });
        }
      });
    });
  }

  // ── Delete ───────────────────────────────────────────────────────

  deleteShop(shop: ShopDto): void {
    if (!shop.id) return;
    Swal.fire({
      title: 'ลบร้านค้า?',
      text: `"${shop.name}" จะถูกลบออกจากระบบ`,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonColor: '#d33',
      cancelButtonColor: '#3085d6',
      confirmButtonText: 'ใช่, ลบเลย',
      cancelButtonText: 'ยกเลิก',
      background: '#141414',
      color: '#FFFFFF'
    }).then(result => {
      if (!result.isConfirmed || !shop.id) return;

      Swal.fire({
        title: 'กำลังลบ...',
        allowOutsideClick: false,
        background: '#141414',
        color: '#FFFFFF',
        didOpen: () => {
          Swal.showLoading();
        }
      });

      this.shopService.delete(shop.id).subscribe({
        next: () => {
          this.loadShops(); // Reload from backend to update pagination
          Swal.fire({ 
            icon: 'success', 
            title: 'ลบสำเร็จ', 
            timer: 1500, 
            showConfirmButton: false,
            background: '#141414',
            color: '#FFFFFF'
          });
        },
        error: (err) => {
          const serverMessage = err?.error?.message ?? err?.error?.Message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
          Swal.fire({ 
            icon: 'error', 
            title: 'ลบไม่สำเร็จ', 
            text: serverMessage,
            background: '#141414',
            color: '#FFFFFF'
          });
        }
      });
    });
  }

  // ── Helpers ──────────────────────────────────────────────────────

  shortId(id?: string): string {
    return id ? id.slice(0, 8).toUpperCase() : '—';
  }

  getShopTrackingCode(shop?: ShopDto | null): string {
    if (!shop) return '—';
    return shop.trackingCode ? shop.trackingCode : this.shortId(shop.id);
  }

  getSelectedShopTrackingCode(): string {
    if (!this.selectedShopId) return '—';
    const shop = this.shops.find(s => s.id === this.selectedShopId);
    return shop ? this.getShopTrackingCode(shop) : this.shortId(this.selectedShopId);
  }

  formatCoord(val?: number | null): string {
    return val != null ? val.toFixed(5) : '—';
  }

  formatDate(val?: string): string {
    if (!val) return '—';
    return new Date(val).toLocaleDateString('th-TH', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
