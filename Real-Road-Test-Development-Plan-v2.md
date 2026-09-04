# Real-Road-Test-Development-Plan v2

> **Purpose:** Development specification for extending the existing Delivery Development project so it can be tested on a real road using a physical Android phone and real GPS.
>
> **Scope:** Development/testing only. This is NOT a Production deployment specification.

---

# 1. Core Requirement

The project must support a **Real Road Test** with:

```text
Physical Android Phone
        ↓
Flutter Rider App
        ↓
Real GPS
        ↓
4G / 5G
        ↓
Internet / Test Tunnel
        ↓
Docker Test Server
        ↓
Existing Backend
        ↓
Redis + PostgreSQL/PostGIS
        ↓
Existing Web/Admin Map
```

The primary objective is to verify that the existing delivery/rider system can receive, process, store, and display real GPS coordinates while a rider/device is moving on an actual road.

---

# 2. CRITICAL ARCHITECTURE RULE

## Road Test is NOT a second application

The `road-test/` area is only a **test workspace / test configuration / test tooling area**.

It must NOT contain duplicate implementations of existing application services.

### Correct concept

```text
road-test/
    ↓
controls/configures/tests
    ↓
EXISTING APPLICATION
    ↓
real Android device
```

### Incorrect concept

```text
existing application
        +
road-test application
        ↓
two separate GPS systems
two separate backend systems
two separate business logic systems
```

The second architecture is forbidden.

---

# 3. NO DUPLICATE SYSTEM RULE

Before creating any new service, class, controller, repository, database table, or business logic:

1. Search the existing repository.
2. Determine whether the functionality already exists.
3. Reuse the existing implementation.
4. Modify the existing implementation only when necessary.
5. Create a new component only if the functionality genuinely does not exist.

Do NOT duplicate existing functionality simply to isolate Road Test.

---

# 4. Existing Components That MUST Be Reused

The existing project already contains the major GPS pipeline.

The following components should remain the core implementation.

## Flutter

Reuse existing:

```text
LocationService
GpsBufferService
SignalRService
Dio/API client
SQLite/local database
authentication/storage
```

The existing `LocationService` already uses Geolocator for real GPS.

The existing GPS buffer already supports local storage for offline operation.

The existing SignalR client already supports real-time communication.

---

## Backend

Reuse existing:

```text
TelemetryController
TelemetryService
TrackingHub
RiderPresenceService
GpsHistoryService
```

Do not create:

```text
RoadTestTelemetryController       ❌
RoadTestTelemetryService          ❌
RoadTestTrackingHub               ❌
RoadTestGpsHistoryService         ❌
```

unless repository inspection proves that a genuinely separate capability is required.

---

## Infrastructure

Reuse:

```text
PostgreSQL
PostGIS
Redis
OSRM
```

Road Test should use the same core infrastructure, but with a separate **test configuration** where appropriate.

---

# 5. Road Test Folder Structure

Create a dedicated test workspace:

```text
Delivery-Development/
│
├── backend/
│
├── rider_app/
│
├── admin/
│
├── docker-compose.yml
├── docker-compose.override.yml
│
├── road-test/                         ← NEW
│   │
│   ├── README.md
│   │
│   ├── docker/
│   │   └── docker-compose.test.yml
│   │
│   ├── config/
│   │   └── .env.test.example
│   │
│   ├── scripts/
│   │   ├── start-test.sh
│   │   ├── stop-test.sh
│   │   ├── health-check.sh
│   │   └── reset-test-data.sh
│   │
│   └── docs/
│       ├── 01-server-setup.md
│       ├── 02-android-setup.md
│       ├── 03-gps-test.md
│       ├── 04-offline-test.md
│       └── 05-road-test.md
│
└── Real-Road-Test-Development-Plan-v2.md
```

This structure is recommended, but AI IDE must first inspect the existing repository and adapt paths to the actual project structure.

---

# 6. What Belongs Inside `road-test/`

Allowed:

