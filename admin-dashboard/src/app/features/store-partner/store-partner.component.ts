import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, Validators, FormArray } from '@angular/forms';
import { Router } from '@angular/router';
import Swal from 'sweetalert2';
import {
  LucideAngularModule,
  Plus,
  Minus,
  Trash2,
  Edit3,
  Check,
  X,
  Compass,
  Utensils,
  Warehouse,
  Clock,
  DollarSign,
  Tag,
  ListPlus,
  BellRing
} from 'lucide-angular';
import { ShopService, ShopDto } from '../../core/services/shop.service';
import { StoreService, MenuItem, MenuOption, OptionItem } from '../../core/services/store.service';
import { AuthService } from '../../core/services/auth.service';
import { Subscription } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';

export interface IncomingOrder {
  id: string;
  state: string; // "CREATED", "ACCEPTED_BY_STORE", "MATCHING", etc.
  pickupLat?: number;
  pickupLng?: number;
  dropoffLat?: number;
  dropoffLng?: number;
  deliveryFee: number;
  distanceKm: number;
  createdAt: string;
  customerId: string;
  customerName: string;
}

@Component({
  selector: 'app-store-partner',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, LucideAngularModule],
  templateUrl: './store-partner.component.html',
  styleUrl: './store-partner.component.scss'
})
export class StorePartnerComponent implements OnInit, OnDestroy {
  private shopService = inject(ShopService);
  private storeService = inject(StoreService);
  private authService = inject(AuthService);
  private formBuilder = inject(FormBuilder);
  private router = inject(Router);

  // States
  myShops: ShopDto[] = [];
  selectedShop: ShopDto | null = null;
  menus: MenuItem[] = [];
  currentUser: any = null;
  loading = false;

  // Active view: 'menus' or 'orders'
  activeTab: 'menus' | 'orders' = 'menus';

  // Order Queue
  incomingOrders: IncomingOrder[] = [];
  hubConnection: signalR.HubConnection | null = null;

  // Menu Form Modal state
  showMenuModal = false;
  menuForm = this.formBuilder.group({
    id: [''],
    name: ['', Validators.required],
    description: ['', Validators.required],
    price: [0, [Validators.required, Validators.min(1)]],
    imageUrl: ['', Validators.required],
    options: this.formBuilder.array([])
  });

  // Subscriptions
  private subs = new Subscription();

  // Icons
  icons = {
    Plus, Minus, Trash2, Edit3, Check, X, Compass, Utensils,
    Warehouse, Clock, DollarSign, Tag, ListPlus, BellRing
  };

  get optionsArray(): FormArray {
    return this.menuForm.get('options') as FormArray;
  }

  ngOnInit() {
    this.currentUser = this.authService.getUserData();
    this.fetchStoreShops();
    this.startSignalRForStore();
  }

  ngOnDestroy() {
    this.subs.unsubscribe();
    if (this.hubConnection) {
      this.hubConnection.stop();
    }
  }

  fetchStoreShops() {
    this.loading = true;
    this.shopService.getAll().subscribe({
      next: (data) => {
        this.myShops = data;
        if (data.length > 0) {
          // Preselect first shop as default represented business
          this.selectShop(data[0]);
        }
        this.loading = false;
      },
      error: (err) => {
        console.error('Failed to load shops', err);
        this.loading = false;
      }
    });
  }

  selectShop(shop: ShopDto) {
    this.selectedShop = shop;
    if (shop.id) {
      this.menus = this.storeService.getShopMenus(shop.id);
    }
  }

  // ── Menu Options Form Array Builders (Options Builder) ──
  initOptionGroupForm(option?: MenuOption) {
    const group = this.formBuilder.group({
      id: [option?.id || 'opt_' + Math.random().toString(36).substring(2, 9)],
      name: [option?.name || '', Validators.required],
      required: [option?.required !== undefined ? option.required : false],
      maxSelections: [option?.maxSelections || 1, [Validators.required, Validators.min(1)]],
      items: this.formBuilder.array([])
    });

    const itemsArray = group.get('items') as FormArray;
    if (option?.items) {
      option.items.forEach(i => {
        itemsArray.push(this.initOptionChoiceForm(i));
      });
    } else {
      // Put 1 blank choice by default to improve builder UX
      itemsArray.push(this.initOptionChoiceForm());
    }

    return group;
  }

  initOptionChoiceForm(item?: OptionItem) {
    return this.formBuilder.group({
      name: [item?.name || '', Validators.required],
      price: [item?.price || 0, [Validators.required, Validators.min(0)]]
    });
  }

  addOptionGroup() {
    this.optionsArray.push(this.initOptionGroupForm());
  }

