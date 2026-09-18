import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'app_theme.dart';
import '../features/home/providers/home_provider.dart';
import '../features/store/providers/store_orders_provider.dart';
import '../shared/widgets/error_dialog.dart';
import 'app_router.dart';

// ═══════════════════════════════════════════════════════════════════
// Main Shell — Rider Bottom Navigation
// ═══════════════════════════════════════════════════════════════════
class MainShell extends ConsumerWidget {
  final Widget child;

  const MainShell({super.key, required this.child});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // Listen for auth alerts (unauthorized access attempts)
    ref.listen<String?>(authAlertProvider, (previous, next) {
      if (next != null) {
        ref.read(authAlertProvider.notifier).state = null;
        ErrorDialog.show(
          context,
          title: 'การเข้าถึงถูกปฏิเสธ',
          message: next,
        );
      }
    });

    final homeState = ref.watch(homeNotifierProvider);
    final hasOffer = homeState.incomingOffer != null;

    return Theme(
      data: AppTheme.darkTheme, // Riders prefer Dark Mode (customized green)
      child: Scaffold(
        body: child,
        bottomNavigationBar: NavigationBar(
          selectedIndex: _calculateSelectedIndex(context),
          onDestinationSelected: (index) => _onItemTapped(index, context),
          destinations: [
            const NavigationDestination(
              icon: Icon(Icons.home_outlined),
              selectedIcon: Icon(Icons.home),
              label: 'หน้าหลัก',
            ),
            NavigationDestination(
              icon: hasOffer
                  ? const Badge(
                      backgroundColor: Colors.red,
                      label: Text(
                        '!',
                        style: TextStyle(
                          color: Colors.white,
                          fontWeight: FontWeight.bold,
                          fontSize: 10,
                        ),
                      ),
                      child: Icon(Icons.delivery_dining_outlined),
                    )
                  : const Icon(Icons.delivery_dining_outlined),
              selectedIcon: hasOffer
                  ? const Badge(
                      backgroundColor: Colors.red,
                      label: Text(
                        '!',
                        style: TextStyle(
                          color: Colors.white,
                          fontWeight: FontWeight.bold,
                          fontSize: 10,
                        ),
                      ),
                      child: Icon(Icons.delivery_dining),
                    )
                  : const Icon(Icons.delivery_dining),
              label: 'งานส่ง',
            ),
            const NavigationDestination(
              icon: Icon(Icons.map_outlined),
              selectedIcon: Icon(Icons.map),
              label: 'แผนที่',
            ),
            const NavigationDestination(
              icon: Icon(Icons.history_outlined),
              selectedIcon: Icon(Icons.history),
              label: 'ประวัติ',
            ),
            const NavigationDestination(
              icon: Icon(Icons.person_outline),
              selectedIcon: Icon(Icons.person),
              label: 'โปรไฟล์',
            ),
          ],
        ),
      ),
    );
  }

  int _calculateSelectedIndex(BuildContext context) {
    final location = GoRouterState.of(context).matchedLocation;
    if (location == '/') return 0;
    if (location == '/delivery/active' || location.startsWith('/delivery/confirm')) return 1;
    if (location == '/tracking' || location.startsWith('/delivery/tracking')) return 2;
    if (location == '/delivery/history') return 3;
    if (location == '/profile') return 4;
    return 0;
  }

  void _onItemTapped(int index, BuildContext context) {
    switch (index) {
      case 0:
        context.goNamed('home');
      case 1:
        context.goNamed('activeDelivery');
      case 2:
        context.goNamed('tracking');
      case 3:
        context.goNamed('deliveryHistory');
      case 4:
        context.goNamed('profile');
    }
  }
}

// ═══════════════════════════════════════════════════════════════════
// Store Shell — StorePartner Bottom Navigation
// ═══════════════════════════════════════════════════════════════════
class StoreShell extends ConsumerWidget {
  final Widget child;

  const StoreShell({super.key, required this.child});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // Listen for auth alerts (unauthorized access attempts)
    ref.listen<String?>(authAlertProvider, (previous, next) {
      if (next != null) {
        ref.read(authAlertProvider.notifier).state = null;
        ErrorDialog.show(
          context,
          title: 'การเข้าถึงถูกปฏิเสธ',
          message: next,
        );
      }
    });