```text
Test Docker configuration
Test environment variables
Test scripts
Health-check scripts
Test documentation
Test procedures
Test data utilities
Road Test helper tools
```

Potentially allowed:

```text
Road Test UI configuration
Feature flag configuration
Test-specific logging configuration
```

Only if these cannot be cleanly handled by existing application configuration.

---

# 7. What MUST NOT Be Inside `road-test/`

Do not copy existing source code into:

```text
road-test/
```

Do NOT create:

```text
road-test/backend/
road-test/rider_app/
road-test/services/
road-test/location/
road-test/database/
```

when those directories contain duplicated production/application logic.

Do NOT copy:

```text
LocationService
GpsBufferService
TelemetryService
TrackingHub
GpsHistoryService
RiderPresenceService
```

into the Road Test folder.

---

# 8. Configuration Separation

Road Test should have its own configuration.

Recommended:

```text
road-test/config/.env.test.example
```

The purpose is to configure the existing application for Road Test.

Example categories:

```text
API_BASE_URL
DATABASE_CONNECTION
REDIS_CONNECTION
OSRM_URL
TEST_MODE
MOCK_GPS
```

Do not commit real secrets.

Use:

```text
.env.test.example
```

as a template.

Actual local test secrets should remain outside source control.

---

# 9. Docker Test Environment

Create:

```text
road-test/docker/docker-compose.test.yml
```

The file should define how the existing services are started/configured for the Road Test.

It should NOT create a second implementation of the backend.

Concept:

```text
docker-compose.test.yml
        ↓
Existing Backend
Existing PostgreSQL/PostGIS
Existing Redis
Existing OSRM
```

The exact service list must be determined after inspecting the existing Compose dependency graph.

---

# 10. Existing Development Environment Must Be Preserved

Do not break:

```text
docker-compose.yml
docker-compose.override.yml
local development
Web Demo
Mock GPS
```

The existing Mock GPS system should remain available.

The Road Test environment should be isolated through configuration.

---

# 11. Mock GPS Rule

Existing Mock GPS is useful for Web Demo and development.

Keep it.

However:

```text
WEB DEMO
    MOCK GPS = ON

ANDROID ROAD TEST
    MOCK GPS = OFF
```

The physical Android device must use:

```text
Real GPS
```

Never use Mock GPS for the final road test.

---

# 12. Real Android Architecture

The physical Android phone should run the existing Flutter Rider application.

Do NOT create a second Android application.

Expected:

```text
rider_app/
    ↓
Flutter Android Build
    ↓
APK
    ↓
Physical Android Device
```

Build example:

```bash
flutter build apk
```

Development installation can use:

```bash
flutter run
```

---

# 13. Real GPS Flow

Reuse the existing location system.

Expected:

```text
Android GPS
      ↓
Geolocator
      ↓
Existing LocationService
      ↓
Existing GPS filtering/sampling
      ↓
Existing GPS buffer/upload logic
```

GPS information should include, where available:

```text
latitude
longitude
accuracy
speed
heading
timestamp
```

Do not create another GPS service specifically for Road Test.

---

# 14. Background GPS

The existing Android configuration already contains location and foreground-service-related permissions/configuration.

This must be verified on a real Android device.

Test:

```text
Start Tracking
      ↓
Lock screen
      ↓
Move/drive
      ↓
Unlock screen
      ↓
Check GPS history
```

Also test:

```text
Foreground
Background
Screen locked
Screen unlocked
```

Do not assume background GPS works just because the manifest/code exists.

The actual device and Android version must be tested.

---

# 15. Notification / Foreground Service Verification

Because background location may depend on foreground service behavior, verify the Android notification behavior required by the Android version being tested.

The test should confirm:

```text
Tracking started
      ↓
Foreground location service active
      ↓
Required notification visible
      ↓
GPS continues while app is backgrounded
```

Do not add unnecessary Android infrastructure.

---

# 16. Server Accessibility

A local address such as:

```text
192.168.x.x
```

is suitable for LAN testing but not for a phone that has left the local network.

For real road testing:

