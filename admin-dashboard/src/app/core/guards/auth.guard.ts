import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { catchError, map, of } from 'rxjs';

/**
 * Auth Guard — ป้องกันการเข้าถึงหน้าที่ต้อง Login ก่อน
 * 
 * ตรวจสอบ:
 * 1. มี Access Token ที่ยังไม่หมดอายุอยู่หรือไม่
 * 2. ถ้า Token หมดอายุ → ลอง Refresh Token อัตโนมัติ
 * 3. ถ้า Refresh ไม่ได้ → แจ้งเตือนผู้ใช้แล้ว Redirect ไปหน้า Login
 */
export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  // ── Case 1: Token ยังใช้งานได้ → ผ่าน
  if (authService.isLoggedIn()) {
    return true;
  }

  return authService.verifySession().pipe(
    map(valid => valid
      ? true
      : router.createUrlTree(['/login'], {
          queryParams: { returnUrl: state.url }
        })),
    catchError(() => of(router.createUrlTree(['/login'], {
      queryParams: { returnUrl: state.url }
    })))
  );
};
