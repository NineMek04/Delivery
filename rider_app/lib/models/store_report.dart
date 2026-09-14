import '../core/api/api_helpers.dart';

class StoreTopItemDto {
  final String menuItemId;
  final String name;
  final int quantity;
  final double revenue;

  const StoreTopItemDto({
    required this.menuItemId,
    required this.name,
    required this.quantity,
    required this.revenue,
  });

  factory StoreTopItemDto.fromJson(Map<String, dynamic> json) {
    return StoreTopItemDto(
      menuItemId: readField<String>(json, 'MenuItemId') ??
          readField<String>(json, 'menuItemId') ??
          '',
      name: readField<String>(json, 'Name') ??
          readField<String>(json, 'name') ??
          '',
      quantity: readField<int>(json, 'Quantity') ??
          readField<int>(json, 'quantity') ??
          0,
      revenue: (readField<num>(json, 'Revenue') ??
              readField<num>(json, 'revenue') ??
              readField<num>(json, 'TotalAmount') ??
              readField<num>(json, 'totalAmount') ??
              0)
          .toDouble(),
    );
  }
}

class StoreOrderDetailDto {
  final String orderId;
  final String trackingNumber;
  final DateTime? createdAt;
  final String status;
  final double totalAmount;
  final int itemCount;
  final String? riderName;

  const StoreOrderDetailDto({
    required this.orderId,
    required this.trackingNumber,
    this.createdAt,
    required this.status,
    required this.totalAmount,
    required this.itemCount,
    this.riderName,
  });

  factory StoreOrderDetailDto.fromJson(Map<String, dynamic> json) {
    final rawDate = readField<String>(json, 'CreatedAt') ??
        readField<String>(json, 'createdAt');
    return StoreOrderDetailDto(
      orderId: readField<String>(json, 'OrderId') ??
          readField<String>(json, 'orderId') ??
          '',
      trackingNumber: readField<String>(json, 'TrackingNumber') ??
          readField<String>(json, 'trackingNumber') ??
          (readField<num>(json, 'RefNumber') != null
              ? '${readField<num>(json, 'RefNumber')}'
              : (readField<num>(json, 'refNumber') != null
                  ? '${readField<num>(json, 'refNumber')}'
                  : '')),
      createdAt: rawDate != null ? DateTime.tryParse(rawDate) : null,
      status: readField<String>(json, 'Status') ??
          readField<String>(json, 'status') ??
          '',
      totalAmount: (readField<num>(json, 'TotalAmount') ??
              readField<num>(json, 'totalAmount') ??
              readField<num>(json, 'OrderTotal') ??
              readField<num>(json, 'orderTotal') ??
              0)
          .toDouble(),
      itemCount: readField<int>(json, 'ItemCount') ??
          readField<int>(json, 'itemCount') ??
          0,
      riderName: readField<String>(json, 'RiderName') ??
          readField<String>(json, 'riderName') ??
          readField<String>(json, 'CustomerName') ??
          readField<String>(json, 'customerName'),
    );
  }
}

class StoreReportSummaryDto {
  final String period;
  final DateTime? startDate;
  final DateTime? endDate;
  final int totalOrders;
  final int completedOrders;
  final int cancelledOrders;
  final double totalRevenue;
  final double averageOrderValue;
  final List<StoreTopItemDto> topItems;
  final List<StoreOrderDetailDto> orders;

  const StoreReportSummaryDto({
    required this.period,
    this.startDate,
    this.endDate,
    this.totalOrders = 0,
    this.completedOrders = 0,
    this.cancelledOrders = 0,
    this.totalRevenue = 0.0,
    this.averageOrderValue = 0.0,
    this.topItems = const [],
    this.orders = const [],
  });

  factory StoreReportSummaryDto.fromJson(Map<String, dynamic> json) {
    final rawStart = readField<String>(json, 'StartDate') ??
        readField<String>(json, 'startDate');
    final rawEnd = readField<String>(json, 'EndDate') ??
        readField<String>(json, 'endDate');

    final rawTop = json['topItems'] ?? json['TopItems'];
    final topList = (rawTop is List)
        ? rawTop.map((e) => StoreTopItemDto.fromJson(asMap(e))).toList()
        : <StoreTopItemDto>[];

    final rawOrders = json['orders'] ?? json['Orders'];
    final orderList = (rawOrders is List)
        ? rawOrders.map((e) => StoreOrderDetailDto.fromJson(asMap(e))).toList()
        : <StoreOrderDetailDto>[];

    return StoreReportSummaryDto(
      period: readField<String>(json, 'Period') ??
          readField<String>(json, 'period') ??
          'day',
      startDate: rawStart != null ? DateTime.tryParse(rawStart) : null,
      endDate: rawEnd != null ? DateTime.tryParse(rawEnd) : null,
      totalOrders: readField<int>(json, 'TotalOrders') ??
          readField<int>(json, 'totalOrders') ??
          0,
      completedOrders: readField<int>(json, 'CompletedOrders') ??
          readField<int>(json, 'completedOrders') ??
          0,
      cancelledOrders: readField<int>(json, 'CancelledOrders') ??
          readField<int>(json, 'cancelledOrders') ??
          0,
      totalRevenue: (readField<num>(json, 'TotalRevenue') ??
              readField<num>(json, 'totalRevenue') ??
              0)
          .toDouble(),
      averageOrderValue: (readField<num>(json, 'AverageOrderValue') ??
              readField<num>(json, 'averageOrderValue') ??
              0)
          .toDouble(),
      topItems: topList,
      orders: orderList,
    );
  }
}
