import { Injectable } from '@angular/core';
import { BaseApiService } from './base-api.service';
import { OrderDto } from '../../api/generated/model/order-dto';
import { Observable } from 'rxjs';
import { req } from '../http/delivery-http-request';

@Injectable({
  providedIn: 'root'
})
export class OrderService extends BaseApiService<OrderDto> {
  protected get endpoint(): string {
    return '/orders';
  }

  // Override to include pagination if needed, or custom methods like cancel, update status
  public cancelOrder(id: string): Observable<any> {
    return req<any>(`${this.endpoint}/${id}/cancel`).post();
  }

  public retryDispatch(id: string): Observable<any> {
    return req<any>(`${this.endpoint}/${id}/dispatch`).post();
  }

  public batchDispatch(orderIds: string[]): Observable<any> {
    return req<any>(`${this.endpoint}/batch-dispatch`).body({ orderIds }).post();
  }
}
