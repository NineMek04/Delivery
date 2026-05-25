import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CustomerComponent } from './customer.component';
import { ShopService } from '../../core/services/shop.service';
import { StoreService } from '../../core/services/store.service';
import { AuthService } from '../../core/services/auth.service';
import { TrackingSignalRService } from '../../core/services/tracking-signalr.service';
import { of } from 'rxjs';
import { LucideAngularModule } from 'lucide-angular';

describe('CustomerComponent', () => {
  let component: CustomerComponent;
  let fixture: ComponentFixture<CustomerComponent>;
  let mockShopService: any;
  let mockStoreService: any;
  let mockAuthService: any;
  let mockSignalRService: any;

  beforeEach(async () => {
    mockShopService = jasmine.createSpyObj('ShopService', ['getAll']);
    mockShopService.getAll.and.returnValue(of([]));

    mockStoreService = jasmine.createSpyObj('StoreService', ['getShopMenus', 'addToCart', 'updateCartQuantity', 'removeFromCart', 'getCartTotal', 'placeOrder', 'clearCart']);
    mockStoreService.cart$ = of([]);
    mockStoreService.getCartTotal.and.returnValue(0);

    mockAuthService = jasmine.createSpyObj('AuthService', ['getUserData', 'getToken', 'canAccessDashboard', 'logout']);
    mockAuthService.getUserData.and.returnValue({ Email: 'cust@test.com', FullName: 'Customer Test' });
    mockAuthService.getToken.and.returnValue('mock-token');

    mockSignalRService = jasmine.createSpyObj('TrackingSignalRService', ['start', 'stop']);

    await TestBed.configureTestingModule({
      imports: [
        CustomerComponent,
        LucideAngularModule
      ],
      providers: [
        provideRouter([]),
        { provide: ShopService, useValue: mockShopService },
        { provide: StoreService, useValue: mockStoreService },
        { provide: AuthService, useValue: mockAuthService },
        { provide: TrackingSignalRService, useValue: mockSignalRService }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(CustomerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
