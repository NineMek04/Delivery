import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import Swal from 'sweetalert2';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      let errorMsg = 'เกิดข้อผิดพลาดบางอย่าง โปรดลองอีกครั้ง';

      if (error.error instanceof ErrorEvent) {
        // Client-side error
        errorMsg = `Error: ${error.error.message}`;
      } else {
        // Server-side error
        switch (error.status) {
          case 401:
            errorMsg = 'เซสชันของคุณหมดอายุ โปรดเข้าสู่ระบบใหม่';
            authService.logout();
            Swal.fire({
              icon: 'warning',
              title: 'หมดเวลา',
              text: errorMsg,
              confirmButtonColor: '#3085d6'
            });
            break;
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
            // Custom or unknown error
            if (error.error && error.error.Message) {
              errorMsg = error.error.Message;
            }
            // Avoid showing swal on every failed login attempt automatically if preferred, 
            // but for a base setup, it's good to show the backend's error message.
            Swal.fire({
               icon: 'error',
               title: `Error Code: ${error.status}`,
               text: errorMsg,
            });
            break;
        }
      }

      return throwError(() => error);
    })
  );
};
