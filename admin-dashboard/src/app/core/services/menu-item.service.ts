import { Injectable } from '@angular/core';
import { BaseApiService, unwrapValue } from './base-api.service';
import { Observable, map } from 'rxjs';

export interface MenuItemOptionItemDto {
  id?: string;
  name: string;
  price: number;
}

export interface MenuItemOptionDto {
  id?: string;
  name: string;
  required: boolean;
  maxSelections: number;
  items?: MenuItemOptionItemDto[];
}

export interface MenuItemDto {
  id?: string;
  name: string;
  description?: string;
  price: number;
  imageUrl?: string;
  shopId: string;
  options?: MenuItemOptionDto[];
  createdAt?: string;
}

@Injectable({
  providedIn: 'root'
})
export class MenuItemService extends BaseApiService<MenuItemDto> {
  protected get endpoint(): string {
    return '/menu-items';
  }

  getByShop(shopId: string): Observable<MenuItemDto[]> {
    return this.getByEndpoint(`${this.endpoint}/shop/${shopId}`).pipe(
      map(res => {
        const value = unwrapValue<any>(res);
        return Array.isArray(value?.items) ? value.items : (Array.isArray(value) ? value : []);
      })
    );
  }
}
