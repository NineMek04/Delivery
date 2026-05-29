import 'package:flutter/material.dart';

import '../../app/app_theme.dart';
import '../../core/signalr/signalr_service.dart';

/// แถบสถานะการเชื่อมต่อ SignalR + GPS.
class ConnectionStatusBar extends StatelessWidget {
  final SignalRConnectionState signalRState;
  final bool isGpsTracking;
  final bool isOnline;

  const ConnectionStatusBar({
    super.key,
    required this.signalRState,
    required this.isGpsTracking,
    required this.isOnline,
  });

  @override
  Widget build(BuildContext context) {
    final (label, color, icon) = _resolve();

    return Material(
      color: color.withValues(alpha: 0.15),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        child: Row(
          children: [
            Icon(icon, size: 16, color: color),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                label,
                style: TextStyle(color: color, fontSize: 12),
              ),
            ),
            if (isOnline)
              Icon(
                isGpsTracking ? Icons.gps_fixed : Icons.gps_off,
                size: 16,
                color: isGpsTracking ? AppTheme.accentColor : AppTheme.textMuted,
              ),
          ],
        ),
      ),
    );
  }

  (String, Color, IconData) _resolve() {
    if (!isOnline) {
      return ('ออฟไลน์ — ไม่รับงาน', AppTheme.textMuted, Icons.cloud_off);
    }
    switch (signalRState) {
      case SignalRConnectionState.connected:
        return ('ออนไลน์ — เชื่อมต่อแล้ว', AppTheme.accentColor, Icons.cloud_done);
      case SignalRConnectionState.connecting:
      case SignalRConnectionState.reconnecting:
        return ('กำลังเชื่อมต่อ...', AppTheme.warningColor, Icons.cloud_sync);
      case SignalRConnectionState.error:
        return ('เชื่อมต่อล้มเหลว', AppTheme.errorColor, Icons.cloud_off);
      case SignalRConnectionState.disconnected:
        return ('รอเชื่อมต่อ SignalR', AppTheme.warningColor, Icons.cloud_queue);
    }
  }
}
