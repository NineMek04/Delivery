import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="auth-container">
      <div class="glass-panel">
        <div class="brand-header">
          <div class="logo">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M16 21v-2a4 4 0 0 0-4-4H5c-1.1 0-2 .9-2 2v2"></path>
              <circle cx="8.5" cy="7" r="4"></circle>
              <line x1="20" y1="8" x2="20" y2="14"></line>
              <line x1="23" y1="11" x2="17" y2="11"></line>
            </svg>
          </div>
          <h2>Create Account</h2>
          <p>Join us to start managing deliveries</p>
        </div>
        
        <form [formGroup]="registerForm" (ngSubmit)="onSubmit()" class="auth-form">
          <div class="input-group">
            <label for="fullName">Full Name</label>
            <div class="input-wrapper">
              <span class="icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg>
              </span>
              <input id="fullName" type="text" formControlName="fullName" placeholder="John Doe" />
            </div>
            <div *ngIf="submitted && f['fullName'].errors" class="error-msg">
              <span *ngIf="f['fullName'].errors['required']">Full Name is required</span>
            </div>
          </div>

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
              <span *ngIf="f['password'].errors['minlength']">Must be at least 6 characters</span>
            </div>
          </div>

          <div class="input-group">
            <label for="role">Role</label>
            <div class="input-wrapper">
              <span class="icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path></svg>
              </span>
              <select id="role" formControlName="role">
                <option value="Admin">Admin</option>
                <option value="Dispatcher">Dispatcher</option>
                <option value="Rider">Rider</option>
              </select>
            </div>
          </div>
          
          <button type="submit" class="submit-btn" [disabled]="loading">
            <span *ngIf="loading" class="spinner"></span>
            <span *ngIf="!loading">Create Account</span>
          </button>
        </form>

        <div class="auth-footer">
          <p>Already have an account? <a routerLink="/login">Sign in</a></p>
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
      right: -10%;
      width: 50vw;
      height: 50vw;
      background: radial-gradient(circle, rgba(236, 72, 153, 0.15) 0%, transparent 70%);
      border-radius: 50%;
      animation: float 12s ease-in-out infinite alternate;
    }
    
    .auth-container::after {
      content: '';
      position: absolute;
      bottom: -20%;
      left: -10%;
      width: 40vw;
      height: 40vw;
      background: radial-gradient(circle, rgba(99, 102, 241, 0.1) 0%, transparent 70%);
      border-radius: 50%;
      animation: float 15s ease-in-out infinite alternate-reverse;
    }
    
    @keyframes float {
      0% { transform: translate(0, 0); }
      100% { transform: translate(-5%, -5%); }
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
      background: linear-gradient(135deg, #ec4899 0%, #8b5cf6 100%);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      margin: 0 auto 16px;
      box-shadow: 0 10px 20px -5px rgba(236, 72, 153, 0.4);
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
    
    input, select {
      width: 100%;
      padding: 12px 16px 12px 42px;
      background: rgba(15, 23, 42, 0.6);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 12px;
      color: #fff;
      font-size: 15px;
      transition: all 0.2s ease;
      box-sizing: border-box;
      appearance: none;
    }
    
    input:focus, select:focus {
      outline: none;
      border-color: #ec4899;
      background: rgba(15, 23, 42, 0.8);
      box-shadow: 0 0 0 3px rgba(236, 72, 153, 0.2);
    }
    
    select {
      cursor: pointer;
    }
    
    option {
      background: #1e1e2f;
      color: #fff;
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
    
    .submit-btn {
      width: 100%;
      padding: 14px;
      margin-top: 10px;
      background: linear-gradient(135deg, #ec4899 0%, #8b5cf6 100%);
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
      box-shadow: 0 4px 12px rgba(236, 72, 153, 0.3);
    }
    
    .submit-btn:hover:not(:disabled) {
      transform: translateY(-2px);
      box-shadow: 0 6px 16px rgba(236, 72, 153, 0.4);
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
      color: #f472b6;
      text-decoration: none;
      font-weight: 600;
      transition: color 0.2s;
    }
    
    .auth-footer a:hover {
      color: #fbcfe8;
    }
  `]
})
export class RegisterComponent {
  private formBuilder = inject(FormBuilder);
  private authService = inject(AuthService);
  private router = inject(Router);

  registerForm = this.formBuilder.group({
    fullName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(6)]],
    role: ['Admin', Validators.required]
  });

  loading = false;
  submitted = false;

  get f() { return this.registerForm.controls; }

  onSubmit() {
    this.submitted = true;

    if (this.registerForm.invalid) {
      return;
    }

    this.loading = true;
    this.authService.register(this.registerForm.value).subscribe({
      next: () => {
        this.router.navigate(['/']);
      },
      error: () => {
        this.loading = false;
      }
    });
  }
}
