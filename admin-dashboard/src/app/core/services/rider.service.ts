import { Injectable } from '@angular/core';
import { BaseApiService } from './base-api.service';
import { RiderDto } from '../../api/generated/model/rider-dto';

@Injectable({
  providedIn: 'root'
})
export class RiderService extends BaseApiService<RiderDto> {
  protected get endpoint(): string {
    return '/riders';
  }
}
