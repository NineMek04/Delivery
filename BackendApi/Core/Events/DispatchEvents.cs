namespace BackendApi.Core.Events;

/// <summary>
/// In-memory Dispatch Events — เตรียม pattern สำหรับ Event-Driven Orchestration
/// สามารถขยายไปใช้ MediatR หรือ Message Bus ในอนาคตได้
/// </summary>
public abstract record DispatchEvent(string OrderId, DateTime OccurredAt);

/// <summary>ออเดอร์ต้องการหาคนขับ</summary>
public sealed record DispatchRequested(
    string OrderId,
    DateTime OccurredAt) : DispatchEvent(OrderId, OccurredAt);

/// <summary>ยิง Offer ไปหา Rider แล้ว</summary>
public sealed record OfferCreated(
    string OrderId,
    string RiderId,
    string OfferId,
    int OfferVersion,
    DateTime ExpiresAt,
    DateTime OccurredAt) : DispatchEvent(OrderId, OccurredAt);

/// <summary>Rider กดรับงาน</summary>
public sealed record OfferAccepted(
    string OrderId,
    string RiderId,
    string OfferId,
    int OfferVersion,
    DateTime OccurredAt) : DispatchEvent(OrderId, OccurredAt);

/// <summary>Rider กดปฏิเสธ</summary>
public sealed record OfferRejected(
    string OrderId,
    string RiderId,
    string OfferId,
    DateTime OccurredAt) : DispatchEvent(OrderId, OccurredAt);

/// <summary>หมดเวลารอ — ไม่มีการตอบกลับ</summary>
public sealed record OfferExpired(
    string OrderId,
    string RiderId,
    string OfferId,
    DateTime OccurredAt) : DispatchEvent(OrderId, OccurredAt);

/// <summary>Rider สถานะเปลี่ยนเป็น STALE (เน็ตหลุด)</summary>
public sealed record RiderWentStale(
    string RiderId,
    DateTime LastHeartbeat,
    DateTime OccurredAt) : DispatchEvent(string.Empty, OccurredAt);
