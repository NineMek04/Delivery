import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'app_theme.dart';
import 'app_router_shells.dart';
export 'app_router_shells.dart';
import '../core/auth/auth_constants.dart';
import '../core/auth/auth_service.dart';
import '../features/auth/screens/login_screen.dart';
import '../features/auth/screens/register_screen.dart';
import '../features/home/screens/home_screen.dart';
import '../features/home/providers/home_provider.dart';
import '../features/delivery/screens/active_delivery_screen.dart';
import '../features/delivery/screens/delivery_confirmation_screen.dart';
import '../features/delivery/screens/delivery_history_screen.dart';
import '../features/tracking/screens/map_tracking_screen.dart';
import '../features/profile/screens/profile_screen.dart';
import '../features/store/screens/store_home_screen.dart';
import '../features/store/screens/store_summary_screen.dart';
import '../features/store/screens/store_profile_screen.dart';
import '../features/store/screens/store_orders_screen.dart';
import '../features/store/providers/store_orders_provider.dart';

// Customer imports
import '../features/stores/store_list_screen.dart';
import '../features/stores/shop_details_screen.dart';
import '../features/orders/customer_orders_screen.dart';
import '../features/profile/screens/customer_profile_screen.dart';
import '../features/profile/screens/customer_addresses_screen.dart';
import '../features/profile/screens/customer_address_map_screen.dart';
import '../features/tracking/customer_tracking_screen.dart';
import '../features/settings/screens/server_settings_screen.dart';
import '../shared/widgets/error_dialog.dart';

/// Provider to notify the UI about unauthorized route access attempts.
final authAlertProvider = StateProvider<String?>((ref) => null);

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
      final isServerSettingsRoute = state.matchedLocation == '/server-settings';

      // Always permit accessing server settings regardless of login state or role
      if (isServerSettingsRoute) {
        return null;
      }

      final isGuestRoute = isLoginRoute || isRegisterRoute;
      
      final isStoreRoute = state.matchedLocation.startsWith('/store');
      final isCustomerRoute = state.matchedLocation.startsWith('/customer');
      final isRiderRoute = !isGuestRoute && !isStoreRoute && !isCustomerRoute;

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
        if (role == AuthConstants.roleCustomer) {
          return '/customer';
        }
        return '/';
      }

      // ── Role-based Access Control (Enforce strict separation) ──
      if (authState == AuthStatus.authenticated) {
        final role = authNotifier.userRole;

        // Check if role is allowed to access the target path
        bool isAllowed = true;
        String redirectTarget = '/';

        // 1. Customer Enforcement
        if (role == AuthConstants.roleCustomer) {
          if (!isCustomerRoute) {
            isAllowed = false;
            redirectTarget = '/customer';
          }
        }
        // 2. StorePartner Enforcement
        else if (role == AuthConstants.roleStorePartner) {
          if (!isStoreRoute) {
            isAllowed = false;
            redirectTarget = '/store';
          }
        }
        // 3. Rider Enforcement (Default)
        else {
          if (!isRiderRoute) {
            isAllowed = false;
            redirectTarget = '/';
          }
        }

        if (!isAllowed) {
          // Schedule the alert to be set on the next frame to avoid build-phase modification errors
          Future.microtask(() {
            ref.read(authAlertProvider.notifier).state = 'สิทธิ์นี้ไม่ได้รับอนุญาตให้เข้า';
          });
          return redirectTarget;
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
      GoRoute(
        path: '/server-settings',
        name: 'serverSettings',
        builder: (context, state) => const ServerSettingsScreen(),
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
            path: '/delivery/confirm/:orderId',
            name: 'confirmDelivery',
            builder: (context, state) {
              final orderId = state.pathParameters['orderId']!;
              return DeliveryConfirmationScreen(orderId: orderId);
            },
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
            path: '/delivery/tracking/:orderId',
            redirect: (context, state) => '/tracking',
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
            path: '/store/orders',
            name: 'storeOrders',
            builder: (context, state) => const StoreOrdersScreen(),
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

      // ── Customer Routes ──────────────────────────────────────────
      ShellRoute(
        builder: (context, state, child) => CustomerShell(child: child),
        routes: [
          GoRoute(
            path: '/customer',
            name: 'customerHome',
            builder: (context, state) => const StoreListScreen(),
          ),
          GoRoute(
            path: '/customer/shop/:shopId',
            name: 'customerShopDetails',
            builder: (context, state) {
              final shopId = state.pathParameters['shopId']!;
              return ShopDetailsScreen(shopId: shopId);
            },
          ),
          GoRoute(
            path: '/customer/orders',
            name: 'customerOrders',
            builder: (context, state) => const CustomerOrdersScreen(),
          ),
          GoRoute(
            path: '/customer/profile',
            name: 'customerProfile',
            builder: (context, state) => const CustomerProfileScreen(),
          ),
        ],
      ),
      // Tracking order for customer (Standalone)
      GoRoute(
        path: '/customer/tracking/:orderId',
        name: 'customerTracking',
        builder: (context, state) {
          final orderId = state.pathParameters['orderId']!;
          return CustomerTrackingScreen(orderId: orderId);
        },
      ),
      GoRoute(
        path: '/customer/addresses',
        name: 'customerAddresses',
        builder: (context, state) => const CustomerAddressesScreen(),
      ),
      GoRoute(
        path: '/customer/addresses/map',
        name: 'customerAddressMap',
        builder: (context, state) => const CustomerAddressMapScreen(),
      ),
    ],
  );
});


