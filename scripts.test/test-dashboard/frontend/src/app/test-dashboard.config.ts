import { TestCase } from './test-dashboard.model';

export const INITIAL_CSHARP_CASES: TestCase[] = [
  {
    name: 'CreateOrder_ShouldSuccess',
    location: 'BackendApi.IntegrationTests.Orders.CreateOrderTests',
    inputs: 'ShopId: 9b1deb4d, Dropoff: 17.4300, 102.7900',
    status: 'PASS',
    durationMs: 245,
    requestPayload: `POST /api/v1/orders HTTP/1.1
Host: api.smartdelivery.local
Content-Type: application/json

{
  "shopId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "pickupLat": 17.4138,
  "pickupLng": 102.7872,
  "dropoffLat": 17.4300,
  "dropoffLng": 102.7900,
  "expectedDeliveryTime": "2026-05-28T16:15:00.000Z"
}`,
    responseTrace: `HTTP/1.1 201 Created
Content-Type: application/json

{
  "id": "27f5a1bc-7b3d-4bad-9bdd-2b0d7b3dcb6d",
  "shopId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "state": "PENDING_DISPATCH",
  "pickupLat": 17.4138,
  "pickupLng": 102.7872,
  "dropoffLat": 17.4300,
  "dropoffLng": 102.7900,
  "createdAt": "2026-05-28T15:30:00.123Z"
}

// Database Assertion: Order row exists with State='PENDING_DISPATCH'
// EventBus Assertion: OrderCreatedIntegrationEvent published to 'delivery.events'`
  },
  {
    name: 'RegisterRider_ShouldVerifyIdempotency',
    location: 'BackendApi.IntegrationTests.Riders.RegisterRiderTests',
    inputs: 'MsgId: e8fbc4c5, Email: rider1@delivery.com',
    status: 'PASS',
    durationMs: 98,
    requestPayload: `POST /api/v1/auth/register HTTP/1.1
Host: api.smartdelivery.local
Content-Type: application/json
X-Idempotency-Key: e8fbc4c5-3b7d-4bad-9bdd-2b0d7b3dcb6d

{
  "email": "rider1@delivery.com",
  "fullName": "Somchai Jaidee",
  "role": "Rider"
}`,
    responseTrace: `// Request 1 Response:
HTTP/1.1 200 OK
Content-Type: application/json

{
  "id": "rider-123-uuid",
  "email": "rider1@delivery.com",
  "state": "OFFLINE",
  "registeredAt": "2026-05-28T15:31:00.000Z"
}

// Request 2 (Concurrent with same X-Idempotency-Key) Response:
HTTP/1.1 200 OK
Content-Type: application/json

{
  "id": "rider-123-uuid",
  "email": "rider1@delivery.com",
  "state": "OFFLINE",
  "registeredAt": "2026-05-28T15:31:00.000Z"
}

// Database Assertion: SELECT COUNT(*) FROM "Riders" WHERE "Email" = 'rider1@delivery.com' -> 1`
  },
  {
    name: 'ProcessPayment_ShouldFail_WhenBalanceInsufficient',
    location: 'BackendApi.IntegrationTests.Payments.ProcessPaymentTests',
    inputs: 'RiderId: rider-123, Amount: 150.00 THB',
    status: 'FAIL',
    durationMs: 120,
    requestPayload: `POST /api/v1/payments/debit HTTP/1.1
Host: api.smartdelivery.local
Content-Type: application/json

{
  "riderId": "rider-123-uuid",
  "orderId": "27f5a1bc-7b3d-4bad-9bdd-2b0d7b3dcb6d",
  "amount": 150.00,
  "currency": "THB",
  "method": "Wallet"
}`,
    error: `HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://api.smartdelivery.local/errors/insufficient-funds",
  "title": "Insufficient wallet balance",
  "status": 400,
  "detail": "Wallet debit failed: Insufficient balance. Remaining: 42.00 THB",
  "errorCode": "INSUFFICIENT_FUNDS",
  "currentBalance": 42.00,
  "requiredAmount": 150.00
}

// Database Assertion: Transaction rolled back. Wallet balance remains 42.00 THB.`
  }
];

