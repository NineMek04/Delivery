# 03 Realtime And Events

## SignalR Hubs

```text
/hubs/tracking -> BackendApi/Hubs/Tracking/TrackingHub*.cs
/hubs/chat     -> BackendApi/Hubs/Chat/ChatHub.cs
```

`TrackingHub` is pure transport: authenticate, validate, group routing, and delegate to services. It must not contain business mutation logic.

## TrackingHub Client Methods

```text
UpdateLocation(lat, lng, accuracy)
UpdateRiderLocation(lat, lng)
UpdateHeartbeat()
UpdateStatus(riderState)
AcceptOffer(offerId, version)
RejectOffer(offerId, orderId)
```

Order phase transitions must use REST order status APIs, not `UpdateStatus`.

## Main Server Events

```text
OfferReceived
RiderLocationUpdated
ShopStatusChanged
DispatchScanStarted
DispatchCandidatesRanked
DispatchOfferSent
OrderStatusChanged
TelemetryUpdated
```

Payloads must be camelCase. `RiderLocationUpdated` uses `state`, not `status`, for rider state.

## Groups

```text
admins
rider:{riderId}
customer:{userId}
store:{shopId}
```

Legacy `stores` can exist only as compatibility path. New store events must use `store:{shopId}`.

## Event Classes

- Domain Events: internal .NET events inside one bounded context.
- Integration Events: RabbitMQ cross-service events named `<Domain><Action>IntegrationEvent`.
- Telemetry Events: high-frequency realtime streams named like `RiderLocationUpdatedTelemetryEvent`.

## RabbitMQ Rules

- Consumers must check `ProcessedEvents` before running logic.
- Retry is bounded and failed messages must go to DLQ.
- High-frequency raw telemetry should not become expensive DB-per-message loops.

