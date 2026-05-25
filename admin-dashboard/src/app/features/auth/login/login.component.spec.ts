import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { Router, ActivatedRoute, provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/services/auth.service';
import { of, throwError } from 'rxjs';
import Swal from 'sweetalert2';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let mockAuthService: jasmine.SpyObj<AuthService>;
  let router: Router;
  let mockActivatedRoute: any;

  beforeEach(async () => {
    mockAuthService = jasmine.createSpyObj('AuthService', ['login', 'getUserRole', 'getUserData', 'logout']);
    mockActivatedRoute = {
      snapshot: {
        queryParams: {}
      }
    };

    await TestBed.configureTestingModule({
      imports: [LoginComponent, ReactiveFormsModule],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: mockAuthService },
        { provide: ActivatedRoute, useValue: mockActivatedRoute }
      ]
    }).compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl');

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('ควรสร้าง Component ได้สำเร็จ', () => {
    expect(component).toBeTruthy();
  });

  // ─── 1. Form Validation Tests ─────────────────────────────────────
  
  it('ฟอร์มควรไม่ผ่านการตรวจสอบ (Invalid) เมื่อไม่มีการกรอกข้อมูล', () => {
    expect(component.loginForm.valid).toBeFalse();
    expect(component.loginForm.get('email')?.hasError('required')).toBeTrue();
    expect(component.loginForm.get('password')?.hasError('required')).toBeTrue();
  });

  it('ฟอร์มควรแจ้งเตือน Invalid เมื่อกรอกรูปแบบอีเมลไม่ถูกต้อง', () => {
    const emailCtrl = component.loginForm.get('email');
    emailCtrl?.setValue('invalid-email-format');
    expect(emailCtrl?.hasError('email')).toBeTrue();
  });

  it('ฟอร์มควรผ่านการตรวจสอบ (Valid) เมื่อกรอกข้อมูลถูกต้องครบถ้วน', () => {
    component.loginForm.patchValue({
      email: 'admin@delivery.com',
      password: 'SecurePassword123!'
    });
    expect(component.loginForm.valid).toBeTrue();
  });

  // ─── 2. Successful Login & Role Redirects Tests ───────────────────

  it('บทบาท Admin ควรเข้าสู่ระบบและเปลี่ยนเส้นทางไปยัง /dashboard', fakeAsync(() => {
    // Arrange
    component.loginForm.patchValue({ email: 'admin@test.com', password: 'password123' });
    mockAuthService.login.and.returnValue(of({}));
    mockAuthService.getUserRole.and.returnValue('Admin');
    mockAuthService.getUserData.and.returnValue({ Email: 'admin@test.com', FullName: 'System Admin' });

    const swalFireSpy = spyOn(Swal, 'fire').and.returnValue(Promise.resolve({ isConfirmed: true } as any));
    const mockToast = jasmine.createSpyObj('Toast', ['fire']);
    const swalMixinSpy = spyOn(Swal, 'mixin').and.returnValue(mockToast);

    // Act
    component.onSubmit();
    tick();

    // Assert
    expect(mockAuthService.login).toHaveBeenCalledWith({ email: 'admin@test.com', password: 'password123' });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
    expect(swalMixinSpy).toHaveBeenCalled();
    expect(mockToast.fire).toHaveBeenCalled();
  }));

  it('บทบาท StorePartner ควรเข้าสู่ระบบและเปลี่ยนเส้นทางไปยัง /store-partner', fakeAsync(() => {
    // Arrange
    component.loginForm.patchValue({ email: 'partner@test.com', password: 'password123' });
    mockAuthService.login.and.returnValue(of({}));
    mockAuthService.getUserRole.and.returnValue('StorePartner');
    mockAuthService.getUserData.and.returnValue({ Email: 'partner@test.com', FullName: 'Store Partner' });

    spyOn(Swal, 'fire').and.returnValue(Promise.resolve({ isConfirmed: true } as any));
    const mockToast = jasmine.createSpyObj('Toast', ['fire']);
    spyOn(Swal, 'mixin').and.returnValue(mockToast);

    // Act
    component.onSubmit();
    tick();

    // Assert
    expect(router.navigateByUrl).toHaveBeenCalledWith('/store-partner');
  }));

  it('บทบาท Customer ควรเข้าสู่ระบบและเปลี่ยนเส้นทางไปยัง /customer', fakeAsync(() => {
    // Arrange
    component.loginForm.patchValue({ email: 'customer@test.com', password: 'password123' });
    mockAuthService.login.and.returnValue(of({}));
    mockAuthService.getUserRole.and.returnValue('Customer');
    mockAuthService.getUserData.and.returnValue({ Email: 'customer@test.com', FullName: 'Lovely Customer' });

    spyOn(Swal, 'fire').and.returnValue(Promise.resolve({ isConfirmed: true } as any));
    const mockToast = jasmine.createSpyObj('Toast', ['fire']);
    spyOn(Swal, 'mixin').and.returnValue(mockToast);

    // Act
    component.onSubmit();
    tick();

    // Assert
    expect(router.navigateByUrl).toHaveBeenCalledWith('/customer');
  }));

  // ─── 3. Blocked Rider Login Tests ───────────────────────────────

  it('บทบาท Rider ควรถูกบล็อกไม่ให้เข้าสู่ระบบ แสดง SweetAlert2 ป้องกัน และเรียก logout ทันที', fakeAsync(() => {
    // Arrange
    component.loginForm.patchValue({ email: 'rider@test.com', password: 'password123' });
    mockAuthService.login.and.returnValue(of({}));
    mockAuthService.getUserRole.and.returnValue('Rider');
    mockAuthService.logout.and.returnValue(of({}));

    // Mock Swal fire to immediately resolve the promise
    const swalFireSpy = spyOn(Swal, 'fire').and.returnValue(Promise.resolve({ isConfirmed: true } as any));

    // Act
    component.onSubmit();
    tick();

    // Assert
    expect(component.loading).toBeFalse();
    expect(swalFireSpy).toHaveBeenCalled();
    expect(mockAuthService.logout).toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  }));

  // ─── 4. Failed Login Handling Tests ──────────────────────────────

  it('เมื่อเข้าสู่ระบบล้มเหลว ควรหยุด loading และแสดงกล่องข้อความ SweetAlert2 แจ้งข้อผิดพลาด', fakeAsync(() => {
    // Arrange
    component.loginForm.patchValue({ email: 'wrong@test.com', password: 'wrongpassword' });
    const mockError = { error: { message: 'อีเมลหรือรหัสผ่านไม่ถูกต้อง' } };
    mockAuthService.login.and.returnValue(throwError(() => mockError));

    const swalFireSpy = spyOn(Swal, 'fire').and.returnValue(Promise.resolve({ isConfirmed: true } as any));

    // Act
    component.onSubmit();
    tick();

    // Assert
    expect(component.loading).toBeFalse();
    expect(swalFireSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      icon: 'error',
      title: 'เข้าสู่ระบบไม่สำเร็จ',
      text: 'อีเมลหรือรหัสผ่านไม่ถูกต้อง'
    }));
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  }));
});
