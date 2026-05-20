import { Routes } from '@angular/router';
import { MainLayoutComponent } from './layouts/main-layout/main-layout.component';
import { authGuard, roleGuard, guestGuard } from './core/guards';

export const routes: Routes = [
  // ── หน้าแรก: ถ้ายังไม่ Login → ไปหน้า Login / ถ้า Login แล้ว → ไปหน้า Dashboard ──
  { path: '', redirectTo: 'login', pathMatch: 'full' },

  // ── Guest Routes (เฉพาะคนที่ยังไม่ Login) ──
  // guestGuard จะ redirect ไปหน้า Dashboard อัตโนมัติถ้า Login อยู่แล้ว
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent),
    data: { title: 'เข้าสู่ระบบ' }
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/register/register.component').then(m => m.RegisterComponent),
    data: { title: 'ลงทะเบียน' }
  },

  // ── Protected Routes (ต้อง Login + Role Admin/Dispatcher) ──
  {
    path: '',
    component: MainLayoutComponent,
    canActivate: [authGuard, roleGuard],
    data: { roles: ['Admin', 'Dispatcher'] },
    children: [
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent),
        data: { title: 'Dashboard' }
      },
      {
        path: 'map',
        loadComponent: () => import('./features/sim-map/sim-map.component').then(m => m.SimMapComponent),
        data: { title: 'Simulation Fleet Map' }
      },
      {
        path: 'map-live',
        loadComponent: () => import('./features/map/map.component').then(m => m.MapComponent),
        data: { title: 'Live Fleet Map' }
      },
      {
        path: 'orders',
        loadComponent: () => import('./features/orders/orders.component').then(m => m.OrdersComponent),
        data: { title: 'Order Operations' }
      },
      {
        path: 'analytics',
        loadComponent: () => import('./features/analytics/analytics.component').then(m => m.AnalyticsComponent),
        data: { title: 'Analytics' }
      },
      {
        path: 'riders',
        loadComponent: () => import('./features/riders/riders.component').then(m => m.RidersComponent),
        data: { title: 'Rider Fleet' }
      },
      {
        path: 'shops',
        loadComponent: () => import('./features/shops/shops.component').then(m => m.ShopsComponent),
        data: { title: 'Shop Management' }
      }
    ]
  },

  // ── Customer App ──
  {
    path: 'customer',
    canActivate: [authGuard, roleGuard],
    data: { roles: ['Customer'], title: 'Smart Customer Portal' },
    loadComponent: () => import('./features/customer/customer.component').then(m => m.CustomerComponent)
  },

  // ── Store Partner App ──
  {
    path: 'store-partner',
    canActivate: [authGuard, roleGuard],
    data: { roles: ['StorePartner'], title: 'Store Partner Portal' },
    loadComponent: () => import('./features/store-partner/store-partner.component').then(m => m.StorePartnerComponent)
  },

  // ── Fallback → redirect ไปหน้า Login (Guard จะจัดการ Redirect ต่อเอง) ──
  { path: '**', redirectTo: 'login' }
];
