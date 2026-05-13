import { Observable } from 'rxjs';
import { req } from '../http/delivery-http-request';

export abstract class BaseApiService<T> {
  protected abstract get endpoint(): string;

  public getAll(): Observable<T[]> {
    return req<T[]>(`${this.endpoint}`).get();
  }

  public getById(id: string | number): Observable<T> {
    return req<T>(`${this.endpoint}/${id}`).get();
  }

  public create(data: Partial<T>): Observable<T> {
    return req<T>(`${this.endpoint}`)
      .body(data)
      .post();
  }

  public update(id: string | number, data: Partial<T>): Observable<T> {
    return req<T>(`${this.endpoint}/${id}`)
      .body(data)
      .put();
  }

  public delete(id: string | number): Observable<any> {
    return req<any>(`${this.endpoint}/${id}`).delete();
  }
}
