import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface OptionItem {
  name: string;
  price: number;
}

export interface MenuOption {
  id: string;
  name: string; // e.g., "Add-on Toppings", "Beverage Size"
  required: boolean;
  maxSelections: number;
  items: OptionItem[];
}

export interface MenuItem {
  id: string;
  name: string;
  description: string;
  price: number;
  imageUrl: string;
  options: MenuOption[];
}

export interface CartItem {
  id: string; // unique item instance ID in cart
  menuItem: MenuItem;
  quantity: number;
  selectedOptions: { [optionName: string]: OptionItem[] };
  notes?: string;
}

@Injectable({
  providedIn: 'root'
})
export class StoreService {
  private http = inject(HttpClient);
  private readonly STORAGE_KEY = 'ninemek_delivery_store_menus';

  // In-memory store of menus keyed by shopId
  private menusMap: { [shopId: string]: MenuItem[] } = {};

  // Cart state
  private _cart = new BehaviorSubject<CartItem[]>([]);
  public cart$ = this._cart.asObservable();

  private _activeShopId = new BehaviorSubject<string | null>(null);
  public activeShopId$ = this._activeShopId.asObservable();

  constructor() {
    this.loadFromStorage();
  }

  private loadFromStorage() {
    const data = localStorage.getItem(this.STORAGE_KEY);
    if (data) {
      try {
        this.menusMap = JSON.parse(data);
      } catch (e) {
        console.error('Failed to parse menus from storage', e);
        this.initializeDefaultMenus();
      }
    } else {
      this.initializeDefaultMenus();
    }
  }

  private saveToStorage() {
    localStorage.setItem(this.STORAGE_KEY, JSON.stringify(this.menusMap));
  }

  private initializeDefaultMenus() {
    // Pre-seeded high quality premium menus with deep option schemas for wow factor
    const defaultBurgerMenu: MenuItem[] = [
      {
        id: 'b1',
        name: 'Signature Wagyu Truffle Burger',
        description: 'Premium grilled Australian Wagyu, black truffle paste, melted Swiss gruyere cheese, caramelized onions in freshly baked brioche buns.',
        price: 320,
        imageUrl: 'https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=600&auto=format&fit=crop&q=80',
        options: [
          {
            id: 'o1_1',
            name: 'Wagyu Doneness',
            required: true,
            maxSelections: 1,
            items: [
              { name: 'Medium Rare', price: 0 },
              { name: 'Medium', price: 0 },
              { name: 'Well Done', price: 0 }
            ]
          },
          {
            id: 'o1_2',
            name: 'Extra Toppings',
            required: false,
            maxSelections: 3,
            items: [
              { name: 'Crispy Applewood Bacon', price: 45 },
              { name: 'Fried Organic Egg', price: 25 },
              { name: 'Avocado Slices', price: 35 }
            ]
          }
        ]
      },
      {
        id: 'b2',
        name: 'Ultimate Double Cheese Burger',
        description: 'Two smashed premium angus patties, double cheddar cheese, secret house sauce, dill pickles, and crisp iceberg lettuce.',
        price: 240,
        imageUrl: 'https://images.unsplash.com/photo-1550547660-d9450f859349?w=600&auto=format&fit=crop&q=80',
        options: [
          {
            id: 'o2_1',
            name: 'Beverage Pairing',
            required: false,
            maxSelections: 1,
            items: [
              { name: 'Craft IPA Beer', price: 120 },
              { name: 'Classic Coca-Cola Zero', price: 30 },
              { name: 'Cold Brew Peach Tea', price: 45 }
            ]
          }
        ]
      }
    ];

    const defaultSushiMenu: MenuItem[] = [
      {
        id: 's1',
        name: 'Premium Omakase Nigiri Set (8pcs)',
        description: 'Chef\'s selection of highest grade seasonal sushi including Otoro, Uni, Botan Ebi, Akami, Madai, Sake, and Unagi.',
        price: 1250,
        imageUrl: 'https://images.unsplash.com/photo-1579871494447-9811cf80d66c?w=600&auto=format&fit=crop&q=80',
        options: [
          {
            id: 'o3_1',
            name: 'Wasabi Level',
            required: true,
            maxSelections: 1,
            items: [
              { name: 'Standard Wasabi', price: 0 },
              { name: 'Extra Fresh Wasabi', price: 20 },
              { name: 'No Wasabi', price: 0 }
            ]
          }
        ]
      },
      {
        id: 's2',
        name: 'Spicy Creamy Salmon Volcano Roll',
        description: 'Torched salmon roll filled with crab salad, cucumber, avocado topped with volcano spicy mayo, unagi sauce, green onion, and tobiko.',
        price: 360,
        imageUrl: 'https://images.unsplash.com/photo-1611143669185-af224c5e3252?w=600&auto=format&fit=crop&q=80',
        options: [
          {
            id: 'o4_1',
            name: 'Add-on Sauce',
            required: false,
            maxSelections: 2,
            items: [
              { name: 'Sriracha Spicy Mayo', price: 15 },
              { name: 'Sweet Unagi Glaze', price: 15 }
            ]
          }
        ]
      }
    ];

    // Seed default menus map. These fallback menus work seamlessly if shops are fetched.
    // We also dynamically assign these templates to any fetched shop dynamically so menus never look blank!
    this.menusMap['burger_default'] = defaultBurgerMenu;
    this.menusMap['sushi_default'] = defaultSushiMenu;
    this.saveToStorage();
  }