    // Listen for new orders to trigger a floating SnackBar notification
    ref.listen<StoreOrdersState>(storeOrdersProvider, (previous, next) {
      final prevCount = previous?.newOrderBadgeCount ?? 0;
      if (next.newOrderBadgeCount > prevCount) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Row(
              children: [
                Icon(Icons.notifications_active, color: Colors.white),
                SizedBox(width: 12),
                Expanded(
                  child: Text(
                    '🔔 มีออเดอร์ใหม่เข้ามา! กรุณาตรวจสอบและกดยืนยันการทำงาน',
                    style: TextStyle(fontWeight: FontWeight.bold),
                  ),
                ),
              ],
            ),
            backgroundColor: Colors.red,
            duration: Duration(seconds: 6),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    });

    final ordersState = ref.watch(storeOrdersProvider);
    final badgeCount = ordersState.newOrderBadgeCount;

    return Theme(
      data: AppTheme.darkTheme,
      child: Scaffold(
        body: child,
        bottomNavigationBar: NavigationBar(
          selectedIndex: _calculateSelectedIndex(context),
          onDestinationSelected: (index) => _onItemTapped(index, context),
          destinations: [
            const NavigationDestination(
              icon: Icon(Icons.storefront_outlined),
              selectedIcon: Icon(Icons.storefront),
              label: 'ร้านค้า',
            ),
            NavigationDestination(
              icon: badgeCount > 0
                  ? Badge(
                      backgroundColor: Colors.red,
                      label: Text(
                        '$badgeCount',
                        style: const TextStyle(
                          color: Colors.white,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      child: const Icon(Icons.receipt_long_outlined),
                    )
                  : const Icon(Icons.receipt_long_outlined),
              selectedIcon: const Icon(Icons.receipt_long),
              label: 'ออเดอร์',
            ),
            const NavigationDestination(
              icon: Icon(Icons.analytics_outlined),
              selectedIcon: Icon(Icons.analytics),
              label: 'สรุป',
            ),
            const NavigationDestination(
              icon: Icon(Icons.person_outline),
              selectedIcon: Icon(Icons.person),
              label: 'โปรไฟล์',
            ),
          ],
        ),
      ),
    );
  }

  int _calculateSelectedIndex(BuildContext context) {
    final location = GoRouterState.of(context).matchedLocation;
    if (location == '/store') return 0;
    if (location == '/store/orders') return 1;
    if (location == '/store/summary') return 2;
    if (location == '/store/profile') return 3;
    return 0;
  }

  void _onItemTapped(int index, BuildContext context) {
    switch (index) {
      case 0:
        context.goNamed('storeHome');
      case 1:
        context.goNamed('storeOrders');
      case 2:
        context.goNamed('storeSummary');
      case 3:
        context.goNamed('storeProfile');
    }
  }
}

// ═══════════════════════════════════════════════════════════════════
// Customer Shell — Customer Bottom Navigation
// ═══════════════════════════════════════════════════════════════════
class CustomerShell extends ConsumerWidget {
  final Widget child;

  const CustomerShell({super.key, required this.child});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // Listen for auth alerts (unauthorized access attempts)
    ref.listen<String?>(authAlertProvider, (previous, next) {
      if (next != null) {
        ref.read(authAlertProvider.notifier).state = null;
        ErrorDialog.show(
          context,
          title: 'การเข้าถึงถูกปฏิเสธ',
          message: next,
        );
      }
    });

    return Theme(
      data: AppTheme.lightTheme, // Customers prefer Light Mode (customized green)
      child: Scaffold(
        body: child,
        bottomNavigationBar: NavigationBar(
          selectedIndex: _calculateSelectedIndex(context),
          onDestinationSelected: (index) => _onItemTapped(index, context),
          destinations: const [
            NavigationDestination(
              icon: Icon(Icons.restaurant_outlined),
              selectedIcon: Icon(Icons.restaurant),
              label: 'ร้านอาหาร',
            ),
            NavigationDestination(
              icon: Icon(Icons.receipt_long_outlined),
              selectedIcon: Icon(Icons.receipt_long),
              label: 'ออเดอร์',
            ),
            NavigationDestination(
              icon: Icon(Icons.person_outline),
              selectedIcon: Icon(Icons.person),
              label: 'โปรไฟล์',
            ),
          ],
        ),
      ),
    );
  }

  int _calculateSelectedIndex(BuildContext context) {
    final location = GoRouterState.of(context).matchedLocation;
    if (location == '/customer') return 0;
    if (location == '/customer/orders') return 1;
    if (location == '/customer/profile') return 2;
    return 0;
  }

  void _onItemTapped(int index, BuildContext context) {
    switch (index) {
      case 0:
        context.goNamed('customerHome');
      case 1:
        context.goNamed('customerOrders');
      case 2:
        context.goNamed('customerProfile');
    }
  }
}
