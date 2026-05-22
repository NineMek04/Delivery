import 'package:rider_app/core/auth/safe_storage.dart';

SafeStorage getSafeStorage() => throw UnsupportedError(
    'Cannot create a SafeStorage without dart:html or dart:io');
