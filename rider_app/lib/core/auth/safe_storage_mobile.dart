import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:rider_app/core/auth/safe_storage.dart';

SafeStorage getSafeStorage() => MobileSafeStorage();

class MobileSafeStorage implements SafeStorage {
  static const _storage = FlutterSecureStorage();

  @override
  Future<String?> read({required String key}) async {
    try {
      return await _storage.read(key: key);
    } catch (_) {
      return null;
    }
  }

  @override
  Future<void> write({required String key, required String value}) async {
    try {
      await _storage.write(key: key, value: value);
    } catch (_) {}
  }

  @override
  Future<void> delete({required String key}) async {
    try {
      await _storage.delete(key: key);
    } catch (_) {}
  }
}
