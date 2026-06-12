import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError, switchMap } from 'rxjs';
import { AuthService } from '../services/auth.service';
import {
  getApiErrorMessage,
  getApiErrorTitle,
  isPublicAuthRequest
} from '../http/api-error';
import Swal from 'sweetalert2';

let sessionExpiredAlert: Promise<unknown> | null = null;

function handleSessionExpired(authService: AuthService): void {
  authService.forceLogout();

  if (sessionExpiredAlert) {
    return;
  }

  sessionExpiredAlert = Swal.fire({
    icon: 'warning',
    title: 'เซสชันหมดอายุ',
    text: 'กรุณาเข้าสู่ระบบใหม่อีกครั้ง',
    confirmButtonText: 'ไปหน้าเข้าสู่ระบบ',
    confirmButtonColor: '#3085d6',
    allowOutsideClick: false
  }).finally(() => {
    sessionExpiredAlert = null;
  });
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      let errorMsg = getApiErrorMessage(error);

      if (error.error instanceof ErrorEvent) {
        // Client-side error
        errorMsg = `Error: ${error.error.message}`;
      } else {
        // Prevent refresh loop
        if (isPublicAuthRequest(req.url)) {
          return throwError(() => error);
        }

        // Server-side error
        switch (error.status) {
          case 401:
            return authService.refreshAccessToken().pipe(
              switchMap((success: boolean) => {
                if (success) {
                  return next(req);
                }

                handleSessionExpired(authService);
                return throwError(() => error);
              }),
              catchError(() => throwError(() => error))
            );
          case 403:
            errorMsg = 'คุณไม่มีสิทธิ์เข้าถึงข้อมูลส่วนนี้';
            Swal.fire({
              icon: 'error',
              title: 'ปฏิเสธการเข้าถึง',
              text: errorMsg,
              confirmButtonColor: '#d33'
            });
            break;
          case 500:
            errorMsg = 'เซิร์ฟเวอร์เกิดข้อผิดพลาดภายใน';
            Swal.fire({
              icon: 'error',
              title: 'Server Error (500)',
              text: errorMsg,
              confirmButtonColor: '#d33'
            });
            break;
          default:
            Swal.fire({
              icon: 'error',
              title: getApiErrorTitle(error),
              text: errorMsg,
            });
            break;
        }
      }

      return throwError(() => error);
    })
  );
};
