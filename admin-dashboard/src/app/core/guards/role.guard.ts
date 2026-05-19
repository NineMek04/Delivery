import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import Swal from 'sweetalert2';

/**
 * Role Guard — ป้องกันผู้ใช้ที่ไม่มีสิทธิ์ Admin/Dispatcher เข้าใช้ Dashboard
 * 
 * Guard นี้ควรใช้ร่วมกับ authGuard เสมอ (authGuard ตรวจ Login ก่อน)
 * 
 * ตรวจสอบ:
 * 1. ผู้ใช้มี Role ที่อนุญาต (Admin, Dispatcher) หรือไม่
 * 2. ถ้าเป็น Rider/Customer → แจ้งเตือนว่าไม่มีสิทธิ์เข้าถึง Dashboard
 * 3. ถ้าไม่มี Role ข้อมูลเลย → แจ้งเตือนให้ติดต่อผู้ดูแลระบบ
 */
export const roleGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  // ดึง Role ที่กำหนดไว้ใน Route Data (ถ้ามี) หรือใช้ default
  const allowedRoles: string[] = route.data?.['roles'] || ['Admin', 'Dispatcher'];

  const userRole = authService.getUserRole();

  // ── Case 1: ไม่พบ Role → อาจเป็น Token เก่าหรือข้อมูลผิดพลาด
  if (!userRole) {
    Swal.fire({
      icon: 'error',
      title: 'ไม่พบข้อมูลสิทธิ์',
      html: `
        <p>ระบบไม่สามารถตรวจสอบสิทธิ์ของคุณได้</p>
        <p style="color: #888; font-size: 0.85em;">กรุณาเข้าสู่ระบบใหม่ หรือติดต่อผู้ดูแลระบบ</p>
      `,
      confirmButtonText: 'เข้าสู่ระบบใหม่',
      confirmButtonColor: '#d33',
      showCancelButton: true,
      cancelButtonText: 'ปิด',
      allowOutsideClick: false
    }).then((result) => {
      if (result.isConfirmed) {
        authService.forceLogout();
      }
    });
    return false;
  }

  // ── Case 2: มี Role แต่ไม่ได้อยู่ในรายการที่อนุญาต
  const isAllowed = allowedRoles.some(r => r.toLowerCase() === userRole.toLowerCase());

  if (!isAllowed) {
    const roleDisplay = getRoleDisplayName(userRole);
    const allowedDisplay = allowedRoles.map(r => getRoleDisplayName(r)).join(', ');

    Swal.fire({
      icon: 'error',
      title: 'ไม่มีสิทธิ์เข้าถึง',
      html: `
        <div style="text-align: left; padding: 0 16px;">
          <p><strong>บทบาทปัจจุบัน:</strong> ${roleDisplay}</p>
          <p><strong>บทบาทที่อนุญาต:</strong> ${allowedDisplay}</p>
          <hr style="border-color: #333; margin: 12px 0;">
          <p style="color: #f87171; font-size: 0.9em;">
            ⚠️ ระบบ Admin Dashboard สงวนสิทธิ์เฉพาะผู้ดูแลระบบและผู้ควบคุมการจัดส่งเท่านั้น
          </p>
        </div>
      `,
      confirmButtonText: 'รับทราบ',
      confirmButtonColor: '#d33',
      showCancelButton: true,
      cancelButtonText: 'ออกจากระบบ',
      cancelButtonColor: '#666',
      allowOutsideClick: false
    }).then((result) => {
      if (!result.isConfirmed) {
        // กด "ออกจากระบบ"
        authService.logout().subscribe();
      }
    });

    return false;
  }

  // ── Case 3: Role ตรงตามที่อนุญาต → ผ่าน
  return true;
};

/**
 * Factory function สำหรับสร้าง Role Guard เฉพาะ Role ที่ต้องการ
 * ใช้สำหรับ Route ที่ต้องการจำกัดสิทธิ์เฉพาะ Admin เท่านั้น
 * 
 * @example
 * { path: 'settings', canActivate: [authGuard, adminOnlyGuard], ... }
 */
export const adminOnlyGuard: CanActivateFn = (route, state) => {
  // กำหนด allowed roles เป็น Admin เท่านั้น
  route.data = { ...route.data, roles: ['Admin'] };
  return roleGuard(route, state);
};

/**
 * แปลง Role เป็นชื่อภาษาไทยสำหรับแสดงผล
 */
function getRoleDisplayName(role: string): string {
  const displayNames: Record<string, string> = {
    'admin': '👑 ผู้ดูแลระบบ (Admin)',
    'dispatcher': '📡 ผู้ควบคุมการจัดส่ง (Dispatcher)',
    'rider': '🏍️ ไรเดอร์ (Rider)',
    'customer': '👤 ลูกค้า (Customer)',
    'storepartner': '🏪 ร้านค้าพันธมิตร (Store Partner)'
  };
  return displayNames[role.toLowerCase()] || `🔹 ${role}`;
}
