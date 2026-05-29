import 'package:rider_app/core/auth/safe_storage_stub.dart'
    if (dart.library.html) 'package:rider_app/core/auth/safe_storage_web.dart'
    if (dart.library.io) 'package:rider_app/core/auth/safe_storage_mobile.dart';

/// Cross-platform storage helper that uses standard HTML LocalStorage on Web,
/// and FlutterSecureStorage on native (Android/iOS) platforms.
abstract class SafeStorage {
  Future<String?> read({required String key});
  Future<void> write({required String key, required String value});
  Future<void> delete({required String key});

  factory SafeStorage() => getSafeStorage();
}
