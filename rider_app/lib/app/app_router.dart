import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../core/auth/auth_constants.dart';
import '../core/auth/auth_service.dart';
import '../features/auth/screens/login_screen.dart';
import '../features/auth/screens/register_screen.dart';
import '../features/home/screens/home_screen.dart';
import '../features/delivery/screens/active_delivery_screen.dart';
import '../features/delivery/screens/delivery_history_screen.dart';
import '../features/tracking/screens/map_tracking_screen.dart';
import '../features/profile/screens/profile_screen.dart';
import '../features/store/screens/store_home_screen.dart';
import '../features/store/screens/store_summary_screen.dart';
import '../features/store/screens/store_profile_screen.dart';

/// Re-run GoRouter redirect when [authServiceProvider] changes.
final _routerRefreshProvider = Provider<Listenable>((ref) {
  final notifier = ValueNotifier<int>(0);
  ref.listen(authServiceProvider, (_, __) {
    notifier.value++;
  });
  ref.onDispose(notifier.dispose);
  return notifier;
});

final appRouterProvider = Provider<GoRouter>((ref) {
  final refreshListenable = ref.watch(_routerRefreshProvider);

  return GoRouter(
    initialLocation: '/login',
    debugLogDiagnostics: true,
    refreshListenable: refreshListenable,
    redirect: (context, state) {
      final authState = ref.read(authServiceProvider);
      final authNotifier = ref.read(authServiceProvider.notifier);
      final isLoginRoute = state.matchedLocation == '/login';
      final isRegisterRoute = state.matchedLocation == '/register';
      final isGuestRoute = isLoginRoute || isRegisterRoute;
      final isStoreRoute = state.matchedLocation.startsWith('/store');
      final isRiderRoute = !isGuestRoute && !isStoreRoute;

      if (authState == AuthStatus.loading) {
        return isGuestRoute ? null : '/login';
      }
      if (authState != AuthStatus.authenticated && !isGuestRoute) {
        return '/login';
      }
      if (authState == AuthStatus.authenticated && isGuestRoute) {
        // Route based on role
        final role = authNotifier.userRole;
        if (role == AuthConstants.roleStorePartner) {
          return '/store';
        }
        return '/';
      }
      // Prevent StorePartner from accessing rider routes
      if (authState == AuthStatus.authenticated && isRiderRoute) {
        final role = authNotifier.userRole;
        if (role == AuthConstants.roleStorePartner) {
          return '/store';
        }
      }
      // Prevent non-StorePartner from accessing store routes
      if (authState == AuthStatus.authenticated && isStoreRoute) {
        final role = authNotifier.userRole;
        if (role != AuthConstants.roleStorePartner) {
          return '/';
        }
      }
      return null;
    },
    routes: [
      GoRoute(
        path: '/login',
        name: 'login',
        builder: (context, state) => const LoginScreen(),
      ),
      GoRoute(
        path: '/register',
        name: 'register',
        builder: (context, state) => const RegisterScreen(),
      ),

      // ── Rider Routes ─────────────────────────────────────────────
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
          GoRoute(
            path: '/profile',
            name: 'profile',
            builder: (context, state) => const ProfileScreen(),
          ),
        ],
      ),

      // ── Store Partner Routes ──────────────────────────────────────
      ShellRoute(
        builder: (context, state, child) => StoreShell(child: child),
        routes: [
          GoRoute(
            path: '/store',
            name: 'storeHome',
            builder: (context, state) => const StoreHomeScreen(),
          ),
          GoRoute(
            path: '/store/summary',
            name: 'storeSummary',
            builder: (context, state) => const StoreSummaryScreen(),
          ),
          GoRoute(
            path: '/store/profile',
            name: 'storeProfile',
            builder: (context, state) => const StoreProfileScreen(),
          ),
        ],
      ),
    ],
  );
});

// ═══════════════════════════════════════════════════════════════════
// Main Shell — Rider Bottom Navigation
// ═══════════════════════════════════════════════════════════════════
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
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'โปรไฟล์',
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
// Store Shell — StorePartner Bottom Navigation (3 tabs)
// ═══════════════════════════════════════════════════════════════════
class StoreShell extends StatelessWidget {
  final Widget child;

  const StoreShell({super.key, required this.child});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: child,
      bottomNavigationBar: NavigationBar(
        selectedIndex: _calculateSelectedIndex(context),
        onDestinationSelected: (index) => _onItemTapped(index, context),
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.storefront_outlined),
            selectedIcon: Icon(Icons.storefront),
            label: 'ร้านค้า',
          ),
          NavigationDestination(
            icon: Icon(Icons.analytics_outlined),
            selectedIcon: Icon(Icons.analytics),
            label: 'สรุป',
          ),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'โปรไฟล์',
          ),
        ],
      ),
    );
  }

  int _calculateSelectedIndex(BuildContext context) {
    final location = GoRouterState.of(context).matchedLocation;
    if (location == '/store') return 0;
    if (location == '/store/summary') return 1;
    if (location == '/store/profile') return 2;
    return 0;
  }

  void _onItemTapped(int index, BuildContext context) {
    switch (index) {
      case 0:
        context.goNamed('storeHome');
      case 1:
        context.goNamed('storeSummary');
      case 2:
        context.goNamed('storeProfile');
    }
  }
}
