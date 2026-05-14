import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="auth-container">
      <div class="glass-panel">
        <div class="brand-header">
          <div class="logo">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path>
              <polyline points="9 22 9 12 15 12 15 22"></polyline>
            </svg>
          </div>
          <h2>Welcome Back</h2>
          <p>Login to your account to continue</p>
        </div>
        
        <form [formGroup]="loginForm" (ngSubmit)="onSubmit()" class="auth-form">
          <div class="input-group">
            <label for="email">Email Address</label>
            <div class="input-wrapper">
              <span class="icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"></path><polyline points="22,6 12,13 2,6"></polyline></svg>
              </span>
              <input id="email" type="email" formControlName="email" placeholder="you@example.com" />
            </div>
            <div *ngIf="submitted && f['email'].errors" class="error-msg">
              <span *ngIf="f['email'].errors['required']">Email is required</span>
              <span *ngIf="f['email'].errors['email']">Email must be valid</span>
            </div>
          </div>

          <div class="input-group">
            <label for="password">Password</label>
            <div class="input-wrapper">
              <span class="icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect><path d="M7 11V7a5 5 0 0 1 10 0v4"></path></svg>
              </span>
              <input id="password" type="password" formControlName="password" placeholder="••••••••" />
            </div>
            <div *ngIf="submitted && f['password'].errors" class="error-msg">
              <span *ngIf="f['password'].errors['required']">Password is required</span>
            </div>
          </div>
          
          <div class="form-actions">
            <label class="remember-me">
              <input type="checkbox" />
              <span>Remember me</span>
            </label>
            <a href="#" class="forgot-password">Forgot password?</a>
          </div>

          <button type="submit" class="submit-btn" [disabled]="loading">
            <span *ngIf="loading" class="spinner"></span>
            <span *ngIf="!loading">Sign In</span>
          </button>
        </form>

        <div class="auth-footer">
          <p>Don't have an account? <a routerLink="/register">Sign up now</a></p>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host {
      display: block;
      min-height: 100vh;
      font-family: 'Inter', 'Roboto', sans-serif;
    }
    
    .auth-container {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: linear-gradient(135deg, #1e1e2f 0%, #151522 100%);
      position: relative;
      overflow: hidden;
    }
    
    .auth-container::before {
      content: '';
      position: absolute;
      top: -20%;
      left: -10%;
      width: 50vw;
      height: 50vw;
      background: radial-gradient(circle, rgba(99, 102, 241, 0.15) 0%, transparent 70%);
      border-radius: 50%;
      animation: float 15s ease-in-out infinite alternate;
    }
    
    .auth-container::after {
      content: '';
      position: absolute;
      bottom: -20%;
      right: -10%;
      width: 40vw;
      height: 40vw;
      background: radial-gradient(circle, rgba(236, 72, 153, 0.1) 0%, transparent 70%);
      border-radius: 50%;
      animation: float 10s ease-in-out infinite alternate-reverse;
    }
    
    @keyframes float {
      0% { transform: translate(0, 0); }
      100% { transform: translate(5%, 5%); }
    }
    
    .glass-panel {
      position: relative;
      z-index: 10;
      width: 100%;
      max-width: 420px;
      padding: 40px;
      background: rgba(255, 255, 255, 0.03);
      backdrop-filter: blur(20px);
      -webkit-backdrop-filter: blur(20px);
      border: 1px solid rgba(255, 255, 255, 0.05);
      border-radius: 24px;
      box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5);
      color: #fff;
    }
    
    .brand-header {
      text-align: center;
      margin-bottom: 32px;
    }
    
    .logo {
      width: 48px;
      height: 48px;
      background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      margin: 0 auto 16px;
      box-shadow: 0 10px 20px -5px rgba(99, 102, 241, 0.4);
    }
    
    .logo svg {
      width: 24px;
      height: 24px;
      color: #fff;
    }
    
    h2 {
      margin: 0 0 8px;
      font-size: 28px;
      font-weight: 700;
      letter-spacing: -0.5px;
    }
    
    p {
      margin: 0;
      color: #94a3b8;
      font-size: 15px;
    }
    
    .input-group {
      margin-bottom: 20px;
    }
    
    .input-group label {
      display: block;
      margin-bottom: 8px;
      font-size: 14px;
      font-weight: 500;
      color: #cbd5e1;
    }
    
    .input-wrapper {
      position: relative;
      display: flex;
      align-items: center;
    }
    
    .icon {
      position: absolute;
      left: 14px;
      color: #64748b;
      display: flex;
    }
    
    .icon svg {
      width: 18px;
      height: 18px;
    }
    
    input {
      width: 100%;
      padding: 12px 16px 12px 42px;
      background: rgba(15, 23, 42, 0.6);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 12px;
      color: #fff;
      font-size: 15px;
      transition: all 0.2s ease;
      box-sizing: border-box;
    }
    
    input:focus {
      outline: none;
      border-color: #6366f1;
      background: rgba(15, 23, 42, 0.8);
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.2);
    }
    
    input::placeholder {
      color: #475569;
    }
    
    .error-msg {
      color: #ef4444;
      font-size: 13px;
      margin-top: 6px;
      display: block;
    }
    
    .form-actions {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 24px;
      font-size: 14px;
    }
    
    .remember-me {
      display: flex;
      align-items: center;
      gap: 8px;
      color: #cbd5e1;
      cursor: pointer;
    }
    
    .remember-me input {
      width: 16px;
      height: 16px;
      accent-color: #6366f1;
      cursor: pointer;
    }
    
    .forgot-password {
      color: #8b5cf6;
      text-decoration: none;
      font-weight: 500;
      transition: color 0.2s;
    }
    
    .forgot-password:hover {
      color: #a78bfa;
    }
    
    .submit-btn {
      width: 100%;
      padding: 14px;
      background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
      color: #fff;
      border: none;
      border-radius: 12px;
      font-size: 16px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.3s ease;
      display: flex;
      justify-content: center;
      align-items: center;
      box-shadow: 0 4px 12px rgba(99, 102, 241, 0.3);
    }
    
    .submit-btn:hover:not(:disabled) {
      transform: translateY(-2px);
      box-shadow: 0 6px 16px rgba(99, 102, 241, 0.4);
    }
    
    .submit-btn:disabled {
      opacity: 0.7;
      cursor: not-allowed;
    }
    
    .spinner {
      width: 20px;
      height: 20px;
      border: 3px solid rgba(255,255,255,0.3);
      border-radius: 50%;
      border-top-color: #fff;
      animation: spin 1s ease-in-out infinite;
    }
    
    @keyframes spin {
      to { transform: rotate(360deg); }
    }
    
    .auth-footer {
      margin-top: 24px;
      text-align: center;
      padding-top: 24px;
      border-top: 1px solid rgba(255, 255, 255, 0.05);
    }
    
    .auth-footer a {
      color: #8b5cf6;
      text-decoration: none;
      font-weight: 600;
      transition: color 0.2s;
    }
    
    .auth-footer a:hover {
      color: #a78bfa;
    }
  `]
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
        // Assume auth logic sets the token, navigate to dashboard
        this.router.navigate(['/']);
      },
      error: () => {
        this.loading = false;
        // Error will be caught and shown by ErrorInterceptor
      }
    });
  }
}
