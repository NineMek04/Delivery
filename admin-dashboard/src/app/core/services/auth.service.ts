import { Injectable, OnDestroy } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, interval, Subscription, Observable, of } from 'rxjs';
import { tap, catchError, map } from 'rxjs/operators';
import { jwtDecode } from 'jwt-decode';
import { AppComponent } from '../../app.component';
import { req } from '../http/delivery-http-request';

@Injectable({
  providedIn: 'root'
})
export class AuthService implements OnDestroy {
  private readonly TOKEN_KEY = 'delivery_access_token';
  private readonly REFRESH_TOKEN_KEY = 'delivery_refresh_token';
  private readonly USER_DATA_KEY = 'delivery_user_data';

  private clockingSubscription?: Subscription;
  public isAuthenticated$ = new BehaviorSubject<boolean>(this.hasValidToken());
  private isRefreshing = false;

  constructor(private router: Router) {
    this.startTokenClocking();
  }

  public getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  public getRefreshToken(): string | null {
    return localStorage.getItem(this.REFRESH_TOKEN_KEY);
  }

  public getUserData(): any | null {
    const data = localStorage.getItem(this.USER_DATA_KEY);
    return data ? JSON.parse(data) : null;
  }

  public setTokens(accessToken: string, refreshToken: string, userData?: any): void {
    localStorage.setItem(this.TOKEN_KEY, accessToken);
    localStorage.setItem(this.REFRESH_TOKEN_KEY, refreshToken);
    if (userData) {
      localStorage.setItem(this.USER_DATA_KEY, JSON.stringify(userData));
    }
    this.isAuthenticated$.next(true);
    this.startTokenClocking();
  }

  // public getToken(): string | null {
  //   return localStorage.getItem(this.TOKEN_KEY);
  // }

  public setToken(token: string): void {
    localStorage.setItem(this.TOKEN_KEY, token);
    this.isAuthenticated$.next(true);
    this.startTokenClocking();
  }

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
    // Optional: could call backend logout API if needed
    this.forceLogout();
    return req<any>('/Auth/logout').post();
  }

  private forceLogout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_TOKEN_KEY);
    localStorage.removeItem(this.USER_DATA_KEY);
    this.isAuthenticated$.next(false);
    this.stopTokenClocking();
    const router = AppComponent.InjectorInstance.get(Router);
    router.navigate(['/login']);
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
