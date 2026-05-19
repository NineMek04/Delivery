import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, RefreshCcw, Search, Pencil, Trash2, X, Check, Plus, Menu } from 'lucide-angular';
import { ShopService, ShopDto } from '../../core/services/shop.service';
import { StoreService, MenuItem } from '../../core/services/store.service';
import Swal from 'sweetalert2';

@Component({
  selector: 'app-shops',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  templateUrl: './shops.component.html',
  styleUrl: './shops.component.scss'
})
export class ShopsComponent implements OnInit {
  readonly title = 'Shop_Management';
  readonly icons = { RefreshCcw, Search, Pencil, Trash2, X, Check, Plus, Menu };

  private readonly shopService = inject(ShopService);
  private readonly storeService = inject(StoreService);

  shops: ShopDto[] = [];
  isLoading = false;
  query = '';

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
    this.shopService.getAll().subscribe({
      next: (shops) => {
        this.shops = shops;
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
      }
    });
  }

  loadMenuItems(shopId: string): void {
    this.selectedShopId = shopId;
    this.storeService.loadMenusFromApi(shopId).subscribe({
      next: (menus) => {
        this.menuItems = menus;
        this.isMenuModalOpen = true;
      },
      error: () => {
        Swal.fire({ icon: 'error', title: 'โหลดเมนูไม่สำเร็จ', text: 'กรุณาลองใหม่อีกครั้ง' });
      }
    });
  }

  closeMenuModal(): void {
    this.isMenuModalOpen = false;
    this.selectedShopId = null;
    this.menuItems = [];
  }

  get filteredShops(): ShopDto[] {
    const q = this.query.trim().toLowerCase();
    if (!q) return this.shops;
    return this.shops.filter(s =>
      (s.id || '').toLowerCase().includes(q) ||
      (s.name || '').toLowerCase().includes(q) ||
      (s.menuName || '').toLowerCase().includes(q)
    );
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
    const payload: Partial<ShopDto> = {
      name: this.editSnapshot.name,
      menuName: this.editSnapshot.menuName,
      menuPrice: this.editSnapshot.menuPrice,
      lat: shop.lat,
      lng: shop.lng
    };
    this.shopService.update(shop.id, payload).subscribe({
      next: () => {
        shop.name = payload.name!;
        shop.menuName = payload.menuName!;
        shop.menuPrice = payload.menuPrice!;
        this.cancelEdit();
        Swal.fire({ icon: 'success', title: 'บันทึกสำเร็จ', timer: 1500, showConfirmButton: false });
      },
      error: () => {
        Swal.fire({ icon: 'error', title: 'บันทึกไม่สำเร็จ', text: 'กรุณาลองใหม่อีกครั้ง' });
      }
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
      cancelButtonText: 'ยกเลิก'
    }).then(result => {
      if (!result.isConfirmed || !shop.id) return;
      this.shopService.delete(shop.id).subscribe({
        next: () => {
          this.shops = this.shops.filter(s => s.id !== shop.id);
          Swal.fire({ icon: 'success', title: 'ลบสำเร็จ', timer: 1500, showConfirmButton: false });
        },
        error: () => {
          Swal.fire({ icon: 'error', title: 'ลบไม่สำเร็จ', text: 'กรุณาลองใหม่อีกครั้ง' });
        }
      });
    });
  }

  // ── Helpers ──────────────────────────────────────────────────────

  shortId(id?: string): string {
    return id ? id.slice(0, 8).toUpperCase() : '—';
  }

  formatCoord(val?: number | null): string {
    return val != null ? val.toFixed(5) : '—';
  }

  formatDate(val?: string): string {
    if (!val) return '—';
    return new Date(val).toLocaleDateString('th-TH', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
