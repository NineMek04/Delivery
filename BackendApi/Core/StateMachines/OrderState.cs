namespace BackendApi.Core.StateMachines;

/// <summary>
/// สถานะของ Order ในระบบ Dispatch
/// Flow: CREATED → MATCHING → OFFERING → ASSIGNED → PICKING_UP → DELIVERING → COMPLETED
/// </summary>
public enum OrderState
{
    /// <summary>ออเดอร์ถูกสร้าง รอเข้าสู่กระบวนการหาคนขับ</summary>
    CREATED,

    /// <summary>กำลังค้นหา Rider ที่เหมาะสม (ถาม AI Engine)</summary>
    MATCHING,

    /// <summary>ยิง Offer ไปหา Rider แล้ว รอการตอบรับ (จองตัวชั่วคราว)</summary>
    OFFERING,

    /// <summary>Rider กดรับงานแล้ว เริ่ม flow การส่ง</summary>
    ASSIGNED,

    /// <summary>Rider กำลังเดินทางไปรับของ</summary>
    PICKING_UP,

    /// <summary>Rider รับของแล้ว กำลังส่ง</summary>
    DELIVERING,

    /// <summary>ส่งเรียบร้อย</summary>
    COMPLETED,

    /// <summary>ยกเลิก</summary>
    CANCELLED
}

/// <summary>
/// กฎการเปลี่ยนสถานะ Order — ป้องกัน Illegal State Transition
/// </summary>
public static class OrderStateRules
{
    /// <summary>
    /// ตรวจสอบว่าการเปลี่ยนสถานะ Order ถูกต้องหรือไม่
    /// </summary>
    public static bool IsValidTransition(OrderState from, OrderState to) => (from, to) switch
    {
        (OrderState.CREATED, OrderState.CREATED) => true,
        (OrderState.MATCHING, OrderState.CREATED) => true,
        (OrderState.CREATED, OrderState.MATCHING) => true,
        (OrderState.CREATED, OrderState.CANCELLED) => true,
        (OrderState.MATCHING, OrderState.OFFERING) => true,
        (OrderState.MATCHING, OrderState.CANCELLED) => true,
        (OrderState.OFFERING, OrderState.ASSIGNED) => true,
        (OrderState.OFFERING, OrderState.MATCHING) => true,     // Re-dispatch: Rider ปฏิเสธ/timeout
        (OrderState.OFFERING, OrderState.CANCELLED) => true,
        (OrderState.ASSIGNED, OrderState.PICKING_UP) => true,
        (OrderState.ASSIGNED, OrderState.CANCELLED) => true,
        (OrderState.PICKING_UP, OrderState.DELIVERING) => true,
        (OrderState.PICKING_UP, OrderState.CANCELLED) => true,
        (OrderState.DELIVERING, OrderState.COMPLETED) => true,
        _ => false
    };
}
