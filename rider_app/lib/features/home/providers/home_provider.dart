import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'home_provider.g.dart';

/// Home Provider — state สำหรับหน้า Home Dashboard.
///
/// TODO: Fetch dashboard data จาก BackendApi:
/// - จำนวน orders ที่ assign ให้ rider
/// - สรุปสถิติวันนี้ (ส่งสำเร็จ, ระยะทาง)
/// - Rider status
@riverpod
class HomeNotifier extends _$HomeNotifier {
  @override
  HomeState build() {
    return const HomeState();
  }

  /// โหลดข้อมูล dashboard.
  Future<void> loadDashboard() async {
    state = state.copyWith(isLoading: true);

    try {
      // TODO: Call BackendApi
      // final orders = await dio.get('/orders?assignedRiderId=...');
      state = state.copyWith(isLoading: false);
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }
}

/// Home dashboard state.
class HomeState {
  final bool isLoading;
  final String? error;
  final int assignedOrderCount;
  final int completedOrderCount;
  final double totalDistanceKm;

  const HomeState({
    this.isLoading = false,
    this.error,
    this.assignedOrderCount = 0,
    this.completedOrderCount = 0,
    this.totalDistanceKm = 0.0,
  });

  HomeState copyWith({
    bool? isLoading,
    String? error,
    int? assignedOrderCount,
    int? completedOrderCount,
    double? totalDistanceKm,
  }) {
    return HomeState(
      isLoading: isLoading ?? this.isLoading,
      error: error,
      assignedOrderCount: assignedOrderCount ?? this.assignedOrderCount,
      completedOrderCount: completedOrderCount ?? this.completedOrderCount,
      totalDistanceKm: totalDistanceKm ?? this.totalDistanceKm,
    );
  }
}
