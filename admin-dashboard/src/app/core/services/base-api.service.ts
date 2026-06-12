import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { req } from '../http/delivery-http-request';

/**
 * Unwrap ApiResponse wrapper ที่ GlobalResponseFilter ห่อให้อัตโนมัติ
 * Backend Response format: { success: boolean, value: T, message: string }
 */
export function unwrapValue<T>(res: any): T {
  // ถ้า Backend ห่อด้วย ApiResponse → ดึง value ออก
  if (res && typeof res === 'object' && 'value' in res) {
    return res.value as T;
  }
  return res as T;
}

/**
 * Unwrap ApiResponse + PaginatedResult → ดึงเฉพาะ items array ออกมา
 * Backend format: { success, value: { items: T[], totalCount, page, pageSize } }
 */
export function unwrapList<T>(res: any): T[] {
  const value = unwrapValue<any>(res);
  if (Array.isArray(value)) return value;
  if (value && Array.isArray(value.items)) return value.items;
  return [];
}

export abstract class BaseApiService<T> {
  protected abstract get endpoint(): string;

  /**
   * ดึงข้อมูลทั้งหมด (แบบแบ่งหน้า) — คืนเฉพาะ items array
   */
  public getAll(page = 1, pageSize = 50, search?: string): Observable<T[]> {
    const q: any = { page, pageSize };
    if (search) q.search = search;
    return req<any>(`${this.endpoint}`)
      .queryString(q)
      .get()
      .pipe(map(res => unwrapList<T>(res)));
  }

  /**
   * ดึงข้อมูลแบบ raw PaginatedResult (สำหรับ Component ที่ต้องการ totalCount)
   */
  public getAllPaginated(page = 1, pageSize = 20, search?: string): Observable<{ items: T[]; totalCount: number; page: number; pageSize: number }> {
    const q: any = { page, pageSize };
    if (search) q.search = search;
    return req<any>(`${this.endpoint}`)
      .queryString(q)
      .get()
      .pipe(map(res => {
        const value = unwrapValue<any>(res);
        return {
          items: Array.isArray(value?.items) ? value.items : (Array.isArray(value) ? value : []),
          totalCount: value?.totalCount ?? 0,
          page: value?.page ?? page,
          pageSize: value?.pageSize ?? pageSize
        };
      }));
  }

  public getById(id: string | number): Observable<T> {
    return req<any>(`${this.endpoint}/${id}`)
      .get()
      .pipe(map(res => unwrapValue<T>(res)));
  }

  public create(data: Partial<T>): Observable<T> {
    return req<any>(`${this.endpoint}`)
      .body(data)
      .post()
      .pipe(map(res => unwrapValue<T>(res)));
  }

  public update(id: string | number, data: Partial<T>): Observable<T> {
    return req<any>(`${this.endpoint}/${id}`)
      .body(data)
      .put()
      .pipe(map(res => unwrapValue<T>(res)));
  }

  public delete(id: string | number): Observable<any> {
    return req<any>(`${this.endpoint}/${id}`).delete();
  }

  /**
   * ดึงข้อมูลแบบ custom endpoint (เช่น /menu-items/shop/{shopId})
   */
  public getByEndpoint(endpoint: string): Observable<any> {
    return req<any>(endpoint).get();
  }
}
