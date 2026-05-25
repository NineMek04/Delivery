import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { StorePartnerComponent } from './store-partner.component';
import { ShopService } from '../../core/services/shop.service';
import { StoreService } from '../../core/services/store.service';
import { AuthService } from '../../core/services/auth.service';
import { of } from 'rxjs';
import { LucideAngularModule } from 'lucide-angular';

describe('StorePartnerComponent', () => {
  let component: StorePartnerComponent;
  let fixture: ComponentFixture<StorePartnerComponent>;
  let mockShopService: any;
  let mockStoreService: any;
  let mockAuthService: any;

  beforeEach(async () => {
    mockShopService = jasmine.createSpyObj('ShopService', ['getAll']);
    mockShopService.getAll.and.returnValue(of([]));

    mockStoreService = jasmine.createSpyObj('StoreService', ['getShopMenus', 'addMenuItem', 'updateMenuItem', 'deleteMenuItem', 'acceptOrderByStore']);

    mockAuthService = jasmine.createSpyObj('AuthService', ['getUserData', 'getToken', 'canAccessDashboard', 'logout']);
    mockAuthService.getUserData.and.returnValue({ Email: 'partner@test.com', FullName: 'Partner Test' });
    mockAuthService.getToken.and.returnValue('mock-token');

    await TestBed.configureTestingModule({
      imports: [
        StorePartnerComponent,
        ReactiveFormsModule,
        LucideAngularModule
      ],
      providers: [
        provideRouter([]),
        { provide: ShopService, useValue: mockShopService },
        { provide: StoreService, useValue: mockStoreService },
        { provide: AuthService, useValue: mockAuthService }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(StorePartnerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
