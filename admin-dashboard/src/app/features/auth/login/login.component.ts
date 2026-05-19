import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import Swal from 'sweetalert2';
import { AuthService } from '../../../core/services/auth.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  private formBuilder = inject(FormBuilder);
  private authService = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  loginForm = this.formBuilder.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  loading = false;
  submitted = false;

  get f() { return this.loginForm.controls; }

  onSubmit() {
    this.submitted = true;

    if (this.loginForm.invalid) {
      return;
    }

    this.loading = true;
    this.authService.login(this.loginForm.value).subscribe({
      next: () => {
        const role = this.authService.getUserRole() || '';
        const roleLower = role.toLowerCase();

        let defaultUrl = '/dashboard';
        if (roleLower === 'customer') {
          defaultUrl = '/customer';
        } else if (roleLower === 'storepartner') {
          defaultUrl = '/store-partner';
        } else if (roleLower === 'rider') {
          this.loading = false;
          Swal.fire({
            icon: 'error',
            title: 'ไม่มีสิทธิ์เข้าถึง',
            html: `
              <p>บัญชีนี้มีบทบาท <strong>ไรเดอร์ (Rider)</strong></p>
              <p style="color: #f87171;">กรุณาเข้าสู่ระบบผ่านแอปพลิเคชันมือถือ (Rider App)</p>
            `,
            confirmButtonText: 'รับทราบ',
            confirmButtonColor: '#d33'
          }).then(() => {
            this.authService.logout().subscribe();
          });
          return;
        }

        // ดึง returnUrl ที่ Guard ส่งมา (ถ้ามี) → redirect กลับไปหน้าที่ต้องการเข้าถึง
        const returnUrl = this.route.snapshot.queryParams['returnUrl'] || defaultUrl;

        // แสดง Toast ยินดีต้อนรับ
        const userData = this.authService.getUserData();
        const displayName = userData?.FullName || userData?.fullName || userData?.Email || userData?.email || '';

        const Toast = Swal.mixin({
          toast: true,
          position: 'top-end',
          showConfirmButton: false,
          timer: 3000,
          timerProgressBar: true
        });

        Toast.fire({
          icon: 'success',
          title: `เข้าสู่ระบบสำเร็จ`,
          text: displayName ? `ยินดีต้อนรับ ${displayName}` : undefined
        });

        this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.loading = false;
        let msg = 'อีเมลหรือรหัสผ่านไม่ถูกต้อง';
        if (err.error?.Message || err.error?.message) {
          msg = err.error.Message || err.error.message;
        }
        Swal.fire({
          icon: 'error',
          title: 'เข้าสู่ระบบไม่สำเร็จ',
          text: msg,
          confirmButtonColor: '#3b82f6'
        });
      }
    });
  }
}
