import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import Swal from 'sweetalert2';
import {
  LucideAngularModule,
  ShoppingCart,
  Plus,
  Minus,
  Search,
  Trash2,
  ArrowLeft,
  Star,
  Clock,
  MapPin,
  X,
  Check,
  Map,
  Utensils,
  Compass,
  Bell,
  Info,
  DollarSign,
} from 'lucide-angular';
import { ShopService, ShopDto } from '../../core/services/shop.service';
import {
  StoreService,
  MenuItem,
  MenuOption,
  OptionItem,
  CartItem,
} from '../../core/services/store.service';
import { AuthService } from '../../core/services/auth.service';
import { TrackingSignalRService } from '../../core/services/tracking-signalr.service';
import { Subscription } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';

export interface OrderTrackingState {
  orderId: string;
  status: string; // "CREATED", "ACCEPTED_BY_STORE", "MATCHING", "ASSIGNED", "PICKING_UP", "DELIVERING", "COMPLETED", "CANCELLED"
  riderId?: string;
  riderLat?: number;
  riderLng?: number;
  distanceRemainingKm?: number;
  timelineIndex: number;
}

@Component({
  selector: 'app-customer',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    LucideAngularModule,
  ],
  templateUrl: './customer.component.html',
  styleUrl: './customer.component.scss',
})
export class CustomerComponent implements OnInit, OnDestroy {
  private shopService = inject(ShopService);
  private storeService = inject(StoreService);
  private authService = inject(AuthService);
  private signalRService = inject(TrackingSignalRService);
  private router = inject(Router);

  // States
  shops: ShopDto[] = [];
  filteredShops: ShopDto[] = [];
  searchQuery = '';
  loading = false;
  currentUser: any = null;

  selectedShop: ShopDto | null = null;
  shopMenus: MenuItem[] = [];

  // Menu detail modal state
  selectedMenuItem: MenuItem | null = null;
  modalQuantity = 1;
  selectedOptions: { [optionName: string]: OptionItem[] } = {};
  menuItemNotes = '';

  // Cart sidebar state
  isCartOpen = false;
  cartItems: CartItem[] = [];
  cartTotal = 0;

  // Checkout state
  showCheckoutModal = false;
  dropoffAddress = 'Udon Thani Center, Thailand';
  dropoffLat = 17.4138;
  dropoffLng = 102.7872;
  expectedDeliveryMinutes = 30;

  // Real-time tracking state
  activeOrderTracking: OrderTrackingState | null = null;
  showTrackingPanel = false;
  hubConnection: signalR.HubConnection | null = null;

  // Subscriptions
  private subs = new Subscription();

  // Icons mapping
  icons = {
    ShoppingCart,
    Plus,
    Minus,
    Search,
    Trash2,
    ArrowLeft,
    Star,
    Clock,
    MapPin,
    X,
    Check,
    Map,
    Utensils,
    Compass,
    Bell,
    Info,
    DollarSign,
  };

  ngOnInit() {
    this.currentUser = this.authService.getUserData();
    this.fetchShops();
    this.initCartSubscription();
    this.startSignalRForCustomer();
  }

  ngOnDestroy() {
    this.subs.unsubscribe();
    if (this.hubConnection) {
      this.hubConnection.stop();
    }
  }

  fetchShops() {
    this.loading = true;
    this.shopService.getAll().subscribe({
      next: (data) => {
        this.shops = data;
        this.filteredShops = data;
        this.loading = false;
      },
      error: (err) => {
        console.error('Failed to load shops', err);
        this.loading = false;
      },
    });
  }

  onSearch() {
    if (!this.searchQuery.trim()) {
      this.filteredShops = this.shops;
      return;
    }
    const query = this.searchQuery.toLowerCase();
    this.filteredShops = this.shops.filter(
      (s) =>
        s.name.toLowerCase().includes(query) ||
        (s.menuName && s.menuName.toLowerCase().includes(query)),
    );
  }