```text
Android Phone
      ↓
4G / 5G
      ↓
Internet
      ↓
Test Tunnel / Accessible Test Endpoint
      ↓
Docker Server
```

A development tunnel can be used.

Examples:

```text
Cloudflare Tunnel
ngrok
Tailscale
```

The exact tool should be selected based on the existing environment.

The goal is simply to make the development server reachable by the physical phone.

This is NOT a Production networking architecture.

---

# 17. Backend GPS API

Reuse the existing API.

Current architecture already contains:

```text
POST /api/v1/telemetry/gps
```

and:

```text
POST /api/v1/telemetry/gps/batch
```

Do not create:

```text
POST /api/v1/road-test/gps
```

unless repository inspection proves the existing API cannot support the requirement.

Prefer adapting existing configuration/client behavior instead.

---

# 18. Backend GPS Processing

Reuse the existing GPS processing pipeline.

Concept:

```text
GPS
 ↓
Telemetry API
 ↓
TelemetryService
 ↓
Validation
 ↓
Accuracy/anomaly handling
 ↓
Redis current position
 ↓
SignalR broadcast
 ↓
GPS history
 ↓
PostGIS
```

Do not duplicate this pipeline inside `road-test/`.

---

# 19. Redis

Reuse the existing Redis rider-presence/current-location mechanism.

Purpose:

> Store/read the rider's current position for real-time use.

Concept:

```text
GPS
 ↓
Backend
 ↓
Redis
 ↓
Current Rider Position
```

Do not create a separate Road Test Redis service unless absolutely required.

---

# 20. PostgreSQL + PostGIS

Reuse the existing GPS history system.

Purpose:

> Store historical GPS coordinates and spatial data.

Expected:

```text
GPS
 ↓
Backend
 ↓
PostGIS
 ↓
GPS History
 ↓
Map / Track Review
```

The existing `RiderLocationHistory` / GPS history implementation should be reused.

Do not create duplicate Road Test GPS history tables unless a real test-data isolation requirement exists.

If test data isolation is needed, prefer:

```text
test rider/account
test session identifier
test flag
```

over duplicating the whole data model.

---

# 21. SignalR

Reuse the existing SignalR tracking implementation.

Expected:

```text
Android
   ↓
GPS update
   ↓
Backend
   ↓
TrackingHub
   ↓
Web/Admin Map
```

Existing hub/client behavior should be reused.

Do not create:

```text
RoadTestTrackingHub
RoadTestSignalRService
```

without a proven requirement.

---

# 22. Offline GPS Buffer

Reuse the existing SQLite GPS buffer.

Expected behavior:

```text
GPS
 |
 +-- Internet available
 |        ↓
 |      Backend
 |
 +-- Internet unavailable
          ↓
       SQLite
          ↓
    Internet returns
          ↓
       Batch API
          ↓
       Backend
```

The existing:

```text
GpsBufferService
```

should remain the source of truth for local buffering.

Do not create:

```text
RoadTestGpsBufferService
```

---

# 23. GPS Sampling

The existing GPS buffering logic uses distance/heading-based filtering.

This should NOT be rewritten without testing.

For Road Test, determine whether current sampling is sufficient.

If higher GPS frequency is needed, prefer configurable parameters such as:

```text
GPS interval
distance filter
heading threshold
accuracy threshold
```

rather than creating a second GPS implementation.

Example Road Test configuration:

```text
GPS interval: 2–5 seconds
Distance filter: 5–15 meters
```

These are starting values only. Validate on the physical device.

---

# 24. Road Test Mode

A simple Road Test mode is recommended.

It should reuse the existing GPS/tracking pipeline.

Example:

```text
ROAD TEST

GPS: ACTIVE
Accuracy: 7.5 m
Speed: 42 km/h
Heading: 120°

Network: ONLINE
SignalR: CONNECTED

GPS Points: 352
Distance: 4.82 km

[ STOP TEST ]
```

The UI is for debugging and test visibility.

It must NOT implement a second GPS architecture.

---

# 25. Optional Road Test Session

