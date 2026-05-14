namespace BackendApi.Core.StateMachines;

/// <summary>
/// สถานะของ Rider ในระบบ Dispatch
/// Flow: OFFLINE → IDLE → RESERVED → BUSY → (STALE เมื่อเน็ตหลุด)
/// </summary>
public enum RiderState
{
    /// <summary>ปิดแอป / ออกจากระบบ</summary>
    OFFLINE,

    /// <summary>ว่างงาน พร้อมรับงาน</summary>
    IDLE,

    /// <summary>ถูกจองชั่วคราว (ยิง Offer ไปแล้ว รอตอบรับ 30 วิ)</summary>
    RESERVED,

    /// <summary>กำลังวิ่งงาน</summary>
    BUSY,

    /// <summary>เน็ตหลุด / ไม่ส่ง heartbeat เกินเวลาที่กำหนด</summary>
    STALE
}

/// <summary>
/// กฎการเปลี่ยนสถานะ Rider — ป้องกัน Illegal State Transition
/// </summary>
public static class RiderStateRules
{
    /// <summary>
    /// ตรวจสอบว่าการเปลี่ยนสถานะ Rider ถูกต้องหรือไม่
    /// </summary>
    public static bool IsValidTransition(RiderState from, RiderState to) => (from, to) switch
    {
        (RiderState.OFFLINE, RiderState.IDLE) => true,          // เปิดแอป / เชื่อมต่อ
        (RiderState.IDLE, RiderState.RESERVED) => true,         // ถูกจองตัวชั่วคราว
        (RiderState.IDLE, RiderState.OFFLINE) => true,          // ปิดแอป
        (RiderState.RESERVED, RiderState.BUSY) => true,         // กดรับงาน
        (RiderState.RESERVED, RiderState.IDLE) => true,         // ปฏิเสธงาน / Timeout
        (RiderState.RESERVED, RiderState.STALE) => true,        // เน็ตหลุดระหว่างรอกดรับ
        (RiderState.BUSY, RiderState.IDLE) => true,             // ส่งเสร็จ
        (RiderState.BUSY, RiderState.OFFLINE) => true,          // ส่งเสร็จ + ปิดแอป
        (RiderState.BUSY, RiderState.STALE) => true,            // เน็ตหลุดระหว่างวิ่งงาน
        (RiderState.STALE, RiderState.IDLE) => true,            // เน็ตกลับมา + ไม่มีงาน
        (RiderState.STALE, RiderState.BUSY) => true,            // เน็ตกลับมา + ยังมีงานอยู่
        (RiderState.STALE, RiderState.OFFLINE) => true,         // เน็ตหลุดนานเกิน threshold
        _ => false
    };
}