  selectShop(shop: ShopDto) {
    this.selectedShop = shop;
    const shopId = shop.id;
    if (shopId) {
      this.loading = true;
      this.storeService.loadMenusFromApi(shopId).subscribe({
        next: (menus) => {
          this.shopMenus = menus.length > 0 ? menus : this.storeService.getShopMenus(shopId);
          this.loading = false;
        },
        error: (err) => {
          console.error('Failed to load menus from API, falling back to local seeded menus', err);
          this.shopMenus = this.storeService.getShopMenus(shopId);
          this.loading = false;
        }
      });
    }
  }

  backToStores() {
    this.selectedShop = null;
    this.shopMenus = [];
  }

  // ── Product Details Modal ──
  openProductDetail(menu: MenuItem) {
    this.selectedMenuItem = menu;
    this.modalQuantity = 1;
    this.menuItemNotes = '';
    this.selectedOptions = {};

    // Initialize required options selection with empty lists or defaults
    menu.options.forEach((opt) => {
      this.selectedOptions[opt.name] = [];
    });
  }

  closeProductDetail() {
    this.selectedMenuItem = null;
  }

  toggleOptionSelection(option: MenuOption, item: OptionItem, event: any) {
    const isChecked = event.target.checked;
    const currentSelections = this.selectedOptions[option.name] || [];

    if (option.maxSelections === 1) {
      // Radio mode
      if (isChecked) {
        this.selectedOptions[option.name] = [item];
      }
    } else {
      // Checkbox mode
      if (isChecked) {
        if (currentSelections.length < option.maxSelections) {
          currentSelections.push(item);
        } else {
          // Deselect the first and add the new one or block (standard UX: block or replace)
          event.target.checked = false;
          Swal.fire({
            icon: 'warning',
            title: `เลือกได้สูงสุด ${option.maxSelections} อย่าง`,
            timer: 1500,
            showConfirmButton: false,
          });
          return;
        }
      } else {
        const index = currentSelections.findIndex((i) => i.name === item.name);
        if (index !== -1) {
          currentSelections.splice(index, 1);
        }
      }
      this.selectedOptions[option.name] = currentSelections;
    }
  }

  isOptionChecked(optionName: string, itemName: string): boolean {
    const list = this.selectedOptions[optionName] || [];
    return list.some((i) => i.name === itemName);
  }

  adjustModalQuantity(delta: number) {
    this.modalQuantity = Math.max(1, this.modalQuantity + delta);
  }

  addToCart() {
    if (!this.selectedMenuItem || !this.selectedShop || !this.selectedShop.id)
      return;

    // Validate required options
    const missingRequired: string[] = [];
    this.selectedMenuItem.options.forEach((opt) => {
      const selections = this.selectedOptions[opt.name] || [];
      if (opt.required && selections.length === 0) {
        missingRequired.push(opt.name);
      }
    });

    if (missingRequired.length > 0) {
      Swal.fire({
        icon: 'error',
        title: 'กรุณาเลือกตัวเลือกที่จำเป็น',
        text: `ขาดตัวเลือก: ${missingRequired.join(', ')}`,
        confirmButtonColor: '#3b82f6',
      });
      return;
    }

    this.storeService.addToCart(
      this.selectedShop.id,
      this.selectedMenuItem,
      this.modalQuantity,
      { ...this.selectedOptions },
      this.menuItemNotes,
    );

    this.closeProductDetail();
    this.isCartOpen = true; // Open cart sidebar to give visual confirmation

    const Toast = Swal.mixin({
      toast: true,
      position: 'top-end',
      showConfirmButton: false,
      timer: 1500,
    });
    Toast.fire({
      icon: 'success',
      title: 'เพิ่มลงตะกร้าเรียบร้อย',
    });
  }

  // ── Cart & Checkout ──
  initCartSubscription() {
    this.subs.add(
      this.storeService.cart$.subscribe((items) => {
        this.cartItems = items;
        this.cartTotal = this.storeService.getCartTotal();
      }),
    );
  }

  toggleCart() {
    this.isCartOpen = !this.isCartOpen;
  }

  adjustCartItemQuantity(cartItemId: string, delta: number) {
    this.storeService.updateCartQuantity(cartItemId, delta);
  }

  removeCartItem(cartItemId: string) {
    this.storeService.removeFromCart(cartItemId);
  }