A simple test-session concept may be added later.

Example:

```text
Test ID
Start Time
End Time
Distance
Duration
GPS Point Count
Average Accuracy
Network Disconnect Count
SignalR Disconnect Count
```

This is optional.

Do not implement it before the basic GPS road-test pipeline works.

---

# 26. Development Phases

## PHASE 1 — Repository Inspection

Before modifying anything:

```text
1. Inspect project structure.
2. Inspect existing Docker Compose files.
3. Inspect service dependencies.
4. Inspect Flutter GPS architecture.
5. Inspect Android configuration.
6. Inspect backend telemetry flow.
7. Inspect Redis usage.
8. Inspect PostGIS GPS history.
9. Inspect SignalR.
10. Identify all existing reusable components.
```

Output required:

```text
Existing component
Location
Purpose
Can reuse?
Needs modification?
Reason
```

Do not modify source code yet unless necessary for inspection/testing.

---

# 27. PHASE 2 — Road Test Folder

Create the dedicated:

```text
road-test/
```

workspace.

Start with:

```text
road-test/
├── README.md
├── docker/
├── config/
├── scripts/
└── docs/
```

Do not copy application source code into it.

---

# 28. PHASE 3 — Docker Test Configuration

Create:

```text
road-test/docker/docker-compose.test.yml
```

Determine required services from actual dependency analysis.

Target:

```text
Backend
PostgreSQL/PostGIS
Redis
OSRM
```

RabbitMQ/Vault/other services should only be included if required by the existing backend/service dependency graph.

Do not blindly remove them.

Do not blindly add them.

Inspect first.

---

# 29. PHASE 4 — Test Environment

Create:

```text
road-test/config/.env.test.example
```

Configure the existing application for Road Test.

Ensure:

```text
Mock GPS = OFF for Android Road Test
```

Keep development Mock GPS configuration intact.

---

# 30. PHASE 5 — Start and Validate Docker

Start the test environment.

Example:

```bash
docker compose -f road-test/docker/docker-compose.test.yml up -d
```

The exact command may differ depending on the final Compose structure.

Validate:

```text
Backend      ✓
PostgreSQL   ✓
PostGIS      ✓
Redis        ✓
OSRM         ✓
```

Then inspect logs.

---

# 31. PHASE 6 — Backend API Test

Before using the physical phone:

1. Authenticate.
2. Test health endpoint.
3. Send test GPS data.
4. Verify backend receives it.
5. Verify Redis.
6. Verify PostGIS.
7. Verify SignalR.

Only continue after the basic pipeline works.

---

# 32. PHASE 7 — External/Test Network Access

Configure a development tunnel or equivalent.

Test:

```text
Phone on mobile network
       ↓
Test endpoint
       ↓
Backend
```

Do not start driving yet.

---

# 33. PHASE 8 — Android APK

Build the existing Rider app:

```bash
flutter build apk
```

Install on the physical Android phone.

Configure the API endpoint to point to the Road Test server.

Do not hard-code a production URL.

---

# 34. PHASE 9 — Stationary Real GPS Test

Before driving:

```text
Phone stationary
      ↓
Real GPS
      ↓
Backend
      ↓
Redis
      ↓
PostGIS
```

Verify coordinates.

Record:

```text
Accuracy
Update frequency
Latency
GPS point count
```

---

# 35. PHASE 10 — Walking Test

Walk approximately:

```text
100–500 meters
```

Verify:

```text
GPS track
Position updates
SignalR
PostGIS history
Map marker
```

---

# 36. PHASE 11 — Vehicle Test

Start slowly:

```text
10–20 km/h
```

Then:

```text
30–60 km/h
```

Verify:

```text
latitude
longitude
speed
heading
accuracy
timestamp
track continuity
```

---

# 37. PHASE 12 — Background Test

Test:

```text
Start Tracking
 ↓
Home button
 ↓
Lock screen
 ↓
Move/drive
 ↓
Unlock
 ↓
Review history
```

Confirm GPS does not unexpectedly stop.

---