  // Get menus for a shop (returns seeded menus or initializes a blank list)
  public getShopMenus(shopId: string): MenuItem[] {
    if (!this.menusMap[shopId]) {
      // Intelligently bind to a fallback to populate visual beauty!
      if (shopId.toLowerCase().includes('burger') || shopId.toLowerCase().includes('bar')) {
        this.menusMap[shopId] = JSON.parse(JSON.stringify(this.menusMap['burger_default']));
      } else if (shopId.toLowerCase().includes('sushi') || shopId.toLowerCase().includes('zen')) {
        this.menusMap[shopId] = JSON.parse(JSON.stringify(this.menusMap['sushi_default']));
      } else {
        // Create generic elegant menu item based on store name
        this.menusMap[shopId] = [
          {
            id: `g_${shopId}_1`,
            name: 'Chef\'s Special Fried Rice Premium',
            description: 'Stir-fried premium jasmine rice with giant river prawns, cage-free egg, scallions, served with green lime.',
            price: 180,
            imageUrl: 'https://images.unsplash.com/photo-1603133872878-684f208fb84b?w=600&auto=format&fit=crop&q=80',
            options: [
              {
                id: 'og_1',
                name: 'Spiciness Level',
                required: true,
                maxSelections: 1,
                items: [
                  { name: 'Not Spicy', price: 0 },
                  { name: 'Medium Spicy', price: 0 },
                  { name: 'Extra Spicy 🔥', price: 0 }
                ]
              },
              {
                id: 'og_2',
                name: 'Extra Toppings',
                required: false,
                maxSelections: 2,
                items: [
                  { name: 'Extra Giant Prawn (1pc)', price: 60 },
                  { name: 'Fried Organic Egg', price: 20 }
                ]
              }
            ]
          }
        ];
      }
      this.saveToStorage();
    }
    return this.menusMap[shopId];
  }

  // CRUD for menu management (Store Role)
  public addMenuItem(shopId: string, item: Omit<MenuItem, 'id'>): MenuItem {
    const newItem: MenuItem = {
      ...item,
      id: 'm_' + Math.random().toString(36).substring(2, 9)
    };
    if (!this.menusMap[shopId]) {
      this.menusMap[shopId] = [];
    }
    this.menusMap[shopId].push(newItem);
    this.saveToStorage();
    return newItem;
  }

  public updateMenuItem(shopId: string, item: MenuItem): void {
    const items = this.menusMap[shopId] || [];
    const index = items.findIndex(i => i.id === item.id);
    if (index !== -1) {
      items[index] = item;
      this.saveToStorage();
    }
  }

  public deleteMenuItem(shopId: string, itemId: string): void {
    const items = this.menusMap[shopId] || [];
    this.menusMap[shopId] = items.filter(i => i.id !== itemId);
    this.saveToStorage();
  }

  // ── Cart Management ──
  public addToCart(shopId: string, item: MenuItem, quantity: number, selectedOptions: { [optionName: string]: OptionItem[] }, notes?: string) {
    // If shop changes, reset cart
    if (this._activeShopId.getValue() !== shopId) {
      this._cart.next([]);
      this._activeShopId.next(shopId);
    }

    const currentCart = this._cart.getValue();
    
    // Generate unique identifier for this item config (based on selected option combinations)
    const optionsHash = JSON.stringify(selectedOptions);
    const existingIndex = currentCart.findIndex(c => 
      c.menuItem.id === item.id && 
      JSON.stringify(c.selectedOptions) === optionsHash &&
      c.notes === notes
    );

    if (existingIndex !== -1) {
      // Increase quantity of existing config
      currentCart[existingIndex].quantity += quantity;
      this._cart.next([...currentCart]);
    } else {
      // Add as new configuration
      const newCartItem: CartItem = {
        id: 'c_' + Math.random().toString(36).substring(2, 9),
        menuItem: item,
        quantity,
        selectedOptions,
        notes
      };
      this._cart.next([...currentCart, newCartItem]);
    }
  }

  public updateCartQuantity(cartItemId: string, delta: number) {
    const currentCart = this._cart.getValue();
    const index = currentCart.findIndex(c => c.id === cartItemId);
    if (index !== -1) {
      currentCart[index].quantity += delta;
      if (currentCart[index].quantity <= 0) {
        currentCart.splice(index, 1);
      }
      this._cart.next([...currentCart]);
    }
  }

  public removeFromCart(cartItemId: string) {
    const currentCart = this._cart.getValue();
    this._cart.next(currentCart.filter(c => c.id !== cartItemId));
  }

  public clearCart() {
    this._cart.next([]);
    this._activeShopId.next(null);
  }

  public getCartTotal(): number {
    return this._cart.getValue().reduce((sum, item) => {
      let optionsCost = 0;
      Object.values(item.selectedOptions).forEach(opts => {
        opts.forEach(opt => optionsCost += opt.price);
      });
      return sum + ((item.menuItem.price + optionsCost) * item.quantity);
    }, 0);
  }

  // ── Place Order API ──
  public placeOrder(orderPayload: {
    pickupLat: number;
    pickupLng: number;
    dropoffLat: number;
    dropoffLng: number;
    expectedDeliveryTime: string;
  }): Observable<any> {
    const apiUrl = environment.config.baseConfig.apiUrl + '/orders';
    return this.http.post<any>(apiUrl, orderPayload);
  }

  // ── Accept Order by Store API ──
  public acceptOrderByStore(orderId: string, customerId: string): Observable<any> {
    const apiUrl = `${environment.config.baseConfig.apiUrl}/orders/${orderId}/accept-by-store?customerId=${customerId}`;
    return this.http.post<any>(apiUrl, {});
  }
}