  removeOptionGroup(index: number) {
    this.optionsArray.removeAt(index);
  }

  getGroupChoices(groupIndex: number): FormArray {
    return this.optionsArray.at(groupIndex).get('items') as FormArray;
  }

  addChoice(groupIndex: number) {
    this.getGroupChoices(groupIndex).push(this.initOptionChoiceForm());
  }

  removeChoice(groupIndex: number, choiceIndex: number) {
    this.getGroupChoices(groupIndex).removeAt(choiceIndex);
  }

  // ── Menu CRUD Operations ──
  openAddMenu() {
    this.menuForm.reset({
      id: '',
      name: '',
      description: '',
      price: 0,
      imageUrl: 'https://images.unsplash.com/photo-1546069901-ba9599a7e63c?w=600&auto=format&fit=crop&q=80',
      options: []
    });
    this.optionsArray.clear();

    // Seed 1 default option group like "Add-on Topping" to make form friendly
    this.addOptionGroup();

    this.showMenuModal = true;
  }

  openEditMenu(item: MenuItem) {
    this.menuForm.reset({
      id: item.id,
      name: item.name,
      description: item.description,
      price: item.price,
      imageUrl: item.imageUrl,
      options: []
    });

    this.optionsArray.clear();
    if (item.options) {
      item.options.forEach(opt => {
        this.optionsArray.push(this.initOptionGroupForm(opt));
      });
    }

    this.showMenuModal = true;
  }

  closeMenuModal() {
    this.showMenuModal = false;
  }

  saveMenuItem() {
    if (this.menuForm.invalid || !this.selectedShop || !this.selectedShop.id) {
      Swal.fire({
        icon: 'error',
        title: 'ข้อมูลไม่ครบถ้วน',
        text: 'กรุณากรอกข้อมูลและรายละเอียดตัวเลือกในแบบฟอร์มให้ครบถ้วน',
        confirmButtonColor: '#3b82f6'
      });
      return;
    }

    const val = this.menuForm.value;
    const shopId = this.selectedShop.id;

    if (val.id) {
      // Edit mode
      const updatedItem: MenuItem = val as MenuItem;
      this.storeService.updateMenuItem(shopId, updatedItem);

      Swal.fire({
        icon: 'success',
        title: 'แก้ไขเมนูสำเร็จ!',
        timer: 1500,
        showConfirmButton: false
      });
    } else {
      // Add mode
      const newItem: Omit<MenuItem, 'id'> = {
        name: val.name!,
        description: val.description!,
        price: val.price!,
        imageUrl: val.imageUrl!,
        options: (val.options as MenuOption[]) || []
      };
      this.storeService.addMenuItem(shopId, newItem);

      Swal.fire({
        icon: 'success',
        title: 'เพิ่มเมนูใหม่สำเร็จ!',
        timer: 1500,
        showConfirmButton: false
      });
    }

    // Refresh menus
    this.menus = this.storeService.getShopMenus(shopId);
    this.closeMenuModal();
  }

  deleteMenuItem(itemId: string) {
    if (!this.selectedShop || !this.selectedShop.id) return;

    Swal.fire({
      title: 'ยืนยันการลบเมนู?',
      text: 'คุณแน่ใจว่าต้องการลบเมนูนี้ออกจากร้านค้าพันธมิตร?',
      icon: 'warning',
      showCancelButton: true,
      confirmButtonText: 'ใช่, ลบเลย',
      cancelButtonText: 'ยกเลิก',
      confirmButtonColor: '#d33',
      cancelButtonColor: '#3085d6'
    }).then((result) => {
      if (result.isConfirmed) {
        this.storeService.deleteMenuItem(this.selectedShop!.id!, itemId);
        this.menus = this.storeService.getShopMenus(this.selectedShop!.id!);

        Swal.fire({
          icon: 'success',
          title: 'ลบเมนูเรียบร้อย!',
          timer: 1500,
          showConfirmButton: false
        });
      }
    });
  }

  // ── Real-Time Order Queue via SignalR ──
  startSignalRForStore() {
    const token = this.authService.getToken();
    if (!token) return;

    const hubUrl = environment.config.baseConfig.apiUrl.replace('/api/v1', '/hubs/tracking');

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token,
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.start()
      .then(() => {
        console.log('Store partner connected to TrackingHub via SignalR');
        this.registerStoreListeners();
      })
      .catch(err => console.error('SignalR Store connection failed', err));
  }

