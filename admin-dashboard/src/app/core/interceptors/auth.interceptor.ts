import { HttpInterceptorFn } from '@angular/common/http';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const clonedReq = req.clone({
    withCredentials: true,
    setHeaders: {
      'X-Client-Type': 'Dashboard'
    }
  });

  return next(clonedReq);
};
