// Role-based navigation guard and smoke tests for the Rider App.

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:rider_app/app/app.dart';
import 'package:rider_app/app/app_router.dart';
import 'package:rider_app/core/auth/auth_service.dart';

class MockAuthService extends AuthService {
  final AuthStatus mockStatus;
  final String mockRole;

  MockAuthService(this.mockStatus, this.mockRole);

  @override
  AuthStatus build() {
    return mockStatus;
  }

  @override
  String? get userRole => mockRole;

  @override
  bool get isTokenValid => mockStatus == AuthStatus.authenticated;

  @override
  String? get currentToken => mockStatus == AuthStatus.authenticated ? 'mock.jwt.token' : null;
}

void main() {
  testWidgets('App starts and shows Login screen for unauthenticated users', (WidgetTester tester) async {
    await tester.pumpWidget(
      const ProviderScope(child: App()),
    );

    await tester.pumpAndSettle(const Duration(seconds: 1));

    expect(find.text('Rider App'), findsOneWidget);
    expect(find.text('เข้าสู่ระบบ'), findsOneWidget);
  });

  testWidgets('Authenticated Customer is redirected to customer route, and gets alert when navigating to store route', (WidgetTester tester) async {
    final mockAuth = MockAuthService(AuthStatus.authenticated, 'Customer');

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          authServiceProvider.overrideWith(() => mockAuth),
        ],
        child: const App(),
      ),
    );

    // Let the GoRouter redirect to /customer run and settle
    await tester.pumpAndSettle(const Duration(seconds: 1));

    // We should be on the Customer home (StoreListScreen)
    expect(find.text('ร้านอาหาร'), findsOneWidget);

    // Retrieve GoRouter and try to navigate to /store (unauthorized for Customer)
    final router = ProviderScope.containerOf(tester.element(find.byType(App))).read(appRouterProvider);
    router.go('/store');

    // Wait for the redirect and microtask for alert state to finish
    await tester.pumpAndSettle();

    // Verify that the warning dialog appears with the message "สิทธิ์นี้ไม่ได้รับอนุญาตให้เข้า"
    expect(find.text('การเข้าถึงถูกปฏิเสธ'), findsOneWidget);
    expect(find.text('สิทธิ์นี้ไม่ได้รับอนุญาตให้เข้า'), findsOneWidget);

    // Tap OK to close the dialog
    await tester.tap(find.text('ตกลง'));
    await tester.pumpAndSettle();

    // The dialog should be closed
    expect(find.text('การเข้าถึงถูกปฏิเสธ'), findsNothing);
  });
}
