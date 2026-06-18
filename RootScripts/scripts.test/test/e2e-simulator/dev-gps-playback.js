/**
 * Development GPS playback for rider map/navigation testing.
 *
 * This script is the only supported way to simulate rider movement in dev.
 * The Flutter app must not mutate its own GPS position. It should only render
 * real device GPS or backend SignalR telemetry.
 *
 * Usage:
 *   DELIVERY_RIDER_EMAIL=rider@test.com DELIVERY_RIDER_PASSWORD=Pass123! \
 *   node dev-gps-playback.js --order-id <orderId> --phase PICKUP
 *
 * Optional:
 *   --api http://localhost:5000/api/v1
 *   --hub http://localhost:5000/hubs/tracking
 *   --from-lat 17.4138 --from-lng 102.7872
 *   --interval 1500 --step-meters 12 --accuracy 5
 *   --points "17.4138,102.7872;17.4200,102.7900"
 */

'use strict';

const axios = require('axios');
const signalR = require('@microsoft/signalr');

const args = process.argv.slice(2);

function getArg(name, fallback = undefined) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : fallback;
}

function requireValue(name, value) {
  if (!value) {
    throw new Error(`${name} is required.`);
  }
  return value;
}

const API = getArg('api', process.env.DELIVERY_API_URL || 'http://localhost:5000/api/v1');
const HUB = getArg('hub', process.env.DELIVERY_HUB_URL || 'http://localhost:5000/hubs/tracking');
const EMAIL = getArg('email', process.env.DELIVERY_RIDER_EMAIL);
const PASSWORD = getArg('password', process.env.DELIVERY_RIDER_PASSWORD);
const ORDER_ID = getArg('order-id', process.env.DELIVERY_ORDER_ID);
const PHASE = getArg('phase', process.env.DELIVERY_ROUTE_PHASE || 'PICKUP').toUpperCase();
const INTERVAL_MS = Number(getArg('interval', process.env.DELIVERY_GPS_INTERVAL_MS || '1500'));
const STEP_METERS = Number(getArg('step-meters', process.env.DELIVERY_GPS_STEP_METERS || '12'));
const ACCURACY = Number(getArg('accuracy', process.env.DELIVERY_GPS_ACCURACY || '5'));
const FROM_LAT = getArg('from-lat', process.env.DELIVERY_FROM_LAT);
const FROM_LNG = getArg('from-lng', process.env.DELIVERY_FROM_LNG);
const POINTS = getArg('points', process.env.DELIVERY_GPS_POINTS);

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function unwrapValue(response) {
  return response.data?.value || response.data?.Value || response.data;
}

