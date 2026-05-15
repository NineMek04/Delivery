import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
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
        // Navigate to dashboard only if token was successfully set
        if (this.authService.getToken()) {
          this.router.navigate(['/']);
        } else {
          this.loading = false;
          Swal.fire('Error', 'ไม่สามารถเข้าสู่ระบบได้ โปรดตรวจสอบข้อมูล', 'error');
        }
      },
      error: (err) => {
        this.loading = false;
        let msg = 'อีเมลหรือรหัสผ่านไม่ถูกต้อง';
        if (err.error?.Message) msg = err.error.Message;
        Swal.fire('Login Failed', msg, 'error');
      }
    });
  }
}
