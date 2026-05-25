import { Injectable } from '@angular/core';
import * as L from 'leaflet';

@Injectable({
  providedIn: 'root'
})
export class MapMathService {

  constructor() { }

  public decodeRoute(polyline?: string): L.LatLng[] {
    if (!polyline) return [];
    let index = 0;
    let lat = 0;
    let lng = 0;
    const coords: L.LatLng[] = [];

    while (index < polyline.length) {
      let shift = 0;
      let result = 0;
      let b: number;
      do {
        b = polyline.charCodeAt(index++) - 63;
        result |= (b & 0x1f) << shift;
        shift += 5;
      } while (b >= 0x20);
      lat += (result & 1) ? ~(result >> 1) : result >> 1;

      shift = 0;
      result = 0;
      do {
        b = polyline.charCodeAt(index++) - 63;
        result |= (b & 0x1f) << shift;
        shift += 5;
      } while (b >= 0x20);
      lng += (result & 1) ? ~(result >> 1) : result >> 1;

      coords.push(L.latLng(lat / 1e5, lng / 1e5));
    }

    return coords;
  }

  public calculateBearing(from: L.LatLng, to: L.LatLng): number {
    const fromLat = from.lat * Math.PI / 180;
    const toLat = to.lat * Math.PI / 180;
    const deltaLng = (to.lng - from.lng) * Math.PI / 180;
    const y = Math.sin(deltaLng) * Math.cos(toLat);
    const x = Math.cos(fromLat) * Math.sin(toLat) - Math.sin(fromLat) * Math.cos(toLat) * Math.cos(deltaLng);
    return (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;
  }

  public findNearestRouteIndex(position: L.LatLng, coords: L.LatLng[]): number {
    let bestIndex = 0;
    let bestDistance = Number.MAX_SAFE_INTEGER;
    coords.forEach((coord, index) => {
      const distance = position.distanceTo(coord);
      if (distance < bestDistance) {
        bestDistance = distance;
        bestIndex = index;
      }
    });
    return bestIndex;
  }
}