  getOptionText(item: CartItem): string {
    const list: string[] = [];
    Object.entries(item.selectedOptions).forEach(([optName, opts]) => {
      opts.forEach((opt) => {
        list.push(`${optName}: ${opt.name} (+฿${opt.price})`);
      });
    });
    return list.join(', ');
  }

  openCheckout() {
    if (this.cartItems.length === 0) return;
    this.showCheckoutModal = true;
    this.isCartOpen = false;
  }

  closeCheckout() {
    this.showCheckoutModal = false;
  }

  submitOrder() {
    if (!this.selectedShop) return;

    this.loading = true;
    const pickupLat = this.selectedShop.lat || 17.4138;
    const pickupLng = this.selectedShop.lng || 102.7872;

    // Add small random noise to dropoff coordinates to make dispatch matching feel dynamic and simulated!
    const offsetLat = (Math.random() - 0.5) * 0.04;
    const offsetLng = (Math.random() - 0.5) * 0.04;
    const dropoffLat = pickupLat + offsetLat;
    const dropoffLng = pickupLng + offsetLng;

    const deliveryTime = new Date();
    deliveryTime.setMinutes(
      deliveryTime.getMinutes() + this.expectedDeliveryMinutes,
    );

    const payload = {
      pickupLat,
      pickupLng,
      dropoffLat,
      dropoffLng,
      expectedDeliveryTime: deliveryTime.toISOString(),
    };

    this.storeService.placeOrder(payload).subscribe({
      next: (res: any) => {
        this.loading = false;
        this.showCheckoutModal = false;

        // Grab new order ID
        const orderVal = res.value || res;
        const orderId = orderVal.id;

        Swal.fire({
          icon: 'success',
          title: 'สั่งซื้อสำเร็จ!',
          text: 'คำสั่งซื้อของคุณส่งไปยังร้านค้าแล้ว ระบบกำลังเตรียมอาหาร...',
          confirmButtonColor: '#10b981',
        });

        // Initialize active order tracking state
        this.activeOrderTracking = {
          orderId: orderId,
          status: 'CREATED',
          timelineIndex: 0,
        };
        this.showTrackingPanel = true;

        // Clear cart
        this.storeService.clearCart();
      },
      error: (err) => {
        this.loading = false;
        console.error('Checkout failed', err);
        Swal.fire({
          icon: 'error',
          title: 'ชำระเงินไม่สำเร็จ',
          text:
            err.error?.message ||
            'เกิดข้อผิดพลาดในการสร้างคำสั่งซื้อ กรุณาลองใหม่อีกครั้ง',
          confirmButtonColor: '#ef4444',
        });
      },
    });
  }

  // ── Real-time SignalR Integration for Customer App ──
  startSignalRForCustomer() {
    const token = this.authService.getToken();
    if (!token) return;

    const hubUrl = environment.config.baseConfig.apiUrl.replace(
      '/api/v1',
      '/hubs/tracking',
    );

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token,
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection
      .start()
      .then(() => {
        console.log('Customer connected to TrackingHub via SignalR');
        this.registerCustomerListeners();
      })
      .catch((err) => console.error('SignalR Customer connection failed', err));
  }

