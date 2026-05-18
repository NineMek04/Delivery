/**
 * Guards สำหรับระบบป้องกันสิทธิ์ Admin Dashboard
 * 
 * การใช้งาน:
 * 
 * 1. authGuard     → ตรวจสอบว่า Login แล้วหรือยัง (ถ้ายัง → Redirect ไป Login)
 * 2. roleGuard     → ตรวจสอบ Role (Admin/Dispatcher เท่านั้นเข้าได้)
 * 3. adminOnlyGuard → เฉพาะ Admin เท่านั้น (Dispatcher ก็เข้าไม่ได้)
 * 4. guestGuard    → ป้องกันคนที่ Login แล้วกลับไปหน้า Login/Register
 * 
 * ตัวอย่าง Route Config:
 * 
 *   // ป้องกัน Login + Role
 *   { path: 'dashboard', canActivate: [authGuard, roleGuard], ... }
 * 
 *   // เฉพาะ Admin
 *   { path: 'settings', canActivate: [authGuard, adminOnlyGuard], ... }
 * 
 *   // หน้า Guest (Login/Register)
 *   { path: 'login', canActivate: [guestGuard], ... }
 */
export { authGuard } from './auth.guard';
export { roleGuard, adminOnlyGuard } from './role.guard';
export { guestGuard } from './guest.guard';
