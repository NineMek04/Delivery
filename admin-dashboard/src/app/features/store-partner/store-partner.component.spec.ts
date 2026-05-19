import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StorePartnerComponent } from './store-partner.component';

describe('StorePartnerComponent', () => {
  let component: StorePartnerComponent;
  let fixture: ComponentFixture<StorePartnerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StorePartnerComponent]
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
