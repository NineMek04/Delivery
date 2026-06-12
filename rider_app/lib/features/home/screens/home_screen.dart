import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
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
        try {
          await ref.read(homeNotifierProvider.notifier).acceptOffer();
          if (mounted) {
            ErrorDialog.showSuccess(context, 'รับงานแล้ว');
            context.goNamed('activeDelivery');
          }
        } catch (e) {
          if (mounted) {
            await ErrorDialog.show(
              context,
              title: 'รับงานไม่สำเร็จ',
              message: e.toString(),
            );
          }
          rethrow;
        }
      },
      onReject: () async {
        try {
          await ref.read(homeNotifierProvider.notifier).rejectOffer();
        } catch (e) {
          if (mounted) {
            await ErrorDialog.show(
              context,
              title: 'ปฏิเสธงานไม่สำเร็จ',
              message: e.toString(),
            );
          }
          rethrow;
        }
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

    return Scaffold(
      appBar: AppBar(
        title: const Text('หน้าหลัก'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: () {
              HapticFeedback.lightImpact();
              ref.read(homeNotifierProvider.notifier).loadDashboard();
              ref.read(deliveryNotifierProvider.notifier).loadOrders();
            },
          ),
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: () {
              HapticFeedback.lightImpact();
              _logout();
            },
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
                      // ── Rider Profile Card ─────────────────────────────
                      Card(
                        elevation: 0,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(16),
                          side: const BorderSide(color: AppTheme.surfaceElevated),
                        ),
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Row(
                            children: [
                              CircleAvatar(
                                radius: 28,
                                backgroundColor: Theme.of(context).colorScheme.primary.withValues(alpha: 0.15),
                                child: Text(
                                  home.user?.fullName != null && home.user!.fullName.trim().isNotEmpty
                                      ? home.user!.fullName[0].toUpperCase()
                                      : 'R',
                                  style: TextStyle(
                                    color: Theme.of(context).colorScheme.primary,
                                    fontWeight: FontWeight.bold,
                                    fontSize: 20,
                                  ),
                                ),
                              ),
                              const SizedBox(width: 16),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      'สวัสดี, ${home.user?.fullName ?? 'Rider'}',
                                      style: Theme.of(context).textTheme.titleLarge?.copyWith(
                                            fontWeight: FontWeight.bold,
                                          ),
                                    ),
                                    const SizedBox(height: 4),
                                    Text(
                                      home.user?.email ?? 'rider@smartrouting.com',
                                      style: const TextStyle(
                                        color: AppTheme.textMuted,
                                        fontSize: 13,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),

                      // ── Online/Offline Status Switch Card ──────────────
                      Card(
                        elevation: 0,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(16),
                          side: const BorderSide(color: AppTheme.surfaceElevated),
                        ),
                        child: SwitchListTile.adaptive(
                          title: const Text(
                            'สถานะการรับงาน',
                            style: TextStyle(fontWeight: FontWeight.w600),
                          ),
                          subtitle: Text(
                            home.isOnline ? 'ออนไลน์ — พร้อมรับงานส่ง' : 'ออฟไลน์ — หยุดรับงานชั่วคราว',
                            style: TextStyle(
                              color: home.isOnline ? AppTheme.accentColor : AppTheme.textMuted,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          value: home.isOnline,
                          activeColor: AppTheme.accentColor,
                          secondary: Icon(
                            home.isOnline ? Icons.circle : Icons.circle_outlined,
                            color: home.isOnline ? AppTheme.accentColor : AppTheme.textMuted,
                          ),
                          onChanged: home.isTransitioning
                              ? null
                              : (v) async {
                                  HapticFeedback.mediumImpact();
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
                          _stat(context, 'งานที่ได้รับ', '${home.assignedOrderCount}', Icons.assignment, Colors.blue),
                          const SizedBox(width: 8),
                          _stat(context, 'ส่งสำเร็จ', '${home.completedOrderCount}', Icons.check_circle, Colors.green),
                          const SizedBox(width: 8),
                          _stat(context, 'รายได้', '฿450', Icons.account_balance_wallet, Colors.orange),
                        ],
                      ),
                      const SizedBox(height: 24),
                      if (delivery.activeOrder != null) ...[
                        Text('งานปัจจุบัน', style: Theme.of(context).textTheme.titleMedium),
                        const SizedBox(height: 8),
                        Card(
                          elevation: 2,
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                          child: ListTile(
                            contentPadding: const EdgeInsets.all(16),
                            leading: Container(
                              padding: const EdgeInsets.all(12),
                              decoration: BoxDecoration(color: Colors.blueAccent.withOpacity(0.1), shape: BoxShape.circle),
                              child: const Icon(Icons.local_shipping, color: Colors.blueAccent),
                            ),
                            title: Text(
                              delivery.activeOrder!.trackingCode ?? 'Order',
                              style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16),
                            ),
                            subtitle: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                const SizedBox(height: 4),
                                Text(delivery.activeOrder!.status, style: TextStyle(color: Colors.grey[600])),
                              ],
                            ),
                            trailing: const Icon(Icons.chevron_right),
                            onTap: () {
                              HapticFeedback.lightImpact();
                              // context.goNamed('route_tracking', pathParameters: {'id': delivery.activeOrder!.id});
                              context.goNamed('activeDelivery');
                            },
                          ),
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

  Widget _stat(BuildContext context, String label, String value, IconData icon, Color color) {
    return Expanded(
      child: Card(
        elevation: 1,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 8),
          child: Column(
            children: [
              Icon(icon, color: color, size: 28),
              const SizedBox(height: 8),
              Text(value, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
              const SizedBox(height: 4),
              Text(label, style: Theme.of(context).textTheme.bodySmall, textAlign: TextAlign.center),
            ],
          ),
        ),
      ),
    );
  }
}
