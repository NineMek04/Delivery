import '../../core/config/app_constants.dart';

/// Labels and next status for order state machine (BackendApi).
class OrderStatusHelper {
  OrderStatusHelper._();

  static String label(String status) {
    switch (status.toUpperCase()) {
      case 'CREATED':
        return 'สร้างแล้ว';
      case 'MATCHING':
        return 'กำลังจับคู่';
      case 'OFFERING':
        return 'รอตอบรับ';
      case 'ASSIGNED':
        return 'รับงานแล้ว';
      case 'PICKING_UP':
        return 'กำลังไปร้าน';
      case 'DELIVERING':
        return 'กำลังส่ง';
      case 'COMPLETED':
        return 'ส่งสำเร็จ';
      case 'CANCELLED':
        return 'ยกเลิก';
      default:
        return status;
    }
  }

  /// Next status Rider can set via PATCH, or null if terminal / not actionable.
  static String? nextRiderStatus(String current) {
    switch (current.toUpperCase()) {
      case 'ASSIGNED':
        return 'PICKING_UP';
      case 'PICKING_UP':
        return AppConstants.orderDelivering;
      case 'DELIVERING':
        return AppConstants.orderCompleted;
      default:
        return null;
    }
  }

  static String nextActionLabel(String current) {
    switch (current.toUpperCase()) {
      case 'ASSIGNED':
        return 'ถึงร้านแล้ว';
      case 'PICKING_UP':
        return 'รับสินค้าแล้ว — เริ่มส่ง';
      case 'DELIVERING':
        return 'ส่งสำเร็จ';
      default:
        return 'อัปเดตสถานะ';
    }
  }

  static bool isActive(String status) {
    const active = {
      'OFFERING',
      'ASSIGNED',
      'PICKING_UP',
      'DELIVERING',
    };
    return active.contains(status.toUpperCase());
  }

  static bool isCompleted(String status) {
    final s = status.toUpperCase();
    return s == 'COMPLETED' || s == 'CANCELLED';
  }
}