  registerStoreListeners() {
    if (!this.hubConnection) return;

    // 1. Listen for new customer order placements
    this.hubConnection.on('OrderCreated', (data: any) => {
      console.log('SignalR OrderCreated broadcast received by Store:', data);

      // Play alert chime or trigger flashing visual alert
      this.playOrderChime();

      const newOrder: IncomingOrder = {
        id: data.id,
        state: 'CREATED',
        pickupLat: data.pickupLocation?.coordinates?.[1] || 13.7,
        pickupLng: data.pickupLocation?.coordinates?.[0] || 100.5,
        dropoffLat: data.dropoffLocation?.coordinates?.[1] || 13.7,
        dropoffLng: data.dropoffLocation?.coordinates?.[0] || 100.5,
        deliveryFee: data.deliveryFee || 35,
        distanceKm: parseFloat((data.distanceKm || 1.2).toFixed(2)),
        createdAt: data.createdAt || new Date().toISOString(),
        customerId: data.customerId || 'c_demo_user',
        customerName: data.customerName || 'ผู้ใช้แอปพลิเคชัน (Customer)'
      };

      // Add to front of queue
      this.incomingOrders = [newOrder, ...this.incomingOrders];

      Swal.fire({
        icon: 'info',
        title: 'มีคำสั่งซื้อใหม่เข้ามา!',
        text: `ออเดอร์ #${newOrder.id.slice(0, 8)} เข้าคิวกรุณากดรับอาหาร`,
        toast: true,
        position: 'top-end',
        showConfirmButton: false,
        timer: 5000,
        timerProgressBar: true
      });
    });

    // 2. Listen for dispatcher matched offer alerts
    this.hubConnection.on('OfferReceived', (offer: any) => {
      console.log('SignalR OfferReceived received by Store:', offer);
      // Update matched order status in queue if present
      const index = this.incomingOrders.findIndex(o => o.id === offer.order?.id);
      if (index !== -1) {
        this.incomingOrders[index].state = 'MATCHING';
      }
    });

    // 3. Listen for general status transitions
    this.hubConnection.on('OrderStatusChanged', (orderId: string, newStatus: string) => {
      console.log('SignalR OrderStatusChanged in Store:', orderId, newStatus);
      const index = this.incomingOrders.findIndex(o => o.id === orderId);
      if (index !== -1) {
        this.incomingOrders[index].state = newStatus;
        if (newStatus === 'COMPLETED' || newStatus === 'CANCELLED') {
          // Remove from queue after completed/cancelled
          setTimeout(() => {
            this.incomingOrders = this.incomingOrders.filter(o => o.id !== orderId);
          }, 5000);
        }
      }
    });
  }

  playOrderChime() {
    try {
      // Audio Synth fallback chime so no external file dependency is broken!
      const audioCtx = new (window.AudioContext || (window as any).webkitAudioContext)();

      const playTone = (freq: number, start: number, duration: number) => {
        const osc = audioCtx.createOscillator();
        const gainNode = audioCtx.createGain();
        osc.connect(gainNode);
        gainNode.connect(audioCtx.destination);
        osc.frequency.setValueAtTime(freq, start);
        gainNode.gain.setValueAtTime(0.15, start);
        gainNode.gain.exponentialRampToValueAtTime(0.01, start + duration);
        osc.start(start);
        osc.stop(start + duration);
      };

      // Play elegant sweet double ding
      const now = audioCtx.currentTime;
      playTone(523.25, now, 0.4);      // C5
      playTone(659.25, now + 0.15, 0.5); // E5
    } catch (e) {
      console.warn('Audio chime synthesize not supported', e);
    }
  }

  acceptIncomingOrder(order: IncomingOrder) {
    this.loading = true;
    this.storeService.acceptOrderByStore(order.id, order.customerId).subscribe({
      next: () => {
        this.loading = false;

        // Update local state to accepted/preparing
        const index = this.incomingOrders.findIndex(o => o.id === order.id);
        if (index !== -1) {
          this.incomingOrders[index].state = 'ACCEPTED_BY_STORE';
        }

        Swal.fire({
          icon: 'success',
          title: 'รับออเดอร์เรียบร้อย',
          text: 'เริ่มเตรียมอาหารแสนอร่อยได้เลย! ระบบแจ้งเตือนลูกค้าและเริ่มหาไรเดอร์อัตโนมัติแล้ว',
          confirmButtonColor: '#10b981'
        });
      },
      error: (err) => {
        this.loading = false;
        console.error('Accept order failed', err);
        Swal.fire({
          icon: 'error',
          title: 'รับออเดอร์ล้มเหลว',
          text: err.error?.message || 'เกิดข้อผิดพลาดในการประมวลผลคำขอ',
          confirmButtonColor: '#ef4444'
        });
      }
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
        confirmButtonColor: '#3b82f6'
      });
    }
  }
}
