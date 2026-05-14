import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../core/auth/auth_service.dart';
import '../features/auth/screens/login_screen.dart';
import '../features/home/screens/home_screen.dart';
import '../features/delivery/screens/active_delivery_screen.dart';
import '../features/delivery/screens/delivery_history_screen.dart';
import '../features/tracking/screens/map_tracking_screen.dart';

part 'app_router.g.dart';

/// App Router — Declarative routing ด้วย go_router.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/app/app.routes.ts`
///
/// Route structure:
/// - `/login` — หน้า Login (unauthenticated only)
/// - `/` — Home Dashboard
/// - `/delivery/active` — Active Delivery
/// - `/delivery/history` — Delivery History
/// - `/tracking` — Map Tracking (real-time GPS)
@riverpod
GoRouter appRouter(Ref ref) {
  final authState = ref.watch(authServiceProvider);

  return GoRouter(
    initialLocation: '/',
    debugLogDiagnostics: true,

    // ── Redirect Logic (Auth Guard) ──────────────────────────────────
    redirect: (context, state) {
      final isLoggedIn = authState == AuthStatus.authenticated;
      final isLoading = authState == AuthStatus.loading;
      final isLoginRoute = state.matchedLocation == '/login';

      // ยังโหลดอยู่ → อย่า redirect
      if (isLoading) return null;

      // ยังไม่ login + ไม่ได้อยู่หน้า login → ไปหน้า login
      if (!isLoggedIn && !isLoginRoute) return '/login';

      // login แล้ว + อยู่หน้า login → ไปหน้า home
      if (isLoggedIn && isLoginRoute) return '/';

      return null;
    },

    // ── Routes ───────────────────────────────────────────────────────
    routes: [
      GoRoute(
        path: '/login',
        name: 'login',
        builder: (context, state) => const LoginScreen(),
      ),

      // ── Main App Shell (with bottom navigation) ────────────────────
      ShellRoute(
        builder: (context, state, child) => MainShell(child: child),
        routes: [
          GoRoute(
            path: '/',
            name: 'home',
            builder: (context, state) => const HomeScreen(),
          ),
          GoRoute(
            path: '/delivery/active',
            name: 'activeDelivery',
            builder: (context, state) => const ActiveDeliveryScreen(),
          ),
          GoRoute(
            path: '/delivery/history',
            name: 'deliveryHistory',
            builder: (context, state) => const DeliveryHistoryScreen(),
          ),
          GoRoute(
            path: '/tracking',
            name: 'tracking',
            builder: (context, state) => const MapTrackingScreen(),
          ),
        ],
      ),
    ],
  );
}

/// Main Shell — Bottom Navigation wrapper.
///
/// ครอบ child routes ด้วย bottom navigation bar.
class MainShell extends StatelessWidget {
  final Widget child;

  const MainShell({super.key, required this.child});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: child,
      bottomNavigationBar: NavigationBar(
        selectedIndex: _calculateSelectedIndex(context),
        onDestinationSelected: (index) => _onItemTapped(index, context),
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home),
            label: 'หน้าหลัก',
          ),
          NavigationDestination(
            icon: Icon(Icons.delivery_dining_outlined),
            selectedIcon: Icon(Icons.delivery_dining),
            label: 'งานส่ง',
          ),
          NavigationDestination(
            icon: Icon(Icons.map_outlined),
            selectedIcon: Icon(Icons.map),
            label: 'แผนที่',
          ),
          NavigationDestination(
            icon: Icon(Icons.history_outlined),
            selectedIcon: Icon(Icons.history),
            label: 'ประวัติ',
          ),
        ],
      ),
    );
  }

  int _calculateSelectedIndex(BuildContext context) {
    final location = GoRouterState.of(context).matchedLocation;
    if (location == '/') return 0;
    if (location == '/delivery/active') return 1;
    if (location == '/tracking') return 2;
    if (location == '/delivery/history') return 3;
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
    }
  }
}
