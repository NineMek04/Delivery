import { HttpInterceptorFn } from '@angular/common/http';

function getCookie(name: string): string | null {
  const nameEQ = name + '=';
  const ca = document.cookie.split(';');
  for (let i = 0; i < ca.length; i++) {
    let c = ca[i];
    while (c.charAt(0) === ' ') {
      c = c.substring(1, c.length);
    }
    if (c.indexOf(nameEQ) === 0) {
      return c.substring(nameEQ.length, c.length);
    }
  }
  return null;
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const xsrfToken = getCookie('XSRF-TOKEN');
  const headers: { [name: string]: string } = {
    'X-Client-Type': 'Dashboard'
  };

  if (xsrfToken) {
    headers['X-XSRF-TOKEN'] = xsrfToken;
  }

  const clonedReq = req.clone({
    withCredentials: true,
    setHeaders: headers
  });

  return next(clonedReq);
};
