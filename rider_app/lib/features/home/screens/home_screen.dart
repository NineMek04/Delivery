import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/app_theme.dart';
import '../../../core/config/app_constants.dart';
import '../../../models/dispatch_offer.dart';
import '../../../core/signalr/signalr_service.dart';
import '../../../features/auth/providers/auth_provider.dart';
import '../../../features/delivery/providers/delivery_provider.dart';
import '../../../shared/widgets/connection_status_bar.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../../../shared/widgets/loading_overlay.dart';
import '../../../shared/widgets/offer_bottom_sheet.dart';
import '../providers/home_provider.dart';

/// Home — dashboard, online toggle, incoming offers.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  DispatchOffer? _shownOffer;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(homeNotifierProvider.notifier).loadDashboard();
      ref.read(deliveryNotifierProvider.notifier).loadOrders();
    });
  }

  void _maybeShowOffer(HomeState home) {
    final offer = home.incomingOffer;
    if (offer == null) {
      _shownOffer = null;
      return;
    }
    if (_shownOffer?.offerId == offer.offerId) return;
    _shownOffer = offer;

    OfferBottomSheet.show(
      context,
      offer: offer,
      onAccept: () async {
        await ref.read(homeNotifierProvider.notifier).acceptOffer();
        if (mounted) {
          ErrorDialog.showSuccess(context, 'รับงานแล้ว');
          context.goNamed('activeDelivery');
        }
      },
      onReject: () {
        ref.read(homeNotifierProvider.notifier).rejectOffer();
      },
    );
  }

  Future<void> _logout() async {
    final ok = await ErrorDialog.showConfirm(
      context,
      title: 'ออกจากระบบ',
      message: 'ต้องการออกจากระบบใช่หรือไม่?',
      confirmText: 'ออกจากระบบ',
    );
    if (ok == true) {
      if (ref.read(homeNotifierProvider).isOnline) {
        await ref.read(homeNotifierProvider.notifier).setOnline(false);
      }
      await ref.read(authNotifierProvider.notifier).logout();
    }
  }

  @override
  Widget build(BuildContext context) {
    final home = ref.watch(homeNotifierProvider);
    final signalR = ref.watch(signalRServiceProvider);
    final delivery = ref.watch(deliveryNotifierProvider);

    ref.listen(homeNotifierProvider, (_, next) => _maybeShowOffer(next));

    final riderLabel = home.isOnline
        ? AppConstants.statusAvailable
        : AppConstants.statusOffline;
    final riderColor = home.isOnline
        ? AppTheme.accentColor
        : AppTheme.textMuted;

    return Scaffold(
      appBar: AppBar(
        title: const Text('หน้าหลัก'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: () {
              ref.read(homeNotifierProvider.notifier).loadDashboard();
              ref.read(deliveryNotifierProvider.notifier).loadOrders();
            },
          ),
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: _logout,
          ),
        ],
      ),
      body: Stack(
        children: [
          Column(
            children: [
              ConnectionStatusBar(
                signalRState: signalR,
                isGpsTracking: home.isOnline,
                isOnline: home.isOnline,
              ),
              Expanded(
                child: RefreshIndicator(
                  onRefresh: () async {
                    await ref.read(homeNotifierProvider.notifier).loadDashboard();
                    await ref.read(deliveryNotifierProvider.notifier).loadOrders();
                  },
                  child: ListView(
                    padding: const EdgeInsets.all(16),
                    children: [
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(20),
                          child: Row(
                            children: [
                              CircleAvatar(
                                radius: 28,
                                backgroundColor: Theme.of(context).colorScheme.primary,
                                child: const Icon(Icons.person, color: Colors.white),
                              ),
                              const SizedBox(width: 16),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      'สวัสดี, ${home.user?.fullName ?? 'Rider'}',
                                      style: Theme.of(context).textTheme.titleLarge,
                                    ),
                                    const SizedBox(height: 4),
                                    Container(
                                      padding: const EdgeInsets.symmetric(
                                        horizontal: 12,
                                        vertical: 4,
                                      ),
                                      decoration: BoxDecoration(
                                        color: riderColor.withValues(alpha: 0.2),
                                        borderRadius: BorderRadius.circular(12),
                                      ),
                                      child: Text(
                                        '● $riderLabel',
                                        style: TextStyle(color: riderColor, fontSize: 12),
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                              Switch(
                                value: home.isOnline,
                                onChanged: home.isTransitioning
                                    ? null
                                    : (v) async {
                                        try {
                                          await ref
                                              .read(homeNotifierProvider.notifier)
                                              .setOnline(v);
                                        } catch (e) {
                                          if (context.mounted) {
                                            ErrorDialog.show(
                                              context,
                                              title: 'ไม่สามารถเปลี่ยนสถานะ',
                                              message: e.toString(),
                                            );
                                          }
                                        }
                                      },
                              ),
                            ],
                          ),
                        ),
                      ),
                      if (home.sessionError != null) ...[
                        const SizedBox(height: 8),
                        Text(
                          home.sessionError!,
                          style: TextStyle(color: Theme.of(context).colorScheme.error),
                        ),
                      ],
                      const SizedBox(height: 16),
                      Text('สรุปวันนี้', style: Theme.of(context).textTheme.titleMedium),
                      const SizedBox(height: 12),
                      Row(
                        children: [
                          _stat(context, 'งานที่ได้รับ', '${home.assignedOrderCount}', Icons.assignment),
                          const SizedBox(width: 12),
                          _stat(context, 'ส่งสำเร็จ', '${home.completedOrderCount}', Icons.check_circle),
                          const SizedBox(width: 12),
                          _stat(
                            context,
                            'ระยะทาง',
                            '${home.totalDistanceKm.toStringAsFixed(1)} km',
                            Icons.route,
                          ),
                        ],
                      ),
                      const SizedBox(height: 24),
                      if (delivery.activeOrder != null) ...[
                        Text('งานปัจจุบัน', style: Theme.of(context).textTheme.titleMedium),
                        const SizedBox(height: 8),
                        ListTile(
                          leading: const Icon(Icons.local_shipping),
                          title: Text(delivery.activeOrder!.trackingCode ?? 'Order'),
                          subtitle: Text(delivery.activeOrder!.status),
                          trailing: const Icon(Icons.chevron_right),
                          onTap: () => context.goNamed('activeDelivery'),
                        ),
                      ] else ...[
                        const SizedBox(height: 32),
                        Center(
                          child: Column(
                            children: [
                              Icon(
                                Icons.delivery_dining,
                                size: 64,
                                color: Theme.of(context).colorScheme.primary.withValues(alpha: 0.5),
                              ),
                              const SizedBox(height: 16),
                              Text(
                                home.isOnline ? 'รอรับงานจากระบบ...' : 'เปิดสวิตช์ออนไลน์เพื่อรับงาน',
                                style: Theme.of(context).textTheme.bodyLarge,
                              ),
                            ],
                          ),
                        ),
                      ],
                      const SizedBox(height: 16),
                      OutlinedButton.icon(
                        onPressed: () => context.goNamed('tracking'),
                        icon: const Icon(Icons.map),
                        label: const Text('เปิดแผนที่'),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
          if (home.isLoading) const LoadingOverlay(message: 'กำลังโหลด...'),
        ],
      ),
    );
  }

  Widget _stat(BuildContext context, String label, String value, IconData icon) {
    return Expanded(
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            children: [
              Icon(icon, color: Theme.of(context).colorScheme.primary),
              const SizedBox(height: 8),
              Text(value, style: Theme.of(context).textTheme.titleLarge),
              Text(label, style: Theme.of(context).textTheme.bodySmall, textAlign: TextAlign.center),
            ],
          ),
        ),
      ),
    );
  }
}
