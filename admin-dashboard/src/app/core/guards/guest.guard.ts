import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import Swal from 'sweetalert2';

/**
 * Guest Guard — ป้องกันผู้ใช้ที่ Login แล้วไม่ให้เข้าหน้า Login/Register ซ้ำ
 * 
 * ถ้า Token ยังใช้งานได้ → Redirect ไปหน้า Dashboard พร้อมแจ้งเตือน
 * ถ้ายังไม่ Login → อนุญาตให้เข้าหน้า Login/Register ได้ปกติ
 */
export const guestGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isLoggedIn()) {
    const userData = authService.getUserData();
    const displayName = userData?.FullName || userData?.fullName || userData?.Email || userData?.email || 'ผู้ใช้';

    // แจ้งเตือนว่า Login อยู่แล้ว พร้อม Toast แบบมุมขวาบน
    const Toast = Swal.mixin({
      toast: true,
      position: 'top-end',
      showConfirmButton: false,
      timer: 3000,
      timerProgressBar: true,
      didOpen: (toast) => {
        toast.onmouseenter = Swal.stopTimer;
        toast.onmouseleave = Swal.resumeTimer;
      }
    });

    Toast.fire({
      icon: 'info',
      title: `ยินดีต้อนรับกลับ`,
      text: `${displayName} — คุณเข้าสู่ระบบอยู่แล้ว`
    });

    const role = authService.getUserRole();
    if (role?.toLowerCase() === 'customer') {
      router.navigate(['/customer']);
    } else if (role?.toLowerCase() === 'storepartner') {
      router.navigate(['/store-partner']);
    } else {
      router.navigate(['/dashboard']);
    }
    return false;
  }

  return true;
};
