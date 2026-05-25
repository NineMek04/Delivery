// Basic smoke test for the Rider App.
// Verifies that the app starts and shows the login screen.

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:rider_app/app/app.dart';

void main() {
  testWidgets('App starts and shows Login screen', (WidgetTester tester) async {
    // Build the app inside a ProviderScope (required by Riverpod).
    await tester.pumpWidget(
      const ProviderScope(child: App()),
    );

    // Allow async initialization (AuthService.build runs initializeAuth).
    await tester.pumpAndSettle(const Duration(seconds: 3));

    // The login screen should be visible.
    expect(find.text('Rider App'), findsOneWidget);
    expect(find.text('เข้าสู่ระบบ'), findsOneWidget);
  });
}