export const INITIAL_PYTHON_CASES: TestCase[] = [
  {
    name: 'test_vrp_optimizer_routing',
    location: 'ai_engine.tests.test_vrp',
    inputs: 'Locations: 2, Vehicles: 1',
    status: 'PASS',
    durationMs: 820,
    requestPayload: `POST /api/v1/dispatch/rank HTTP/1.1
Host: ai.smartdelivery.local
Content-Type: application/json

{
  "locations": [
    {"id": "shop", "lat": 17.4138, "lng": 102.7872},
    {"id": "drop1", "lat": 17.4200, "lng": 102.7900}
  ],
  "demands": [0, 1],
  "vehicle_capacities": [100],
  "distance_matrix_type": "OSRM_DRIVING"
}`,
    responseTrace: `HTTP/1.1 200 OK
Content-Type: application/json

{
  "status": "ROUTING_SUCCESS",
  "statusCode": 1,
  "objectiveValue": 4800,
  "routes": [
    {
      "vehicleId": 0,
      "path": ["shop", "drop1", "shop"],
      "distanceMeters": 4800,
      "totalDemand": 1
    }
  ]
}

// Assertion: status == "ROUTING_SUCCESS"
// Assertion: routes[0].distanceMeters == 4800`
  },
  {
    name: 'test_vrp_capacity_constraints',
    location: 'ai_engine.tests.test_vrp',
    inputs: 'VehicleCapacity: 50, Demands: [10, 20, 30]',
    status: 'PASS',
    durationMs: 340,
    requestPayload: `POST /api/v1/dispatch/rank HTTP/1.1
Host: ai.smartdelivery.local
Content-Type: application/json

{
  "locations": [
    {"id": "shop", "lat": 17.4138, "lng": 102.7872},
    {"id": "drop1", "lat": 17.4200, "lng": 102.7900},
    {"id": "drop2", "lat": 17.4250, "lng": 102.7950}
  ],
  "demands": [0, 30, 30],
  "vehicle_capacities": [50, 50]
}`,
    responseTrace: `HTTP/1.1 200 OK
Content-Type: application/json

{
  "status": "ROUTING_SUCCESS",
  "statusCode": 1,
  "routes": [
    {
      "vehicleId": 0,
      "path": ["shop", "drop1", "shop"],
      "totalDemand": 30
    },
    {
      "vehicleId": 1,
      "path": ["shop", "drop2", "shop"],
      "totalDemand": 30
    }
  ]
}

// Assertion: routes.length == 2 (due to capacity constraints)
// Assertion: routes[0].totalDemand <= 50 AND routes[1].totalDemand <= 50`
  },
  {
    name: 'test_time_window_compatibility',
    location: 'ai_engine.tests.test_time_window',
    inputs: 'PickupWindow: 690-720, Arrival: 702',
    status: 'PASS',
    durationMs: 150,
    requestPayload: `POST /api/v1/dispatch/check-time-window HTTP/1.1
Host: ai.smartdelivery.local
Content-Type: application/json

{
  "pickup_window": ["11:30", "12:00"],
  "rider_arrival_time": "11:42"
}`,
    responseTrace: `HTTP/1.1 200 OK
Content-Type: application/json

{
  "isFeasible": true,
  "pickupStartMinutes": 690,
  "pickupEndMinutes": 720,
  "arrivalMinutes": 702,
  "penaltyScore": 0
}

// Assertion: isFeasible == true
// Assertion: penaltyScore == 0`
  }
];

export const INITIAL_LOAD_CASES: TestCase[] = [
  {
    name: 'concurrency_stress_gps_signalr',
    location: 'load-test/resilience-stress.js',
    inputs: 'VUs: 1000, Msgs/Sec: 5000, Duration: 10s',
    status: 'PASS',
    durationMs: 12400,
    requestPayload: `// k6 WebSocket Payload Sample
const wsUrl = 'ws://api.smartdelivery.local/hubs/tracking';
const payload = {
  "protocol": "json",
  "version": 1
};

// ... after handshake ...
{
  "type": 1,
  "target": "UpdateLocation",
  "arguments": [
    {
      "lat": 17.4140,
      "lng": 102.7875,
      "heading": 90,
      "speed": 45.5,
      "timestamp": "2026-05-28T16:00:00.000Z"
    }
  ]
}`,
    responseTrace: `{
  "metrics": {
    "ws_connecting": { "avg": "2.4ms", "max": "45ms", "p(95)": "8.4ms" },
    "ws_msgs_sent": { "count": 50230, "rate": "5023/s" },
    "ws_msgs_received": { "count": 50230, "rate": "5023/s" },
    "ws_sessions_established": { "count": 1000, "rate": "100/s" },
    "errors": { "count": 0, "rate": "0.00%" }
  }
}

// Assertion: metrics.errors.count == 0
// Assertion: metrics.ws_msgs_sent.rate > 4500`
  },
  {
    name: 'idempotency_locking_order_spam',
    location: 'load-test/resilience-stress.js',
    inputs: 'ConcurrentAccepts: 50, OrderId: 27f5a1bc',
    status: 'PASS',
    durationMs: 1850,
    requestPayload: `// k6 HTTP Batch Request (50 concurrent calls to same endpoint)
PATCH /api/v1/orders/27f5a1bc-7b3d-4bad-9bdd-2b0d7b3dcb6d/accept HTTP/1.1
Host: api.smartdelivery.local
Content-Type: application/json
Authorization: Bearer <rider_token>

{
  "riderId": "rider-1"
}`,
    responseTrace: `{
  "metrics": {
    "http_reqs": { "count": 50 },
    "http_req_status_200": { "count": 1 },
    "http_req_status_409": { "count": 49 }
  }
}

// Assertion: http_req_status_200.count == 1
// Assertion: http_req_status_409.count == 49
// Database Assertion: Order.AssignedRiderId == "rider-1"`
  }
];

