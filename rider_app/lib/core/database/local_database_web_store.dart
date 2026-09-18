import 'dart:convert';
import 'package:logger/logger.dart';
import '../../models/order.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class LocalDatabaseWebStore {
  final Map<String, Map<String, dynamic>> orders = {};
  final Map<String, String> session = {};
  final List<Map<String, dynamic>> pendingUpdates = [];
  final List<Map<String, dynamic>> pendingGpsPoints = [];
  final List<Map<String, dynamic>> errorLogs = [];
  int gpsSequence = 0;

  void saveOrders(List<OrderDto> orderList, bool Function(String) isActiveStatus) {
    for (final order in orderList) {
      orders[order.id] = {
        'id': order.id,
        'status': order.status,
        'is_active': isActiveStatus(order.status) ? 1 : 0,
        'json_data': jsonEncode(order.toJson()),
      };
    }
    _logger.d('[Web] Saved ${orderList.length} orders to memory');
  }

  void saveOrder(OrderDto order, bool Function(String) isActiveStatus) {
    orders[order.id] = {
      'id': order.id,
      'status': order.status,
      'is_active': isActiveStatus(order.status) ? 1 : 0,
      'json_data': jsonEncode(order.toJson()),
    };
    _logger.d('[Web] Saved order ${order.id} to memory');
  }

  List<OrderDto> getActiveOrders() {
    return orders.values
        .where((m) => m['is_active'] == 1)
        .map((m) => OrderDto.fromJson(jsonDecode(m['json_data'] as String) as Map<String, dynamic>))
        .toList();
  }

  List<OrderDto> getCompletedOrders() {
    return orders.values
        .where((m) => m['is_active'] == 0)
        .map((m) => OrderDto.fromJson(jsonDecode(m['json_data'] as String) as Map<String, dynamic>))
        .toList();
  }

  void clearAllData() {
    orders.clear();
    session.clear();
    pendingUpdates.clear();
    pendingGpsPoints.clear();
    errorLogs.clear();
    _logger.i('[Web] Cleared memory database');
  }

  void saveIsOnline(bool isOnline) {
    session['is_online'] = isOnline.toString();
    _logger.d('[Web] Saved session status: is_online = $isOnline');
  }

  bool getIsOnline() {
    final val = session['is_online'];
    return val == 'true';
  }

  void savePendingStatusUpdate(String orderId, String status) {
    pendingUpdates.add({
      'id': pendingUpdates.length + 1,
      'order_id': orderId,
      'status': status,
      'timestamp': DateTime.now().millisecondsSinceEpoch,
    });
    _logger.d('[Web] Saved pending status update to memory: orderId=$orderId, status=$status');
  }

  List<Map<String, dynamic>> getPendingStatusUpdates() {
    return List<Map<String, dynamic>>.from(pendingUpdates);
  }

  void deletePendingStatusUpdate(int id) {
    pendingUpdates.removeWhere((item) => item['id'] == id);
    _logger.d('[Web] Deleted pending status update from memory: id=$id');
  }

  void savePendingGpsPoint({
    required double latitude,
    required double longitude,
    required double accuracy,
    required String timestamp,
    required int createdAt,
  }) {
    pendingGpsPoints.add({
      'id': ++gpsSequence,
      'latitude': latitude,
      'longitude': longitude,
      'accuracy': accuracy,
      'timestamp': timestamp,
      'created_at': createdAt,
    });
    if (pendingGpsPoints.length > 10000) {
      pendingGpsPoints.removeRange(
        0,
        pendingGpsPoints.length - 10000,
      );
    }
  }

  List<Map<String, dynamic>> getPendingGpsPoints(int limit) {
    return List<Map<String, dynamic>>.from(pendingGpsPoints.take(limit));
  }

  int getPendingGpsPointCount() => pendingGpsPoints.length;

  void deletePendingGpsPoints(Iterable<int> ids) {
    final idSet = ids.toSet();
    pendingGpsPoints.removeWhere((item) => idSet.contains(item['id']));
  }

  void clearPendingGpsPoints() {
    pendingGpsPoints.clear();
  }

  void saveLocalErrorLog(String endpoint, String errorMessage, String payload) {
    errorLogs.add({
      'id': errorLogs.length + 1,
      'timestamp': DateTime.now().millisecondsSinceEpoch,
      'endpoint': endpoint,
      'error_message': errorMessage,
      'payload': payload,
    });
    _logger.d('[Web] Saved local error log to memory: endpoint=$endpoint');
  }

  List<Map<String, dynamic>> getLocalErrorLogs() {
    return List<Map<String, dynamic>>.from(errorLogs);
  }

  void clearLocalErrorLogs() {
    errorLogs.clear();
  }
}
