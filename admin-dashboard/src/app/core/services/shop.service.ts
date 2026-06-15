import { Injectable } from '@angular/core';
import { BaseApiService } from './base-api.service';

export interface ShopDto {
  id?: string;
  trackingCode?: string;
  name: string;
  menuName: string;
  menuPrice: number;
  lat?: number;
  lng?: number;
  isOpen?: boolean;
  prepTimeMinutes?: number;
  openingHours?: string;
  menuItems?: unknown[];
  menuCategories?: unknown[];
  createdAt?: string;
}

@Injectable({
  providedIn: 'root'
})
export class ShopService extends BaseApiService<ShopDto> {
  protected get endpoint(): string {
    return '/shops';
  }
}
