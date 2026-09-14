import 'dart:io' show Platform;
import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:url_launcher/url_launcher.dart';
import '../config/environment.dart';

class AppUpdateInfo {
  final String latestVersion;
  final int buildNumber;
  final String minSupportedVersion;
  final String downloadUrl;
  final String releaseNotes;
  final bool forceUpdate;
  final int fileSizeBytes;

  AppUpdateInfo({
    required this.latestVersion,
    required this.buildNumber,
    required this.minSupportedVersion,
    required this.downloadUrl,
    required this.releaseNotes,
    required this.forceUpdate,
    required this.fileSizeBytes,
  });

  factory AppUpdateInfo.fromJson(Map<String, dynamic> json) {
    return AppUpdateInfo(
      latestVersion: json['latestVersion']?.toString() ?? '1.0.0',
      buildNumber: json['buildNumber'] is int
          ? json['buildNumber'] as int
          : int.tryParse(json['buildNumber']?.toString() ?? '1') ?? 1,
      minSupportedVersion: json['minSupportedVersion']?.toString() ?? '1.0.0',
      downloadUrl: json['downloadUrl']?.toString() ?? '',
      releaseNotes: json['releaseNotes']?.toString() ?? '',
      forceUpdate: json['forceUpdate'] == true,
      fileSizeBytes: json['fileSizeBytes'] is int
          ? json['fileSizeBytes'] as int
          : int.tryParse(json['fileSizeBytes']?.toString() ?? '0') ?? 0,
    );
  }
}

class AppUpdateService {
  final Dio _dio = Dio(BaseOptions(
    connectTimeout: const Duration(seconds: 5),
    receiveTimeout: const Duration(seconds: 10),
  ));

  /// เวอร์ชันปัจจุบันของแอปที่คอมไพล์อยู่
  static const String currentVersion = '1.0.2';
  static const int currentBuildNumber = 2;

  /// ตรวจสอบว่ามีเวอร์ชันใหม่จากเซิร์ฟเวอร์หรือไม่
  Future<AppUpdateInfo?> checkForUpdate() async {
    // รันเฉพาะบน Android หรือตอนทดสอบ
    if (kIsWeb) return null;

    try {
      final endpoint = '${Environment.apiUrl}/app/version?platform=android';
      final response = await _dio.get(endpoint);

      if (response.statusCode == 200 && response.data != null) {
        final info = AppUpdateInfo.fromJson(Map<String, dynamic>.from(response.data));

        // ตรวจสอบว่าเวอร์ชันเซิร์ฟเวอร์ใหม่กว่าเวอร์ชันในเครื่องหรือไม่
        if (info.buildNumber > currentBuildNumber ||
            _isVersionHigher(info.latestVersion, currentVersion)) {
          return info;
        }
      }
    } catch (e) {
      debugPrint('[AppUpdateService] Check update error (ignored): $e');
    }
    return null;
  }

  /// สั่งดาวน์โหลดและติดตั้ง APK
  Future<bool> startUpdate(String downloadUrl) async {
    try {
      final uri = Uri.parse(downloadUrl);
      if (await canLaunchUrl(uri)) {
        // เปิด Android Package Installer / Download Manager โดยตรง
        return await launchUrl(uri, mode: LaunchMode.externalApplication);
      }
    } catch (e) {
      debugPrint('[AppUpdateService] Failed to launch update URL: $e');
    }
    return false;
  }

  bool _isVersionHigher(String remote, String local) {
    try {
      final rParts = remote.split('.').map((e) => int.tryParse(e) ?? 0).toList();
      final lParts = local.split('.').map((e) => int.tryParse(e) ?? 0).toList();

      for (int i = 0; i < 3; i++) {
        final r = i < rParts.length ? rParts[i] : 0;
        final l = i < lParts.length ? lParts[i] : 0;
        if (r > l) return true;
        if (r < l) return false;
      }
    } catch (_) {}
    return false;
  }
}
