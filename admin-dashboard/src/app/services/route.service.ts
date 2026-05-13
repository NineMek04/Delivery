import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { req } from '../core/http/delivery-http-request';

@Injectable({
  providedIn: 'root'
})
export class RouteService {

  constructor() { }

  /**
   * ตัวอย่างการเรียกใช้งาน API Optimize Route ไปยัง Backend -> AI Engine
   */
  public optimizeRoute(data: any): Observable<any> {
    return req<any>('ai/optimize-route')
      .body(data)
      .post();
  }
}
