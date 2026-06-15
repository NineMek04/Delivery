import { ApplicationConfig, provideZoneChangeDetection, APP_INITIALIZER } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideCharts, withDefaultRegisterables } from 'ng2-charts';

import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { AuthService } from './core/services/auth.service';

/**
 * App Initializer — ตรวจสอบ Session ก่อน App โหลด
 * 
 * กฎ:
 * - ถ้าไม่มี Token → ผ่านทันที (ไม่ block) → Route จะพาไปหน้า Login เอง
 * - ถ้ามี Token → verify กับ Backend แบบมี Timeout 5 วินาที
 * - ถ้า Backend ไม่ตอบหรือ error → clear state แล้วผ่าน (ไม่ block App ค้าง)
 */
function initializeAuth(authService: AuthService) {
  return (): Promise<boolean> => {
    return new Promise<boolean>((resolve) => {
      const timeout = setTimeout(() => {
        console.warn('[AuthInit] Session verification timed out.');
        resolve(true);
      }, 5000);

      authService.verifySession().subscribe({
        next: () => {
          clearTimeout(timeout);
          resolve(true);
        },
        error: () => {
          clearTimeout(timeout);
          // Session verify ล้มเหลว → ผ่านไปก่อน (Guard จะเช็คอีกชั้น)
          console.warn('[AuthInit] Session verification failed. Guards will handle redirect.');
          resolve(true);
        }
      });
    });
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }), 
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([authInterceptor, errorInterceptor]),
      withXsrfConfiguration({
        cookieName: 'XSRF-TOKEN',
        headerName: 'X-XSRF-TOKEN'
      })
    ),
    provideCharts(withDefaultRegisterables()),
    {
      provide: APP_INITIALIZER,
      useFactory: initializeAuth,
      deps: [AuthService],
      multi: true
    }
  ]
};
