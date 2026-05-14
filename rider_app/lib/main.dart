import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app/app.dart';

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
void main() {
  WidgetsFlutterBinding.ensureInitialized();

  runApp(
    const ProviderScope(
      child: App(),
    ),
  );
}