  registerCustomerListeners() {
    if (!this.hubConnection) return;

    // 1. Listen for Store acceptance
    this.hubConnection.on(
      'OrderAcceptedByStore',
      (data: { orderId: string; status: string }) => {
        console.log('SignalR OrderAcceptedByStore received:', data);
        if (
          this.activeOrderTracking &&
          this.activeOrderTracking.orderId === data.orderId
        ) {
          this.activeOrderTracking.status = 'ACCEPTED_BY_STORE';
          this.activeOrderTracking.timelineIndex = 1;
          this.triggerAlert(
            'ร้านค้ารับออเดอร์แล้ว',
            'ร้านค้ากำลังเตรียมอาหารแสนอร่อยของคุณ 🍳',
            'success',
          );
        }
      },
    );

    // 2. Listen for AI Dispatch matching
    this.hubConnection.on('OfferReceived', (offer: any) => {
      if (
        this.activeOrderTracking &&
        this.activeOrderTracking.orderId === offer.order?.id
      ) {
        this.activeOrderTracking.status = 'MATCHING';
        this.activeOrderTracking.timelineIndex = 2;
      }
    });

    // 3. Listen for Rider assignment
    this.hubConnection.on(
      'OrderAssigned',
      (data: { id: string; riderId: string; assignedAt: string }) => {
        console.log('SignalR OrderAssigned received:', data);
        if (
          this.activeOrderTracking &&
          this.activeOrderTracking.orderId === data.id
        ) {
          this.activeOrderTracking.status = 'ASSIGNED';
          this.activeOrderTracking.riderId = data.riderId;
          this.activeOrderTracking.timelineIndex = 3;
          this.triggerAlert(
            'จับคู่ไรเดอร์สำเร็จ!',
            'ไรเดอร์กำลังเดินทางไปรับอาหารที่ร้าน 🏍️',
            'info',
          );
        }
      },
    );

    // 4. Listen for general order status modifications
    this.hubConnection.on(
      'OrderStatusChanged',
      (orderId: string, newStatus: string) => {
        console.log('SignalR OrderStatusChanged received:', orderId, newStatus);
        if (
          this.activeOrderTracking &&
          this.activeOrderTracking.orderId === orderId
        ) {
          this.activeOrderTracking.status = newStatus;

          let index = this.activeOrderTracking.timelineIndex;
          if (newStatus === 'PICKING_UP') {
            index = 4;
            this.triggerAlert(
              'ไรเดอร์ถึงร้านค้าแล้ว',
              'ไรเดอร์กำลังตรวจสอบและรับอาหารจากร้านค้า',
              'info',
            );
          } else if (newStatus === 'DELIVERING') {
            index = 5;
            this.triggerAlert(
              'อาหารกำลังเดินทาง!',
              'ไรเดอร์ได้รับอาหารเรียบร้อยและกำลังเดินทางไปหาคุณ 💨',
              'success',
            );
          } else if (newStatus === 'COMPLETED') {
            index = 6;
            this.triggerAlert(
              'จัดส่งเรียบร้อย!',
              'ทานให้อร่อยนะครับ! ขอบคุณที่ใช้บริการ 💚',
              'success',
            );
            // Reset tracking panel after a short delay
            setTimeout(() => {
              this.activeOrderTracking = null;
              this.showTrackingPanel = false;
            }, 10000);
          } else if (newStatus === 'CANCELLED') {
            index = 0;
            this.triggerAlert(
              'ออเดอร์ถูกยกเลิก',
              'ขออภัย ออเดอร์ของคุณถูกยกเลิก',
              'error',
            );
          }

          this.activeOrderTracking.timelineIndex = index;
        }
      },
    );

    // 5. Track Rider GPS coordinates real-time on live map/simulation
    this.hubConnection.on(
      'RiderLocationUpdated',
      (data: { riderId: string; lat: number; lng: number; status: string }) => {
        if (
          this.activeOrderTracking &&
          this.activeOrderTracking.riderId === data.riderId
        ) {
          this.activeOrderTracking.riderLat = data.lat;
          this.activeOrderTracking.riderLng = data.lng;
          // Mock distance remaining
          this.activeOrderTracking.distanceRemainingKm = parseFloat(
            (Math.random() * 2 + 0.3).toFixed(2),
          );
        }
      },
    );
  }

  triggerAlert(
    title: string,
    text: string,
    icon: 'success' | 'info' | 'error',
  ) {
    const Toast = Swal.mixin({
      toast: true,
      position: 'top-end',
      showConfirmButton: false,
      timer: 4000,
      timerProgressBar: true,
    });
    Toast.fire({
      icon,
      title,
      text,
    });
  }

  logout() {
    this.authService.logout().subscribe();
  }

  navigateToDashboard() {
    if (this.authService.canAccessDashboard()) {
      this.router.navigate(['/dashboard']);
    } else {
      Swal.fire({
        icon: 'error',
        title: 'จำกัดสิทธิ์การเข้าถึง',
        text: 'เฉพาะบทบาท Admin หรือ Dispatcher เท่านั้น',
        confirmButtonColor: '#3b82f6',
      });
    }
  }
}
