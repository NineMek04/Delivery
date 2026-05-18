import { Injectable, OnDestroy } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, interval, Subscription, Observable, of } from 'rxjs';
import { tap, catchError, map } from 'rxjs/operators';
import { jwtDecode } from 'jwt-decode';
import { AppComponent } from '../../app.component';
import { req } from '../http/delivery-http-request';

/** Role ที่ได้รับอนุญาตให้เข้าถึง Admin Dashboard */
export type DashboardRole = 'Admin' | 'Dispatcher';

/** Role ทั้งหมดในระบบ */
export type AppRole = DashboardRole | 'Rider' | 'Customer';

/** Interface สำหรับ decoded JWT token claims */
interface JwtClaims {
  sub?: string;
  email?: string;
  role?: string;
  exp?: number;
  iat?: number;
  [key: string]: any;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService implements OnDestroy {
  private readonly TOKEN_KEY = 'delivery_access_token';
  private readonly REFRESH_TOKEN_KEY = 'delivery_refresh_token';
  private readonly USER_DATA_KEY = 'delivery_user_data';

  /** Roles ที่อนุญาตให้ใช้งาน Admin Dashboard */
  private readonly ALLOWED_DASHBOARD_ROLES: AppRole[] = ['Admin', 'Dispatcher'];

  private clockingSubscription?: Subscription;
  public isAuthenticated$ = new BehaviorSubject<boolean>(this.hasValidToken());

  /** BehaviorSubject สำหรับ Role ปัจจุบัน — ให้ Component subscribe ได้ */
  public currentRole$ = new BehaviorSubject<string | null>(this.extractCurrentRole());

  private isRefreshing = false;

  constructor(private router: Router) {
    this.startTokenClocking();
  }

  // ── Token Management ──────────────────────────────────────────────

  public getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  public getRefreshToken(): string | null {
    return localStorage.getItem(this.REFRESH_TOKEN_KEY);
  }

  public getUserData(): any | null {
    const data = localStorage.getItem(this.USER_DATA_KEY);
    try {
      return data ? JSON.parse(data) : null;
    } catch {
      return null;
    }
  }

  public setTokens(accessToken: string, refreshToken: string, userData?: any): void {
    localStorage.setItem(this.TOKEN_KEY, accessToken);
    localStorage.setItem(this.REFRESH_TOKEN_KEY, refreshToken);
    if (userData) {
      localStorage.setItem(this.USER_DATA_KEY, JSON.stringify(userData));
    }
    this.isAuthenticated$.next(true);
    this.currentRole$.next(this.extractCurrentRole());
    this.startTokenClocking();
  }

  public setToken(token: string): void {
    localStorage.setItem(this.TOKEN_KEY, token);
    this.isAuthenticated$.next(true);
    this.currentRole$.next(this.extractCurrentRole());
    this.startTokenClocking();
  }

  // ── Role & Permission Checks ──────────────────────────────────────

  /**
   * ดึง Role ปัจจุบันจาก JWT Token หรือ userData ใน localStorage
   */
  public getUserRole(): string | null {
    return this.extractCurrentRole();
  }

  /**
   * ตรวจสอบว่าผู้ใช้มี Role ที่ระบุหรือไม่
   */
  public hasRole(role: string): boolean {
    const currentRole = this.getUserRole();
    if (!currentRole) return false;
    return currentRole.toLowerCase() === role.toLowerCase();
  }

  /**
   * ตรวจสอบว่าผู้ใช้มีสิทธิ์เข้าถึง Admin Dashboard หรือไม่
   * อนุญาตเฉพาะ Admin และ Dispatcher เท่านั้น
   */
  public canAccessDashboard(): boolean {
    const role = this.getUserRole();
    if (!role) return false;
    return this.ALLOWED_DASHBOARD_ROLES.some(r => r.toLowerCase() === role.toLowerCase());
  }

  /**
   * ตรวจสอบว่ามี Token ที่ยังไม่หมดอายุอยู่หรือไม่ — ใช้สำหรับ Guard
   */
  public isLoggedIn(): boolean {
    return this.hasValidToken();
  }

  /**
   * ดึงข้อมูลจาก JWT Claims โดยตรง (ไม่ต้องเรียก API)
   */
  public getDecodedToken(): JwtClaims | null {
    const token = this.getToken();
    if (!token) return null;
    try {
      return jwtDecode<JwtClaims>(token);
    } catch {
      return null;
    }
  }

  /**
   * ตรวจสอบ Session กับ Backend — เรียกตอน App Initialize
   * เพื่อยืนยันว่า Token ยังใช้งานได้จริงจากฝั่ง Server
   */
  public verifySession(): Observable<boolean> {
    const token = this.getToken();
    if (!token || !this.hasValidToken()) {
      this.clearAuthState();
      return of(false);
    }

    // Token ยังไม่หมดอายุ → ถือว่า Session ยังใช้ได้
    // ไม่เรียก API /Auth/session ตอน Startup เพื่อป้องกันปัญหา:
    // 1. Backend ไม่ได้รัน → App ค้าง
    // 2. errorInterceptor จับ 401 แล้ว refresh ซ้ำ
    // การเช็คสิทธิ์จริงจะทำผ่าน Guards + Interceptors ขณะใช้งาน
    this.isAuthenticated$.next(true);
    this.currentRole$.next(this.extractCurrentRole());
    return of(true);
  }

  // ── Auth Actions ──────────────────────────────────────────────────

  public login(credentials: any): Observable<any> {
    return req<any>('/Auth/login').body(credentials).post().pipe(
      tap((res: any) => {
        const data = res.Value || res.value || res;
        const accessToken = data?.AccessToken || data?.accessToken;
        const refreshToken = data?.RefreshToken || data?.refreshToken;
        const user = data?.User || data?.user;

        if (accessToken) {
          this.setTokens(accessToken, refreshToken, user);
        }
      })
    );
  }

  public register(data: any): Observable<any> {
    return req<any>('/Auth/register').body(data).post().pipe(
      tap((res: any) => {
        const d = res.Value || res.value || res;
        const accessToken = d?.AccessToken || d?.accessToken;
        const refreshToken = d?.RefreshToken || d?.refreshToken;
        const user = d?.User || d?.user;

        if (accessToken) {
          this.setTokens(accessToken, refreshToken, user);
        }
      })
    );
  }

  public refreshAccessToken(): Observable<boolean> {
    if (this.isRefreshing) {
      return of(false);
    }

    const refreshToken = this.getRefreshToken();
    if (!refreshToken) {
      this.forceLogout();
      return of(false);
    }

    this.isRefreshing = true;
    return req<any>('/auth/refresh')
      .body({ RefreshToken: refreshToken })
      .post()
      .pipe(
        map((res: any) => {
          this.isRefreshing = false;
          const data = res.Value || res.value || res;
          const newAccessToken = data?.AccessToken || data?.accessToken;
          const newRefreshToken = data?.RefreshToken || data?.refreshToken;
          const user = data?.User || data?.user;

          if (newAccessToken) {
            this.setTokens(newAccessToken, newRefreshToken, user);
            return true;
          } else {
            this.forceLogout();
            return false;
          }
        }),
        catchError((err) => {
          this.isRefreshing = false;
          this.forceLogout();
          return of(false);
        })
      );
  }

  public logout(): Observable<any> {
    // เรียก API ก่อน (token ยังอยู่ใน localStorage ตอนนี้)
    // แล้วค่อยลบ token หลังจาก API ตอบกลับ (สำเร็จหรือไม่สำเร็จก็ logout)
    return req<any>('/Auth/logout').post().pipe(
      catchError(() => of(null)), // ถ้า API พัง ก็ logout ฝั่ง client อยู่ดี
      tap(() => this.forceLogout())
    );
  }

  // ── Private Helpers ───────────────────────────────────────────────

  /**
   * ดึง Role จาก JWT Claims หรือ userData
   * Priority: JWT Claims → localStorage userData
   */
  private extractCurrentRole(): string | null {
    // 1. ลองดึงจาก JWT Claims ก่อน (แม่นยำที่สุด)
    const decoded = this.getDecodedToken();
    if (decoded) {
      // ASP.NET Core อาจเก็บ role เป็น claim key ต่างกัน
      const role = decoded['role']
        || decoded['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
        || decoded['Role'];
      if (role) return role;
    }

    // 2. Fallback ไปดึงจาก userData ที่เก็บไว้ตอน Login
    const userData = this.getUserData();
    return userData?.Role || userData?.role || null;
  }

  public forceLogout(): void {
    this.clearAuthState();
    const router = AppComponent.InjectorInstance.get(Router);
    router.navigate(['/login']);
  }

  /**
   * ล้าง Auth State ทั้งหมด (ไม่ Navigate — ใช้ภายใน)
   */
  private clearAuthState(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_TOKEN_KEY);
    localStorage.removeItem(this.USER_DATA_KEY);
    this.isAuthenticated$.next(false);
    this.currentRole$.next(null);
    this.stopTokenClocking();
  }

  private hasValidToken(): boolean {
    const token = this.getToken();
    if (!token) return false;
    try {
      const decoded: any = jwtDecode(token);
      const currentTime = Math.floor(Date.now() / 1000);
      return decoded.exp > currentTime;
    } catch {
      return false;
    }
  }

  private startTokenClocking(): void {
    this.stopTokenClocking();
    // Check every 30 seconds if token expired (Token Clocking)
    this.clockingSubscription = interval(30000).subscribe(() => {
      const token = this.getToken();
      if (!token || !this.isAuthenticated$.value) return;

      try {
        const decoded: any = jwtDecode(token);
        const currentTime = Math.floor(Date.now() / 1000);
        const timeRemaining = decoded.exp - currentTime;

        // Proactive refresh if expiring in < 2 mins (120 seconds)
        if (timeRemaining < 120) {
          this.refreshAccessToken().subscribe();
        } else if (timeRemaining <= 0) {
          // Token is already expired, force refresh or logout
          this.refreshAccessToken().subscribe(success => {
            if (!success) this.forceLogout();
          });
        }
      } catch (err) {
        this.forceLogout();
      }
    });
  }

  private stopTokenClocking(): void {
    if (this.clockingSubscription) {
      this.clockingSubscription.unsubscribe();
      this.clockingSubscription = undefined;
    }
  }

  ngOnDestroy(): void {
    this.stopTokenClocking();
  }
}
