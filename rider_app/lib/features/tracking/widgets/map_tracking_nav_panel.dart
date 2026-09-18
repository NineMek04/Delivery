import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:latlong2/latlong.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../models/order.dart';
import 'map_tracking_types.dart';

Future<void> launchExternalMaps(BuildContext context, LatLng target, String label) async {
  final googleUrl = Uri.parse(
      'https://www.google.com/maps/dir/?api=1&destination=${target.latitude},${target.longitude}&travelmode=two_wheeler');
  final appleUrl = Uri.parse(
      'maps://?q=${target.latitude},${target.longitude}');

  try {
    if (await canLaunchUrl(googleUrl)) {
      await launchUrl(googleUrl, mode: LaunchMode.externalApplication);
    } else if (await canLaunchUrl(appleUrl)) {
      await launchUrl(appleUrl, mode: LaunchMode.externalApplication);
    } else {
      throw 'Could not launch maps application';
    }
  } catch (e) {
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('ไม่สามารถเปิดแผนที่นำทางได้: $e')),
      );
    }
  }
}

class NavigationInstructionPanel extends StatelessWidget {
  final LatLng riderPos;
  final OrderDto? activeOrder;
  final LatLng? pickup;
  final LatLng? dropoff;
  final double? routeDistance;
  final SimFlowPhase simPhase;
  final bool forceDropoff;

  const NavigationInstructionPanel({
    super.key,
    required this.riderPos,
    this.activeOrder,
    this.pickup,
    this.dropoff,
    this.routeDistance,
    required this.simPhase,
    this.forceDropoff = false,
  });

  @override
  Widget build(BuildContext context) {
    LatLng? target = pickup;
    String targetName = "จุดรับอาหาร (ร้านค้า)";
    bool isPickup = true;

    if (activeOrder != null) {
      if (activeOrder!.status.toUpperCase() == "DELIVERING" || forceDropoff) {
        target = dropoff;
        targetName = "จุดส่งอาหาร (บ้านลูกค้า)";
        isPickup = false;
      }
    } else if (simPhase == SimFlowPhase.delivery || simPhase == SimFlowPhase.completed) {
      target = dropoff;
      targetName = "จุดส่งอาหาร (บ้านลูกค้า)";
      isPickup = false;
    }

    if (target == null) return const SizedBox.shrink();

    final distance = routeDistance ?? Geolocator.distanceBetween(
      riderPos.latitude, riderPos.longitude,
      target.latitude, target.longitude,
    );

    IconData icon;
    String instruction;

    if (distance > 1000) {
      icon = Icons.navigation_outlined;
      instruction = "ตรงไปตามถนนอุดรธานี อีก ${(distance / 1000).toStringAsFixed(1)} กม.";
    } else if (distance > 400) {
      icon = Icons.turn_slight_right;
      instruction = "อีก ${(distance).toStringAsFixed(0)} ม. เตรียมชิดขวาเพื่อเลี้ยว";
    } else if (distance > 100) {
      icon = isPickup ? Icons.store : Icons.turn_right;
      instruction = "อีก ${(distance).toStringAsFixed(0)} ม. เลี้ยวขวาเข้าสู่${isPickup ? 'ร้านค้า' : 'บ้านลูกค้า'}";
    } else {
      icon = Icons.flag;
      instruction = "คุณเดินทางถึง${isPickup ? 'จุดรับอาหาร' : 'จุดส่งมอบอาหาร'}แล้ว!";
    }

    return Card(
      elevation: 6,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      color: Colors.grey[900]?.withOpacity(0.9) ?? Colors.black87,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Row(
          children: [
            Container(
              width: 46,
              height: 46,
              decoration: const BoxDecoration(
                shape: BoxShape.circle,
                color: Color(0xFF1A73E8),
              ),
              child: Icon(icon, color: Colors.white, size: 26),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    instruction,
                    style: const TextStyle(
                      color: Colors.white,
                      fontSize: 15,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    "มุ่งหน้าสู่ $targetName",
                    style: TextStyle(
                      color: Colors.grey[400],
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
            IconButton(
              icon: const Icon(Icons.directions, color: Color(0xFF1A73E8), size: 28),
              tooltip: 'เปิดแผนที่นำทางภายนอก',
              onPressed: () => launchExternalMaps(context, target!, targetName),
            ),
          ],
        ),
      ),
    );
  }
}
