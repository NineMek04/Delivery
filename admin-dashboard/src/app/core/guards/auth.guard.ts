import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import Swal from 'sweetalert2';

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

  // ── Case 2: Token หมดอายุแต่มี Refresh Token → ลอง Refresh
  const refreshToken = authService.getRefreshToken();
  if (refreshToken) {
    // แจ้งเตือนกำลังตรวจสอบ Session
    Swal.fire({
      title: 'กำลังตรวจสอบ Session...',
      text: 'กรุณารอสักครู่',
      allowOutsideClick: false,
      showConfirmButton: false,
      didOpen: () => Swal.showLoading()
    });

    return authService.refreshAccessToken().toPromise().then(success => {
      Swal.close();

      if (success) {
        return true;
      }

      // Refresh ล้มเหลว → แจ้งเตือนและ Redirect
      showSessionExpiredAlert(router, state.url);
      return false;
    }).catch(() => {
      Swal.close();
      showSessionExpiredAlert(router, state.url);
      return false;
    });
  }

  // ── Case 3: ไม่มี Token เลย → แจ้งเตือนและ Redirect
  // ถ้าเข้าหน้าแรกสุดของเว็บ (Root Path) ให้ส่งไปหน้า login เงียบๆ ไม่ต้องขึ้นป๊อปอัปให้ผู้ใช้ตกใจ
  const targetUrl = state.url.split('?')[0];
  if (targetUrl === '/' || targetUrl === '') {
    router.navigate(['/login']);
  } else {
    showLoginRequiredAlert(router, state.url);
  }
  return false;
};

/**
 * แจ้งเตือนว่าต้อง Login ก่อนเข้าถึงหน้านี้
 */
function showLoginRequiredAlert(router: Router, returnUrl: string): void {
  Swal.fire({
    icon: 'warning',
    title: 'กรุณาเข้าสู่ระบบ',
    text: 'คุณต้องเข้าสู่ระบบก่อนเข้าถึงหน้านี้',
    confirmButtonText: 'ไปหน้า Login',
    confirmButtonColor: '#3b82f6',
    allowOutsideClick: false,
    timer: 5000,
    timerProgressBar: true
  }).then(() => {
    router.navigate(['/login'], {
      queryParams: { returnUrl }
    });
  });
}

/**
 * แจ้งเตือนว่า Session หมดอายุ
 */
function showSessionExpiredAlert(router: Router, returnUrl: string): void {
  Swal.fire({
    icon: 'info',
    title: 'เซสชันหมดอายุ',
    text: 'กรุณาเข้าสู่ระบบใหม่อีกครั้ง',
    confirmButtonText: 'เข้าสู่ระบบ',
    confirmButtonColor: '#3b82f6',
    allowOutsideClick: false,
    timer: 5000,
    timerProgressBar: true
  }).then(() => {
    router.navigate(['/login'], {
      queryParams: { returnUrl }
    });
  });
}
