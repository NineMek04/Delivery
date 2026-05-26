import 'dart:async';
import 'dart:io';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';

import '../api/services/notifications_api_service.dart';
import '../auth/auth_service.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Background message handler for FCM.
/// Must be a top-level function.
@pragma('vm:entry-point')
Future<void> _firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  // Initialize firebase if not already done in this isolate.
  await Firebase.initializeApp();
  _logger.i('FCM Background message received: ${message.messageId}');
  _logger.d('FCM Background payload: ${message.data}');
}

final pushNotificationServiceProvider = Provider<PushNotificationService>((ref) {
  return PushNotificationService(ref);
});

class PushNotificationService {
  final Ref _ref;
  bool _initialized = false;

  PushNotificationService(this._ref);

  /// Initializes FCM and permissions.
  /// Safely catches errors if Firebase configurations are missing on local dev / Flutter web.
  Future<void> initialize() async {
    if (_initialized) return;

    try {
      _logger.i('Initializing Firebase...');
      
      // Initialize Firebase (if configurations exist, otherwise will throw on certain targets,
      // which we catch to run in simulation mode gracefully).
      await Firebase.initializeApp();
      
      // Set background handler
      FirebaseMessaging.onBackgroundMessage(_firebaseMessagingBackgroundHandler);

      // Request permissions (primarily for iOS and Android 13+)
      final messaging = FirebaseMessaging.instance;
      final settings = await messaging.requestPermission(
        alert: true,
        badge: true,
        sound: true,
        provisional: false,
      );

      _logger.i('FCM Notification permission status: ${settings.authorizationStatus}');

      if (settings.authorizationStatus == AuthorizationStatus.authorized ||
          settings.authorizationStatus == AuthorizationStatus.provisional) {
        // Setup listeners
        _setupMessageListeners();
        
        // Listen to auth state changes to register FCM token when logged in
        _ref.listen<AuthStatus>(authServiceProvider, (previous, next) {
          if (next == AuthStatus.authenticated) {
            registerDeviceToken();
          }
        }, fireImmediately: true);
      }
      
      _initialized = true;
    } catch (e) {
      _logger.w('Firebase initialization skipped or failed (Running in local simulation mode): $e');
    }
  }

  /// Fetches the device token and sends it to the backend.
  Future<void> registerDeviceToken() async {
    try {
      final messaging = FirebaseMessaging.instance;
      
      // Fetch token
      String? token;
      if (kIsWeb) {
        // If web, require VAPID key optionally
        token = await messaging.getToken();
      } else {
        token = await messaging.getToken();
      }

      if (token != null) {
        _logger.i('FCM Token generated: ${token.substring(0, 10)}...');
        
        // Determine device type
        String deviceType = 'Web';
        if (!kIsWeb) {
          if (Platform.isAndroid) {
            deviceType = 'Android';
          } else if (Platform.isIOS) {
            deviceType = 'iOS';
          }
        }

        // Register token with backend
        final apiService = _ref.read(notificationsApiServiceProvider);
        await apiService.registerFcmToken(token: token, deviceType: deviceType);
        _logger.i('FCM Token successfully registered with backend API');
      } else {
        _logger.w('FCM Token returned null');
      }
    } catch (e) {
      _logger.e('Failed to register FCM device token', error: e);
    }
  }

  /// Configures foreground messaging listeners.
  void _setupMessageListeners() {
    // 1. Foreground messaging
    FirebaseMessaging.onMessage.listen((RemoteMessage message) {
      _logger.i('FCM Foreground message received: ${message.messageId}');
      _logger.d('FCM Foreground payload: ${message.data}');
      // Trigger notification UI banner or state changes if needed
    });

    // 2. Message clicked when app in background but open
    FirebaseMessaging.onMessageOpenedApp.listen((RemoteMessage message) {
      _logger.i('FCM Message opened app: ${message.messageId}');
      _logger.d('FCM Opened payload: ${message.data}');
      _handleNotificationNavigation(message.data);
    });

    // 3. App opened from terminated state via message
    FirebaseMessaging.instance.getInitialMessage().then((RemoteMessage? message) {
      if (message != null) {
        _logger.i('FCM Initial message opened app: ${message.messageId}');
        _logger.d('FCM Initial payload: ${message.data}');
        _handleNotificationNavigation(message.data);
      }
    });
  }

  /// Handles custom navigation logic when a push notification is tapped
  void _handleNotificationNavigation(Map<String, dynamic> data) {
    // E.g., Navigate to active delivery if order status updated
    final orderId = data['orderId'] ?? data['OrderId'];
    if (orderId != null) {
      _logger.i('Navigating to order: $orderId from notification tap');
      // Perform navigation using GoRouter, e.g. ref.read(goRouterProvider).go('/delivery/active')
    }
  }
}
