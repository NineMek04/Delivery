import { Injectable, OnDestroy } from '@angular/core';
import { Router } from '@angular/router';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, interval, Subscription, Observable, of, throwError } from 'rxjs';
import { tap, catchError, finalize, map, shareReplay } from 'rxjs/operators';
import { req } from '../http/delivery-http-request';

/** Role ที่ได้รับอนุญาตให้เข้าถึง Admin Dashboard */
export type DashboardRole = 'Admin' | 'Dispatcher';

/** Role ทั้งหมดในระบบ */
export type AppRole = DashboardRole | 'Rider' | 'Customer' | 'StorePartner';

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
  public isAuthenticated$ = new BehaviorSubject<boolean>(false);

  /** BehaviorSubject สำหรับ Role ปัจจุบัน — ให้ Component subscribe ได้ */
  public currentRole$ = new BehaviorSubject<string | null>(this.extractCurrentRole());

  private isRefreshing = false;
  private refreshRequest$?: Observable<boolean>;

  constructor(
    private router: Router,
    private http: HttpClient
  ) {
    this.startTokenClocking();
  }

  // ── Token Management ──────────────────────────────────────────────

  public getToken(): string | null {
    // Return null to ensure no JWT token is stored or accessed on client-side
    return null;
  }

  public getRefreshToken(): string | null {
    return null;
  }

  public getUserData(): any | null {
    const data = localStorage.getItem(this.USER_DATA_KEY);
    try {
      return data ? JSON.parse(data) : null;
    } catch {
      return null;
    }
  }

  public setTokens(accessToken: string, refreshToken: string, userData?: any, expiresAt?: string): void {
    if (userData) {
      localStorage.setItem(this.USER_DATA_KEY, JSON.stringify(userData));
    }
    if (expiresAt) {
      localStorage.setItem('delivery_access_token_expires', expiresAt);
    }
    this.isAuthenticated$.next(true);
    this.currentRole$.next(this.extractCurrentRole());
    this.startTokenClocking();
  }

  public setToken(token: string): void {
    this.isAuthenticated$.next(true);
    this.currentRole$.next(this.extractCurrentRole());
    this.startTokenClocking();
  }

  // ── Role & Permission Checks ──────────────────────────────────────

  /**
   * ดึง Role ปัจจุบันจาก userData ใน localStorage
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
    return this.isAuthenticated$.value;
  }

  /**
   * สำหรับ Cookie Authentication ดึงข้อมูลจาก JWT Claims ฝั่ง Web จะใช้ UserData แทน
   */
  public getDecodedToken(): JwtClaims | null {
    return null;
  }

  /**
   * ตรวจสอบ Session กับ Backend — เรียกตอน App Initialize
   * เพื่อยืนยันว่า Token ยังใช้งานได้จริงจากฝั่ง Server
   */
  public verifySession(): Observable<boolean> {
    return req<any>('/auth/session', this.http).get().pipe(
      map((res: any) => {
        const user = res?.Value || res?.value || res;
        if (!user) {
          this.clearAuthState();
          return false;
        }

        localStorage.setItem(this.USER_DATA_KEY, JSON.stringify(user));
        this.isAuthenticated$.next(true);
        this.currentRole$.next(this.extractCurrentRole());
        return true;
      }),
      catchError((error: HttpErrorResponse) => {
        if (error.status === 401 || error.status === 403) {
          this.clearAuthState();
        }
        return of(false);
      })
    );
  }

  // ── Auth Actions ──────────────────────────────────────────────────

  public login(credentials: any): Observable<any> {
    return req<any>('/Auth/login', this.http).body(credentials).post().pipe(
      tap((res: any) => {
        const data = res.Value || res.value || res;
        const accessToken = data?.AccessToken || data?.accessToken;
        const refreshToken = data?.RefreshToken || data?.refreshToken;
        const user = data?.User || data?.user;
        const expiresAt = data?.ExpiresAt || data?.expiresAt;

        if (expiresAt) {
          this.setTokens(accessToken, refreshToken, user, expiresAt);
        }
      })
    );
  }

  public register(data: any): Observable<any> {
    return req<any>('/Auth/register', this.http).body(data).post().pipe(
      tap((res: any) => {
        const d = res.Value || res.value || res;
        const accessToken = d?.AccessToken || d?.accessToken;
        const refreshToken = d?.RefreshToken || d?.refreshToken;
        const user = d?.User || d?.user;
        const expiresAt = d?.ExpiresAt || d?.expiresAt;

        if (expiresAt) {
          this.setTokens(accessToken, refreshToken, user, expiresAt);
        }
      })
    );
  }

  public refreshAccessToken(): Observable<boolean> {
    if (this.refreshRequest$) {
      return this.refreshRequest$;
    }

    this.isRefreshing = true;
    const request$ = req<any>('/auth/refresh', this.http)
      .body({ RefreshToken: '' }) // Refresh Token is stored in cookie, send empty string to match DTO
      .post()
      .pipe(
        map((res: any) => {
          const data = res.Value || res.value || res;
          const newAccessToken = data?.AccessToken || data?.accessToken;
          const newRefreshToken = data?.RefreshToken || data?.refreshToken;
          const user = data?.User || data?.user;
          const expiresAt = data?.ExpiresAt || data?.expiresAt;

          if (expiresAt) {
            this.setTokens(newAccessToken, newRefreshToken, user, expiresAt);
            return true;
          }

          return false;
        }),
        catchError((error: HttpErrorResponse) =>
          [400, 401, 403].includes(error.status)
            ? of(false)
            : throwError(() => error)),
        finalize(() => {
          this.isRefreshing = false;
          this.refreshRequest$ = undefined;
        }),
        shareReplay({ bufferSize: 1, refCount: false })
      );

    this.refreshRequest$ = request$;
    return request$;
  }

  public logout(): Observable<any> {
    return req<any>('/Auth/logout', this.http).post().pipe(
      catchError(() => of(null)), // ถ้า API พัง ก็ logout ฝั่ง client อยู่ดี
      tap(() => this.forceLogout())
    );
  }

  // ── Private Helpers ───────────────────────────────────────────────

  /**
   * ดึง Role จาก userData
   */
  private extractCurrentRole(): string | null {
    const userData = this.getUserData();
    return userData?.Role || userData?.role || null;
  }

  public forceLogout(): void {
    this.clearAuthState();
    void this.router.navigateByUrl('/login', { replaceUrl: true });
  }

  /**
   * ล้าง Auth State ทั้งหมด (ไม่ Navigate — ใช้ภายใน)
   */
  private clearAuthState(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_TOKEN_KEY);
    localStorage.removeItem(this.USER_DATA_KEY);
    localStorage.removeItem('delivery_access_token_expires');
    this.isAuthenticated$.next(false);
    this.currentRole$.next(null);
    this.stopTokenClocking();
  }

  private startTokenClocking(): void {
    this.stopTokenClocking();
    this.clockingSubscription = interval(30000).subscribe(() => {
      const expires = localStorage.getItem('delivery_access_token_expires');
      if (!expires || !this.isAuthenticated$.value) return;

      try {
        const expiresTime = new Date(expires).getTime();
        const currentTime = Date.now();
        const timeRemainingSeconds = Math.floor((expiresTime - currentTime) / 1000);

        // Proactive refresh if expiring in < 2 mins (120 seconds)
        if (timeRemainingSeconds <= 0) {
          this.refreshProactively();
        } else if (timeRemainingSeconds < 120) {
          this.refreshProactively();
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

  private refreshProactively(): void {
    this.refreshAccessToken().subscribe({
      next: success => {
        if (!success) this.forceLogout();
      },
      error: error => {
        console.warn('[Auth] Proactive refresh deferred due to network error.', error);
      }
    });
  }

  ngOnDestroy(): void {
    this.stopTokenClocking();
  }
}
