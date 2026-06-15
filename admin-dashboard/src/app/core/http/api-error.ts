import { HttpErrorResponse } from '@angular/common/http';

type ApiErrorBody = {
  message?: string;
  Message?: string;
  title?: string;
  Title?: string;
  errors?: Record<string, string | string[]>;
};

export function isPublicAuthRequest(url: string): boolean {
  const normalizedUrl = url.toLowerCase();
  return ['/auth/login', '/auth/register', '/auth/refresh', '/auth/session']
    .some(path => normalizedUrl.includes(path));
}

export function getApiErrorMessage(
  error: HttpErrorResponse,
  fallback = 'เกิดข้อผิดพลาดบางอย่าง โปรดลองอีกครั้ง'
): string {
  if (error.status === 0) {
    return 'ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ได้ กรุณาตรวจสอบว่า API เปิดอยู่และอนุญาต origin ของหน้านี้';
  }

  if (typeof error.error === 'string' && error.error.trim()) {
    return error.error.trim();
  }

  const body = error.error as ApiErrorBody | null;
  const message = body?.message ?? body?.Message;
  if (message?.trim()) {
    return message.trim();
  }

  const validationMessages = body?.errors
    ? Object.values(body.errors).flatMap(value => Array.isArray(value) ? value : [value])
    : [];
  if (validationMessages.length > 0) {
    return validationMessages.join('\n');
  }

  const title = body?.title ?? body?.Title;
  return title?.trim() || fallback;
}

export function getApiErrorTitle(error: HttpErrorResponse): string {
  return error.status === 0 ? 'Network Error' : `Error Code: ${error.status}`;
}
