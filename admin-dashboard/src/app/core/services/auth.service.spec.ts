import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';

import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: Router,
          useValue: {
            navigateByUrl: jasmine.createSpy('navigateByUrl')
          }
        }
      ]
    });

    service = TestBed.inject(AuthService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    service.ngOnDestroy();
    localStorage.clear();
  });

  it('verifies the session before AppComponent is initialized', () => {
    let result: boolean | undefined;

    service.verifySession().subscribe(value => {
      result = value;
    });

    const request = httpTesting.expectOne(req => req.url.endsWith('/auth/session'));
    expect(request.request.method).toBe('GET');
    request.flush({ value: { role: 'Admin', email: 'admin@example.com' } });

    expect(result).toBeTrue();
    expect(service.isLoggedIn()).toBeTrue();
    expect(service.getUserRole()).toBe('Admin');
  });
});
