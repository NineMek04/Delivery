import 'package:flutter/material.dart';
import '../../../app/app_theme.dart';
import '../../../core/config/server_config_service.dart';

class ServerStatusCard extends StatelessWidget {
  final String activeUrl;
  final bool isTesting;
  final ServerConnectionResult? lastTestResult;

  const ServerStatusCard({
    super.key,
    required this.activeUrl,
    required this.isTesting,
    this.lastTestResult,
  });

  @override
  Widget build(BuildContext context) {
    final bool isConfigured = activeUrl.isNotEmpty;
    final bool isOnline = lastTestResult?.success == true;

    Color badgeColor = Colors.orange;
    String statusText = 'ยังไม่ได้ตั้งค่า';
    IconData statusIcon = Icons.warning_amber_rounded;

    if (isConfigured) {
      if (isTesting) {
        badgeColor = Colors.blue;
        statusText = 'กำลังตรวจสอบ...';
        statusIcon = Icons.sync;
      } else if (isOnline) {
        badgeColor = Colors.green;
        statusText = 'ออนไลน์ (${lastTestResult?.latencyMs ?? 0} ms)';
        statusIcon = Icons.check_circle_rounded;
      } else if (lastTestResult != null && !lastTestResult!.success) {
        badgeColor = Colors.red;
        statusText = 'เชื่อมต่อไม่สำเร็จ';
        statusIcon = Icons.error_rounded;
      } else {
        badgeColor = Colors.green.shade400;
        statusText = 'กำหนดค่าแล้ว';
        statusIcon = Icons.link_rounded;
      }
    }

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppTheme.surfaceDark,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: badgeColor.withValues(alpha: 0.4), width: 1.5),
        boxShadow: [
          BoxShadow(
            color: badgeColor.withValues(alpha: 0.1),
            blurRadius: 10,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              const Text(
                'สถานะเซิร์ฟเวอร์ปัจจุบัน',
                style: TextStyle(fontWeight: FontWeight.bold, fontSize: 14, color: Colors.white),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                decoration: BoxDecoration(
                  color: badgeColor.withValues(alpha: 0.2),
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(color: badgeColor, width: 1),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(statusIcon, size: 14, color: badgeColor),
                    const SizedBox(width: 4),
                    Text(
                      statusText,
                      style: TextStyle(fontSize: 12, fontWeight: FontWeight.bold, color: badgeColor),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            isConfigured ? activeUrl : '(ไม่มี URL เซิร์ฟเวอร์ที่กำหนด)',
            style: TextStyle(
              fontFamily: 'monospace',
              fontSize: 13,
              color: isConfigured ? Colors.white : Colors.white38,
            ),
          ),
        ],
      ),
    );
  }
}
