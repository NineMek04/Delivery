import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app_router.dart';
import 'app_theme.dart';

/// Root App Widget.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/app/app.component.ts`
/// - .NET: `Program.cs` → `app.Run()`
///
/// Structure:
/// - ProviderScope (main.dart) → App → MaterialApp.router → GoRouter → Screens
class App extends ConsumerWidget {
  const App({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(appRouterProvider);

    return MaterialApp.router(
      // ── App Identity ───────────────────────────────────────────────
      title: 'Rider App',
      debugShowCheckedModeBanner: false,

      // ── Theme ──────────────────────────────────────────────────────
      theme: AppTheme.lightTheme,
      darkTheme: AppTheme.darkTheme,
      themeMode: ThemeMode.dark, // ใช้ Dark mode เป็นหลัก

      // ── Router ─────────────────────────────────────────────────────
      routerConfig: router,
    );
  }
}
