import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { StorePartnerComponent } from './store-partner.component';
import { ShopService } from '../../core/services/shop.service';
import { StoreService } from '../../core/services/store.service';
import { AuthService } from '../../core/services/auth.service';
import { TrackingSignalRService } from '../../core/services/tracking-signalr.service';
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

    const mockSignalRService = jasmine.createSpyObj('TrackingSignalRService', ['start', 'stop', 'startConnection', 'stopConnection', 'on', 'off', 'listenForOrderStatus', 'listenForRiderLocation']);
    mockSignalRService.startConnection.and.returnValue(of(true));
    mockSignalRService.stopConnection.and.returnValue(of(true));
    mockSignalRService.listenForOrderStatus.and.returnValue(of({}));
    mockSignalRService.listenForRiderLocation.and.returnValue(of({}));
    mockSignalRService.orderCreated$ = of({});
    mockSignalRService.orderAcceptedByStore$ = of({});
    mockSignalRService.offerReceived$ = of({});
    mockSignalRService.orderAssigned$ = of({});
    mockSignalRService.orderAssignedToRider$ = of({});
    mockSignalRService.orderStatusChanged$ = of({});
    mockSignalRService.orderStatusUpdated$ = of({});
    mockSignalRService.riderLocationUpdated$ = of({});

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
        { provide: AuthService, useValue: mockAuthService },
        { provide: TrackingSignalRService, useValue: mockSignalRService }
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
