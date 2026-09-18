import 'package:flutter/material.dart';
import '../../../app/app_theme.dart';
import '../../../shared/utils/order_status_helper.dart';

class OrderProgressBar extends StatelessWidget {
  final String status;
  const OrderProgressBar({super.key, required this.status});

  int get _currentStep {
    switch (status) {
      case 'ASSIGNED': return 0;
      case 'PICKING_UP': return 1;
      case 'DELIVERING': return 2;
      case 'COMPLETED': return 3;
      default:
        if (status == 'CANCELLED') return -2;
        return -1;
    }
  }

  @override
  Widget build(BuildContext context) {
    final step = _currentStep;
    if (step < 0) {
      return Text(
        OrderStatusHelper.label(status),
        style: const TextStyle(fontSize: 20, fontWeight: FontWeight.bold, color: AppTheme.primaryColor),
      );
    }

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _buildStep(0, 'รับออเดอร์', step),
          _buildLine(0, step),
          _buildStep(1, 'กำลังไปรับ', step),
          _buildLine(1, step),
          _buildStep(2, 'กำลังนำส่ง', step),
          _buildLine(2, step),
          _buildStep(3, 'ส่งสำเร็จ', step),
        ],
      ),
    );
  }

  Widget _buildStep(int stepIndex, String label, int currentStep) {
    final isActive = currentStep >= stepIndex;
    return Column(
      children: [
        Container(
          width: 24,
          height: 24,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: isActive ? AppTheme.primaryColor : Colors.grey[300],
          ),
          child: isActive ? const Icon(Icons.check, size: 16, color: Colors.white) : null,
        ),
        const SizedBox(height: 6),
        SizedBox(
          width: 50,
          child: Text(
            label,
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 10,
              color: isActive ? Colors.black87 : Colors.grey,
              fontWeight: isActive ? FontWeight.bold : FontWeight.normal,
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildLine(int stepIndex, int currentStep) {
    final isActive = currentStep > stepIndex;
    return Expanded(
      child: Container(
        height: 2,
        color: isActive ? AppTheme.primaryColor : Colors.grey[300],
        margin: const EdgeInsets.only(top: 12),
      ),
    );
  }
}

String formatDistance(double? meters) {
  if (meters == null) return '--';
  if (meters < 1000) {
    return '${meters.toStringAsFixed(0)} m';
  } else {
    return '${(meters / 1000).toStringAsFixed(1)} km';
  }
}

String formatDuration(double? seconds) {
  if (seconds == null) return '--';
  final minutes = (seconds / 60).round();
  if (minutes < 60) {
    return '$minutes mins';
  } else {
    final hours = minutes ~/ 60;
    final remainingMins = minutes % 60;
    if (remainingMins == 0) {
      return '$hours hrs';
    } else {
      return '$hours hrs $remainingMins mins';
    }
  }
}