function decodePolyline(str) {
  let index = 0;
  let lat = 0;
  let lng = 0;
  const coordinates = [];

  while (index < str.length) {
    let b;
    let shift = 0;
    let result = 0;
    do {
      if (index >= str.length || shift > 30) return [];
      b = str.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    lat += (result & 1) ? ~(result >> 1) : (result >> 1);

    shift = 0;
    result = 0;
    do {
      if (index >= str.length || shift > 30) return [];
      b = str.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    lng += (result & 1) ? ~(result >> 1) : (result >> 1);

    const point = { lat: lat / 1e5, lng: lng / 1e5 };
    if (
      point.lat < -90 ||
      point.lat > 90 ||
      point.lng < -180 ||
      point.lng > 180
    ) {
      return [];
    }
    coordinates.push(point);
  }

  return coordinates;
}

function distanceMeters(a, b) {
  const earthRadius = 6371000;
  const lat1 = a.lat * Math.PI / 180;
  const lat2 = b.lat * Math.PI / 180;
  const dLat = (b.lat - a.lat) * Math.PI / 180;
  const dLng = (b.lng - a.lng) * Math.PI / 180;
  const sinLat = Math.sin(dLat / 2);
  const sinLng = Math.sin(dLng / 2);
  const h =
    sinLat * sinLat +
    Math.cos(lat1) * Math.cos(lat2) * sinLng * sinLng;
  return 2 * earthRadius * Math.atan2(Math.sqrt(h), Math.sqrt(1 - h));
}

function interpolateRoute(points, stepMeters) {
  if (points.length < 2) return points;
  const result = [points[0]];

  for (let i = 0; i < points.length - 1; i++) {
    const start = points[i];
    const end = points[i + 1];
    const segmentMeters = distanceMeters(start, end);
    const steps = Math.max(1, Math.floor(segmentMeters / stepMeters));

    for (let step = 1; step <= steps; step++) {
      const t = step / steps;
      result.push({
        lat: start.lat + (end.lat - start.lat) * t,
        lng: start.lng + (end.lng - start.lng) * t,
      });
    }
  }

  return result;
}

function parsePoints(pointsText) {
  if (!pointsText) return [];
  return pointsText.split(';').map(pair => {
    const [lat, lng] = pair.split(',').map(Number);
    if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
      throw new Error(`Invalid --points coordinate: ${pair}`);
    }
    return { lat, lng };
  });
}

async function login() {
  requireValue('DELIVERY_RIDER_EMAIL or --email', EMAIL);
  requireValue('DELIVERY_RIDER_PASSWORD or --password', PASSWORD);

  const response = await axios.post(`${API}/auth/login`, {
    email: EMAIL,
    password: PASSWORD,
  });
  const value = unwrapValue(response);
  const token = value?.accessToken || value?.AccessToken;
  return requireValue('accessToken', token);
}

async function resolveRoute(token) {
  const explicitPoints = parsePoints(POINTS);
  if (explicitPoints.length >= 2) return explicitPoints;

  requireValue('DELIVERY_ORDER_ID or --order-id', ORDER_ID);
  requireValue('DELIVERY_FROM_LAT or --from-lat', FROM_LAT);
  requireValue('DELIVERY_FROM_LNG or --from-lng', FROM_LNG);

  const response = await axios.post(
    `${API}/rider-routes/resolve`,
    {
      orderId: ORDER_ID,
      routePhase: PHASE,
      currentLat: Number(FROM_LAT),
      currentLng: Number(FROM_LNG),
    },
    {
      headers: { Authorization: `Bearer ${token}` },
    },
  );

  const value = unwrapValue(response);
  const encodedPolyline = value?.encodedPolyline || value?.EncodedPolyline || '';
  const source = value?.source || value?.Source || 'UNKNOWN';
  if (source !== 'LOCAL_OSRM') {
    throw new Error(
      `Backend did not return a road route. source=${source}. Check local OSRM before GPS playback.`,
    );
  }
  const points = decodePolyline(encodedPolyline);
  if (points.length < 2) {
    throw new Error(
      `Backend returned an empty or invalid road polyline. source=${source}.`,
    );
  }
  return points;
}

async function connect(token) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(HUB, { accessTokenFactory: () => token })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  await connection.start();
  return connection;
}

async function playback(connection, points) {
  const playbackPoints = interpolateRoute(points, STEP_METERS);
  console.log(`GPS playback started: ${playbackPoints.length} points`);

  for (let i = 0; i < playbackPoints.length; i++) {
    const point = playbackPoints[i];
    await connection.invoke('UpdateLocation', point.lat, point.lng, ACCURACY);
    console.log(
      `[${i + 1}/${playbackPoints.length}] ${point.lat.toFixed(6)}, ${point.lng.toFixed(6)}`,
    );
    await sleep(INTERVAL_MS);
  }
}

async function main() {
  if (PHASE !== 'PICKUP' && PHASE !== 'DELIVERY') {
    throw new Error('--phase must be PICKUP or DELIVERY.');
  }

  const token = await login();
  const points = await resolveRoute(token);
  const connection = await connect(token);

  try {
    await playback(connection, points);
  } finally {
    await connection.stop();
  }
}

main().catch(error => {
  console.error(error.message || error);
  process.exit(1);
});
