import 'dart:html' as html;
import 'package:rider_app/core/auth/safe_storage.dart';

SafeStorage getSafeStorage() => WebSafeStorage();

class WebSafeStorage implements SafeStorage {
  final Map<String, String> _inMemoryFallback = {};

  @override
  Future<String?> read({required String key}) async {
    try {
      return html.window.sessionStorage[key];
    } catch (_) {
      return _inMemoryFallback[key];
    }
  }

  @override
  Future<void> write({required String key, required String value}) async {
    try {
      html.window.sessionStorage[key] = value;
    } catch (_) {
      _inMemoryFallback[key] = value;
    }
  }

  @override
  Future<void> delete({required String key}) async {
    try {
      html.window.sessionStorage.remove(key);
    } catch (_) {
      _inMemoryFallback.remove(key);
    }
  }
}
