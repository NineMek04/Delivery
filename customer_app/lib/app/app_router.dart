import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../core/auth/auth_service.dart';
import '../features/auth/screens/login_screen.dart';
import '../features/auth/screens/register_screen.dart';
import '../features/stores/store_list_screen.dart';

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
    initialLocation: '/',
    debugLogDiagnostics: true,
    refreshListenable: refreshListenable,
    redirect: (context, state) {
      final authState = ref.read(authServiceProvider);
      final isLoginRoute = state.matchedLocation == '/login';
      final isRegisterRoute = state.matchedLocation == '/register';
      final isGuestRoute = isLoginRoute || isRegisterRoute;

      if (authState == AuthStatus.loading) {
        return isGuestRoute ? null : '/login';
      }
      if (authState != AuthStatus.authenticated && !isGuestRoute) {
        return '/login';
      }
      if (authState == AuthStatus.authenticated && isGuestRoute) {
        return '/';
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

      // ── Customer Routes ──────────────────────────────────────────
      ShellRoute(
        builder: (context, state, child) => MainShell(child: child),
        routes: [
          GoRoute(
            path: '/',
            name: 'home',
            builder: (context, state) => const StoreListScreen(),
          ),
          GoRoute(
            path: '/orders',
            name: 'orders',
            builder: (context, state) => const Scaffold(body: Center(child: Text('ออเดอร์ของฉัน'))),
          ),
          GoRoute(
            path: '/profile',
            name: 'profile',
            builder: (context, state) => const Scaffold(body: Center(child: Text('โปรไฟล์'))),
          ),
        ],
      ),
    ],
  );
});

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
    );
  }

  int _calculateSelectedIndex(BuildContext context) {
    final location = GoRouterState.of(context).matchedLocation;
    if (location == '/') return 0;
    if (location == '/orders') return 1;
    if (location == '/profile') return 2;
    return 0;
  }

  void _onItemTapped(int index, BuildContext context) {
    switch (index) {
      case 0:
        context.goNamed('home');
      case 1:
        context.goNamed('orders');
      case 2:
        context.goNamed('profile');
    }
  }
}
