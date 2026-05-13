import { Injectable, OnDestroy } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, interval, Subscription } from 'rxjs';
import { jwtDecode } from 'jwt-decode';

@Injectable({
  providedIn: 'root'
})
export class AuthService implements OnDestroy {
  private readonly TOKEN_KEY = 'delivery_access_token';
  private clockingSubscription?: Subscription;
  public isAuthenticated$ = new BehaviorSubject<boolean>(this.hasValidToken());

  constructor(private router: Router) {
    this.startTokenClocking();
  }

  public getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  public setToken(token: string): void {
    localStorage.setItem(this.TOKEN_KEY, token);
    this.isAuthenticated$.next(true);
    this.startTokenClocking();
  }

  public logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    this.isAuthenticated$.next(false);
    this.stopTokenClocking();
    this.router.navigate(['/']); // Update this to actual login route if available
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
    // Check every 5 seconds if token expired (Token Clocking)
    this.clockingSubscription = interval(5000).subscribe(() => {
      if (!this.hasValidToken() && this.isAuthenticated$.value) {
        // Token expired, trigger logout
        this.logout();
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