# 38. PHASE 13 — Offline Test

Test:

```text
Internet ON
 ↓
GPS uploading
 ↓
Internet OFF
 ↓
Continue moving
 ↓
SQLite buffer
 ↓
Internet ON
 ↓
Batch upload
 ↓
PostGIS history
```

Verify buffered points are not unnecessarily duplicated.

---

# 39. PHASE 14 — SignalR Reconnection

Test:

```text
SignalR connected
       ↓
Network interruption
       ↓
SignalR disconnected
       ↓
Network restored
       ↓
SignalR reconnect
```

Reuse existing reconnect behavior.

Only modify it if the real-device test exposes an actual problem.

---

# 40. PHASE 15 — Real Road Test

Only after all previous phases pass.

Recommended staged test:

```text
Test A: Stationary
Test B: Walking
Test C: Slow vehicle
Test D: Normal road speed
Test E: Screen locked
Test F: Internet loss
Test G: SignalR reconnect
```

Do not immediately perform a long-distance road test.

---

# 41. Definition of Done

## Docker

```text
[ ] Test Docker environment starts
[ ] Backend works
[ ] PostgreSQL works
[ ] PostGIS works
[ ] Redis works
[ ] OSRM works if required
```

## Android

```text
[ ] APK builds
[ ] APK installs
[ ] Physical device obtains real GPS
[ ] Mock GPS is disabled
[ ] Background GPS tested
[ ] Notification/foreground service behavior tested
```

## Network

```text
[ ] Phone can reach server over mobile Internet
[ ] Authentication works
[ ] GPS API works
[ ] Batch API works
[ ] SignalR works
```

## Data

```text
[ ] GPS reaches backend
[ ] Current location reaches Redis
[ ] GPS history reaches PostGIS
[ ] Map receives/displays location
[ ] Track can be reviewed
```

## Offline

```text
[ ] Offline GPS is stored locally
[ ] Reconnection works
[ ] Buffered points upload
[ ] No major data loss
```

## Road Test

```text
[ ] Stationary test passes
[ ] Walking test passes
[ ] Vehicle test passes
[ ] Screen-lock test passes
[ ] Network-loss test passes
[ ] SignalR reconnect test passes
```

---

# 42. Priority

## MUST HAVE

```text
1. Road Test workspace
2. Docker Test configuration
3. Existing Backend
4. PostgreSQL + PostGIS
5. Redis
6. Internet/test endpoint
7. Android APK
8. Real GPS
9. GPS upload
10. GPS history
11. Map tracking
```

## SHOULD HAVE

```text
12. Background GPS
13. SignalR real-time tracking
14. SQLite offline buffer
15. Reconnect handling
```

## NICE TO HAVE

```text
16. Road Test UI
17. Test Session
18. Test analytics
19. Route matching improvements
```

Do not implement NICE TO HAVE features before MUST HAVE functionality works.

---

# 43. AI IDE Mandatory Rules

The AI IDE must follow all rules below.

### Rule 1 — Inspect first

Never assume functionality is missing.

Search the repository first.

### Rule 2 — Reuse existing code

If an existing service already performs the required function, reuse it.

### Rule 3 — Modify instead of duplicate

If existing functionality needs improvement:

```text
Modify existing implementation
```

instead of:

```text
Create duplicate Road Test implementation
```

### Rule 4 — Road Test folder is not a second application

`road-test/` contains test infrastructure/configuration/tools/documentation.

It does not contain duplicated application source code.

### Rule 5 — Preserve existing development

Do not break:

```text
Local Development
Web Demo
Mock GPS
Existing Docker environment
Existing APIs
```

### Rule 6 — No unnecessary refactoring

Only modify files required for Road Test.

### Rule 7 — No Production expansion

Do not introduce unnecessary Production infrastructure.

### Rule 8 — Explain changes

After each implementation phase, report:

```text
Files changed
Files created
Files deleted
Reason
Existing component reused
New functionality
How to test
Expected result
```

### Rule 9 — Test each phase

Do not implement all phases at once.

