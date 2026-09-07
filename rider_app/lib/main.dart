import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app/app.dart';
import 'core/config/environment.dart';
import 'core/config/server_config_service.dart';

/// Entry point ของ Rider App.
///
/// เทียบกับ:
/// - .NET: `Program.cs` → `WebApplication.Run()`
/// - Angular: `main.ts` → `bootstrapApplication(AppComponent, appConfig)`
///
/// Structure:
/// ```
/// main() → ProviderScope (DI Container) → App → MaterialApp → GoRouter → Screens
/// ```
///
/// `ProviderScope` ทำหน้าที่เทียบเท่า:
/// - .NET: `builder.Services.Add...()` (DI Container)
/// - Angular: `providers: [...]` ใน app.config.ts
void main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Load persistent custom server URL from storage if configured
  final serverConfig = ServerConfigService();
  final savedUrl = await serverConfig.getSavedServerUrl();
  if (savedUrl != null && savedUrl.isNotEmpty) {
    Environment.setCustomBaseUrl(savedUrl);
  }

  runApp(
    ProviderScope(
      overrides: [
        if (savedUrl != null && savedUrl.isNotEmpty)
          serverUrlProvider.overrideWith((ref) => savedUrl),
      ],
      child: const App(),
    ),
  );
}
