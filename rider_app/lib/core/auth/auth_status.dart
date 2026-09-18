/// Authentication state — ใช้ track สถานะ login ของ Rider.
enum AuthStatus {
  /// กำลังตรวจสอบ token เริ่มต้น
  loading,

  /// ยืนยันตัวตนแล้ว (มี valid token)
  authenticated,

  /// ยังไม่ได้ login หรือ token หมดอายุ
  unauthenticated,
}