Complete one phase, validate it, then continue.

### Rule 10 — Stop if architecture is unclear

If a proposed change may create duplicate architecture:

```text
STOP
Inspect existing implementation
Explain the conflict
Propose reuse/modification
Wait before making large architectural changes
```

---

# 44. AI IDE First Task

The next task is **NOT to modify GPS code**.

Start with repository analysis.

Perform:

```text
1. Inspect current repository structure.
2. Inspect docker-compose.yml.
3. Inspect docker-compose.override.yml.
4. Inspect all service dependencies.
5. Inspect current Flutter GPS architecture.
6. Inspect LocationService.
7. Inspect GpsBufferService.
8. Inspect SignalRService.
9. Inspect AndroidManifest.xml.
10. Inspect TelemetryController.
11. Inspect TelemetryService.
12. Inspect TrackingHub.
13. Inspect RiderPresenceService.
14. Inspect GpsHistoryService.
15. Inspect PostgreSQL/PostGIS configuration.
16. Inspect Redis configuration.
17. Identify what is already reusable.
18. Identify what genuinely needs modification.
19. Propose the `road-test/` structure.
20. Propose `docker-compose.test.yml`.
21. Do NOT create duplicate application services.
22. Do NOT modify existing application logic yet unless required for inspection.
```

Then produce a report in this format:

```text
## Existing Component Analysis

| Component | Existing Location | Reusable? | Modification Needed? | Reason |
|---|---|---:|---:|---|
| LocationService | ... | YES | ... | ... |
| GpsBufferService | ... | YES | ... | ... |
| SignalRService | ... | YES | ... | ... |
| TelemetryController | ... | YES | ... | ... |
| TelemetryService | ... | YES | ... | ... |
| TrackingHub | ... | YES | ... | ... |
| Redis | ... | YES | ... | ... |
| PostGIS | ... | YES | ... | ... |

## Proposed Road Test Structure

...

## Proposed Docker Test Architecture

...

## Files That Need Modification

...

## Files That Should NOT Be Duplicated

...

## Next Implementation Step

...
```

Do not proceed to broad implementation until this analysis is complete.

---

# 45. Final Architecture

The intended final architecture is:

```text
                         REAL ROAD
                            |
                            v
                  ┌──────────────────┐
                  │ Physical Android │
                  │                  │
                  │ Existing Flutter │
                  │ Rider App        │
                  │                  │
                  │ Real GPS         │
                  │ Geolocator       │
                  │ SQLite Buffer    │
                  │ SignalR Client   │
                  └────────┬─────────┘
                           |
                         4G/5G
                           |
                           v
                        Internet
                           |
                    Test Tunnel/Endpoint
                           |
                           v
             ┌──────────────────────────┐
             │     Docker Test Host    │
             │                          │
             │   Existing Backend       │
             │          |               │
             │     +----+----+          │
             │     |         |          │
             │   Redis    PostgreSQL    │
             │                 |        │
             │              PostGIS     │
             │                          │
             │               OSRM       │
             └──────────────────────────┘
                           |
                           v
                     Existing Map
                           |
                           v
                    Real GPS Track
```

The key architectural principle is:

```text
ROAD TEST = NEW TEST ENVIRONMENT
             +
            EXISTING APPLICATION
             +
        REAL ANDROID GPS
```

NOT:

```text
ROAD TEST = NEW DUPLICATE APPLICATION
```

---

# 46. Success Criterion

The project is successful when the existing Delivery system can be used as-is or with minimal targeted modifications to perform this complete real-world flow:

```text
Real Android GPS
      ↓
Existing Flutter LocationService
      ↓
Existing GPS Buffer/Upload
      ↓
Existing Backend Telemetry API
      ↓
Existing TelemetryService
      ↓
Redis current location
      ↓
SignalR real-time update
      ↓
PostgreSQL/PostGIS GPS history
      ↓
Existing Map
      ↓
Real rider track on a real road
```

The `road-test/` folder exists only to organize and control the test environment.

It must never become a second implementation of the application.