export const INITIAL_SIMULATOR_CASES: TestCase[] = [
  {
    name: 'Backend Health',
    location: 'e2e-simulator/simulate-e2e.js',
    inputs: 'URL: http://localhost:5000/health',
    status: 'PASS',
    durationMs: 120,
    requestPayload: `GET /health HTTP/1.1
Host: localhost:5000
Accept: application/json`,
    responseTrace: `HTTP/1.1 200 OK
Content-Type: application/json

{
  "status": "Healthy",
  "totalDuration": "00:00:00.0083000",
  "entries": {
    "postgres-db": {
      "data": {},
      "duration": "00:00:00.0024000",
      "status": "Healthy",
      "tags": []
    },
    "redis-cache": {
      "data": {},
      "duration": "00:00:00.0008000",
      "status": "Healthy",
      "tags": []
    },
    "rabbitmq-broker": {
      "data": {},
      "duration": "00:00:00.0051000",
      "status": "Healthy",
      "tags": []
    }
  }
}`
  },
  {
    name: 'Admin Login',
    location: 'e2e-simulator/simulate-e2e.js',
    inputs: 'Email: admin@delivery.com',
    status: 'PASS',
    durationMs: 230,
    requestPayload: `POST /api/v1/auth/login HTTP/1.1
Host: localhost:5000
Content-Type: application/json

{
  "email": "admin@delivery.com",
  "password": "Password123!"
}`,
    responseTrace: `HTTP/1.1 200 OK
Content-Type: application/json

{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJhZG1pbiIs...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "user": {
    "id": "admin-1",
    "email": "admin@delivery.com",
    "role": "Admin"
  }
}`
  },
  {
    name: 'Create Shop',
    location: 'e2e-simulator/simulate-e2e.js',
    inputs: 'Lat: 17.4138, Lng: 102.7872',
    status: 'PASS',
    durationMs: 450,
    requestPayload: `POST /api/v1/shops HTTP/1.1
Host: localhost:5000
Content-Type: application/json
Authorization: Bearer <admin_token>

{
  "name": "UD Town Coffee Sim 20260528082053",
  "lat": 17.4138,
  "lng": 102.7872
}`,
    responseTrace: `HTTP/1.1 201 Created
Content-Type: application/json

{
  "id": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "name": "UD Town Coffee Sim 20260528082053",
  "lat": 17.4138,
  "lng": 102.7872,
  "createdAt": "2026-05-28T16:20:53.000Z"
}`
  },
  {
    name: 'Create Order',
    location: 'e2e-simulator/simulate-e2e.js',
    inputs: 'Pickup: UD Town Coffee, Dropoff: 17.4428, 102.7915',
    status: 'PASS',
    durationMs: 610,
    requestPayload: `POST /api/v1/orders HTTP/1.1
Host: localhost:5000
Content-Type: application/json
Authorization: Bearer <user_token>

{
  "shopId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "pickupLat": 17.4138,
  "pickupLng": 102.7872,
  "dropoffLat": 17.4428,
  "dropoffLng": 102.7915
}`,
    responseTrace: `HTTP/1.1 201 Created
Content-Type: application/json

{
  "id": "27f5a1bc-7b3d-4bad-9bdd-2b0d7b3dcb6d",
  "shopId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "state": "PENDING_DISPATCH",
  "pickupLat": 17.4138,
  "pickupLng": 102.7872,
  "dropoffLat": 17.4428,
  "dropoffLng": 102.7915
}`
  },
  {
    name: 'E2E Delivery Lifecycle',
    location: 'e2e-simulator/simulate-e2e.js',
    inputs: 'Rider: Sim Rider 1, OrderId: 27f5a1bc',
    status: 'PASS',
    durationMs: 14200,
    requestPayload: `// 1. SignalR Accept Offer
{
  "type": 1,
  "target": "AcceptOffer",
  "arguments": ["27f5a1bc-7b3d-4bad-9bdd-2b0d7b3dcb6d"]
}

// 2. SignalR Update Location Loop
{
  "type": 1,
  "target": "UpdateLocation",
  "arguments": [{
    "lat": 17.4138, "lng": 102.7872, "timestamp": "..."
  }]
}

// 3. HTTP Update State (Arrived at Shop)
PATCH /api/v1/orders/27f5a1bc.../state
{ "newState": "DELIVERING" }

// 4. HTTP Update State (Completed)
PATCH /api/v1/orders/27f5a1bc.../state
{ "newState": "COMPLETED" }`,
    responseTrace: `// Event stream asserted sequentially:
1. ORDER_ACCEPTED {"orderId": "...", "riderId": "..."}
2. RIDER_LOCATION_UPDATED {"lat": 17.4138, "lng": 102.7872}
3. ORDER_STATE_CHANGED {"state": "DELIVERING"}
4. ORDER_COMPLETED {"orderId": "..."}

// Final Database State Assertion:
// DB.Orders.Find(id).State == "COMPLETED"`
  }
];
